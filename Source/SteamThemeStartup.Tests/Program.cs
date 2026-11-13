using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using GamingMode.Services;

if (args.Contains("--fixture-child")) { Thread.Sleep(30000); return; }
int passed = 0;
void Check(string name, bool value) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
SteamThemeReceipt applied = new(7, new string('a',32), "g-"+new string('b',32),1,"applied","style-resolved");
SteamThemeStartupResult Observe(SteamThemeStartupPolicy p,long now=0,bool origin=false,bool steam=true,bool main=true,
    bool alive=true,bool context=true,bool fullscreen=true,SteamThemeReceipt? receipt=null) =>
    p.Observe(now,origin,steam,main,alive,context,fullscreen,receipt);
Check("resolved style proof releases an occluded main document", Observe(new(0,0,8000),receipt:applied)==SteamThemeStartupResult.Ready);
Check("fullscreen alone waits",Observe(new(0,0,8000))==SteamThemeStartupResult.Waiting);
Check("receipt alone waits for native main",Observe(new(0,0,8000),main:false,receipt:applied)==SteamThemeStartupResult.Waiting);
Check("nonfullscreen main waits",Observe(new(0,0,8000),fullscreen:false,receipt:applied)==SteamThemeStartupResult.Waiting);
Check("user minimum is retained",Observe(new(0,1200,8000),now:1199,receipt:applied)==SteamThemeStartupResult.Waiting);
Check("user minimum releases without extra fixed hold",Observe(new(0,1200,8000),now:1200,receipt:applied)==SteamThemeStartupResult.Ready);
Check("deadline releases without fabricated readiness",Observe(new(0,0,8000),now:8000,receipt:applied)==SteamThemeStartupResult.TimedOut);
var slowInitialization = new SteamThemeStartupPolicy(0,1200,5000);
Check("startup waits while Decky initialization is in progress",Observe(slowInitialization,now:500,origin:true,steam:false,main:false)==SteamThemeStartupResult.Waiting);
Check("Decky delay beyond configured maximum cannot earn readiness",Observe(slowInitialization,now:10000,receipt:applied)==SteamThemeStartupResult.TimedOut);
var initializationDeparture = new SteamThemeStartupPolicy(0,1200,5000);
Check("AltTab during initialization cancels before any Steam window",Observe(initializationDeparture,now:250,origin:false,steam:false,main:false)==SteamThemeStartupResult.UserLeft);
Check("returning from AltTab cannot restart the cancelled transition",Observe(initializationDeparture,now:1500,origin:true,receipt:applied)==SteamThemeStartupResult.UserLeft);
Check("unknown foreign foreground cancels before Steam",Observe(new(0,0,8000),steam:false,main:false)==SteamThemeStartupResult.UserLeft);
var departure = new SteamThemeStartupPolicy(0,0,8000); Observe(departure);
Check("AltTab back to launcher cancels after main arrival",Observe(departure,main:false,origin:true)==SteamThemeStartupResult.UserLeft);
Check("Steam exit cancels",Observe(new(0,0,8000),alive:false)==SteamThemeStartupResult.SteamExited);
Check("native window replacement cancels",Observe(new(0,0,8000),context:false)==SteamThemeStartupResult.ContextReplaced);
Check("failed CSS batch waits",Observe(new(0,0,8000),receipt:applied with {State="failed"})==SteamThemeStartupResult.Waiting);
Check("loading zero-theme batch waits",Observe(new(0,0,8000),receipt:applied with {State="loading"})==SteamThemeStartupResult.Waiting);
Check("ready policy is terminal",Observe(departure,receipt:applied)==SteamThemeStartupResult.UserLeft);

string Receipt(int protocol=1,int browser=7,string? document=null,string? receiptDocument=null) => JsonSerializer.Serialize(new {
    main=true,browserId=browser,physicalWidth=1920,physicalHeight=1080,documentId=document??new string('a',32),receipt=new {protocol,browserId=7,
    documentId=receiptDocument??new string('a',32),generation="g-"+new string('b',32),serial=1,state="applied",proof="style-resolved"}});
SteamThemeReceipt? Parse(string data) { using var doc=JsonDocument.Parse(data);return SteamThemeReceipt.Parse(doc.RootElement); }
Check("current-main receipt parses",Parse(Receipt())?.Applied==true);
Check("foreign browser receipt rejected",Parse(Receipt(browser:8)) is null);
Check("stale document receipt rejected",Parse(Receipt(receiptDocument:new string('c',32))) is null);
Check("unknown protocol rejected",Parse(Receipt(protocol:2)) is null);
Check("malformed document rejected",Parse(Receipt(document:"old")) is null);
Check("missing fields rejected",Parse("{}") is null);

