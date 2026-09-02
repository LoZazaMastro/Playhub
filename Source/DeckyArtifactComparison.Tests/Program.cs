using System.IO.Compression;
using System.Text;
using Playhub.Services;

var root = Path.Combine(Path.GetTempPath(), "Playhub-Decky-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
try
{
    var services = Path.Combine(root, "services");
    Directory.CreateDirectory(services);
    File.WriteAllText(Path.Combine(services, "PluginLoader.exe"), "same-binary");
    Directory.CreateDirectory(Path.Combine(services, "data"));
    File.WriteAllText(Path.Combine(services, "data", "config.json"), "{}");
    var normal = Zip(("PluginLoader.exe", "same-binary"), ("data/config.json", "{}"));
    Check("identical", normal, DeckyArtifactMatch.Identical);
    Check("same-size changed binary", Zip(("PluginLoader.exe", "next-binary")), DeckyArtifactMatch.Different);
    Check("changed supporting file", Zip(("PluginLoader.exe", "same-binary"), ("data/config.json", "[]")), DeckyArtifactMatch.Different);
    Check("missing file", Zip(("PluginLoader.exe", "same-binary"), ("missing.dll", "new")), DeckyArtifactMatch.Different);
    Check("case-insensitive paths", Zip(("PLUGINLOADER.EXE", "same-binary"), ("DATA/CONFIG.JSON", "{}")), DeckyArtifactMatch.Identical);
    Check("backslash paths", Zip(("PluginLoader.exe", "same-binary"), ("data\\config.json", "{}")), DeckyArtifactMatch.Identical);
    Check("empty archive", Zip(), DeckyArtifactMatch.Invalid);
    Check("invalid archive", Encoding.UTF8.GetBytes("not a zip"), DeckyArtifactMatch.Invalid);
    Check("truncated archive", normal[..(normal.Length / 2)], DeckyArtifactMatch.Invalid);
    Check("directory only", Zip(("data/", "")), DeckyArtifactMatch.Invalid);
    Check("no loader", Zip(("data/config.json", "{}")), DeckyArtifactMatch.Invalid);
    Check("empty loader", Zip(("PluginLoader.exe", "")), DeckyArtifactMatch.Invalid);
    Check("case duplicate", Zip(("PluginLoader.exe", "same-binary"), ("PLUGINLOADER.EXE", "same-binary")), DeckyArtifactMatch.Invalid);
    Check("file-directory conflict", Zip(("PluginLoader.exe", "same-binary"), ("data", "file"), ("data/child", "x")), DeckyArtifactMatch.Invalid);
    foreach (var path in new[] { "../outside", "..\\outside", "/absolute", "C:/outside", "C:relative", "//server/share", "data/../../outside", "data//file", "data/./file", "data/file:stream", "data/file.", "data/file ", "NUL.txt", "COM1", "data/?file" })
        Check("reject " + path, Zip(("PluginLoader.exe", "same-binary"), (path, "x")), DeckyArtifactMatch.Invalid);
    Check("unix symlink", Zip(("PluginLoader.exe", "same-binary"), ("symlink", "outside")), DeckyArtifactMatch.Invalid, symlink: true);
    File.WriteAllText(Path.Combine(services, "unrelated-user-file"), "preserve");
    Check("unrelated files ignored", normal, DeckyArtifactMatch.Identical);
    var before = Directory.GetFiles(services, "*", SearchOption.AllDirectories)
        .ToDictionary(p => p, p => (File.ReadAllText(p), File.GetLastWriteTimeUtc(p)));
    Check("read-only repeat", normal, DeckyArtifactMatch.Identical);
    if (before.Any(p => p.Value != (File.ReadAllText(p.Key), File.GetLastWriteTimeUtc(p.Key))))
        throw new Exception("Comparison modified fixture files.");
    File.Delete(Path.Combine(services, "PluginLoader.exe"));
    Check("missing loader", normal, DeckyArtifactMatch.Different);
    if (DeckyArtifactComparison.Compare(normal, Path.Combine(root, "fresh")) != DeckyArtifactMatch.Different)
        throw new Exception("Fresh installation must differ.");
    passed++;
    Console.WriteLine($"PASS: {passed} focused checks; only temporary fixtures, no installer or app dependencies.");

    void Check(string name, byte[] bytes, DeckyArtifactMatch expected, bool symlink = false)
    {
        if (symlink)
        {
            using var stream = new MemoryStream();
            stream.Write(bytes);
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
                zip.GetEntry("symlink")!.ExternalAttributes = unchecked((int)0xA0000000);
            bytes = stream.ToArray();
        }
        var actual = DeckyArtifactComparison.Compare(bytes, services);
        if (actual != expected) throw new Exception($"{name}: expected {expected}, got {actual}");
        passed++;
    }
}
finally
{
    Directory.Delete(root, recursive: true);
}

static byte[] Zip(params (string Name, string Content)[] entries)
{
    using var stream = new MemoryStream();
    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var (name, content) in entries)
        {
            using var output = zip.CreateEntry(name).Open();
            output.Write(Encoding.UTF8.GetBytes(content));
        }
    }
    return stream.ToArray();
}
