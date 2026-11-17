using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GamingMode.Models;
using Microsoft.Win32;

namespace GamingMode.Services;

// Private trial only. A compile flag is an opt-in, never a capability receipt.
internal static class ShellFreeOverlayTrialPolicy
{
    private static readonly object Sync = new();
    private static IDisposable? retained;
    internal static bool Enabled
    {
        get
        {
#if PLAYHUB_SHELL_FREE_OVERLAY_TRIAL
            lock (Sync)
            {
                if (retained is not null) return true;
                try
                {
                    string? expected = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                        .FirstOrDefault(a => a.Key == "PlayhubShellFreeTrialManifestSha256")?.Value;
                    if (!Hex(expected)) return false;
                    string agent = Path.GetFullPath(AppContext.BaseDirectory);
                    string? app = FindApplicationRoot();
                    if (app is null) return false;
                    string manifest = Path.Combine(agent, "Assets", "OverlayPresentation", "standalone-trial.json");
                    var stream = OpenPinned(manifest);
                    try
                    {
                        if (stream.Length is < 2 or > 16384) { stream.Dispose(); return false; }
                        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
                        retained = Verify(bytes.ToArray(), expected!, agent, app, OpenPinned);
                        if (retained is null) { stream.Dispose(); return false; }
                        retained = new Pins([stream, retained]);
                        return true;
                    }
                    catch { stream.Dispose(); throw; }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
                    or ArgumentException or InvalidOperationException or System.Security.SecurityException)
                { return false; }
            }
#else
            return false;
#endif
        }
    }
    internal static bool MayRunWithoutExplorer(GamingSettings gaming) =>
        gaming.CloseExplorerInGamingMode && gaming.AllowExplorerCloseInGamingMode && Enabled;

    // Side-effect-free native capability validation, with an injectable file boundary for inert tests.
    internal static IDisposable? Verify(byte[] manifest, string expected, string agentRoot, string appRoot,
        Func<string, Stream> open)
    {
        List<IDisposable> pins = [];
        try
        {
            if (manifest.Length is < 2 or > 16384 || !Hex(expected)
                || !Convert.ToHexString(SHA256.HashData(manifest)).Equals(expected, StringComparison.OrdinalIgnoreCase)) return null;
            using var doc = JsonDocument.Parse(manifest);
            var value = doc.RootElement;
            if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 4
                || value.GetProperty("schema").GetInt32() != 1 || value.GetProperty("mode").GetString() != "ordinary-topmost"
                || value.GetProperty("band").GetInt32() != 1) return null;
            var components = value.GetProperty("components");
            if (components.ValueKind != JsonValueKind.Object || components.EnumerateObject().Count() != 3) return null;
            foreach (var component in new[] {
                ("steam", "PlayhubSteamCompanion.dll", appRoot),
                ("hud", "PlayhubHudDisplayNative.dll", agentRoot),
                ("curtain", "PlayhubCurtainDisplayNative.dll", agentRoot) })
            {
                var part = components.GetProperty(component.Item1);
                if (part.ValueKind != JsonValueKind.Object || part.EnumerateObject().Count() != 2
                    || part.GetProperty("file").GetString() != component.Item2) throw new InvalidDataException("Trial component path differs.");
                string? hash = part.GetProperty("sha256").GetString();
                if (!Hex(hash) || !Path.IsPathFullyQualified(component.Item3)) throw new InvalidDataException("Trial component pin is absent.");
                string path = Path.Combine(Path.GetFullPath(component.Item3), component.Item2);
                var stream = open(path); pins.Add(stream);
                if (!stream.CanRead || !stream.CanSeek || stream.Length is < 512 or > 536870912
                    || !Convert.ToHexString(SHA256.HashData(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Trial native bytes differ.");
            }
            return new Pins(pins);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        { foreach (var pin in pins) pin.Dispose(); return null; }
    }
    private static bool Hex(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static FileStream OpenPinned(string path)
    {
        string full = Path.GetFullPath(path), root = Path.GetPathRoot(full) ?? "";
        if (root.Length != 3 || full.StartsWith("\\\\", StringComparison.Ordinal)) throw new IOException("A local trial path is required.");
        string current = root;
        foreach (string segment in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Trial paths cannot redirect.");
        }
        return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
    private static string? FindApplicationRoot()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Playhub");
        if (key?.GetValue("InstallLocation") is string configured && Path.IsPathFullyQualified(configured)
            && Directory.Exists(configured)) return Path.GetFullPath(configured);
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Playhub");
        return Directory.Exists(fallback) ? fallback : null;
    }
    private sealed class Pins(IEnumerable<IDisposable> values) : IDisposable
    {
        public void Dispose() { foreach (var pin in values) pin.Dispose(); }
    }
}
