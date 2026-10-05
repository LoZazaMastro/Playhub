"""Real Windows subprocess/IPC smoke against the original plugin, no hardware writes."""
from pathlib import Path
import hashlib, json, os, subprocess, sys, tempfile, time
sys.path.insert(0, str(Path(__file__).resolve().parent))
from settings_snapshot import stage_settings
root = Path(__file__).resolve().parent
source = root.parents[1] / "GamingModeDeckyPlugin"
fixture = Path(tempfile.mkdtemp(prefix="playhub-runtime-read-"))
settings_source = Path(os.environ["USERPROFILE"]) / "homebrew" / "settings" / "gaming-mode"
if not settings_source.is_dir():
    settings_source = fixture / "empty-source"; settings_source.mkdir()
settings = fixture / "settings"
receipt = stage_settings(settings_source, settings)
methods = ["host.status", "get_panel_preferences", "get_qam_preferences", "get_home_news_settings", "get_daily_history_settings", "get_capabilities", "get_initial_state", "get_power_status", "get_agent_status", "get_device_info", "host.shutdown"]
requests = [{"id": i, "method": method} for i, method in enumerate(methods, 1)]
started = time.monotonic()
result = subprocess.run([sys.executable, "-I", str(root / "runner.py"), "--workspace", str(fixture), "--plugin-root", str(source), "--settings-dir", str(settings), "--allow-installed-plugin", "--runtime-reads", "--instance-id", "real-runtime-read"], input="".join(json.dumps(x)+"\n" for x in requests), text=True, encoding="utf-8", capture_output=True, timeout=40)
responses = [json.loads(line) for line in result.stdout.splitlines()]
checks = {method: next((response.get("ok") is True for response in responses if response.get("id") == i), False) for i, method in enumerate(methods, 1)}
manifest = json.loads((settings / "playhub-migration-manifest.json").read_text(encoding="utf-8"))
unchanged = all(hashlib.sha256((settings_source / item["path"]).read_bytes()).hexdigest() == item["sha256"] for item in manifest["files"])
report = {"exitCode": result.returncode, "checks": checks, "snapshot": receipt, "sourceUnchanged": unchanged, "durationMs": round((time.monotonic()-started)*1000), "hardwareLifecycleStarted": False, "fixture": str(fixture)}
print(json.dumps(report, indent=2))
if result.returncode or not all(checks.values()) or not unchanged:
    print(result.stderr[:1000], file=sys.stderr); raise SystemExit(1)
