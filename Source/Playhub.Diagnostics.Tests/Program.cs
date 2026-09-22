using Playhub.Services;

if (args.Length == 3 && args[0] == "--collect")
{
    using var report = new StreamWriter(args[2]);
    DeckyDiagnostics.Append(report, args[1]);
    return;
}

var root = Path.Combine(Path.GetTempPath(), "playhub-diagnostics-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
void Write(string path, string content)
{
    path = Path.Combine(root, path);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
}
try
{
    Write("plugins/third-party/plugin.json", "{\"name\":\"Independent Plugin\"}");
    Write("plugins/third-party/package.json", "{\"version\":\"7.2.1\"}");
    Write("plugins/without-backend/plugin.json", "{\"name\":\"Frontend only\"}");
    Write("plugins/bad-manifest/plugin.json", "invalid json");
    Write("logs/third-party/old.log", "2026-09-09 10:01:00 [ERROR] first session\nTraceback\n  full stack frame\n");
    Write("logs/third-party/current.log", "2026-09-09 10:02:00 [ERROR] second session\napi_key=super-secret\nAuthorization: Bearer bearer-secret\n{\"token\":\"json-secret\"}\n");
    Write("logs/third-party/current.log.1", "rotated log\n");
    Write("logs/third-party/events.jsonl.1", "{\"event\":\"structured rotation\"}\n");
    Write("logs/old-plugin/session.log", "historical plugin log\n");
    Write("plugins/third-party/logs/nested/helper.txt", "native helper log\n");
    Write("plugins/third-party/local.log", "plugin local log\n");
    Write("plugins/third-party/settings.json", "private-settings-do-not-export");
    Write("settings/third-party/events.log", "events from settings directory");
    Write("settings/third-party/config.txt", "private-text-setting-do-not-export");
    using (var gzipFile = File.Create(Path.Combine(root, "logs/third-party/older.log.gz")))
    using (var gzip = new System.IO.Compression.GZipStream(gzipFile, System.IO.Compression.CompressionMode.Compress))
    using (var gzipWriter = new StreamWriter(gzip)) gzipWriter.Write("compressed historical log");
    Write("plugins/third-party/README.txt", "documentation-do-not-export");
    Write("services/loader.log", "loader failure\n");
    Write("logs/bad-manifest/session.log", "survives broken manifest\n");
    var large = string.Join('\n', Enumerable.Range(0, 5000).Select(i => "line " + i));
    Write("logs/third-party/large.log", large);
    Write("logs/third-party/locked.log", "locked file");
    using var locked = new FileStream(Path.Combine(root, "logs/third-party/locked.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    var output = new StringWriter();
    DeckyDiagnostics.Append(output, root);
    var report = output.ToString();
    Check(report.Contains("Independent Plugin | version=7.2.1"), "installed plugin metadata");
    Check(report.Contains("Frontend only") && report.Contains("Discovered log files: 0"), "plugins without logs remain visible");
    Check(report.Contains("first session") && report.Contains("second session") && report.Contains("full stack frame"), "all sessions and complete tracebacks");
    Check(report.Contains("rotated log") && report.Contains("historical plugin log") && report.Contains("structured rotation"), "rotation and orphan log folders");
    Check(report.Contains("native helper log") && report.Contains("plugin local log") && report.Contains("loader failure"), "additional plugin and loader logs");
    Check(report.Contains("line 0" + Environment.NewLine) && report.Contains("line 4999"), "full log not only tail");
    Check(report.Contains("survives broken manifest") && report.Contains("Manifest unavailable"), "broken manifest isolation");
    Check(report.Contains("log unavailable or incomplete"), "locked file reported without losing other plugins");
    Check(!report.Contains("super-secret") && !report.Contains("bearer-secret") && !report.Contains("json-secret"), "credential redaction");
    Check(!report.Contains("private-settings-do-not-export") && !report.Contains("documentation-do-not-export"), "collect logs only");
    Check(report.Contains("CROSS-COMPONENT ERROR INDEX") && report.Contains("old.log:1"), "error index links source and line");
    Check(report.Contains("events from settings directory") && !report.Contains("private-text-setting-do-not-export"), "settings logs without configuration");
    Check(report.Contains("compressed historical log"), "compressed rotations decoded");
    Check(report.Split("===== LOG [").Length - 1 == 13, "each source log collected once");
    var absent = new StringWriter();
    DeckyDiagnostics.Append(absent, Path.Combine(root, "missing"));
    Check(absent.ToString().Contains("no installed plugin folders found"), "Decky absent");
    Console.WriteLine($"PASS: {checks} diagnostics checks.");
}
finally { Directory.Delete(root, true); }
