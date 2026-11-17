import base64
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import threading
from concurrent.futures import ThreadPoolExecutor
import unittest

SOURCE = Path(os.environ.get("PLAYHUB_NVIDIA_SETTINGS_SOURCE", str(Path(__file__).parents[1] / "quick_settings" / "nvidia_settings.py")))
spec = importlib.util.spec_from_file_location("actual_nvidia_settings", SOURCE)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class Checks(unittest.TestCase):
    def test_rejects_invalid_requests_without_process(self):
        def forbidden(*args, **kwargs):
            self.fail("Invalid input must never start PowerShell")
        for token, feature, value in [("bad", "power", 1), ("a" * 64, "clock", 1),
                                     ("a" * 64, "power", True), ("a" * 64, "power", 5),
                                     ("a" * 64, "vsync", 1), ("a" * 64, "frame_limit", 19),
                                     ("a" * 64, "frame_limit", 1001), ("a" * 64, "frame_limit", -1),
                                     ("a" * 64, "power", "1; unsafe"), ("a" * 64, "power", None)]:
            with self.subTest(feature=feature, value=value):
                result = m.invoke(forbidden, lambda: "powershell", token, feature, value)
                self.assertEqual(result["status"], "invalid")

    def test_exact_encoded_contract_and_loader_guards(self):
        scripts = []
        def run(argv, timeout):
            self.assertEqual(timeout, 12)
            self.assertEqual(argv[1:4], ["-NoProfile", "-NonInteractive", "-EncodedCommand"])
            scripts.append(base64.b64decode(argv[4]).decode("utf-16-le"))
            return 0, json.dumps({"available": False, "status": "busy", "global": True, "settings": []}), ""
        for value in ("default", 0, 20, 1000):
            result = m.invoke(run, lambda: "powershell", "a" * 64, "frame_limit", value, driver_probe=lambda: True)
            self.assertEqual(result["status"], "busy")
        for script in scripts:
            for required in ("[IO.FileShare]::Read", "Get-AuthenticodeSignature", "O=NVIDIA Corporation",
                             "Local\\Playhub.NvidiaShaderCache", "LoadLibraryEx(path,IntPtr.Zero,0x800)",
                             "0x617bff9f", "0x577dd202", "0xe4a26362", "0xfcbc7e14"):
                self.assertIn(required, script)
            self.assertNotIn("DRS_RestoreAllDefaults", script)
            self.assertNotIn("0x8A2CF5F5", script)
            self.assertNotIn("CreateApplication", script)
            self.assertNotIn("RestartDisplayDriver", script)

    def test_invalid_process_output_is_unavailable(self):
        for code, output in [(1, "{}"), (0, "not-json"), (0, "[]")]:
            result = m.invoke(lambda *a, **k: (code, output, ""), lambda: "powershell", driver_probe=lambda: True)
            self.assertFalse(result["available"])
            self.assertEqual(result["status"], "unavailable")

    def test_driver_absent_never_spawns_process(self):
        def forbidden(*args, **kwargs):
            self.fail("Missing system driver must never start PowerShell")
        for request in ({}, {"token": "a" * 64, "feature": "power", "value": 1}):
            result = m.invoke(forbidden, forbidden, driver_probe=lambda: False, **request)
            self.assertEqual(result["status"], "unsupported")
            self.assertFalse(result["available"])

    def test_detected_only_after_existing_constructor_enumeration(self):
        scripts = []
        def capture(argv, timeout):
            scripts.append(base64.b64decode(argv[-1]).decode("utf-16-le"))
            return 0, '{"available":false,"global":true,"status":"unavailable"}', ""
        m.invoke(capture, lambda: "powershell", driver_probe=lambda: True)
        script = scripts[0]
        # Execute the actual constructor/transaction/catch wrapper, using inert
        # boundary types. No NVAPI, device, real DLL, mutex or signature calls.
        start = script.index(" $driver=New-Object -TypeName NvidiaGlobalDrs")
        end = script.index("\nfinally{", start)
        body = script[start:end]
        stub = r'''public sealed class NvidiaGlobalDrs {
          public static bool FailConstruct; public NvidiaGlobalDrs(string p){if(FailConstruct)throw new System.Exception("init unavailable");}
        }
        public static class NvidiaGlobalTransaction {
          public static bool FailRead;
          public static object Run(NvidiaGlobalDrs d,string t,string f,object w,bool mutation){
           if(FailRead)throw new System.Exception("read unavailable");
           return new {available=true,global=true,status="ready"};
          }
          public static string Failure(System.Exception e){return "unavailable";}
        }'''
        for fail_construct, fail_read, expected in [(False,False,True),(False,True,True),(True,False,False)]:
            with self.subTest(constructor=fail_construct,read=fail_read), tempfile.TemporaryDirectory(prefix="Playhub-Nvidia-Detection-") as tmp:
                ps = Path(tmp)/"Check.ps1"
                prelude = "$ErrorActionPreference='Stop';Add-Type -TypeDefinition @'\n"+stub+"\n'@\n"
                prelude += "[NvidiaGlobalDrs]::FailConstruct=$"+str(fail_construct).lower()+";[NvidiaGlobalTransaction]::FailRead=$"+str(fail_read).lower()+";"
                prelude += "$detected=$false;$path='inert';$request=[pscustomobject]@{token='';feature=$null;value=$null};try {\n"
                ps.write_text(prelude+body,encoding="utf-8-sig")
                result=subprocess.run(["powershell.exe","-NoProfile","-NonInteractive","-File",str(ps)],capture_output=True,text=True,timeout=30)
                self.assertEqual(result.returncode,0,result.stdout+result.stderr)
                value=json.loads(result.stdout.lstrip("\ufeff"))
                self.assertIs(value.get("detected"),expected)

    def test_actual_csharp_policy(self):
        fixture = Path(__file__).with_name("nvidia_global_native_fixture.cs")
        with tempfile.TemporaryDirectory(prefix="Playhub-Nvidia-Checks-") as tmp:
            target = Path(tmp)
            (target / "Actual.cs").write_text(m.NATIVE, encoding="utf-8")
            (target / "Checks.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>', encoding="utf-8")
            (target / "Program.cs").write_bytes(fixture.read_bytes())
            run = subprocess.run(["dotnet", "run", "--project", str(target / "Checks.csproj"), "-c", "Release", "--nologo"], capture_output=True, text=True, timeout=60)
            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
            self.assertIn("69 actual NVIDIA transaction checks PASS", run.stdout)

    def test_concurrent_read_write_spawns_only_one_process(self):
        started, release = threading.Event(), threading.Event()
        calls = []
        def delayed(argv, timeout):
            calls.append(argv)
            started.set()
            self.assertTrue(release.wait(2))
            return 0, '{"available":false,"status":"unsupported"}', ""
        with ThreadPoolExecutor(max_workers=2) as pool:
            first = pool.submit(m.invoke, delayed, lambda: "powershell", driver_probe=lambda: True)
            self.assertTrue(started.wait(2))
            second = m.invoke(delayed, lambda: "powershell", "a" * 64, "power", 1, driver_probe=lambda: True)
            self.assertEqual(second["status"], "busy")
            self.assertEqual(len(calls), 1)
            release.set()
            self.assertEqual(first.result(timeout=2)["status"], "unsupported")

    def test_process_exception_releases_singleflight_lock(self):
        def failed(*args, **kwargs):
            raise RuntimeError("fake transport error")
        with self.assertRaises(RuntimeError):
            m.invoke(failed, lambda: "powershell", driver_probe=lambda: True)
        result = m.invoke(lambda *a, **k: (0, '{"available":false,"status":"unsupported"}', ""), lambda: "powershell", driver_probe=lambda: True)
        self.assertEqual(result["status"], "unsupported")


if __name__ == "__main__":
    unittest.main()
