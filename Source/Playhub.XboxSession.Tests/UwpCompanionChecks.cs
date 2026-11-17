using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Playhub.GameSession;
using Playhub.StoreLibrary;

internal static class UwpCompanionChecks
{
    internal static void Run(string root, Action<bool, string> check, Action<Action, string> denied)
    {
        const ulong id = ((ulong)0xF0010203 << 32) | 0x02000000;
        var plan = new UwpCompanionPlan(100, 200, id, "Fixture.Game_abc!Game", @"Binaries\Game.exe");
        check(plan.Matches(100, 200, id), "Registered UWP preparation binds the wrapper birth, shortcut and relative manifest image");
        check(!plan.Matches(101, 200, id) && !plan.Matches(100, 201, id) && !plan.Matches(100, 200, id + (1UL << 32)),
            "A different parent, recycled wrapper or Steam shortcut cannot reuse UWP preparation");
        check(!(plan with { GameId = 480 }).Matches(100, 200, 480) && !(plan with { Aumid = null! }).Matches(100, 200, id),
            "Malformed private plans and Steam store IDs are refused");
        foreach (string image in new[] { @"..\Game.exe", @"C:\Game.exe", @"Folder\..\Game.exe", "Game.exe:stream", "Game.exe\0", "Game.dll", "" })
            check(!UwpCompanionProtocol.IsApplication(plan.Aumid, image), "A private plan cannot authorize an escaping or malformed image");
        string wrapper = @"C:\Playhub\Playhub.GameSession.exe";
        string[] args = [wrapper, "--uwp", plan.Aumid, plan.Executable, "--original-argument"];
        check(UwpCompanionProtocol.ParentArgumentsMatch(args, wrapper, plan), "Actual wrapper command preserves user arguments after the exact UWP prefix");
        check(!UwpCompanionProtocol.ParentArgumentsMatch([wrapper, "--game", plan.Aumid, plan.Executable], wrapper, plan)
            && !UwpCompanionProtocol.ParentArgumentsMatch([wrapper, "--uwp", "Other!App", plan.Executable], wrapper, plan)
            && !UwpCompanionProtocol.ParentArgumentsMatch(args, @"C:\Other\Playhub.GameSession.exe", plan),
            "A Win32 launch, another AUMID or another wrapper image cannot authorize a registered UWP companion");
        var now = DateTimeOffset.UtcNow;
        var privateLaunch = new StorePrivateLaunchPlan(Guid.NewGuid(), new(StoreKind.Xbox,"9NBLGGH1ZB7X"),
            @"C:\XboxPackage\Binaries\CosmoRun.exe", @"C:\XboxPackage", [], new Dictionary<string,string>(), now.AddSeconds(30), "Fixture.Cosmo_abcdef!App");
        var binding = UwpStoreCompanionBinding.Create(privateLaunch, now);
        var storePlan = new UwpCompanionPlan(100,200,id,binding.Aumid,binding.ManifestExecutable,binding);
        string[] actualStoreArgs = [wrapper,"--store-library","xbox","9NBLGGH1ZB7X"];
        check(UwpCompanionProtocol.ParentArgumentsMatch(actualStoreArgs,wrapper,storePlan) && storePlan.Matches(100,200,id),
            "Managed Cosmo Run real wrapper argv is authorized only with its exact private broker binding");
        foreach (string[] foreign in new[] { new[] {wrapper,"--store-library","xbox","9NBLGGH1ZB7Y"}, new[] {wrapper,"--store-library","gog",binding.ProductId},
            new[] {wrapper,"--store-library","xbox",binding.ProductId,"--extra"}, new[] {wrapper,"--uwp",binding.Aumid,binding.ManifestExecutable},
            new[] {@"C:\Other\Playhub.GameSession.exe","--store-library","xbox",binding.ProductId} })
            check(!UwpCompanionProtocol.ParentArgumentsMatch(foreign,wrapper,storePlan), "Another key/store/image/route or additional argv cannot reuse the managed binding");
        check(!UwpCompanionProtocol.ParentArgumentsMatch(actualStoreArgs,wrapper,storePlan with {StoreLaunch=null})
            && !UwpCompanionProtocol.ParentArgumentsMatch(actualStoreArgs,wrapper,storePlan with {Aumid="Other!App"})
            && !UwpCompanionProtocol.ParentArgumentsMatch(actualStoreArgs,wrapper,storePlan with {Executable="Other.exe"}),
            "Bare generic store argv or changed AUMID/manifest cannot authorize a companion");
        check(!storePlan.Matches(101,200,id)&&!storePlan.Matches(100,201,id)&&!storePlan.Matches(100,200,id+(1UL<<32)),
            "Store binding retains the exact parent PID, creation time and Steam shortcut proof");
        var descriptor = new StoreLaunchDescriptor(privateLaunch.Key,1,20,true,true,wrapper,Path.GetDirectoryName(wrapper),
            ["--store-library","xbox",binding.ProductId],GameExecutablePath:binding.GameExecutable);
        check(binding.MatchesBroker(descriptor,wrapper,binding.GameExecutable,now),
            "Independent current broker launch descriptor confirms exact product, wrapper tuple and registered absolute image");
        foreach (var foreign in new[] { descriptor with {Key=new(StoreKind.Xbox,"9NBLGGH1ZB7Y")},descriptor with {Key=new(StoreKind.Gog,binding.ProductId)},
            descriptor with {IsInstalled=false},descriptor with {IsLaunchable=false},descriptor with {ErrorCode="account_changed"},
            descriptor with {ExecutablePath=@"C:\Other\Playhub.GameSession.exe"},descriptor with {WorkingDirectory=@"C:\Other"},
            descriptor with {Arguments=["--store-library","xbox","9NBLGGH1ZB7Y"]},descriptor with {Arguments=["--store-library","xbox",binding.ProductId,"extra"]},
            descriptor with {GameExecutablePath=@"C:\Other\CosmoRun.exe"},descriptor with {GameExecutablePath=null} })
            check(!binding.MatchesBroker(foreign,wrapper,binding.GameExecutable,now),"Changed current broker key/account/tuple/image refuses the managed companion");
        check(!binding.MatchesBroker(descriptor,wrapper,@"C:\Other\CosmoRun.exe",now)
            && !(binding with {ExpiresAt=now}).MatchesBroker(descriptor,wrapper,binding.GameExecutable,now)
            && !(binding with {LaunchId=Guid.Empty}).MatchesBroker(descriptor,wrapper,binding.GameExecutable,now),
            "Registration mismatch, expired private receipt and empty launch ID cannot authorize a companion");
        check(binding.MatchesBroker(descriptor,wrapper,@"D:\PhysicalPackage\Binaries\CosmoRun.exe",now,@"D:\PhysicalPackage\Binaries\CosmoRun.exe")
            && !binding.MatchesBroker(descriptor,wrapper,@"D:\OtherPackage\Binaries\CosmoRun.exe",now,@"D:\PhysicalPackage\Binaries\CosmoRun.exe"),
            "A handle-resolved registered directory junction preserves the exact broker image, never another same-name package executable");
        var storeWire=JsonSerializer.Serialize(storePlan)+"\n";
        check(UwpCompanionProtocol.ReadLine<UwpCompanionPlan>(new MemoryStream(Encoding.UTF8.GetBytes(storeWire)),true,false).GetAwaiter().GetResult()==storePlan,
            "Private two-phase transport preserves the store binding without public command-line payload");
        var target = new UwpCompanionTarget(300, 400, 350);
        check(target.Matches(300, 400, 200, 500), "UWP target is a newly activated exact process");
        check(!target.Matches(301, 400, 200, 500) && !target.Matches(300, 401, 200, 500)
            && !target.Matches(300, 400, 351, 500) && !target.Matches(300, 400, 200, 399)
            && !target.Matches(300, 400, 200, 350 + TimeSpan.FromSeconds(16).Ticks),
            "UWP handoff refuses foreign, recycled, older, future and stale lifetimes");
        string messages = JsonSerializer.Serialize(plan) + "\n" + JsonSerializer.Serialize(target) + "\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(messages));
        check(UwpCompanionProtocol.ReadLine<UwpCompanionPlan>(stream, true, false).GetAwaiter().GetResult() == plan
            && UwpCompanionProtocol.ReadLine<UwpCompanionTarget>(stream, true, true).GetAwaiter().GetResult() == target,
            "Two-phase private transport cannot consume the later target before activation");
        foreach (string text in new[] { JsonSerializer.Serialize(target), JsonSerializer.Serialize(target) + "\nextra", "null\n", new string('x', 65536) })
            denied(() => UwpCompanionProtocol.ReadLine<UwpCompanionTarget>(new MemoryStream(Encoding.UTF8.GetBytes(text)), true, true).GetAwaiter().GetResult(),
                "Private UWP transport refuses incomplete, duplicate and oversized target messages");
        using var empty = new MemoryStream();
        check(UwpCompanionProtocol.ReadLine<UwpCompanionTarget>(empty, true, true).GetAwaiter().GetResult() is null,
            "Activation failure closes UWP preparation without selecting any process");
        CheckManifest(root, check, denied);
    }

