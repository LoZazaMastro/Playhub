"""Compile the production routing method with inert dispatch boundaries."""
from pathlib import Path
import subprocess
import tempfile
import unittest


class DispatchSelectionChecks(unittest.TestCase):
    def test_actual_route_preserves_desktop_and_never_retries(self):
        source = (Path(__file__).parents[1] / "Playhub.GameSession" / "WindowsEpicProtocolRuntime.cs").read_text(encoding="utf-8-sig")
        start = source.index("    internal static void DispatchSelected(")
        opening = source.index("{", start)
        depth = 1
        end = opening + 1
        while depth:
            depth += (source[end] == "{") - (source[end] == "}")
            end += 1
        method = source[start:end]
        code = '''using System;
class Checks {
METHOD
static void Main() {
 int checks=0;
 void Check(bool okay) {if(!okay) throw new Exception("route check "+checks);checks++;}
 int reads=0,desktop=0,agent=0;
 DispatchSelected(()=>{reads++;return true;},()=>desktop++,()=>agent++);
 Check(reads==1 && desktop==1 && agent==0);
 reads=desktop=agent=0;
 DispatchSelected(()=>{reads++;return false;},()=>desktop++,()=>agent++);
 Check(reads==1 && desktop==0 && agent==1);
 foreach(bool shell in new[]{true,false}) {
  reads=desktop=agent=0;bool failed=false;
  try {DispatchSelected(()=>{reads++;return shell;},()=>{desktop++;throw new InvalidOperationException();},()=>{agent++;throw new InvalidOperationException();});}
  catch(InvalidOperationException) {failed=true;}
  Check(failed && reads==1 && desktop==(shell?1:0) && agent==(shell?0:1));
 }
 reads=desktop=agent=0;bool denied=false;
 try {DispatchSelected(()=>{reads++;throw new InvalidOperationException();},()=>desktop++,()=>agent++);}
 catch(InvalidOperationException) {denied=true;}
 Check(denied && reads==1 && desktop==0 && agent==0);
 Console.WriteLine(checks+" actual dispatch selection checks PASS; no Windows calls");
}}
'''.replace("METHOD", method)
        with tempfile.TemporaryDirectory(prefix="Playhub-Epic-Route-") as folder:
            root = Path(folder)
            (root / "Checks.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>', encoding="utf-8")
            (root / "Program.cs").write_text(code, encoding="utf-8")
            result = subprocess.run(["dotnet", "run", "--project", str(root / "Checks.csproj"), "-c", "Release", "--nologo"], capture_output=True, text=True, timeout=45)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("5 actual dispatch selection checks PASS", result.stdout)


if __name__ == "__main__":
    unittest.main()
