using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Playhub.GameSession;

// Preparation belongs to the existing Windows activation. The companion never
// launches another copy of the app, changes its settings, or chooses a PID.
internal sealed class UwpCompanionPreparation : IDisposable
{
    private readonly Process _sidecar;
    private readonly FileStream _native;
    private readonly string _aumid, _expected;
    private readonly Action<string> _log;
    private bool _sent, _disposed;
    private sealed record Plan(uint WrapperPid, long WrapperBirth, ulong GameId, string Aumid, string Executable);
    internal sealed record Ready(string Kind, string Aumid, string PackageFullName, string Executable);

    private UwpCompanionPreparation(Process sidecar, FileStream native, string aumid, string expected, Action<string> log) =>
        (_sidecar, _native, _aumid, _expected, _log) = (sidecar, native, aumid, expected, log);

    internal static UwpCompanionPreparation? TryStart(string aumid, string executable, Action<string> log)
    {
        var attributes = typeof(UwpCompanionPreparation).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        if (!attributes.Any(item => item.Key == "PlayhubUwpSteamCompanion" && item.Value == "true")
            || !Win32CompanionLease.TryReadGameId(Environment.GetEnvironmentVariable("SteamGameId"), out ulong gameId)
            || !string.Equals(Environment.ProcessPath, Path.Combine(AppContext.BaseDirectory, "Playhub.GameSession.exe"), StringComparison.OrdinalIgnoreCase)) return null;
        string helper = Path.Combine(AppContext.BaseDirectory, "Playhub.XboxSession.exe");
        string library = Path.Combine(AppContext.BaseDirectory, "PlayhubSteamCompanion.dll");
        if (!File.Exists(helper) || !File.Exists(library)) return null;
        FileStream? pinned = null;
        Process? child = null;
        try
        {
            return PrepareOptional(() =>
            {
                ValidateApplication(aumid, executable);
                string? digest = attributes.SingleOrDefault(item => item.Key == "PlayhubSteamCompanionSha256")?.Value;
                pinned = new FileStream(library, FileMode.Open, FileAccess.Read, FileShare.Read);
                VerifyDigest(pinned, digest);
                using var wrapper = Process.GetCurrentProcess();
                child = Process.Start(new ProcessStartInfo(helper, "--uwp-steam-companion-sidecar")
                {
                    UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, StandardInputEncoding = new UTF8Encoding(false)
                }) ?? throw new InvalidOperationException("The UWP companion did not start.");
                child.StandardInput.WriteLine(JsonSerializer.Serialize(new Plan((uint)wrapper.Id, wrapper.StartTime.ToFileTimeUtc(), gameId, aumid, executable)));
                child.StandardInput.Flush();
                var ready = ReadReady(child.StandardOutput.BaseStream).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                if (ready is null || ready.Kind != "playhub-uwp-companion-ready" || ready.Aumid != aumid
                    || string.IsNullOrEmpty(ready.PackageFullName) || !Path.IsPathFullyQualified(ready.Executable)
                    || !Path.GetFileName(ready.Executable).Equals(Path.GetFileName(executable), StringComparison.OrdinalIgnoreCase)
                    || child.HasExited) throw new InvalidDataException("The UWP companion preparation identity did not match.");
                bool granted = AllowSetForegroundWindow((uint)child.Id);
                log($"UWP Steam companion prepared; foreground-allowed={granted}.");
                var result = new UwpCompanionPreparation(child, pinned, aumid, executable, log);
                child = null; pinned = null;
                return result;
            }, log);
        }
        finally
        {
            if (child is not null)
            {
                try { child.StandardInput.Close(); child.WaitForExit(1500); } catch (Exception error) when (error is IOException or InvalidOperationException) { }
                child.Dispose();
            }
            pinned?.Dispose();
        }
    }

    // The registered Windows activation remains authoritative. A failed optional
    // sidecar must not prevent that same activation from launching the game.
    // InvalidDataException derives from SystemException, not IOException.
    internal static T? PrepareOptional<T>(Func<T> prepare, Action<string> log) where T : class
    {
        try { return prepare(); }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidDataException
            or InvalidOperationException or UnauthorizedAccessException or TimeoutException or JsonException or ArgumentException)
        {
            log("UWP Steam companion preparation unavailable: " + error.Message);
            return null;
        }
    }

    internal void Complete(string aumid, IUwpProcess game, long activation)
    {
        if (_disposed || _sent) return;
        _sent = true;
        try
        {
            if (aumid != _aumid || game is not WindowsUwpProcess actual || actual.Birth < activation || activation <= 0
                || !UwpLifetime.IsGame(actual.PackageFamily, actual.Executable, aumid.Split('!')[0], _expected))
                throw new InvalidOperationException("The UWP companion requires the exact newly activated game.");
            _sidecar.StandardInput.WriteLine(JsonSerializer.Serialize(new
                { GamePid = actual.Id, GameBirth = actual.Birth, ActivationFileTime = activation }));
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        { _log("UWP Steam companion handoff unavailable: " + error.Message); }
        finally { try { _sidecar.StandardInput.Close(); } catch (IOException) { } }
    }

    internal static void ValidateApplication(string aumid, string executable)
    {
        string[] parts = aumid.Split('!');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace) || aumid.Length > 512 || aumid.Contains('\0')
            || string.IsNullOrWhiteSpace(executable) || executable.Length > 4096 || Path.IsPathRooted(executable)
            || executable.Contains(':') || executable.Contains('\0') || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || executable.Replace('/', '\\').Split('\\').Any(part => part is ".." or "." or ""))
            throw new InvalidDataException("The UWP companion requires a package application and a contained manifest executable.");
    }

    internal static void VerifyDigest(Stream file, string? expected)
    {
        if (expected?.Length != 64 || !expected.All(Uri.IsHexDigit)
            || !Convert.ToHexString(SHA256.HashData(file)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The UWP companion native image does not match this build.");
    }

    internal static async Task<Ready?> ReadReady(Stream input)
    {
        byte[] buffer = new byte[64 * 1024], item = new byte[1];
        int length = 0;
        while (length < buffer.Length)
        {
            if (await input.ReadAsync(item).ConfigureAwait(false) == 0)
                throw new InvalidDataException("The UWP companion preparation message is incomplete.");
            if (item[0] == '\n')
                return JsonSerializer.Deserialize<Ready>(buffer.AsSpan(0, length))
                    ?? throw new InvalidDataException("The UWP companion preparation message is empty.");
            buffer[length++] = item[0];
        }
        throw new InvalidDataException("The UWP companion preparation message exceeds its size limit.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            try { _sidecar.StandardInput.Close(); _sidecar.WaitForExit(1500); }
            catch (Exception error) when (error is IOException or InvalidOperationException) { }
        }
        finally { _sidecar.Dispose(); _native.Dispose(); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint pid);
}
