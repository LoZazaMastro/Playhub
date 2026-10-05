"""One bounded smoke using copied real source and copied public settings only."""
from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import sys
import uuid

workspace=Path(__file__).resolve().parent
fixture=workspace/".local"/"real-import"/uuid.uuid4().hex
plugin=fixture/"Plugin"
settings=fixture/"settings"
plugin.mkdir(parents=True);settings.mkdir()
source=Path(r"F:\Playhub\Plugin\Playhub\Source\GamingModeDeckyPlugin")
shutil.copy2(source/"main.py",plugin/"main.py")
(plugin/"quick_settings").mkdir()
for file in (source/"quick_settings").glob("*.py"):
    shutil.copy2(file,plugin/"quick_settings"/file.name)
original=Path(os.environ["USERPROFILE"])/"homebrew"/"settings"/"gaming-mode"
names=("playhub-panel.json","qam-layout.json","home-news.json","quick-settings-2.3.json","daily-history-settings.json")
baseline={}
for name in names:
    path=original/name
    if path.is_file():
        baseline[name]=hashlib.sha256(path.read_bytes()).hexdigest()
        shutil.copy2(path,settings/name)
requests=[{"id":i,"method":method} for i,method in enumerate(("host.status","get_panel_preferences","get_qam_preferences","get_home_news_settings","get_daily_history_settings","host.shutdown"),1)]
command=[sys.executable,"-I",str(workspace/"runner.py"),"--workspace",str(workspace),"--plugin-root",str(plugin),"--settings-dir",str(settings),"--instance-id","real-import-smoke"]
result=subprocess.run(command,input="".join(json.dumps(r)+"\n" for r in requests),capture_output=True,text=True,encoding="utf-8",timeout=20)
responses=[json.loads(line) for line in result.stdout.splitlines()]
unchanged=all(hashlib.sha256((original/name).read_bytes()).hexdigest()==digest for name,digest in baseline.items())
report={"exitCode":result.returncode,"responses":len(responses),"allRpcSucceeded":all(r.get("ok") is True for r in responses) and len(responses)==6,
        "status":responses[0].get("result") if responses else None,"copiedSettingsFiles":sorted(baseline),
        "originalSettingsUnchanged":unchanged,"hardwareLifecycleInvoked":False,"fixture":str(fixture),
        "stderrPresent":bool(result.stderr)}
(workspace/".local"/"real-import-result.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
print(json.dumps(report,indent=2))
if result.returncode or not report["allRpcSucceeded"] or not unchanged:
    print("Startup diagnostics: "+result.stderr[:300],file=sys.stderr)
    raise SystemExit(1)
