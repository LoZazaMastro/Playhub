using Playhub.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Playhub.Services;

// Unmodified official release assets for offline installation. Online release
// selection always uses upstream metadata; retired private bytes are never served.
internal sealed class BundledDeckyWindowsRelease(string? resourceRoot = null)
{
    internal const string UpstreamTag = "v3.3.1";
    internal const string BuildVersion = "3.3.1";
    internal const string RelativeRoot = "Assets/DeckyRuntime/Official-3.3.1";
    internal const string SourceName = "sources/Decky-v3.3.1-Source.zip";
    internal const string SourceDigest = "abef6c5cfc9ace34de059e46886228cf2fe8a854ad542120ed23852296df494a";
    internal const string ConsoleDigest = "68ca1ebf3711c23057bd6734a8055399119b31d8812b254f8918f1f69af4d142";
    internal const string BackgroundDigest = "8b7a36146591a3d3b54ba69c986dc508e2aea21f56652520b21e1126deebe65b";
    // These hashes identify the retired build only for a transactional rollback
    // to official bytes. No download or installation path selects this pair.
    private const string RetiredConsoleDigest = "afb1efafbfa1587654241acac13a3a1800a9f76f30cbb819c8bba8d6f1ca16e6";
    private const string RetiredBackgroundDigest = "33dca959fe13dfe21ff4078d3ad55bab0c5a37e9b38c3c1ca321256bba8d8c50";
    private static readonly Version UpstreamVersion = new(3, 3, 1);
    private readonly string _root = Path.GetFullPath(resourceRoot ?? Path.Combine(AppContext.BaseDirectory, RelativeRoot));

    internal DeckyWindowsReleaseClient.Release? Get(Architecture architecture, bool offline = false,
        DeckyWindowsRelease? summary = null)
    {
        if (architecture != Architecture.X64 || !Directory.Exists(_root)) return null;
        RequireFile(SourceName, 466_542, SourceDigest);
        if (!File.Exists(OrdinaryPath("LICENSE"))) throw new InvalidDataException("La licenza del loader Decky incluso non è disponibile.");
        return new(summary ?? new(UpstreamTag, new DateTimeOffset(2026, 10, 27, 4, 26, 19, TimeSpan.Zero)),
            Asset(DeckyFileUpgrade.ConsoleName, 15_631_955, ConsoleDigest),
            Asset(DeckyFileUpgrade.BackgroundName, 15_632_613, BackgroundDigest), offline);
    }

    private DeckyWindowsReleaseClient.Asset Asset(string name, int size, string digest) =>
        new(name, new Uri(OrdinaryPath(name)), size, digest);

    internal Task<byte[]> ReadAsync(DeckyWindowsReleaseClient.Asset asset, Architecture architecture) => Task.Run(() =>
    {
        if (architecture != Architecture.X64 || !asset.Url.IsFile || asset.Url.IsUnc || asset.Url.Host.Length != 0)
            throw new InvalidDataException("L'origine locale del loader Decky non è valida.");
        var expected = asset.InstallName switch
        {
            DeckyFileUpgrade.ConsoleName => (Size: 15_631_955, Digest: ConsoleDigest),
            DeckyFileUpgrade.BackgroundName => (Size: 15_632_613, Digest: BackgroundDigest),
            _ => throw new InvalidDataException("Il file locale del loader Decky non è valido.")
        };
        var path = OrdinaryPath(asset.InstallName);
        if (!string.Equals(Path.GetFullPath(asset.Url.LocalPath), path, StringComparison.OrdinalIgnoreCase) ||
            asset.Size != expected.Size || !string.Equals(asset.Digest, expected.Digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Il file locale del loader Decky non corrisponde al pacchetto incluso.");
        RequireFile(SourceName, 466_542, SourceDigest);
        using var stream = File.OpenRead(path);
        if (stream.Length != expected.Size) throw new InvalidDataException("La dimensione del loader Decky incluso non è valida.");
        var bytes = new byte[expected.Size];
        stream.ReadExactly(bytes);
        if (!DeckyWindowsReleaseClient.DigestMatches(SHA256.HashData(bytes), expected.Digest))
            throw new InvalidDataException("Il pacchetto Decky incluso non supera la verifica.");
        return bytes;
    });

    private string OrdinaryPath(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Percorso del loader Decky non valido.");
        DeckyFileUpgrade.RequireOrdinaryPath(path);
        return path;
    }

    private void RequireFile(string relative, int size, string digest)
    {
        var path = OrdinaryPath(relative);
        using var stream = File.OpenRead(path);
        if (stream.Length != size || !DeckyWindowsReleaseClient.DigestMatches(SHA256.HashData(stream), digest))
            throw new InvalidDataException("Il pacchetto Decky incluso non supera la verifica.");
    }

    internal static bool IsOfficialPair(string services) => PairMatches(services, ConsoleDigest, BackgroundDigest);
    internal static bool IsRetiredCompatibilityPair(string services) => PairMatches(services, RetiredConsoleDigest, RetiredBackgroundDigest);

    internal static void RequireSafeOfflineReplacement(string services)
    {
        var console = DeckyFileUpgrade.GetLoaderPath(services, DeckyFileUpgrade.ConsoleName);
        var background = DeckyFileUpgrade.GetLoaderPath(services, DeckyFileUpgrade.BackgroundName);
        if (!File.Exists(console) && !File.Exists(background)) return;
        if (IsOfficialPair(services) || IsRetiredCompatibilityPair(services)) return;
        throw new IOException("Connettiti per verificare la versione di Decky già installata.");
    }

    internal static bool HasNewerInstalledVersion(string services)
    {
        foreach (var name in new[] { DeckyFileUpgrade.ConsoleName, DeckyFileUpgrade.BackgroundName })
        {
            var path = DeckyFileUpgrade.GetLoaderPath(services, name);
            if (!File.Exists(path)) continue;
            var value = FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "";
            if (Version.TryParse(value.Split('+', '-')[0].TrimStart('v'), out var version) && version > UpstreamVersion)
                return true;
        }
        return false;
    }

    private static bool PairMatches(string services, string consoleDigest, string backgroundDigest)
    {
        foreach (var (name, digest) in new[] { (DeckyFileUpgrade.ConsoleName, consoleDigest), (DeckyFileUpgrade.BackgroundName, backgroundDigest) })
        {
            var path = DeckyFileUpgrade.GetLoaderPath(services, name);
            if (!File.Exists(path)) return false;
            using var stream = File.OpenRead(path);
            if (!DeckyWindowsReleaseClient.IsAssetSize(stream.Length) ||
                !DeckyWindowsReleaseClient.DigestMatches(SHA256.HashData(stream), digest)) return false;
        }
        return true;
    }
}
