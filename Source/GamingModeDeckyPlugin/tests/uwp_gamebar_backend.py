import ast
import json
import os
from pathlib import Path
import tempfile
import unittest
import urllib.request
from unittest.mock import patch


tree = ast.parse((Path(__file__).parents[1] / "main.py").read_text(encoding="utf-8-sig"))
plugin = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "Plugin")
methods = [node for node in plugin.body if isinstance(node, ast.FunctionDef) and node.name == "_request_uwp_gamebar"]
isolated = ast.Module(body=[ast.ClassDef(name="Bridge", bases=[], keywords=[], body=methods, decorator_list=[])], type_ignores=[])
scope = {"os": os, "json": json, "urllib": __import__("urllib")}
exec(compile(ast.fix_missing_locations(isolated), "production_gamebar_bridge", "exec"), scope)


class GameBarBridgeTests(unittest.TestCase):
    def setUp(self):
        self.bridge = scope["Bridge"]()
        self.request = {"appId": 2822752531, "requestId": "guide-1", "pressed": True}

    def test_invalid_requests_never_read_token_or_connect(self):
        for change in ({"appId": True}, {"appId": 0}, {"appId": -1}, {"appId": 2**32},
                       {"pressed": 1}, {"requestId": ""}, {"requestId": "x" * 161}):
            with patch("builtins.open", side_effect=AssertionError("must not read")), patch("urllib.request.urlopen") as send:
                self.assertEqual(self.bridge._request_uwp_gamebar({**self.request, **change}), {"ok": False})
                send.assert_not_called()

    def test_secret_stays_backend_and_only_fixed_loopback_route_is_used(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "GamingMode"
            root.mkdir()
            (root / "xbox-shell-token").write_text("secret-token", encoding="utf-8")
            def send(message, timeout):
                self.assertEqual(message.full_url, "http://127.0.0.1:47991/session/uwp/gamebar")
                self.assertEqual(message.get_method(), "POST")
                self.assertEqual(message.get_header("X-playhub-shell-token"), "secret-token")
                self.assertEqual(timeout, 0.8)
                self.assertEqual(json.loads(message.data), self.request)
                class Response:
                    def __enter__(self): return self
                    def __exit__(self, *args): pass
                    def read(self, size):
                        self_size = size
                        assert self_size == 4096
                        return b'{"ok":true,"token":"must-not-escape"}'
                return Response()
            with patch.dict(os.environ, {"APPDATA": directory}), patch("urllib.request.urlopen", side_effect=send):
                self.assertEqual(self.bridge._request_uwp_gamebar({**self.request, "url": "http://untrusted"}), {"ok": True})
                self.request["pressed"] = False
                self.assertEqual(self.bridge._request_uwp_gamebar(self.request), {"ok": True})

    def test_missing_token_and_unavailable_agent_fail_closed(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {"APPDATA": directory}), patch("urllib.request.urlopen") as send:
            self.assertEqual(self.bridge._request_uwp_gamebar(self.request), {"ok": False})
            send.assert_not_called()
            root = Path(directory) / "GamingMode"
            root.mkdir()
            (root / "xbox-shell-token").write_text("token", encoding="utf-8")
            send.side_effect = TimeoutError("offline")
            self.assertEqual(self.bridge._request_uwp_gamebar(self.request), {"ok": False})


if __name__ == "__main__":
    unittest.main()
