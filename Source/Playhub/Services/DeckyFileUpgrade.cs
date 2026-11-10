using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Playhub.Services;

internal static class DeckyFileUpgrade
{
    internal const string ConsoleName = "PluginLoader.exe";
    internal const string BackgroundName = "PluginLoader_noconsole.exe";

    internal static bool Matches(string directory, IEnumerable<DeckyWindowsReleaseClient.Asset> assets)
    {
        var matching = true;
        foreach (var asset in assets)
        {
            var path = GetLoaderPath(directory, asset.InstallName);
            if (!File.Exists(path)) { matching = false; continue; }
            using var file = File.OpenRead(path);
            matching &= DeckyWindowsReleaseClient.IsAssetSize(file.Length) && (asset.Size == 0 || file.Length == asset.Size) &&
                DeckyWindowsReleaseClient.DigestMatches(SHA256.HashData(file), asset.Digest);
        }
        return matching;
    }

    internal static string GetLoaderPath(string directory, string name)
    {
        if (name is not (ConsoleName or BackgroundName)) throw new InvalidDataException("Nome del loader Decky non valido.");
        var path = Path.GetFullPath(Path.Combine(directory, name));
        RequireOrdinaryPath(path);
        return path;
    }

    internal static void RequireOrdinaryPath(string path)
    {
        for (string? component = Path.GetFullPath(path); component is not null; component = Path.GetDirectoryName(component))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(component); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Il percorso di Decky contiene un collegamento. Non è stato sostituito alcun file.");
        }
    }

    // Called inside the startup mutex. Only these two known files can be swapped;
    // backups, prepared bytes and replacement paths are on the same volume.
    internal static void Apply(string services, IReadOnlyDictionary<DeckyWindowsReleaseClient.Asset, byte[]> payload,
        Action prepare, Action stop, Action start, Action stopFailedStart, Action restore)
    {
        if (payload.Count != 2 || payload.Keys.Count(asset => asset.InstallName == ConsoleName) != 1 ||
            payload.Keys.Count(asset => asset.InstallName == BackgroundName) != 1)
            throw new InvalidDataException("La release ufficiale non contiene entrambi i loader Windows.");

        var files = payload.Select(item => (item.Key, item.Value, Target: GetLoaderPath(services, item.Key.InstallName))).ToArray();
        string work = Path.Combine(services, ".playhub-decky-update-" + Guid.NewGuid().ToString("N"));
        string staging = Path.Combine(work, "staging");
        string backup = Path.Combine(work, "backup");
        RequireOrdinaryPath(work);
        Directory.CreateDirectory(staging);
        var replaced = new List<(string Target, string Backup, bool PreviousExists)>();
        var prepared = false;
        var starting = false;
        var preserve = false;
        try
        {
            Directory.CreateDirectory(backup);
            foreach (var (asset, bytes, target) in files)
            {
                string staged = GetLoaderPath(staging, asset.InstallName);
                using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(target)) File.Copy(target, GetLoaderPath(backup, asset.InstallName));
            }
            if (!Matches(staging, payload.Keys))
                throw new InvalidDataException("La copia preparata di Decky non corrisponde alla release ufficiale.");

            prepared = true;
            prepare();
            stop();
            foreach (var (asset, _, target) in files)
            {
                var old = GetLoaderPath(backup, asset.InstallName);
                var previousExists = File.Exists(old);
                RequireOrdinaryPath(target);
                if (File.Exists(target) != previousExists || (previousExists && !FilesEqual(target, old)))
                    throw new IOException("I file di Decky sono cambiati durante l'aggiornamento. Riprova.");
                File.Move(GetLoaderPath(staging, asset.InstallName), target, overwrite: previousExists);
                replaced.Add((target, old, previousExists));
            }
            if (!Matches(services, payload.Keys))
                throw new InvalidDataException("La copia installata di Decky non supera la verifica.");
            starting = true;
            start();
        }
        catch (Exception updateError) when (prepared)
        {
            try
            {
                if (starting) stopFailedStart();
                for (int i = replaced.Count - 1; i >= 0; i--)
                {
                    var entry = replaced[i];
                    RequireOrdinaryPath(entry.Target);
                    if (entry.PreviousExists)
                    {
                        RequireOrdinaryPath(entry.Backup);
                        var staged = GetLoaderPath(staging, Path.GetFileName(entry.Target));
                        File.Copy(entry.Backup, staged, overwrite: true);
                        File.Move(staged, entry.Target, overwrite: true);
                    }
                    else File.Delete(entry.Target);
                }
                restore();
            }
            catch (Exception rollbackError)
            {
                preserve = true;
                throw new IOException($"Aggiornamento di Decky interrotto; il ripristino non è riuscito. Copia di sicurezza: {backup}",
                    new AggregateException(updateError, rollbackError));
            }
            throw;
        }
        finally
        {
            if (!preserve)
            {
                CleanStage(staging);
                CleanStage(backup);
                TryDeleteEmptyDirectory(work);
            }
        }
    }

    private static bool FilesEqual(string left, string right)
    {
        using var first = File.OpenRead(left);
        using var second = File.OpenRead(right);
        return first.Length == second.Length &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(first), SHA256.HashData(second));
    }

    private static void CleanStage(string path)
    {
        foreach (var name in new[] { ConsoleName, BackgroundName })
        {
            try { File.Delete(GetLoaderPath(path, name)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        TryDeleteEmptyDirectory(path);
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try { RequireOrdinaryPath(path); Directory.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
