"""Actual Plugin transport AST with inert token/HTTP edges; no plugin import or network."""
import ast
import asyncio
import io
import json
import os
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import Mock
import urllib.error
import urllib.parse
import urllib.request


SOURCE = Path(__file__).parents[1] / "main.py"
tree = ast.parse(SOURCE.read_text(encoding="utf-8-sig"))
plugin = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "Plugin")
methods = {"_native_session_post", "_native_game_identity", "get_native_game_overlay", "set_native_game_overlay_input"}
selected = [node for node in plugin.body if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)) and node.name in methods]
assert len(selected) == len(methods), "Expected actual transport/native methods missing"


class TokenFile(io.StringIO):
    def __init__(self, text):
        super().__init__(text)
        self.read_sizes = []

    def read(self, size=-1):
        self.read_sizes.append(size)
        return super().read(size)


class Response:
    def __init__(self, value=b'{"ok":true}'):
        self.value = value
        self.read_sizes = []
        self.exited = False
        self.error = None

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.exited = True

    def read(self, maximum):
        self.read_sizes.append(maximum)
        if self.error:
            raise self.error
        return self.value[:maximum]


class Transport(unittest.TestCase):
    def setUp(self):
        self.token = TokenFile("fixture-shell-token\n")
        self.response = Response()
        self.opener = SimpleNamespace(open=Mock(return_value=self.response))
        self.proxy = Mock(side_effect=lambda value: ("no-proxy", value))
        self.build = Mock(return_value=self.opener)
        self.file_open = Mock(return_value=self.token)
        self.environment = {"APPDATA": os.path.join("C:" + os.sep, "FixtureAppData")}
        request = SimpleNamespace(Request=urllib.request.Request, ProxyHandler=self.proxy, build_opener=self.build)
        namespace = {"asyncio": asyncio, "json": json, "os": SimpleNamespace(path=os.path, environ=self.environment),
                     "urllib": SimpleNamespace(request=request, parse=urllib.parse), "open": self.file_open}
        module = ast.Module(body=[ast.ClassDef(name="Plugin", bases=[], keywords=[], body=selected, decorator_list=[])], type_ignores=[])
        exec(compile(ast.fix_missing_locations(module), str(SOURCE), "exec"), namespace)
        self.plugin = namespace["Plugin"]()

    def post(self, path="/session/live-performance", query=None, body=None, **options):
        return self.plugin._native_session_post(path, query, body, **options)

    def request(self):
        return self.opener.open.call_args.args[0]

    def test_default_timeout_and_reply_bound_are_real_parameters(self):
        self.assertEqual(self.post(), {"ok": True})
        self.assertEqual(self.opener.open.call_args.kwargs, {"timeout": 2.5})
        self.assertEqual(self.response.read_sizes, [2048])
        self.assertTrue(self.response.exited)

    def test_post_is_fixed_loopback_and_empty_body_without_content_type(self):
        self.post()
        request = self.request()
        self.assertEqual(request.full_url, "http://127.0.0.1:47991/session/live-performance")
        self.assertEqual(request.get_method(), "POST")
        self.assertEqual(request.data, b"")
        self.assertIsNone(request.get_header("Content-type"))

    def test_token_is_bounded_stripped_and_sent_only_as_auth_header(self):
        self.post()
        self.file_open.assert_called_once_with(os.path.join(self.environment["APPDATA"], "GamingMode", "xbox-shell-token"), "r", encoding="utf-8")
        self.assertEqual(self.token.read_sizes, [513])
        self.assertEqual(self.request().get_header("X-playhub-shell-token"), "fixture-shell-token")
        self.assertNotIn("fixture-shell-token", self.request().full_url)
        self.assertNotIn(b"fixture-shell-token", self.request().data)
        self.assertTrue(self.token.closed)

    def test_environment_proxy_is_explicitly_disabled(self):
        self.post()
        self.proxy.assert_called_once_with({})
        self.build.assert_called_once_with(("no-proxy", {}))

    def test_query_is_encoded_as_query_not_host_or_path(self):
        query = {"appId": 2979426017, "menu": "true", "label": "two words &=", "unicode": "è"}
        self.post("/session/test", query)
        split = urllib.parse.urlsplit(self.request().full_url)
        self.assertEqual((split.scheme, split.netloc, split.path), ("http", "127.0.0.1:47991", "/session/test"))
        self.assertEqual(urllib.parse.parse_qs(split.query), {key: [str(value)] for key, value in query.items()})

    def test_json_post_keeps_opaque_64_bit_strings_and_auth_out_of_body(self):
        body = {"gameBirth": "134389511886362822", "generation": "18446744073709551615", "label": "è", "active": True}
        self.post("/session/test", None, body)
        self.assertEqual(self.request().get_header("Content-type"), "application/json")
        self.assertEqual(json.loads(self.request().data.decode("utf-8")), body)
        self.assertIsInstance(json.loads(self.request().data)["gameBirth"], str)

    def test_explicit_native_timeout_and_reply_bound(self):
        self.post("/session/steam/native-game-overlay-input", None, {"action": "prepare"}, timeout=7.0, maximum=8192)
        self.assertEqual(self.opener.open.call_args.kwargs, {"timeout": 7.0})
        self.assertEqual(self.response.read_sizes, [8192])

    def test_missing_appdata_fails_before_file_or_http(self):
        self.environment.clear()
        self.assertEqual(self.post(), {})
        self.file_open.assert_not_called()
        self.build.assert_not_called()

    def test_missing_token_file_fails_before_http(self):
        self.file_open.side_effect = FileNotFoundError("fixture")
        self.assertEqual(self.post(), {})
        self.build.assert_not_called()

    def test_blank_or_oversize_token_never_opens_http(self):
        for token in (" \n\t", "x" * 513):
            with self.subTest(token_length=len(token)):
                self.token = TokenFile(token)
                self.file_open.return_value = self.token
                self.assertEqual(self.post(), {})
                self.assertEqual(self.token.read_sizes, [513])
                self.assertTrue(self.token.closed)
        self.opener.open.assert_not_called()

    def test_maximum_valid_token_is_not_truncated(self):
        self.token = TokenFile("x" * 512)
        self.file_open.return_value = self.token
        self.assertEqual(self.post(), {"ok": True})
        self.assertEqual(len(self.request().get_header("X-playhub-shell-token")), 512)

    def test_non_object_json_is_unavailable(self):
        for value in (b"[]", b"null", b'"text"', b"true", b"123"):
            with self.subTest(value=value):
                self.token = TokenFile("fixture")
                self.file_open.return_value = self.token
                self.response.value = value
                self.assertEqual(self.post(), {})

    def test_malformed_empty_and_invalid_utf8_fail_closed(self):
        for value in (b"{", b"", b"\xff\xff", b'{"ok":true} trailing'):
            with self.subTest(value=value):
                self.file_open.return_value = TokenFile("fixture")
                self.response.value = value
                self.assertEqual(self.post(), {})
                self.assertTrue(self.response.exited)

    def test_truncated_large_reply_never_parses_as_success(self):
        self.response.value = b'{"padding":"' + b"x" * 3000 + b'","ok":true}'
        self.assertEqual(self.post(), {})
        self.assertEqual(self.response.read_sizes, [2048])

    def test_response_read_error_disposes_response(self):
        self.response.error = OSError("fixture read failure")
        self.assertEqual(self.post(), {})
        self.assertTrue(self.response.exited)

    def test_network_timeout_auth_and_url_failures_are_unavailable(self):
        failures = [urllib.error.URLError("fixture refused"), TimeoutError("fixture timeout"),
                    urllib.error.HTTPError("http://127.0.0.1:47991/session/test", 401, "fixture", {}, None), ValueError("fixture URL")]
        for failure in failures:
            with self.subTest(failure=type(failure).__name__):
                self.file_open.return_value = TokenFile("fixture")
                self.opener.open.side_effect = failure
                self.assertEqual(self.post(), {})

    def test_actual_native_prepare_rpc_uses_real_transport_7s_8192(self):
        app = 2979426017
        proof = dict(protocol=1, canonicalAppId=app, fullGameId=str((app << 32) | 0x02000000), observedGameId=str(app),
                     sessionId="a" * 64, gamePid=10, gameBirth="134389511886362822", wrapperPid=11,
                     wrapperBirth="134389511880000001", steamPid=12, steamBirth="134388877300000001",
                     overlayPid=14, overlayBirth="134389511900000001", companionPid=13, companionBirth="134389511890000001",
                     gameWindow="291", observedAt="100", expiresAt="1600", routingOnly=True)
        browser = dict(overlayPid=14, browserId=17, appId=9858785)
        reply = asyncio.run(self.plugin.set_native_game_overlay_input(app, "prepare", proof, browser))
        self.assertEqual(reply, {"ok": True})
        self.assertEqual(self.opener.open.call_args.kwargs, {"timeout": 7.0})
        self.assertEqual(self.response.read_sizes, [8192])
        body = json.loads(self.request().data)
        self.assertEqual(body["expectedNative"], proof)
        self.assertEqual(body["browser"], browser)
        self.assertEqual(self.request().full_url, "http://127.0.0.1:47991/session/steam/native-game-overlay-input")


if __name__ == "__main__":
    unittest.main()
