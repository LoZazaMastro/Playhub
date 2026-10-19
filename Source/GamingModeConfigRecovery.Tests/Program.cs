using Playhub.Services;
using System.Text;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "Playhub-Recovery-Test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; }
var path = Path.Combine(root, "config.json");
var backup = path + ".bak";
var diagnostics = Path.Combine(root, "diagnostics", "GamingMode");
var invalid = new byte[] { 0xef, 0xbb, 0xbf, 0x7b, 0x22, 0xff, 0x00 };
var valid = Encoding.UTF8.GetBytes("  {\"gaming\": {\"splash\": {\"maxVisibleMs\": 5000, \"logoPath\": \"custom.png\"}}}\n");
var restored = "{\"gaming\":{\"splash\":{\"maxVisibleMs\":5000,\"logoPath\":\"custom.png\"}}}";
try
{
    await File.WriteAllBytesAsync(path, invalid);
    await File.WriteAllBytesAsync(backup, valid);
    await GamingModeConfigRecovery.RestoreAsync(path, restored, diagnostics);
    Check((await File.ReadAllBytesAsync(backup)).SequenceEqual(valid), "Valid backup remains byte-identical after corrupt primary recovery");
    Check((await File.ReadAllBytesAsync(Directory.GetFiles(diagnostics).Single())).SequenceEqual(invalid), "Corrupt primary archived byte-for-byte outside config directory");
    using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
        Check(json.RootElement.GetProperty("gaming").GetProperty("splash").GetProperty("logoPath").GetString() == "custom.png", "Restored primary contains selected custom splash");
    Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Successful restore leaves no temporary files");

    File.Delete(path);
    await GamingModeConfigRecovery.RestoreAsync(path, restored, diagnostics);
    Check((await File.ReadAllBytesAsync(backup)).SequenceEqual(valid), "Missing primary recovery leaves valid backup intact");
    Check(Directory.GetFiles(diagnostics).Length == 1, "Missing primary creates no corrupt diagnostic file");
    Check(await File.ReadAllTextAsync(path) == restored, "Missing primary replaced with complete valid configuration");

    await File.WriteAllBytesAsync(path, invalid);
    bool failed = false;
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        try { await GamingModeConfigRecovery.RestoreAsync(path, restored, diagnostics); }
        catch (IOException) { failed = true; }
    }
    Check(failed, "Locked primary fails safely rather than overwriting backup");
    Check((await File.ReadAllBytesAsync(path)).SequenceEqual(invalid), "Failed restore preserves corrupt primary");
    Check((await File.ReadAllBytesAsync(backup)).SequenceEqual(valid), "Failed restore preserves valid backup");
    Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Failed restore removes temporary file");

    using (var backupReader = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.Read))
        await GamingModeConfigRecovery.RestoreAsync(path, restored, diagnostics);
    Check((await File.ReadAllBytesAsync(backup)).SequenceEqual(valid), "Recovery works while valid backup is read-locked, proving it is never replaced");
    Check(await File.ReadAllTextAsync(path) == restored, "Read-locked backup does not prevent complete primary restoration");
    Check(Directory.GetFiles(diagnostics).Length == 2, "Each corrupt primary archived independently without overwriting earlier evidence");
    Console.WriteLine($"Configuration recovery: {passed} filesystem checks passed; installed settings untouched.");
}
finally
{
    Directory.Delete(root, recursive: true);
}
