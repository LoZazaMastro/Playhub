"""Invoke Playhub's shared diagnostic collector without opening its UI."""
import json
import os
from pathlib import Path
import subprocess
import threading
import uuid

_gate = threading.Lock()


def _installed_app():
    import winreg
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                r"Software\Microsoft\Windows\CurrentVersion\Uninstall\Playhub") as key:
            location, _ = winreg.QueryValueEx(key, "InstallLocation")
            if isinstance(location, str) and Path(location).is_absolute():
                return Path(location) / "Playhub.exe"
    except OSError:
        pass
    return Path(os.environ.get("LOCALAPPDATA", "")) / "Playhub" / "Playhub.exe"


def generate(*, app=None, local_root=None, runner=subprocess.run):
    if not _gate.acquire(blocking=False):
        return {"ok": False, "message": "A diagnostic report is already being generated."}
    receipt = None
    try:
        app = Path(app) if app is not None else _installed_app()
        marker = app.parent / "Assets/support-report-v1.json"
        if not app.is_file() or not marker.is_file():
            return {"ok": False, "message": "Update the Playhub app to generate its complete diagnostic report."}
        protocol = json.loads(marker.read_text(encoding="utf-8-sig"))
        if not isinstance(protocol, dict) or protocol.get("protocol") != 1:
            return {"ok": False, "message": "Unsupported Playhub diagnostic protocol."}
        root = Path(local_root) if local_root is not None else Path(os.environ["LOCALAPPDATA"]) / "Playhub"
        request = uuid.uuid4().hex
        receipt = root / "DiagnosticsRequests" / (request + ".json")
        result = runner([str(app), "diagnostics-report", request], cwd=str(app.parent),
                        timeout=180, capture_output=True,
                        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        if result.returncode != 0 or not receipt.is_file():
            return {"ok": False, "message": "Playhub could not complete the diagnostic report."}
        response = json.loads(receipt.read_text(encoding="utf-8-sig"))
        if not isinstance(response, dict) or response.get("ok") is not True:
            return {"ok": False, "message": str(response.get("message", "Report failed")) if isinstance(response, dict) else "Invalid report response"}
        path = Path(response.get("path", ""))
        if not path.is_absolute() or not path.is_file() or path.suffix.lower() != ".txt":
            return {"ok": False, "message": "The diagnostic report file is missing."}
        with path.open(encoding="utf-8-sig") as report:
            if "PLAYHUB DIAGNOSTIC REPORT" not in report.read(256):
                return {"ok": False, "message": "Unexpected diagnostic report format."}
        return {"ok": True, "path": str(path)}
    except subprocess.TimeoutExpired:
        return {"ok": False, "message": "Diagnostic generation timed out. Try the Playhub app."}
    except (OSError, ValueError, KeyError, TypeError) as error:
        return {"ok": False, "message": "Diagnostic generation failed: " + str(error)}
    finally:
        if receipt is not None:
            try:
                receipt.unlink(missing_ok=True)
            except OSError:
                pass
        _gate.release()
