using System.Collections.ObjectModel;
using Playhub.Importing;
using Playhub.Models;

internal static class CatalogChecks
{
    private static int _checks;
    private static void Check(bool value, string reason)
    { _checks++; if (!value) throw new InvalidOperationException(reason); }
    internal static UwpGameEntry Game(string name = "A", string app = "a", string path = @"C:\Games\A.exe") => new()
    { Name = name, Aumid = path, Executable = path, LocalExecutablePath = path, IsLocalExecutable = true,
        EpicNamespace = "sandbox", EpicCatalogItemId = "catalog", EpicAppName = app };
    internal static void Run()
    {
        var old = Game(); old.Selected = true; old.SteamGridDbCoverPath = "retained-artwork";
        var rows = new ObservableCollection<UwpGameEntry> { old };
        Check(!InstalledGameCatalogReconciliation.Apply(rows, new[] { Game() }), "unchanged scan is no delta");
        Check(ReferenceEquals(rows[0], old) && rows[0].Selected && rows[0].SteamGridDbCoverPath == "retained-artwork", "unchanged object, selection and artwork retained");
        var update = Game(path: @"C:\Games\Version2\A.exe"); update.SourceLaunchArguments = "--new-authored-launch";
        Check(InstalledGameCatalogReconciliation.Apply(rows, new[] { update }), "same Epic title version is a delta");
        Check(ReferenceEquals(rows[0], update) && update.Selected && update.SourceLaunchArguments == "--new-authored-launch", "fresh launch path and arguments with retained pending selection");
        var foreign = Game(app: "other");
        Check(InstalledGameCatalogReconciliation.Apply(rows, new[] { foreign }) && !foreign.Selected, "selection does not cross Epic application identity");
        var otherNamespace = Game(); otherNamespace.EpicNamespace = "other";
        Check(InstalledGameCatalogReconciliation.Apply(rows, new[] { otherNamespace }) && !otherNamespace.Selected, "namespace changes are a different identity");
        var bad = Game(); bad.EpicNamespace = "sandbox\0catalog";
        rows = new() { Game() }; rows[0].Selected = true;
        Check(InstalledGameCatalogReconciliation.Apply(rows, new[] { bad }) && !bad.Selected, "separator/invalid identity cannot inherit selection");
        var partial = Game(); partial.EpicCatalogItemId = "";
        Check(InstalledGameCatalogReconciliation.Apply(rows, new[] { partial }) && !partial.Selected, "partial Epic identity cannot fall back to path");
        var duplicateA = Game(); var duplicateB = Game(); rows = new() { Game() }; rows[0].Selected = true;
        InstalledGameCatalogReconciliation.Apply(rows, new[] { duplicateA, duplicateB });
        Check(rows.Count == 2 && !duplicateA.Selected && !duplicateB.Selected, "incoming ambiguous identity never transfers selected state");
        rows = new() { Game(), Game() }; rows[0].Selected = true; var unique = Game();
        InstalledGameCatalogReconciliation.Apply(rows, new[] { unique });
        Check(!unique.Selected, "ambiguous previous identity never transfers selected state");
        old = Game(); old.EpicNamespace = old.EpicCatalogItemId = old.EpicAppName = ""; old.Selected = true;
        var local = Game(); local.EpicNamespace = local.EpicCatalogItemId = local.EpicAppName = "";
        rows = new() { old };
        Check(!InstalledGameCatalogReconciliation.Apply(rows, new[] { local }) && ReferenceEquals(rows[0], old), "same GOG executable retains object");
        var differentTask = Game(); differentTask.EpicNamespace = differentTask.EpicCatalogItemId = differentTask.EpicAppName = "";
        differentTask.SourceLaunchArguments = "--other-primary-task";
        Check(InstalledGameCatalogReconciliation.Apply(rows, new[] { differentTask }) && !differentTask.Selected, "same exe with different GOG task is not proven same selection");
        var moved = Game(path: @"C:\Games\Different.exe"); moved.EpicNamespace = moved.EpicCatalogItemId = moved.EpicAppName = "";
        Check(InstalledGameCatalogReconciliation.Apply(rows, new[] { moved }) && !moved.Selected, "GOG path change has no provider ID proof and does not inherit selection");
        var installed = Game(); installed.InSteamLibrary = true; rows = new() { Game() };
        Check(InstalledGameCatalogReconciliation.Apply(rows, new[] { installed }) && rows[0].InSteamLibrary, "fresh Steam library observation applied");
        var disabled = Game(); disabled.SteamGridDbArtworkDisabled = true; rows = new() { Game() };
        Check(InstalledGameCatalogReconciliation.Apply(rows, new[] { disabled }) && rows[0].SteamGridDbArtworkDisabled, "new disabled-artwork preference applied");
        old = Game(); old.SteamGridDbGameId = 7; rows = new() { old };
        Check(!InstalledGameCatalogReconciliation.Apply(rows, new[] { Game() }), "resolved automatic metadata survives scan without manual override");
        var overrideId = Game(); overrideId.SteamGridDbGameId = 9;
        Check(InstalledGameCatalogReconciliation.Apply(rows, new[] { overrideId }) && rows[0].SteamGridDbGameId == 9, "fresh nonzero manual metadata override applied");
        Check(InstalledGameCatalogReconciliation.Apply(rows, Array.Empty<UwpGameEntry>()) && rows.Count == 0, "completed explicit empty scan clears import candidates only");
        Check(!InstalledGameCatalogReconciliation.Apply(rows, Array.Empty<UwpGameEntry>()), "repeated empty scan is no delta");
        Console.WriteLine($"PASS helper {_checks} checks; no filesystem, registry, Steam or process actions");
    }
}

internal static class Program { private static void Main() => CatalogChecks.Run(); }
