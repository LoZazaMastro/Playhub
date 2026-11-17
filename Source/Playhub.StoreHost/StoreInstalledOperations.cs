// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Playhub.StoreLibrary;

public sealed record StoreOwnedInstallChunk(long Size, string Hash);
public sealed record StoreOwnedInstallFile(string RelativePath, long Size, string? HashAlgorithm, string? Hash,
    IReadOnlyList<StoreOwnedInstallChunk>? Chunks = null);
public sealed record StoreOwnedUninstallPlan(StoreGameKey Key, string InstallPath, string GameExecutable,
    Guid OwnerJob, string ManifestHash, IReadOnlyList<StoreOwnedInstallFile> Files);
public interface IStoreInstalledGameProvider
{
    Task<bool> CanUninstallAsync(string id, string installPath, Guid ownerJob, CancellationToken token);
    Task<StoreOwnedUninstallPlan> PrepareUninstallAsync(string id, string installPath, Guid ownerJob, CancellationToken token);
    Task UninstallAsync(StoreOwnedUninstallPlan plan, CancellationToken token);
}
internal interface IStoreLegacyInstalledGameProvider
{
    Task<StoreOwnedUninstallPlan?> VerifyLegacyInstallationAsync(string id, string installPath, CancellationToken token);
}

// No recursive directory deletion. Manifest paths and retained file handles are
// the deletion boundary; unrelated files, saves and user configuration remain.
internal static class StoreOwnedInstallFiles
{
    internal static string PathFor(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 2048 || Path.IsPathRooted(relative) ||
            relative.Any(c => c < 32 || c == ':') || relative.Split('/', '\\').Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new StoreOperationException("uninstall_manifest_invalid");
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new StoreOperationException("uninstall_manifest_invalid");
        return full;
    }

