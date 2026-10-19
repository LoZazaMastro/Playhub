namespace Playhub.DeckyHost;

internal static class DeckyRuntimeDirectory
{
    private const string Marker = ".playhub-decky-runtime";
    private const string Version = "Playhub Decky extraction v1\n";
    internal static string BaseDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playhub", "DeckyRuntime");

    internal static string Create(string loader)
    {
        Directory.CreateDirectory(BaseDirectory);
        if (!ParentsAreRegular(BaseDirectory)) throw new IOException("Decky runtime path contains a reparse point.");
        string path = Path.Combine(BaseDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, Marker), Version + Path.GetFullPath(loader));
        return path;
    }

    internal static void CleanupAbandoned(string loader)
    {
        if (!Directory.Exists(BaseDirectory) || DeckyProcessTree.HasLiveLoader(loader)) return;
        foreach (string path in Directory.EnumerateDirectories(BaseDirectory).Take(128)) TryCleanup(path, loader);
    }

    internal static bool TryCleanup(string path, string loader)
    {
        try
        {
            path = Path.GetFullPath(path);
            if (!Path.GetDirectoryName(path)!.Equals(Path.GetFullPath(BaseDirectory), StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Path.GetFileName(path), "N", out _) || !ParentsAreRegular(path)
                || File.ReadAllText(Path.Combine(path, Marker)) != Version + Path.GetFullPath(loader)
                || DeckyProcessTree.HasLiveLoader(loader)) return false;
            // Never descend into links, junctions or unrecognised container data.
            // Probe all files exclusively before deleting anything: a live external
            // child holding an extracted DLL leaves the entire container intact.
            var files = new List<string>();
            var pending = new Stack<string>(); pending.Push(path);
            int entries = 0;
            while (pending.TryPop(out string? current))
                foreach (string entry in Directory.EnumerateFileSystemEntries(current))
                {
                    if (++entries > 4096) return false;
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry); else files.Add(entry);
                }
            var probes = new List<FileStream>();
            try { foreach (string file in files) probes.Add(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)); }
            finally { foreach (var probe in probes) probe.Dispose(); }
            Directory.Delete(path, true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool ParentsAreRegular(string path)
    {
        for (DirectoryInfo? folder = new(path); folder is not null; folder = folder.Parent)
            if ((folder.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }
}
