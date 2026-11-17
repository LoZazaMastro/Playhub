import ast
import asyncio
import io
import json
import os
from pathlib import Path
import types
import unittest
from unittest.mock import patch
import urllib.parse
import urllib.request


ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path(os.environ.get('PLAYHUB_SPATIAL_RPC_MAIN', str(ROOT / 'main.py')))
tree = ast.parse(SOURCE.read_text('utf-8-sig'))
original = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == 'Plugin')
names = {'_native_session_post', 'get_spatial_audio', 'set_spatial_audio'}
methods = [node for node in original.body if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)) and node.name in names]
module = ast.Module(body=[ast.ClassDef(name='Plugin', bases=[], keywords=[], body=methods, decorator_list=[])], type_ignores=[])
space = {'asyncio': asyncio, 'os': os, 'json': json, 'urllib': urllib}
exec(compile(ast.fix_missing_locations(module), str(SOURCE), 'exec'), space)


class SpatialRpcBinding(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.plugin = space['Plugin']()
        self.requests = []
        owner = self
        class Response:
            def __enter__(self): return self
            def __exit__(self, *_): pass
            def read(self, _): return b'{"ok":true,"code":"read"}'
        class Client:
            def open(self, request, timeout):
                owner.requests.append(request)
                return Response()
        self.contexts = [patch.dict(os.environ, {'APPDATA': 'C:\\inert-spatial-fixture'}),
            patch('builtins.open', lambda *args, **kwargs: io.StringIO('inert-owner-token')),
            patch('urllib.request.build_opener', return_value=Client())]
        for context in self.contexts:
            context.start(); self.addCleanup(context.stop)

    def body(self):
        self.assertEqual(len(self.requests), 1)
        request = self.requests[0]
        self.assertEqual(request.full_url, 'http://127.0.0.1:47991/session/audio/spatial')
        self.assertEqual(request.get_method(), 'POST')
        self.assertEqual(request.get_header('Content-type'), 'application/json')
        self.assertEqual(request.get_header('X-playhub-shell-token'), 'inert-owner-token')
        return json.loads(request.data)

    async def test_read_reaches_actual_transport_as_json_body_not_query(self):
        self.assertTrue((await self.plugin.get_spatial_audio())['ok'])
        self.assertEqual(self.body(), {'action': 'read'})

    async def test_set_preserves_exact_endpoint_and_compare_exchange_fields_in_json_body(self):
        request = {'endpointId': 'exact-output', 'expectedFormat': '{00000000-0000-0000-0000-000000000000}',
                   'format': '{11111111-1111-1111-1111-111111111111}'}
        self.assertTrue((await self.plugin.set_spatial_audio(request))['ok'])
        self.assertEqual(self.body(), {'action': 'set', **request})

    async def test_malformed_set_never_reaches_transport(self):
        for value in [None, {}, {'endpointId': 'output'}, {'endpointId': 'output', 'expectedFormat': 'off', 'format': 1},
                      {'endpointId': 'output', 'expectedFormat': 'off', 'format': 'sonic', 'extra': 'refused'}]:
            with self.subTest(value=value):
                self.assertEqual(await self.plugin.set_spatial_audio(value), {'ok': False})
        self.assertEqual(self.requests, [])


if __name__ == '__main__': unittest.main()
