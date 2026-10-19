import ast
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[1]


class IndependentHelperEnvironmentTests(unittest.TestCase):
    @unittest.skipUnless(os.name == "nt", "Windows DLL inheritance")
    def test_real_external_child_has_clean_dll_search_and_worker_is_restored(self):
        # The DLL setting is changed only in an isolated Python fixture process.
        probe = r'''
import ast,ctypes,json,os,subprocess,sys,threading
from ctypes import wintypes
from contextlib import contextmanager
from pathlib import Path
syntax=ast.parse(Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
function=next(node for node in syntax.body if isinstance(node,ast.FunctionDef) and node.name=='_independent_process_dll_directory')
namespace={'os':os,'ctypes':ctypes,'wintypes':wintypes,'contextmanager':contextmanager,'_independent_spawn_lock':threading.Lock()}
exec(compile(ast.Module(body=[function],type_ignores=[]),'quick_settings/main.py','exec'),namespace)
kernel=ctypes.WinDLL('kernel32',use_last_error=True)
kernel.SetDllDirectoryW.argtypes=[wintypes.LPCWSTR]
assert kernel.SetDllDirectoryW(os.getcwd())
command="import ctypes,json; b=ctypes.create_unicode_buffer(32768); ctypes.windll.kernel32.GetDllDirectoryW(len(b),b); print(json.dumps(b.value))"
with namespace['_independent_process_dll_directory']():
 child=json.loads(subprocess.check_output([sys.executable,'-c',command],text=True))
buffer=ctypes.create_unicode_buffer(32768)
kernel.GetDllDirectoryW(len(buffer),buffer)
assert child=='',(child,'inherited DLL directory')
assert buffer.value==os.getcwd(),(buffer.value,'parent restoration')
try:
 with namespace['_independent_process_dll_directory'](): raise RuntimeError('fixture spawn failure')
except RuntimeError: pass
kernel.GetDllDirectoryW(len(buffer),buffer)
assert buffer.value==os.getcwd(),(buffer.value,'failure restoration')
print('Child DLL directory clean; parent state restored on success and failure.')
'''
        result = subprocess.check_output([sys.executable, "-c", probe, str(ROOT / "quick_settings" / "main.py")], text=True)
        self.assertIn("parent state restored", result)

    def test_real_external_child_does_not_inherit_decky_extraction_state(self):
        syntax = ast.parse((ROOT / "quick_settings" / "main.py").read_text(encoding="utf-8-sig"))
        function = next(node for node in syntax.body if isinstance(node, ast.FunctionDef) and node.name == "_independent_process_environment")
        namespace = {"os": os}
        exec(compile(ast.Module(body=[function], type_ignores=[]), "quick_settings/main.py", "exec"), namespace)
        with patch.dict(os.environ, {
            "_PYI_APPLICATION_HOME_DIR": r"C:\fixture\_MEI123",
            "_PYI_PARENT_PROCESS_LEVEL": "1", "_MEIPASS2": r"C:\fixture\_MEI123",
            "PLAYHUB_HELPER_TEST_TOKEN": "preserved", "PYINSTALLER_RESET_ENVIRONMENT": "0",
        }):
            environment = namespace["_independent_process_environment"]()
            child = json.loads(subprocess.check_output([
                sys.executable, "-c", "import os,json;print(json.dumps(dict(os.environ)))"
            ], env=environment, text=True))
            self.assertFalse(any(key.upper().startswith("_PYI") or key.upper() == "_MEIPASS2" for key in child))
            self.assertEqual(child["PYINSTALLER_RESET_ENVIRONMENT"], "1")
            self.assertEqual(child["PLAYHUB_HELPER_TEST_TOKEN"], "preserved")
            self.assertEqual(os.environ["_PYI_PARENT_PROCESS_LEVEL"], "1")
            self.assertEqual(os.environ["PYINSTALLER_RESET_ENVIRONMENT"], "0")

    def test_packaged_helper_has_the_same_environment_boundary(self):
        asset = ROOT.parent / "Playhub" / "Assets" / "GamingModeDeckyPlugin" / "gaming-mode" / "quick_settings" / "main.py"
        self.assertEqual((ROOT / "quick_settings" / "main.py").read_bytes(), asset.read_bytes())


if __name__ == "__main__":
    unittest.main()
