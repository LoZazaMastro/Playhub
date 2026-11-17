// SPDX-License-Identifier: MIT. Original Playhub code, Copyright (c) 2026 LoZazaMastro.
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Playhub.StoreLibrary;

internal sealed record StorePartialManifest(string Fingerprint, IReadOnlyList<StoreOwnedInstallFile> Files,
    IReadOnlyList<string> PartialSuffixes, string? StateFile = null);
internal interface IStorePartialDownloadSource
{
    bool HasOperationLease { get; }
    Task<StorePartialManifest> ReadPartialManifestAsync(string id, CancellationToken token);
}
internal sealed record StorePartialCleanupAttempt(Guid RequestId, long ExpectedRevision);
internal sealed record StorePartialReceipt(int Version, Guid JobId, StoreGameKey Key, long AccountRevision,
    string AccountIdentity, string InstallPath, string DirectoryIdentity, StorePartialManifest? Manifest = null,
    bool WorkerStarted = false, bool DrainConfirmed = true, Guid? CleanupRequestId = null,
    long? CleanupExpectedRevision = null, bool CleanupFinished = false,
    IReadOnlyList<StorePartialCleanupAttempt>? CleanupAttempts = null);

// This never recursively removes a tree. Every deletion uses a retained handle
// to one file from a vendor-bound whitelist, after the entire preflight passes.
internal static class StoreOwnedPartialFiles
{
    const string Marker = ".playhub-install-owner";
    internal static StorePartialReceipt? Capture(StoreDownloadJob job, string accountIdentity)
    {
        try
        {
            using var roots = StoreOwnedInstallFiles.PinRoot(job.InstallPath);
            using var marker = Open(Path.Combine(job.InstallPath, Marker), delete: false);
            CheckMarker(marker, job.JobId);
            if (Directory.EnumerateFileSystemEntries(job.InstallPath).Any(p => !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(p), Marker))) return null;
            return new(1, job.JobId, job.Key, job.AccountRevision, accountIdentity,
                new StoreInstallFileSystem().CanonicalDestination(job.InstallPath), Identity(job.InstallPath));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or StoreOperationException) { return null; }
    }
    internal static void ValidateManifest(StorePartialManifest manifest, string root)
    {
        if (manifest is null || manifest.Fingerprint is not { Length: 64 } || manifest.Files is null || manifest.PartialSuffixes is null || !manifest.Fingerprint.All(Uri.IsHexDigit) ||
            manifest.Files.Count is < 1 or > 100000 || manifest.PartialSuffixes.Count > 4 ||
            manifest.PartialSuffixes.Any(s => s is not (".tmp" or ".patch" or ".delta")) ||
            manifest.StateFile is not (null or ".gogdl-resume")) throw Error("manifest_invalid");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.Files)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.RelativePath)) throw Error("manifest_invalid");
            var path = StoreOwnedInstallFiles.PathFor(root, item.RelativePath);
            if (!names.Add(path) || Path.GetFileName(path).Equals(Marker, StringComparison.OrdinalIgnoreCase)) throw Error("manifest_invalid");
            if (item.HashAlgorithm == "SHAKE128")
            {
                if (!Shake128.IsSupported || item.Size < 0 || item.Hash is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit) || item.Chunks != null) throw Error("manifest_invalid");
            }
            else
            {
                StoreOwnedInstallFiles.ValidateChecksum(item);
                if (item.HashAlgorithm == null && item.Chunks == null) throw Error("manifest_invalid");
            }
        }
        foreach (var item in manifest.Files)
            foreach (var suffix in manifest.PartialSuffixes)
                if (!names.Add(StoreOwnedInstallFiles.PathFor(root, item.RelativePath + suffix))) throw Error("manifest_invalid");
    }
    internal static bool CanReuse(StorePartialReceipt receipt)
    {
        try
        {
            using var roots = StoreOwnedInstallFiles.PinRoot(receipt.InstallPath);
            using var directories = new StoreOwnedInstallFiles.DirectoryPins();
            using var marker = Open(Path.Combine(receipt.InstallPath, Marker), delete: false);
            CheckMarker(marker, receipt.JobId);
            return receipt.CleanupFinished && Identity(receipt.InstallPath) == receipt.DirectoryIdentity && OnlyOwner(receipt.InstallPath, directories);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or StoreOperationException) { return false; }
    }
    internal static void AssertCurrentRoot(StorePartialReceipt receipt)
    {
        using var roots = StoreOwnedInstallFiles.PinRoot(receipt.InstallPath);
        using var directories = new StoreOwnedInstallFiles.DirectoryPins();
        using var marker = Open(Path.Combine(receipt.InstallPath, Marker), delete: false);
        CheckMarker(marker, receipt.JobId);
        if (Identity(receipt.InstallPath) != receipt.DirectoryIdentity ||
            !receipt.WorkerStarted && !OnlyOwner(receipt.InstallPath, directories)) throw Error("ownership_lost");
    }
    internal static StorePartialReceipt? Reclaim(StorePartialReceipt receipt, StoreDownloadJob next, string accountIdentity)
    {
        try
        {
            using var roots = StoreOwnedInstallFiles.PinRoot(receipt.InstallPath);
            using var directories = new StoreOwnedInstallFiles.DirectoryPins();
            var handle = CreateFile(Path.Combine(receipt.InstallPath, Marker), 0xC0000000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); return null; }
            if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x410) != 0 || info.Links != 1) { handle.Dispose(); return null; }
            using var marker = new FileStream(handle, FileAccess.ReadWrite);
            CheckMarker(marker, receipt.JobId);
            if (!receipt.CleanupFinished || Identity(receipt.InstallPath) != receipt.DirectoryIdentity || !OnlyOwner(receipt.InstallPath, directories)) return null;
            marker.Position = 0; marker.Write(System.Text.Encoding.ASCII.GetBytes(next.JobId.ToString("D"))); marker.Flush(true);
            return new(1, next.JobId, next.Key, next.AccountRevision, accountIdentity, receipt.InstallPath, receipt.DirectoryIdentity);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or StoreOperationException) { return null; }
    }
    static bool OnlyOwner(string root, StoreOwnedInstallFiles.DirectoryPins pins)
    {
        var pending = new Queue<string>(); pending.Enqueue(root); int count = 0;
        while (pending.TryDequeue(out var directory))
        {
            if (++count > 65536) return false;
            pins.Add(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                if ((attributes & FileAttributes.Directory) != 0) pending.Enqueue(path);
                else if (!StringComparer.OrdinalIgnoreCase.Equals(path, Path.Combine(root, Marker))) return false;
            }
        }
        return true;
    }
    internal static void Delete(StorePartialReceipt receipt, CancellationToken token, Func<IDisposable>? commitGuard = null)
    {
        if (receipt.Version != 1 || receipt.JobId == Guid.Empty || receipt.WorkerStarted && !receipt.DrainConfirmed) throw Error("drain_unconfirmed");
        string root = new StoreInstallFileSystem().CanonicalDestination(receipt.InstallPath);
        using var roots = StoreOwnedInstallFiles.PinRoot(Path.GetDirectoryName(root)!);
        using var marker = Open(Path.Combine(root, Marker), delete: true);
        CheckMarker(marker, receipt.JobId);
        if (Identity(root) != receipt.DirectoryIdentity) throw Error("ownership_lost");
        StoreOwnedInstallFiles.RefuseRunning(root);
        var accepted = new Dictionary<string, StoreOwnedInstallFile>(StringComparer.OrdinalIgnoreCase);
        if (receipt.Manifest is { } manifest)
        {
            ValidateManifest(manifest, root);
            foreach (var item in manifest.Files)
            {
                accepted.Add(StoreOwnedInstallFiles.PathFor(root, item.RelativePath), item);
                foreach (var suffix in manifest.PartialSuffixes) accepted.Add(StoreOwnedInstallFiles.PathFor(root, item.RelativePath + suffix), item);
            }
        }
        using var directories = new StoreOwnedInstallFiles.DirectoryPins();
        var held = new List<FileStream>();
        var ownedDirectories = new List<(string Path,string Identity)>();
        try
        {
            var queue = new Queue<string>(); queue.Enqueue(root); int visited = 0;
            while (queue.TryDequeue(out var directory))
            {
                token.ThrowIfCancellationRequested();
                if (++visited > 65536) throw Error("too_many_files");
                directories.Add(directory);
                ownedDirectories.Add((directory,Identity(directory)));
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    token.ThrowIfCancellationRequested();
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw Error("reparse_point");
                    if ((attributes & FileAttributes.Directory) != 0) { queue.Enqueue(path); continue; }
                    if (StringComparer.OrdinalIgnoreCase.Equals(path, Path.Combine(root, Marker))) continue;
                    if (held.Count >= 100000) throw Error("too_many_files");
                    var stream = Open(path, delete: true); held.Add(stream);
                    if (accepted.TryGetValue(path, out var file))
                    {
                        if (!receipt.WorkerStarted || stream.Length > file.Size) throw Error("file_unverified");
                        // A completed-size file is never excused as a partial.
                        if (stream.Length == file.Size) Verify(stream, file, token);
                    }
                    else if (receipt.Manifest?.StateFile is { } state && StringComparer.OrdinalIgnoreCase.Equals(path, Path.Combine(root, state)))
                        ValidateGogResume(stream, receipt.Manifest, root);
                    else throw Error("foreign_file");
                }
            }
            // No mutations before all paths, identities and contents are known.
            token.ThrowIfCancellationRequested();
            CheckMarker(marker, receipt.JobId);
            if (Identity(root) != receipt.DirectoryIdentity) throw Error("ownership_lost");
            using var authority = commitGuard?.Invoke();
            foreach (var stream in held)
            {
                var disposition = new Disposition { Delete = true };
                if (!SetFileInformationByHandle(stream.SafeFileHandle, 4, ref disposition, 1)) throw Error("file_busy");
            }
        }
        finally { foreach (var stream in held) stream.Dispose(); }
        directories.Dispose();
        // Delete only the exact verified directory objects, deepest first. A
        // newly added/foreign file makes the non-recursive removal fail safely.
        var emptyDirectories=new List<SafeFileHandle>();
        try
        {
            foreach(var directory in ownedDirectories)
            {
                var handle=CreateFile(directory.Path,0x10080,3,IntPtr.Zero,3,0x02200000,IntPtr.Zero);
                if(handle.IsInvalid){handle.Dispose();throw Error("file_busy");}
                emptyDirectories.Add(handle);
                if(!GetFileInformationByHandle(handle,out var info)||(info.Attributes&0x400)!=0||DirectoryIdentity(info)!=directory.Identity)throw Error("ownership_lost");
            }
            using var authority=commitGuard?.Invoke();
            CheckMarker(marker,receipt.JobId);
            for(var i=emptyDirectories.Count-1;i>0;i--){DeleteHandle(emptyDirectories[i]);emptyDirectories[i].Dispose();}
            if(Directory.EnumerateFileSystemEntries(root).Any(path=>!StringComparer.OrdinalIgnoreCase.Equals(path,Path.Combine(root,Marker))))throw Error("foreign_file");
            DeleteHandle(marker.SafeFileHandle);marker.Dispose();
            try{DeleteHandle(emptyDirectories[0]);emptyDirectories[0].Dispose();}
            catch
            {
                // Retain retry ownership if a foreign file raced the final
                // empty-root check. The root handle still prevents replacement.
                using var restored=new FileStream(Path.Combine(root,Marker),FileMode.CreateNew,FileAccess.Write,FileShare.None);
                restored.Write(System.Text.Encoding.ASCII.GetBytes(receipt.JobId.ToString("D")));restored.Flush(true);throw;
            }
        }
        finally{foreach(var directory in emptyDirectories)directory.Dispose();}
    }
    static void DeleteHandle(SafeFileHandle handle){var disposition=new Disposition{Delete=true};if(!SetFileInformationByHandle(handle,4,ref disposition,1))throw Error("file_busy");}
    static string DirectoryIdentity(FileInfo info)=>$"{info.Volume:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}:{info.Created.dwHighDateTime:X8}{info.Created.dwLowDateTime:X8}";
    static void Verify(FileStream stream, StoreOwnedInstallFile file, CancellationToken token)
    {
        if (file.HashAlgorithm != "SHAKE128")
        {
            try { StoreOwnedInstallFiles.VerifyContents(stream, file, token); }
            catch (StoreOperationException) { throw Error("file_modified"); }
            return;
        }
        token.ThrowIfCancellationRequested(); stream.Position = 0;
        if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(Shake128.HashData(stream, 32)), file.Hash)) throw Error("file_modified");
    }
    static void ValidateGogResume(FileStream stream, StorePartialManifest manifest, string root)
    {
        if (stream.Length > 16 * 1024 * 1024) throw Error("state_unverified");
        using var reader = new StreamReader(stream, leaveOpen: true);
        var names = manifest.Files.Select(f => StoreOwnedInstallFiles.PathFor(root, f.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int rows = 0;
        while (reader.ReadLine() is { } line)
        {
            if (++rows > 100000 || line.Length > 32768) throw Error("state_unverified");
            var parts = line.Split(':', 3);
            if (parts.Length != 3 || parts[0].Length is not (32 or 64) || !parts[0].All(Uri.IsHexDigit) ||
                parts[1] is not ("" or "support")) throw Error("state_unverified");
            // gogdl stores support paths relative to its separate gog-support
            // directory (v2 already prefixes productId); no checksum authority
            // is inferred from this resumelog alone.
            var relative = parts[1] == "support" ? "gog-support/" + parts[2] : parts[2];
            if (!names.Contains(StoreOwnedInstallFiles.PathFor(root, relative))) throw Error("state_unverified");
        }
    }
    static void CheckMarker(FileStream marker, Guid id)
    {
        marker.Position = 0;
        if (marker.Length != 36) throw Error("ownership_lost");
        using var reader = new StreamReader(marker, leaveOpen: true);
        if (reader.ReadToEnd() != id.ToString("D")) throw Error("ownership_lost");
    }
    internal static string Identity(string root)
    {
        using var handle = CreateFile(root, 0x80, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x400) != 0) throw Error("ownership_lost");
        return $"{info.Volume:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}:{info.Created.dwHighDateTime:X8}{info.Created.dwLowDateTime:X8}";
    }
    static FileStream Open(string path, bool delete)
    {
        var handle = CreateFile(path, delete ? 0x80010000u : 0x80000000u, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw Error("file_busy"); }
        if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x410) != 0 || info.Links != 1)
        { handle.Dispose(); throw Error("file_identity_invalid"); }
        return new FileStream(handle, FileAccess.Read);
    }
    static StoreOperationException Error(string suffix) => new("partial_cleanup_" + suffix);
    [StructLayout(LayoutKind.Sequential)] struct Disposition { [MarshalAs(UnmanagedType.U1)] public bool Delete; }
    [StructLayout(LayoutKind.Sequential)] struct FileInfo
    { public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written; public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32", SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle file,out FileInfo info);
    [DllImport("kernel32", SetLastError=true)] static extern bool SetFileInformationByHandle(SafeFileHandle file,int kind,ref Disposition info,uint size);
}
