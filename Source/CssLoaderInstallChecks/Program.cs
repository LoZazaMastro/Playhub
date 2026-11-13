using System.IO.Compression;
using System.Security.Cryptography;
using Playhub.Services;

var scratch = Path.Combine(Path.GetTempPath(), "Playhub-CssLoader-InstallChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
AppPaths.DownloadsRoot = Path.Combine(scratch, "downloads");
var plugins = Path.Combine(scratch, "plugins");
Directory.CreateDirectory(plugins);
var service = new CssLoaderInstallService();
int checks = 0;
void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
var package = Path.Combine(AppContext.BaseDirectory, "Plugins", "CSS Loader", "CSS-Loader-Startup-2.1.5.zip");
var packageHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package)));
Check(packageHash.Equals("AB4FABC15799F3151E257DC260135163D9A88ABBCED6439837D744011FF53463", StringComparison.Ordinal), "Pinned setup package");
var initial = await service.InstallLatestAsync(plugins);
Check(initial.Success && initial.Version == "2.1.5", "Offline bundled installation");
Check(service.GetStatus(plugins).Installed && !service.HasBundledUpdate(plugins), "Installed version and update state");
var installed = Path.Combine(plugins, "SDH-CssLoader");
using (var archive = ZipFile.OpenRead(package)) {
    foreach (var entry in archive.Entries.Where(value => !string.IsNullOrEmpty(value.Name))) {
        var path = Path.GetFullPath(Path.Combine(plugins, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
        Check(path.StartsWith(Path.GetFullPath(installed) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Single expected plugin payload");
        using var input = entry.Open();
        Check(SHA256.HashData(input).SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Installed payload matches packaged source");
    }
}
File.WriteAllText(Path.Combine(installed, "package.json"), "{\"version\":\"2.1.3\"}");
Check(service.HasBundledUpdate(plugins), "Existing upstream 2.1.3 offers the Windows update");
File.WriteAllText(Path.Combine(installed, "package.json"), "{\"version\":\"9.0.0\"}");
var newer = await service.InstallLatestAsync(plugins);
Check(newer.Success && newer.Version == "9.0.0", "A newer user installation is retained");
Check(File.ReadAllText(Path.Combine(installed, "package.json")).Contains("9.0.0"), "No version downgrade");
// Corrupt a copy in this checks executable's output, never the repository ZIP.
var original = File.ReadAllBytes(package);
try {
    original[^1] ^= 1;
    File.WriteAllBytes(package, original);
    File.WriteAllText(Path.Combine(installed, "package.json"), "{\"version\":\"2.1.3\"}");
    var before = File.ReadAllBytes(Path.Combine(installed, "css_browserhook.py"));
    var corrupt = await service.InstallLatestAsync(plugins);
    Check(!corrupt.Success, "Tampered bundle rejected before replacing installation");
    Check(before.SequenceEqual(File.ReadAllBytes(Path.Combine(installed, "css_browserhook.py"))), "Existing plugin retained on checksum failure");
} finally { original[^1] ^= 1; File.WriteAllBytes(package, original); }
Console.WriteLine($"PASS {checks} CSS Loader offline install checks. Scratch: {scratch}");

namespace Playhub.Services
{
    internal static class AppPaths { internal static string DownloadsRoot { get; set; } = ""; }
    internal static class Diag { internal static void Crash(string _, object __) { } }
}
