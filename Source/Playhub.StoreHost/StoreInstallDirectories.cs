// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
namespace Playhub.StoreLibrary;

internal sealed record StoreInstallDirectory(string Name, string Path);
internal sealed record StoreInstallDirectoryListing(string? Path, string? ParentPath,
    IReadOnlyList<StoreInstallLocation> Roots, IReadOnlyList<StoreInstallDirectory> Directories, bool Truncated);

internal static class StoreInstallDirectories
{
    internal static StoreInstallDirectoryListing Read(string? path, IReadOnlyList<StoreInstallLocation> roots, Func<string,IEnumerable<string>>? enumerate = null)
    {
        if (path is null) return new(null, null, roots, [], false);
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32767 || path.Trim() != path || !System.IO.Path.IsPathFullyQualified(path)
            || path.StartsWith("\\\\", StringComparison.Ordinal) || path.Any(char.IsControl)) throw new StoreOperationException("invalid_directory");
        try
        {
            string full = System.IO.Path.GetFullPath(path), root = System.IO.Path.GetPathRoot(full)!;
            if (!roots.Any(r => r.IsReady && string.Equals(r.RootPath, root, StringComparison.OrdinalIgnoreCase)))
                throw new StoreOperationException("directory_unavailable");
            var ancestors=new List<string>();
            for (string? ancestor = full; ancestor is not null; ancestor = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(ancestor)))
            { if(ancestors.Count>=256)throw new StoreOperationException("invalid_directory");ancestors.Add(ancestor); }
            // Check every reachable ancestor before enumerating; a missing
            // suggested subfolder grants no permission to follow a junction.
            foreach(var ancestor in ancestors.AsEnumerable().Reverse())
            {
                FileAttributes attributes;
                try{attributes=File.GetAttributes(ancestor);}
                catch(Exception missing)when(missing is DirectoryNotFoundException or FileNotFoundException){break;}
                if((attributes&FileAttributes.ReparsePoint)!=0)throw new StoreOperationException("directory_reparse_point");
                if((attributes&FileAttributes.Directory)==0)throw new StoreOperationException("directory_unavailable");
            }
            enumerate??=directory=>Directory.EnumerateDirectories(directory,"*",new EnumerationOptions
            { RecurseSubdirectories=false,IgnoreInaccessible=false,AttributesToSkip=FileAttributes.ReparsePoint });
            for(int attempt=0;attempt<256;attempt++)
            {
                try
                {
                    var directories=enumerate(full).Take(513).Select(value=>new StoreInstallDirectory(System.IO.Path.GetFileName(value),value)).ToArray();
                    bool truncated=directories.Length>512;
                    return new(full,string.Equals(full.TrimEnd('\\'),root.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)?null:System.IO.Path.GetDirectoryName(full),
                        roots,directories.Take(512).OrderBy(d=>d.Name,StringComparer.CurrentCultureIgnoreCase).ToArray(),truncated);
                }
                catch(DirectoryNotFoundException)
                {
                    // New suggested library folders need not exist yet. Only
                    // this exact error may move the browser to a parent.
                    string? parent=System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(full));
                    if(parent is null)return new(null,null,roots,[],false);
                    full=parent;
                }
            }
            throw new StoreOperationException("directory_unavailable");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new StoreOperationException("directory_unavailable"); }
    }
}

public sealed partial class StoreQueueEngine
{
    internal StoreInstallDirectoryListing GetInstallDirectories(string? path) => StoreInstallDirectories.Read(path, files.Locations());
    internal async Task<StoreInstallPreparation> PrepareInstallParentAsync(StoreGameKey key, long accountRevision, string parent, CancellationToken token,string? language=null)
    {
        var current = await GetSnapshotAsync(token);
        var game = current.Games.SingleOrDefault(g => g.Key == key && g.AccountRevision == accountRevision) ?? throw new StoreOperationException("account_or_catalog_changed");
        string final;
        try { final = StoreInstallDestination.FromLibraryFolder(parent, game.Title); }
        catch (ArgumentException) { throw new StoreOperationException("invalid_destination"); }
        return await PrepareInstallWithLanguageAsync(key, accountRevision, final, token,language);
    }
}