Check("local exact CDP page allowed",SteamThemeReadinessProbe.IsOwnedEndpoint("ws://127.0.0.1:8080/devtools/page/123",out _));
foreach(string url in new[]{"ws://localhost:8080/devtools/page/123","ws://evil.invalid:8080/devtools/page/123",
    "ws://127.0.0.1:8081/devtools/page/123","ws://name@127.0.0.1:8080/devtools/page/123","ws://127.0.0.1:8080/devtools/browser/123"})
    Check("foreign endpoint rejected "+url,!SteamThemeReadinessProbe.IsOwnedEndpoint(url,out _));
bool Target(string url,string title="SharedJSContext",string type="page")
{ using var doc=JsonDocument.Parse(JsonSerializer.Serialize(new{id="123",url,title,type}));return SteamThemeReadinessProbe.IsSharedSteamTarget(doc.RootElement); }
Check("SteamUI origin accepted",Target("https://steamloopback.host/index.html"));
Check("external tab with SharedJSContext title rejected",!Target("https://example.com/"));
Check("same-name websocket target rejected",!Target("https://steamloopback.host/index.html",type:"worker"));

// Actual Toolhelp snapshot + process birth checks, using only this test's own
// inert hidden child. Never open or attach to an installed Steam process.
Type coverType=typeof(SteamThemeStartupCover);
object lineageCover=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(coverType);
Type identityType=coverType.GetNestedType("Identity",BindingFlags.NonPublic)!;
var clientField=coverType.GetField("_client",BindingFlags.Instance|BindingFlags.NonPublic)!;
var lineage=coverType.GetMethod("HasClientLineage",BindingFlags.Instance|BindingFlags.NonPublic)!;
using var current=Process.GetCurrentProcess();
long currentBirth=current.StartTime.ToFileTimeUtc();
object Client(long birth)=>Activator.CreateInstance(identityType,new object[]{nint.Zero,current.Id,birth})!;
clientField.SetValue(lineageCover,Client(currentBirth));
var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--fixture-child");
using(var child=Process.Start(start)!)
{
    try
    {
        long birth=child.StartTime.ToFileTimeUtc();
        Check("real owned child has exact process ancestry",(bool)lineage.Invoke(lineageCover,new object[]{child.Id,birth})!);
        clientField.SetValue(lineageCover,Client(currentBirth+1));
        Check("reused ancestor birth is refused",!(bool)lineage.Invoke(lineageCover,new object[]{child.Id,birth})!);
        clientField.SetValue(lineageCover,Client(currentBirth));
        Check("direct owner birth must match",!(bool)lineage.Invoke(lineageCover,new object[]{current.Id,currentBirth+1})!);
    }
    finally { if(!child.HasExited)child.Kill();child.WaitForExit(2000); }
}

// Exercise the actual close/cancellation code with a private dispatcher and no
// Window, HWND, Steam connection or user-visible surface.
var splash=new SplashScreenService(new FileLogger());
Type surfaceType=typeof(SplashScreenService).GetNestedType("Surface",BindingFlags.NonPublic)!;
object surface=Activator.CreateInstance(surfaceType,true)!;
var ready=new ManualResetEventSlim();
var thread=new Thread(()=>{surfaceType.GetField("Dispatcher",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(surface,Dispatcher.CurrentDispatcher);ready.Set();Dispatcher.Run();}){IsBackground=true};
thread.SetApartmentState(ApartmentState.STA);
surfaceType.GetField("Thread",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(surface,thread);
typeof(SplashScreenService).GetField("_surface",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(splash,surface);
thread.Start();if(!ready.Wait(2000))throw new Exception("fixture dispatcher not ready");
var pending=splash.HideAsync(minVisibleMs:60000,fade:true); var watch=Stopwatch.StartNew(); splash.CancelImmediately();
await pending.WaitAsync(TimeSpan.FromSeconds(1));
Check("cancellation bypasses pending sixty-second minimum",watch.ElapsedMilliseconds<1000);
Check("owned dispatcher shuts down",thread.Join(1000));
splash.Dispose();
Console.WriteLine($"{passed} checks passed. No Steam, window display or hardware writes.");

namespace GamingMode.Services
{
    public sealed class FileLogger { public void Info(string value){} public void Error(string value,Exception error){} }
    internal static class ModeTransitionVisual
    {
        internal static System.Windows.UIElement Create(GamingMode.Models.ModeKind mode,string? language,GamingMode.Models.GamingSplashSettings? settings)=>new System.Windows.Controls.Grid();
        internal static void AddGlows(System.Windows.Controls.Grid grid,double width,double height,GamingMode.Models.GamingSplashSettings? settings){}
        internal static System.Windows.Media.Effects.Effect? TextShadow()=>null;
    }
}
namespace Playhub.Shared { internal static class DeckyStartupGuard { internal static int? ListenerOwner(int port)=>throw new InvalidOperationException("This fixture must never access Steam"); } }
