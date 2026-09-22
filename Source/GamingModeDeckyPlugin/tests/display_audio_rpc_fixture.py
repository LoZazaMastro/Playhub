"""JSON-line transport for real coordinator tests; all OS operations are mocked."""
import asyncio
import json
import sys
from unittest.mock import patch

from test_panel_backend import PanelBackendTests


fixture = PanelBackendTests()
fixture.setUp()
plugin = fixture.module.Plugin()
runtime = fixture.module._runtime
audio = {"ok": True, "outputs": [], "inputs": [], "default_output_id": "speaker-a",
         "default_input_id": "mic-a", "input_volume": 50}
fail_audio = False
fail_display = False


def display_apply(*args):
    global fail_display
    if fail_display:
        fail_display = False
        return {"ok": True}  # Driver accepts the request but does not apply it.
    return fixture.apply_mode(*args)


fixture.apply.side_effect = display_apply


def audio_helper(action, device_id, kind=None):
    global fail_audio
    if action == "set" and not fail_audio:
        audio["default_input_id" if device_id.startswith("mic") else "default_output_id"] = device_id
    fail_audio = False
    return dict(audio)


fixture.mocks.enter_context(patch.object(runtime, "_run_audio_powershell", side_effect=audio_helper))
fixture.mocks.enter_context(patch("subprocess.run", side_effect=AssertionError("Real subprocess forbidden")))
fixture.mocks.enter_context(patch("subprocess.Popen", side_effect=AssertionError("Real process forbidden")))
allowed = {"begin_display_change", "get_display_change", "finish_display_change",
           "get_audio_devices", "set_audio_output", "set_audio_input"}
try:
    for line in sys.stdin:
        request = json.loads(line)
        method, args = request["method"], request["args"]
        try:
            if method in allowed:
                result = asyncio.run(getattr(plugin, method)(*args))
            elif method == "get_initial_state":
                result = {"capabilities": {}, "audio": dict(audio)}
            elif method == "get_display_status":
                result = {"ok": True, "current": dict(fixture.current), "modes": []}
            elif method == "get_hdr_status":
                result = {"available": True, "enabled": all(fixture.hdr_states.values())}
            elif method == "_inspect":
                result = {"current": fixture.current, "registered": fixture.registered,
                          "hdr": fixture.hdr_states, "pending": plugin._display_pending}
            elif method == "_expire":
                plugin._display_pending["deadline"] = 0
                result = plugin._revert_display(plugin._display_pending["token"])
            elif method == "_fail_audio":
                fail_audio = True
                result = None
            elif method == "_fail_display":
                fail_display = True
                result = None
            else:
                raise ValueError("Unmocked RPC forbidden: " + method)
            print(json.dumps({"id": request["id"], "result": result}), flush=True)
        except Exception as error:
            print(json.dumps({"id": request["id"], "error": str(error)}), flush=True)
finally:
    fixture.doCleanups()
    fixture.tearDown()
