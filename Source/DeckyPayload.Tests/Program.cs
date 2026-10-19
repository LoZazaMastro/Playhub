using System.Security.Cryptography;
using System.Xml.Linq;

var repository = new DirectoryInfo(AppContext.BaseDirectory);
while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Source", "Playhub", "Playhub.csproj"))) repository = repository.Parent;
if (repository is null) throw new DirectoryNotFoundException("Run from the Playhub source checkout.");
string root = repository.FullName;
const string relative = "Assets/DeckyRuntime/Playhub-DeckyHost.exe";
string Normalize(string value) => value.Replace('\\', '/');
int checks = 0;
void Check(bool value, string description) { if (!value) throw new Exception(description); checks++; Console.WriteLine("PASS " + description); }
var project = XDocument.Load(Path.Combine(root, "Source", "Playhub", "Playhub.csproj"));
var target = project.Descendants("Target").Single(element => (string?)element.Attribute("Name") == "BuildDeckyHost");
var contents = target.Descendants("Content").ToArray();
Check(contents.Length == 1 && Normalize((string?)contents[0].Attribute("Include") ?? "").EndsWith("/publish/Playhub-DeckyHost.exe") &&
    !((string?)contents[0].Attribute("Include") ?? "").Contains('*'), "Decky publish includes one exact executable, without wildcard metadata batching");
Check(Normalize((string?)contents[0].Attribute("Link") ?? "") == relative && Normalize((string?)contents[0].Attribute("TargetPath") ?? "") == relative &&
    (string?)contents[0].Attribute("CopyToOutputDirectory") == "PreserveNewest" && (string?)contents[0].Attribute("CopyToPublishDirectory") == "PreserveNewest",
    "Build and publish keep the exact Assets/DeckyRuntime host destination");
var hostProject = XDocument.Load(Path.Combine(root, "Source", "Playhub.DeckyHost", "Playhub.DeckyHost.csproj"));
Check(hostProject.Descendants("SelfContained").Single().Value == "true" && hostProject.Descendants("PublishSingleFile").Single().Value == "true" &&
    hostProject.Descendants("IncludeNativeLibrariesForSelfExtract").Single().Value == "false", "Resident host packaging does not request temporary native extraction");
var script = File.ReadAllText(Path.Combine(root, "Source", "PlayhubSetup", "build-update-preview.ps1"));
Check(script.Contains("Missing 2.5 runtime payload entry:") && script.Contains("@('" + relative + "')") &&
    script.Contains("$_.FullName.StartsWith('Assets/DeckyRuntime/') -and $_.FullName -ne '" + relative + "'"), "Update packaging requires the 2.5 host and rejects unrelated runtime files");
Check(script.Contains("'" + relative + "' = Join-Path") && script.Contains("foreach ($entryPath in $dataAssets.Keys)") &&
    script.Contains("Get-FileHash -LiteralPath $dataAssets[$entryPath] -Algorithm SHA256") && script.Contains("Outdated payload data: $entryPath"),
    "Update packaging pins host bytes to the dedicated publish source");
if (args.Length > 0)
{
    if (args.Length != 2 || args[0] != "--app-dir") throw new ArgumentException("Usage: --app-dir <actual published app directory>");
    var runtime = Path.Combine(Path.GetFullPath(args[1]), "Assets", "DeckyRuntime");
    var files = Directory.GetFiles(runtime, "*", SearchOption.AllDirectories);
    var packaged = Path.Combine(runtime, "Playhub-DeckyHost.exe");
    Check(files.Length == 1 && files[0] == packaged, "Actual published app has exactly one Decky runtime file");
    var source = Path.Combine(root, "Source", "Playhub.DeckyHost", "bin", "Release", "net8.0-windows", "win-x64", "publish", "Playhub-DeckyHost.exe");
    using var packageBytes = File.OpenRead(packaged);
    using var sourceBytes = File.OpenRead(source);
    Check(SHA256.HashData(packageBytes).SequenceEqual(SHA256.HashData(sourceBytes)), "Actual packaged host matches latest dedicated publish bytes");
}
Console.WriteLine($"Decky 2.5 payload: {checks} PASS");
