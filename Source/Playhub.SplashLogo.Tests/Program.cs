using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string path = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "Source", "MainWindow.cs");
var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
string[] names = ["AutoSaveGaming", "PopulateGamingConfigControls", "SaveGamingConfigAsync", "ResolveSplashLogo", "RefreshChoiceCombo", "GetComboKey", "SelectComboKey"];
var methods = names.Select(name => root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == name).ToFullString());
string options = root.DescendantNodes().OfType<FieldDeclarationSyntax>().Single(f => f.Declaration.Variables.Any(v => v.Identifier.Text == "SplashLogoOptions")).ToFullString();
var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Source", "SplashLogoSelection.cs"));
var model = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Source", "GamingModeConfig.cs"));
var harness = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Harness.cs.txt"));
string script = harness.Replace("/*PRODUCTION*/", options + string.Join("\n", methods));
string global = "global using System; global using System.IO; global using System.Linq; global using System.Collections.Generic; global using System.Threading.Tasks;";
var trees = new[] {global, model, source, script}.Select(s => CSharpSyntaxTree.ParseText(s));
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
var compilation = CSharpCompilation.Create("SplashRoundTrip" + Guid.NewGuid().ToString("N"), trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var bytes = new MemoryStream();
var result = compilation.Emit(bytes);
if (!result.Success) { foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic); return 2; }
bytes.Position = 0;
var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
try { return (int)assembly.GetType("MainWindow")!.GetMethod("Run")!.Invoke(null, null)!; }
catch (TargetInvocationException e) { Console.WriteLine(e.InnerException); return 1; }
