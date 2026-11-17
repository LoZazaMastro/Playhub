using Playhub.GameSession;
using Playhub.StoreLibrary;

internal static class StoreXboxCompanionTests
{
    internal static void Run(string root)
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new StorePrivateLaunchPlan(Guid.NewGuid(), new(StoreKind.Xbox, "9NBLGGH1ZB7X"),
            Path.Combine(root, "package", "Binaries", "CosmoRun.exe"), Path.Combine(root, "package"), [],
            new Dictionary<string,string>(), now.AddSeconds(30), "Fixture.Cosmo_abcdef!App");
        var binding = UwpStoreCompanionBinding.Create(plan, now);
        Check(binding.ProductId == plan.Key.Id && binding.LaunchId == plan.LaunchId && binding.Aumid == plan.XboxAumid
            && binding.ManifestExecutable == @"Binaries\CosmoRun.exe", "Store companion binding retains the exact private broker product, receipt, AUMID and manifest image");
        var runtime = new Runtime(plan);
        Check(StoreXboxUwpLaunch.Run(plan, runtime) == 7 && runtime.Activations == 1 && runtime.Waits == 1 && runtime.Disposals == 1
            && runtime.Binding == binding, "Store UWP route forwards the broker binding before one original activation and exact game wait");
        foreach (var invalid in new[] { plan with { Key = new(StoreKind.Epic, plan.Key.Id) }, plan with { LaunchId = Guid.Empty },
            plan with { ExpiresAt = now }, plan with { ExpiresAt = now.AddMinutes(1) }, plan with { XboxAumid = "missing" },
            plan with { GameExecutable = Path.Combine(root,"outside.exe") }, plan with { Arguments = ["extra"] },
            plan with { Environment = new Dictionary<string,string>{{"INJECTED","1"}} }, plan with { Key = new(StoreKind.Xbox,"another-product") } })
            Denied(() => UwpStoreCompanionBinding.Create(invalid, now), "Malformed, expired, escaping or foreign-store private context is refused before preparation");
    }
    private static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    private static void Denied(Action action,string name){try{action();}catch(InvalidDataException){Check(true,name);return;}throw new Exception(name);}
    private sealed class Runtime(StorePrivateLaunchPlan plan):IUwpRuntime
    {
        internal UwpStoreCompanionBinding? Binding; internal int Activations,Waits,Disposals;
        public long Now=>0;public bool ShellReady=>false;public bool PrepareDirectActivation()=>true;
        public void StartShell()=>throw new Exception("No shell or alternate launch");public void Delay(int _){}public void Log(string _){}
        public IDisposable? PrepareLaunch(string aumid,string executable,UwpStoreCompanionBinding? binding){Check(Activations==0&&binding?.MatchesApplication(aumid,executable,DateTimeOffset.UtcNow)==true,"Exact store context reaches preactivation preparation");Binding=binding;return new Lease(()=>Disposals++);}
        public IUwpProcess Activate(string aumid,string arguments){Check(Binding!=null&&aumid==plan.XboxAumid&&arguments=="","One unchanged registered activation uses no injected arguments");Activations++;return new Game(this,plan);}
        public IUwpProcess? FindGame(string _,string __)=>throw new Exception("No guessed process");
        private sealed class Game(Runtime runtime,StorePrivateLaunchPlan plan):IUwpProcess{public uint Id=>123;public string Executable=>plan.GameExecutable;public string PackageFamily=>plan.XboxAumid!.Split('!')[0];public int WaitForExit(){runtime.Waits++;return 7;}public void Dispose(){}}
        private sealed class Lease(Action action):IDisposable{public void Dispose()=>action();}
    }
}