    internal static void ValidatePlan(StoreOwnedUninstallPlan plan)
    {
        StoreCliProvider.ValidateId(plan.Key.Id);
        var canonical = new StoreInstallFileSystem().CanonicalDestination(plan.InstallPath);
        if (plan.OwnerJob == Guid.Empty || !StringComparer.OrdinalIgnoreCase.Equals(canonical, plan.InstallPath) ||
            !Directory.Exists(canonical) || File.ReadAllText(Path.Combine(canonical, ".playhub-install-owner")) != plan.OwnerJob.ToString("D") ||
            plan.Key.Id is "." or ".." || plan.Files.Count is < 1 or > 100000 || plan.ManifestHash.Length != 64 || !plan.ManifestHash.All(Uri.IsHexDigit))
            throw new StoreOperationException("installation_ownership_lost");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Files)
        {
            var path = PathFor(canonical, item.RelativePath);
            if (!paths.Add(path) || item.Size < 0 || StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(path), ".playhub-install-owner")) throw new StoreOperationException("uninstall_manifest_invalid");
            ValidateChecksum(item);
            RejectReparsePath(canonical, path);
        }
        if (!paths.Contains(plan.GameExecutable)) throw new StoreOperationException("uninstall_executable_not_in_manifest");
    }
    internal static void ValidateChecksum(StoreOwnedInstallFile item)
    {
        if (item.Size < 0 || item.HashAlgorithm is not (null or "SHA1" or "SHA256" or "MD5") ||
            item.HashAlgorithm != null && (item.Hash == null || item.Hash.Length != (item.HashAlgorithm == "SHA1" ? 40 : item.HashAlgorithm == "MD5" ? 32 : 64) || !item.Hash.All(Uri.IsHexDigit)))
            throw new StoreOperationException("uninstall_manifest_invalid");
        if (item.Chunks is not { } chunks) return;
        long size = 0;
        if (chunks.Count is < 1 or > 100000 || item.HashAlgorithm != null || item.Hash != null) throw new StoreOperationException("uninstall_manifest_invalid");
        foreach (var chunk in chunks)
        {
            if (chunk.Size is < 1 or > 1073741824 || chunk.Hash.Length != 32 || !chunk.Hash.All(Uri.IsHexDigit) || size > long.MaxValue - chunk.Size)
                throw new StoreOperationException("uninstall_manifest_invalid");
            size += chunk.Size;
        }
        if (size != item.Size) throw new StoreOperationException("uninstall_manifest_invalid");
    }
    // Shared by installation postprocessing and removal. GOG v2 often supplies
    // only per-chunk MD5, which must not be treated as a whole-file digest.
    internal static void VerifyContents(Stream stream, StoreOwnedInstallFile item, CancellationToken token)
    {
        ValidateChecksum(item);
        if (stream.Length != item.Size) throw new StoreOperationException("installed_files_modified");
        stream.Position = 0;
        var buffer = new byte[131072];
        if (item.Chunks is { } chunks)
        {
            foreach (var chunk in chunks)
            {
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
                long left = chunk.Size;
                while (left > 0)
                {
                    token.ThrowIfCancellationRequested();
                    var count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                    if (count == 0) throw new StoreOperationException("installed_files_modified");
                    digest.AppendData(buffer.AsSpan(0, count)); left -= count;
                }
                if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(digest.GetHashAndReset()), chunk.Hash)) throw new StoreOperationException("installed_files_modified");
            }
        }
        else if (item.HashAlgorithm != null)
        {
            using var digest = IncrementalHash.CreateHash(new HashAlgorithmName(item.HashAlgorithm)); int count;
            while ((count = stream.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); digest.AppendData(buffer.AsSpan(0, count)); }
            if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(digest.GetHashAndReset()), item.Hash)) throw new StoreOperationException("installed_files_modified");
        }
    }
    private static void RejectReparsePath(string root, string path)
    {
        for (var p = path; p != null && p.Length >= root.Length; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new StoreOperationException("destination_reparse_point");
    }
    internal static void RefuseRunning(string root)
    {
        // Exact canonical images, never process-name guesses and never termination.
        foreach (var process in Process.GetProcesses())
        using (process)
        {
            try
            {
                using var handle = OpenProcess(0x1000, false, (uint)process.Id);
                if (handle.IsInvalid) continue;
                var size = 32768; var name = new System.Text.StringBuilder(size);
                if (QueryFullProcessImageName(handle, 0, name, ref size) && name.ToString().StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new StoreOperationException("game_running");
            }
            catch (ArgumentException) { }
        }
    }
    private static SafeFileHandle LockDirectory(string path)
    {
        var handle = CreateFile(path, 0x80, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero); // no DELETE sharing
        if (handle.IsInvalid) { handle.Dispose(); throw new StoreOperationException("uninstall_path_busy"); }
        if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x400) != 0)
        { handle.Dispose(); throw new StoreOperationException("destination_reparse_point"); }
        return handle;
    }
    internal sealed class DirectoryPins : IDisposable
    {
        private readonly List<SafeFileHandle> handles = [];
        private readonly HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        internal void Add(string path)
        {
            if (!paths.Add(path)) return;
            if (paths.Count > 65536) throw new StoreOperationException("gog_support_script_invalid");
            handles.Add(LockDirectory(path));
        }
        public void Dispose() { foreach (var handle in handles) handle.Dispose(); handles.Clear(); paths.Clear(); }
    }
    internal static DirectoryPins PinRoot(string root)
    {
        var pins = new DirectoryPins();
        try { for (var p = root; p != null; p = Path.GetDirectoryName(p)) pins.Add(p); return pins; }
        catch { pins.Dispose(); throw; }
    }
    internal static FileStream OpenReadFile(string path) => OpenFile(path, false);
    internal static void DeleteRegisteredMetadata(string path,string expectedSha256,long maximum,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();using var roots=PinRoot(Path.GetDirectoryName(path)!);
        using var handle=CreateFile(path,0x80010000,1,IntPtr.Zero,3,0x00200000,IntPtr.Zero);
        if(handle.IsInvalid)throw new StoreOperationException("uninstall_file_busy");
        if(!GetFileInformationByHandle(handle,out var info)||(info.Attributes&0x410)!=0)throw new StoreOperationException("destination_reparse_point");
        using var stream=new FileStream(handle,FileAccess.Read);
        if(stream.Length<1||stream.Length>maximum||Convert.ToHexString(SHA256.HashData(stream))!=expectedSha256)throw new StoreOperationException("installation_changed");
        var disposition=new Disposition{Delete=true};
        if(!SetFileInformationByHandle(handle,4,ref disposition,1))throw new StoreOperationException("uninstall_file_busy");
    }
    // Removal owns the provider-declared paths, not a requirement that the
    // user's current assets still match the original download byte for byte.
    // Bind current sizes through retained handles; missing assets need no write.
    internal static IReadOnlyList<StoreOwnedInstallFile> BindRemovalFiles(string root,IEnumerable<StoreOwnedInstallFile> manifest,CancellationToken token)
    {
        using var pins=PinRoot(root);var rows=new List<StoreOwnedInstallFile>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in manifest)
        {
            token.ThrowIfCancellationRequested();string path=PathFor(root,file.RelativePath);
            if(!names.Add(path)||rows.Count>=100000||Path.GetFileName(path).Equals(".playhub-install-owner",StringComparison.OrdinalIgnoreCase))throw new StoreOperationException("uninstall_manifest_invalid");
            RejectReparsePath(root,path);long size=file.Size;
            if(File.Exists(path)){using var stream=OpenReadFile(path);size=stream.Length;}
            rows.Add(new(file.RelativePath,size,null,null));
        }
        return rows;
    }
    internal static FileStream OpenWriteFile(string path) => OpenFile(path, true);
    private static FileStream OpenFile(string path, bool write)
    {
        var handle = CreateFile(path, write ? 0xC0000000u : 0x80000000u, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new StoreOperationException("uninstall_file_busy"); }
        if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x410) != 0)
        { handle.Dispose(); throw new StoreOperationException("destination_reparse_point"); }
        return new FileStream(handle, write ? FileAccess.ReadWrite : FileAccess.Read);
    }
    internal static void Delete(StoreOwnedUninstallPlan plan, CancellationToken token)
    {
        ValidatePlan(plan); RefuseRunning(plan.InstallPath);
        // Retain the complete root chain against replacement before any mutation.
        var roots = new List<SafeFileHandle>();
        try
        {
            for (var p = plan.InstallPath; p != null; p = Path.GetDirectoryName(p)) roots.Add(LockDirectory(p));
            ValidatePlan(plan);
            // Preflight the WHOLE manifest before deleting its first file.
            foreach (var item in plan.Files) { token.ThrowIfCancellationRequested(); CheckFile(plan, item, false, token); }
            RefuseRunning(plan.InstallPath);
            foreach (var item in plan.Files) { token.ThrowIfCancellationRequested(); CheckFile(plan, item, true, token); }
        }
        finally { foreach (var h in roots) h.Dispose(); }
        // Remove empty manifest directories only. Foreign files keep their folder.
        foreach (var directory in plan.Files.Select(f => Path.GetDirectoryName(PathFor(plan.InstallPath, f.RelativePath))!)
            .SelectMany(p => Ancestors(p, plan.InstallPath)).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(p => p.Length))
            try { Directory.Delete(directory, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    internal static void VerifyCompleteInstallation(StoreOwnedUninstallPlan plan, CancellationToken token)
    {
        ValidatePlan(plan); RefuseRunning(plan.InstallPath);
        using var pins = PinRoot(plan.InstallPath);
        using var marker = OpenReadFile(Path.Combine(plan.InstallPath, ".playhub-install-owner"));
        ValidatePlan(plan);
        foreach (var item in plan.Files)
        {
            token.ThrowIfCancellationRequested();
            // A migration cannot use a missing file or a size-only manifest as
            // proof that a legacy provider installation completed successfully.
            if (item.HashAlgorithm is null && item.Chunks is null) throw new StoreOperationException("uninstall_checksum_unavailable");
            var path = PathFor(plan.InstallPath, item.RelativePath);
            var directories = new Stack<string>();
            for (var directory = Path.GetDirectoryName(path)!; directory.Length > plan.InstallPath.Length; directory = Path.GetDirectoryName(directory)!) directories.Push(directory);
            foreach (var directory in directories) pins.Add(directory);
            RejectReparsePath(plan.InstallPath, path);
            using var stream = OpenReadFile(path);
            VerifyContents(stream, item, token);
        }
    }
    private static IEnumerable<string> Ancestors(string path, string root)
    { for (var p = path; p.Length > root.Length; p = Path.GetDirectoryName(p)!) yield return p; }
    private static void CheckFile(StoreOwnedUninstallPlan plan, StoreOwnedInstallFile item, bool delete, CancellationToken token)
    {
        var path = PathFor(plan.InstallPath, item.RelativePath); RejectReparsePath(plan.InstallPath, path);
        if (!File.Exists(path)) return; // An already absent manifest file needs no mutation.
        var directories = new List<SafeFileHandle>();
        try
        {
            for (var p = Path.GetDirectoryName(path)!; p.Length > plan.InstallPath.Length; p = Path.GetDirectoryName(p)!) directories.Add(LockDirectory(p));
            using var handle = CreateFile(path, 0x80010000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero); // READ|DELETE, OPEN_REPARSE_POINT
            if (handle.IsInvalid) throw new StoreOperationException("uninstall_file_busy");
            if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x410) != 0) throw new StoreOperationException("destination_reparse_point");
            using var stream = new FileStream(handle, FileAccess.Read);
            VerifyContents(stream, item, token);
            if (delete)
            {
                var disposition = new Disposition { Delete = true };
                if (!SetFileInformationByHandle(handle, 4, ref disposition, 1)) throw new StoreOperationException("uninstall_file_busy");
            }
        }
        finally { foreach (var h in directories) h.Dispose(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Disposition { [MarshalAs(UnmanagedType.U1)] public bool Delete; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfo
    { public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written; public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] private static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32", SetLastError=true)] private static extern bool GetFileInformationByHandle(SafeFileHandle file,out FileInfo info);
    [DllImport("kernel32", SetLastError=true)] private static extern bool SetFileInformationByHandle(SafeFileHandle file,int kind,ref Disposition info,uint size);
    [DllImport("kernel32")] private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32",CharSet=CharSet.Unicode)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,System.Text.StringBuilder name,ref int size);
}
