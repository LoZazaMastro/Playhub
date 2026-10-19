using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Playhub.Services;
using Playhub.Shared;

int count = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    Console.WriteLine("PASS " + description); count++;
}
if (args.Length == 4 && args[0] == "--inspect")
{
    string app = Path.GetFullPath(args[1]);
    string source = Path.Combine(app, "Assets", "GamingModeDeckyPlugin", "gaming-mode");
    string agent = Path.Combine(app, "Plugins", "Gaming Mode", "gaming-mode-win-x64", "GamingMode.exe");
    var timer = Stopwatch.StartNew();
    Check(!GamingModePayloadSync.NeedsAgentUpdate(agent, args[3]), "actual newer installed agent is retained without an installation attempt");
    Console.WriteLine("Agent check ms: " + timer.Elapsed.TotalMilliseconds.ToString("F2"));
    timer.Restart();
    Check(!GamingModePayloadSync.NeedsPluginUpdate(app, source, args[2]), "actual migrated installed plugin does not trigger a repeat update");
    Console.WriteLine("Plugin check ms: " + timer.Elapsed.TotalMilliseconds.ToString("F2"));
    int skipped = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Count() - GamingModePayloadSync.RequiredPluginFiles(app, source).Count();
    Check(skipped > 0, "actual retired source photos are omitted without modifying source or destination");
    Console.WriteLine("Retired source photos omitted: " + skipped);
    return;
}
string root = Path.Combine(Path.GetTempPath(), "PlayhubSyncChecks", Guid.NewGuid().ToString("N"));
try
{
    string source = Path.Combine(root, "Assets", "GamingModeDeckyPlugin", "gaming-mode");
    string assets = Path.Combine(source, "dist", "assets");
    string metadata = Path.Combine(root, "Assets", "EditorialStreaming");
    string destination = Path.Combine(root, "installed", "gaming-mode");
    Directory.CreateDirectory(assets); Directory.CreateDirectory(metadata); Directory.CreateDirectory(destination);
    string photoHash = Convert.ToHexString(SHA256.HashData("original"u8.ToArray()));
    File.WriteAllText(Path.Combine(metadata, "manifest.json"), JsonSerializer.Serialize(new { files = new Dictionary<string, object> { ["photo"] = new { sha256 = photoHash } } }));
    File.WriteAllText(Path.Combine(metadata, "legacy-photos.json"), JsonSerializer.Serialize(new { files = new[] {
        new { name = "photo.jpg", sha256 = photoHash }, new { name = "cover.jpg", sha256 = photoHash },
        new { name = "../outside.jpg", sha256 = photoHash }, new { name = "unapproved.jpg", sha256 = new string('0', 64) } } }));
    File.WriteAllText(Path.Combine(assets, "photo.jpg"), "original");
    File.WriteAllText(Path.Combine(assets, "cover.jpg"), "cover");
    File.WriteAllText(Path.Combine(assets, "unapproved.jpg"), "unknown");
    Check(EditorialPhotoMigration.GetRetiredAssetNames(root, source).Count == 0, "legacy metadata alone cannot filter source files");
    File.WriteAllText(Path.Combine(source, "dist", "index.js"), "assets/on-this-day/v1/");
    var files = GamingModePayloadSync.RequiredPluginFiles(root, source).ToArray();
    Check(!files.Contains(Path.Combine(assets, "photo.jpg")), "approved retired photo is not copied");
    Check(files.Contains(Path.Combine(assets, "cover.jpg")), "cover still belongs to required payload");
    Check(files.Contains(Path.Combine(assets, "unapproved.jpg")), "unapproved photo still belongs to required payload");
    foreach (string file in files)
    {
        string target = Path.Combine(destination, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!); BundledExecutableCopy.Copy(file, target);
    }
    Check(!GamingModePayloadSync.NeedsPluginUpdate(root, source, destination), "migration-aware installed payload is stable on next startup");
    Check(File.Exists(Path.Combine(assets, "photo.jpg")) && !File.Exists(Path.Combine(destination, "dist", "assets", "photo.jpg")), "filter leaves source photo untouched and destination absent");
    File.WriteAllText(Path.Combine(destination, "dist", "assets", "photo.jpg"), "user edit");
    Check(EditorialPhotoMigration.PrunePlugin(root, destination) == 0 && File.ReadAllText(Path.Combine(destination, "dist", "assets", "photo.jpg")) == "user edit", "migration continues preserving modified installed photos");
    File.Delete(Path.Combine(destination, "dist", "assets", "cover.jpg"));
    Check(GamingModePayloadSync.NeedsPluginUpdate(root, source, destination), "missing cover still requires repair");
    string original = Path.Combine(root, "source.exe"), installed = Path.Combine(root, "installed.exe");
    File.WriteAllText(original, "same bytes"); File.Copy(original, installed);
    File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddDays(2));
    DateTime before = File.GetLastWriteTimeUtc(installed);
    using (File.Open(installed, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        Check(BundledExecutableCopy.IsCurrent(original, installed), "identical executable with older installed timestamp is current");
        BundledExecutableCopy.Copy(original, installed);
        Check(File.GetLastWriteTimeUtc(installed) == before, "locked identical executable requires no timestamp write or replacement");
    }
    Check(BundledExecutableCopy.IsCurrent(original, installed), "unchanged helper cannot cause a timestamp repair loop");
    string document = Path.Combine(root, "source.json"), installedDocument = Path.Combine(root, "installed.json");
    File.WriteAllText(document, "source"); File.WriteAllText(installedDocument, "broken");
    File.SetLastWriteTimeUtc(installedDocument, File.GetLastWriteTimeUtc(document));
    BundledExecutableCopy.Copy(document, installedDocument);
    Check(File.ReadAllText(installedDocument) == "source", "explicit copy repairs altered content even when length and timestamp match");
    File.WriteAllText(installed, "other byte");
    Check(!BundledExecutableCopy.IsCurrent(original, installed), "equal-size differing content is detected despite timestamp differences");
    File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddDays(-2));
    Check(!GamingModePayloadSync.NeedsAgentUpdate(original, installed, new Version(2,1,0,0), new Version(2,1,0,0)), "newer installed build of same valid version is retained");
    Check(!GamingModePayloadSync.NeedsAgentUpdate(original, installed, new Version(2,1,0,0), new Version(2,2,0,0)), "newer installed version is never automatically downgraded");
    Check(GamingModePayloadSync.NeedsAgentUpdate(original, installed, new Version(2,2,0,0), new Version(2,1,0,0)), "newer bundled version still updates even when timestamp is older");
    Check(GamingModePayloadSync.NeedsAgentUpdate(original, installed, null, null), "unversioned differing agent is not falsely accepted as a newer build");
    File.Delete(Path.Combine(metadata, "manifest.json"));
    Check(GamingModePayloadSync.RequiredPluginFiles(root, source).Contains(Path.Combine(assets, "photo.jpg")), "missing remote manifest retains all source photos");
    Console.WriteLine($"{count}/{count} startup synchronization checks passed.");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
