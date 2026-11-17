using System.Text;
using System.Xml.Linq;
using Microsoft.Win32.SafeHandles;
using Playhub.GameSession;

internal static class RegisteredDesktopChecks
{
    internal static void Run(string root, Action<bool, string> check, Action<Action, string> denied)
    {
        XNamespace foundation = RegisteredXboxApplication.Foundation, uap = RegisteredXboxApplication.Uap10;
        XElement App(string? entry, string? runtime = null, string? trust = null)
        {
            var result = new XElement(foundation + "Application", new XAttribute("Id", "Game"), new XAttribute("Executable", "GameLaunchHelper.exe"));
            if (entry is not null) result.SetAttributeValue("EntryPoint", entry);
            if (runtime is not null) result.SetAttributeValue(uap + "RuntimeBehavior", runtime);
            if (trust is not null) result.SetAttributeValue(uap + "TrustLevel", trust);
            return result;
        }
        foreach (var app in new[] { App("Windows.FullTrustApplication"), App("windows.fullTrustApplication", "packagedClassicApp", "mediumIL"),
            App(null, "packagedClassicApp", "mediumIL"), App(null, "win32App", "mediumIL") })
            check(RegisteredXboxApplication.Classify(app) == RegisteredXboxApplicationKind.PackagedDesktop,
                "Registered desktop classification follows explicit Microsoft runtime and trust metadata");
        check(RegisteredXboxApplication.Classify(App("Game.App", "windowsApp", "appContainer")) == RegisteredXboxApplicationKind.AppContainer
            && RegisteredXboxApplication.Classify(App("Game.App")) == RegisteredXboxApplicationKind.AppContainer,
            "Classic UWP keeps its distinct AppContainer route");
        foreach (var app in new[] { App("Windows.FullTrustApplication", "windowsApp", "appContainer"),
            App("Windows.PartialTrustApplication", "packagedClassicApp", "mediumIL"), App("Game.App", "win32App", "mediumIL"),
            new XElement("Application", new XAttribute("EntryPoint", "Windows.FullTrustApplication")),
            new XElement(foundation + "Application", App("Windows.FullTrustApplication").Attributes(), new XAttribute(XName.Get("TrustLevel", "urn:untrusted"), "mediumIL")),
            new XElement(foundation + "Application", App(null, "win32App", "mediumIL").Attributes(), new XAttribute(XName.Get("TrustLevel", "urn:duplicate"), "mediumIL")) })
            denied(() => RegisteredXboxApplication.Classify(app), "Unknown namespaces, duplicate trust fields and conflicting desktop entry points are refused");
        check(RegisteredXboxApplication.Classify(App(null, "packagedClassicApp", "appContainer")) == RegisteredXboxApplicationKind.Unsupported
            && RegisteredXboxApplication.Classify(App(null, "win32App")) == RegisteredXboxApplicationKind.Unsupported,
            "An unsupported trust combination never becomes a desktop authorization");

        var folder = Directory.CreateDirectory(Path.Combine(root, "registered-desktop")).FullName;
        var identity = new XElement(foundation + "Identity", new XAttribute("Name", "Playhub.DesktopFixture"),
            new XAttribute("Publisher", "CN=Playhub Desktop Fixture"), new XAttribute("Version", "1.2.3.4"), new XAttribute("ProcessorArchitecture", "x64"));
        string full = RegisteredUwpManifestProof.FullName(identity), family = "Playhub.DesktopFixture_" + full.Split('_')[4];
        var command = new XboxCommand(family + "!Game", "Game.exe", "--original unchanged");
        var package = new RegisteredPackage(full, folder);
        string manifest = Path.Combine(folder, "AppxManifest.xml"), config = Path.Combine(folder, "MicrosoftGame.Config");
        void Save(XElement? application = null, bool duplicate = false)
        {
            File.WriteAllText(manifest, new XElement(foundation + "Package", identity,
                new XElement(foundation + "Applications", application ?? App("Windows.FullTrustApplication"))).ToString());
            var configured = new XElement("Game", new XElement("Identity", identity.Attributes().Where(a => a.Name.LocalName != "ProcessorArchitecture").Select(a => new XAttribute(a))),
                new XElement("ExecutableList", new XElement("Executable", new XAttribute("Id", "Game"), new XAttribute("Name", "Game.exe"))));
            if (duplicate) configured.Element("ExecutableList")!.Add(new XElement("Executable", new XAttribute("Id", "Game"), new XAttribute("Name", "Other.exe")));
            File.WriteAllText(config, configured.ToString());
        }
        File.Copy(Environment.ProcessPath!, Path.Combine(folder, "GameLaunchHelper.exe"));
        File.Copy(Environment.ProcessPath!, Path.Combine(folder, "Game.exe"));
        Save();
        var catalog = new Catalog([package]);
        check(RegisteredXboxApplication.Resolve(command, catalog) == RegisteredXboxApplicationKind.PackagedDesktop,
            "Registered family, complete package version and manifest select the desktop route");
        using (var preparation = RegisteredDesktopPreparation.Prepare(command, catalog))
        {
            check(preparation.PackageFullName == full && preparation.Executable == Path.Combine(folder, "Game.exe")
                && preparation.Plan.Helper == Path.Combine(folder, "GameLaunchHelper.exe"),
                "GameLaunchHelper remains the activation helper while MicrosoftGame.Config identifies the exact game");
            denied(() => File.Open(manifest, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose(), "Manifest stays pinned through activation and game lifetime");
            denied(() => File.Open(config, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose(), "MicrosoftGame.Config stays pinned through activation and game lifetime");
            var handoff = new UwpCompanionTarget(200, 400, 350);
            var observed = new RegisteredDesktopIdentity(200, 400, 3, preparation.Executable, full, family, "", true, false, true);
            check(observed.Matches(command, preparation.Plan, handoff, 3, 300, 500), "A new exact registered desktop process may have no process AUMID");
            check((observed with { Aumid = command.Aumid }).Matches(command, preparation.Plan, handoff, 3, 300, 500), "A published desktop AUMID must match the original activation");
            foreach (var invalid in new[] { observed with { Birth = 340 }, observed with { Pid = 201 }, observed with { Birth = 401 },
                observed with { Session = 4 }, observed with { Package = full + "other" }, observed with { Family = "other" },
                observed with { Image = Path.Combine(root, "Game.exe") }, observed with { Aumid = family + "!Other" },
                observed with { AppContainer = true }, observed with { NativeX64 = false }, observed with { SameUser = false } })
                check(!invalid.Matches(command, preparation.Plan, handoff, 3, 300, 500), "Desktop target refuses old/recycled/foreign process, package, trust and activation identities");
        }
        denied(() => RegisteredDesktopPreparation.Prepare(command with { Executable = "GameLaunchHelper.exe" }, catalog).Dispose(), "The helper can never authorize itself as the game");
        var optionalId = XElement.Load(config);
        optionalId.Element("ExecutableList")!.Element("Executable")!.Attribute("Id")!.Remove();
        optionalId.Element("ExecutableList")!.Add(new XElement("Executable", new XAttribute("Name", "MissingDebug.exe"), new XAttribute("IsDevOnly", "true")));
        optionalId.Save(config);
        using (var preparation = RegisteredDesktopPreparation.Prepare(command, catalog))
            check(preparation.Executable == Path.Combine(folder, "Game.exe"), "Optional GDK Id uses one registered application and one retail PC executable, as in Killer Instinct");
        check(GdkLaunchResolver.Resolve(command.Aumid, command.Executable, catalog).Game == Path.Combine(folder, "Game.exe"),
            "Native GDK resolver and companion preparation use the same optional-Id contract");
        denied(() => RegisteredDesktopPreparation.ValidateConfiguration(optionalId, identity, "Game", Path.Combine(folder, "Game.exe"), folder, 2),
            "Missing GDK Id never guesses between multiple registered applications");
        optionalId.Element("ExecutableList")!.Add(new XElement("Executable", new XAttribute("Name", "Other.exe")));
        optionalId.Save(config);
        denied(() => RegisteredDesktopPreparation.Prepare(command, catalog).Dispose(), "Multiple retail images without Id cannot authorize the companion");
        Save(duplicate: true);
        denied(() => RegisteredDesktopPreparation.Prepare(command, catalog).Dispose(), "Duplicate ExecutableList IDs refuse ambiguous desktop games");
        Save(App("Game.App", "windowsApp", "appContainer"));
        denied(() => RegisteredDesktopPreparation.Prepare(command, catalog).Dispose(), "True AppContainer games cannot enter the desktop target route");
        Save();
        denied(() => RegisteredXboxApplication.Resolve(command, new Catalog([package, package])), "Duplicate registered packages cannot authorize desktop activation");

        File.Copy(Environment.ProcessPath!, Path.Combine(folder, "OtherLauncher.exe"));
        // A direct full-trust title still requires the SAME registered AppId,
        // manifest image and MicrosoftGame.Config image; no launcher guessing.
        Save();
        var directManifest = XElement.Load(manifest);
        directManifest.Element(foundation + "Applications")!.Element(foundation + "Application")!.SetAttributeValue("Executable", "Game.exe");
        directManifest.Save(manifest);
        using (var direct = RegisteredDesktopPreparation.Prepare(command, catalog))
            check(direct.Executable == Path.Combine(folder, "Game.exe") && direct.Plan.Helper == direct.Executable,
                "Direct full-trust activation binds the exact declared and configured game image");
        foreach (string activation in new[] { "OtherLauncher.exe", "../Game.exe", "C:Game.exe", "Game.exe:stream" })
        {
            directManifest.Element(foundation + "Applications")!.Element(foundation + "Application")!.SetAttributeValue("Executable", activation);
            directManifest.Save(manifest);
            denied(() => RegisteredDesktopPreparation.Prepare(command, catalog).Dispose(),
                "A divergent launcher or unsafe manifest path cannot authorize a direct game");
        }
        directManifest.Element(foundation + "Applications")!.Element(foundation + "Application")!.SetAttributeValue("Executable", "Game.exe");
        directManifest.Save(manifest);
        var wrongConfig = XElement.Load(config);
        wrongConfig.Element("ExecutableList")!.Element("Executable")!.SetAttributeValue("Name", "Other.exe");
        wrongConfig.Save(config);
        denied(() => RegisteredDesktopPreparation.Prepare(command, catalog).Dispose(), "Direct activation never bypasses MicrosoftGame.Config image equality");
        Save();
        // Reassert the original helper/game separation after direct-route tests.
        using (var original = RegisteredDesktopPreparation.Prepare(command, catalog))
            check(original.Plan.Helper == Path.Combine(folder, "GameLaunchHelper.exe") && original.Executable == Path.Combine(folder, "Game.exe"),
                "The original registered Xbox helper remains distinct from its game");
        denied(() => RegisteredDesktopPreparation.Prepare(command with { Executable = "GameLaunchHelper.exe" }, catalog).Dispose(),
            "The original launch helper still cannot identify itself as a game");

        using var fake = new SafeProcessHandle(0, false);
        int Absent(SafeProcessHandle _, ref uint length, StringBuilder? value) => 15703;
        check(RegisteredDesktopTarget.ReadAppModel(fake, Absent, true) == "", "Only Windows NoApplication permits a missing desktop process AUMID");
        denied(() => RegisteredDesktopTarget.ReadAppModel(fake, Absent, false), "A missing PFN or family is never optional");
        foreach (int error in new[] { 5, 87, 15700, 122 })
        {
            int Failure(SafeProcessHandle _, ref uint length, StringBuilder? value) => error;
            denied(() => RegisteredDesktopTarget.ReadAppModel(fake, Failure, true), "Access, buffer and identity errors are not misrepresented as absent AUMIDs");
        }
    }
    private sealed class Catalog(IReadOnlyList<RegisteredPackage> packages) : IRegisteredPackageCatalog
    { public IReadOnlyList<RegisteredPackage> ForFamily(string _) => packages; }
}
