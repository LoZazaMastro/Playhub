using Playhub.Shared;
using System.Security.Cryptography;
using System.Text.Json;
if (args.Length == 2 && args[0] == "--prune")
{
    string bundle = Path.Combine(args[1], "Assets/GamingModeDeckyPlugin/gaming-mode/dist/index.js");
    if (!File.ReadAllText(bundle).Contains("assets/on-this-day/v1/", StringComparison.Ordinal))
        throw new InvalidOperationException("The payload does not contain the streaming plugin build.");
    foreach (string name in new[] { "manifest.json", "legacy-photos.json" })
        using (JsonDocument.Parse(File.ReadAllText(Path.Combine(args[1], "Assets/EditorialStreaming", name)))) { }
    Console.WriteLine($"Removed {EditorialPhotoMigration.Prune(args[1], Console.WriteLine)} verified legacy editorial photos from payload.");
    return;
}
string root = Path.Combine(Path.GetTempPath(), "PlayhubEditorialChecks", Guid.NewGuid().ToString("N"));
string assets = Path.Combine(root, EditorialPhotoMigration.AssetDirectory);
string metadata = Path.Combine(root, "Assets/EditorialStreaming");
Directory.CreateDirectory(assets); Directory.CreateDirectory(metadata);
string hash = Convert.ToHexString(SHA256.HashData("original"u8.ToArray()));
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
void Put(string name, string content) => File.WriteAllText(Path.Combine(assets, name), content);
File.WriteAllText(Path.Combine(metadata, "manifest.json"), JsonSerializer.Serialize(new { files = new Dictionary<string, object> { ["photo.jpg"] = new { sha256 = hash } } }));
var entries = new[] { "photo.jpg", "modified.jpg", "cover.jpg", "../outside.jpg", "unknown.jpg", "linked.jpg" }
    .Select(name => new { name, sha256 = name == "unknown.jpg" ? new string('0', 64) : hash });
File.WriteAllText(Path.Combine(metadata, "legacy-photos.json"), JsonSerializer.Serialize(new { files = entries }));
Put("photo.jpg", "original"); Put("modified.jpg", "user edit"); Put("cover.jpg", "original"); Put("unknown.jpg", "original");
File.WriteAllText(Path.Combine(root, "outside.jpg"), "original");
Assert(EditorialPhotoMigration.Prune(root) == 0, "no deletion without streaming bundle");
File.WriteAllText(Path.Combine(assets, "../index.js"), "assets/on-this-day/v1/");
Assert(EditorialPhotoMigration.Prune(root) == 1, "only exact legacy hash removed");
Assert(File.Exists(Path.Combine(assets, "modified.jpg")), "user-modified photo retained");
Assert(File.Exists(Path.Combine(assets, "cover.jpg")), "cover retained");
Assert(File.Exists(Path.Combine(assets, "unknown.jpg")), "unapproved hash retained");
Assert(File.Exists(Path.Combine(root, "outside.jpg")), "path traversal retained");
Assert(EditorialPhotoMigration.Prune(root) == 0, "migration idempotent");
try
{
    File.CreateSymbolicLink(Path.Combine(assets, "linked.jpg"), Path.Combine(root, "outside.jpg"));
    Assert(EditorialPhotoMigration.Prune(root) == 0 && File.Exists(Path.Combine(assets, "linked.jpg")), "reparse file retained");
}
catch (UnauthorizedAccessException) { Console.WriteLine("SKIP symlink creation unavailable; Windows privilege required."); }
File.Delete(Path.Combine(metadata, "manifest.json"));
Put("photo.jpg", "original");
Assert(EditorialPhotoMigration.Prune(root) == 0 && File.Exists(Path.Combine(assets, "photo.jpg")), "missing manifest fails safe");
Console.WriteLine("Fixture retained: " + root);