    private static void CheckManifest(string root, Action<bool, string> check, Action<Action, string> denied)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "registered-uwp-proof")).FullName;
        string image = Path.Combine(folder, "Game.exe"), manifest = Path.Combine(folder, "AppxManifest.xml");
        File.Copy(Environment.ProcessPath!, image);
        var identity = XElement.Parse("<Identity Name='Playhub.Fixture' Publisher='CN=Playhub Fixture' Version='1.2.3.4' ProcessorArchitecture='x64'/>");
        string full = RegisteredUwpManifestProof.FullName(identity);
        string family = "Playhub.Fixture_" + full.Split('_')[4];
        var command = new XboxCommand(family + "!Game", "Game.exe", "");
        var launch = new AppContainerLaunchPlan(full, image, false);
        var package = new RegisteredPackage(full, folder);
        IReadOnlyList<RegisteredPackage> Registered(string _) => [package];
        void Save(XElement current) => File.WriteAllText(manifest, new XElement("Package", current).ToString());
        try
        {
            Save(new XElement(identity));
            using (RegisteredUwpManifestProof.Verify(command, launch, Registered))
                check(true, "Native Windows package identity matches all manifest publisher, version and architecture fields in a controlled fixture");
            foreach (var mutation in new[] { ("Publisher", "CN=Another Publisher"), ("Version", "1.2.3.5"), ("ProcessorArchitecture", "arm64"), ("Name", "Another.Fixture") })
            {
                var changed = new XElement(identity); changed.SetAttributeValue(mutation.Item1, mutation.Item2); Save(changed);
                denied(() => RegisteredUwpManifestProof.Verify(command, launch, Registered).Dispose(), "Partial package-name matches cannot authorize a changed native identity");
            }
            Save(new XElement(identity));
            denied(() => RegisteredUwpManifestProof.Verify(command, launch, _ => [package, package]).Dispose(), "Duplicate exact UWP registrations are refused");
            denied(() => RegisteredUwpManifestProof.Verify(command, launch, _ => []).Dispose(), "A removed package registration cannot authorize the companion");
            denied(() => RegisteredUwpManifestProof.Verify(command, launch with { Executable = Path.Combine(root, "Game.exe") }, Registered).Dispose(),
                "The native manifest proof refuses an image outside the exact registered directory");
        }
        finally { File.Delete(manifest); File.Delete(image); Directory.Delete(folder); }
    }
}
