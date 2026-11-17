// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

public sealed class StoreInstallFileSystem : IStoreInstallFileSystem
{
    private const string Marker = ".playhub-install-owner";
    public IReadOnlyList<StoreInstallLocation> Locations() => DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable).Select(d =>
    {
        try { return new StoreInstallLocation(d.RootDirectory.FullName, d.Name, d.AvailableFreeSpace, d.IsReady); }
        catch (IOException) { return new StoreInstallLocation(d.Name, d.Name, 0, false); }
    }).ToArray();
    public string CanonicalDestination(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.IndexOfAny(['\0', '\r', '\n']) >= 0) throw new StoreOperationException("invalid_destination");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (StringComparer.OrdinalIgnoreCase.Equals(full, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full)!))) throw new StoreOperationException("invalid_destination");
        for (var p = full; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if (Directory.Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new StoreOperationException("destination_reparse_point");
        return full;
    }
    public long FreeBytes(string path) => new DriveInfo(Path.GetPathRoot(CanonicalDestination(path))!).AvailableFreeSpace;
    public bool CanClaim(string path, Guid? existingJob)
    {
        try
        {
            path = CanonicalDestination(path);
            if (File.Exists(path)) return false;
            if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
                return existingJob != null && File.ReadAllText(Path.Combine(path, Marker)) == existingJob.Value.ToString("D");
            var ancestor = path;
            while (!Directory.Exists(ancestor)) ancestor = Path.GetDirectoryName(ancestor)!;
            var probe = Path.Combine(ancestor, ".playhub-write-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or StoreOperationException) { return false; }
    }
    public bool Claim(string path, Guid job)
    {
        try
        {
            path = CanonicalDestination(path);
            if (!CanClaim(path, null)) return false;
            Directory.CreateDirectory(path);
            using var f = new FileStream(Path.Combine(path, Marker), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var w = new StreamWriter(f); w.Write(job.ToString("D")); w.Flush(); f.Flush(true); return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or StoreOperationException) { return false; }
    }
}
