using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Playhub.GameSession;

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
