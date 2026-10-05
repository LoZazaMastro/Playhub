using Playhub.Services;
using Playhub.GameSession;
using VDFParser.Models;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
const string aumid = "StudioMDHR.20872A364DAA1_tm1s6a95559gt!App";
const string helper = @"C:\Fixture\Playhub.GameSession.exe";
const int cuphead = unchecked((int)2485417537u);
string options = UwpShortcutArguments.Build(aumid, "Cuphead.exe", "--language \"it IT\"  --save-slot=2");
VDFEntry Create(VDFEntry? prior = null, bool local = false, string? args = null) =>
    XboxSteamShortcut.Create(prior, cuphead, "Cuphead", helper, "\"C:\\Fixture\"", "selected-icon.ico", args ?? options, local, 123);

var fresh = Create();
Check(fresh.AllowDesktopConfig == 0 && fresh.AllowOverlay == 1, "new UWP shortcut keeps game layout and overlay");
Check(Create(local: true, args: "--game \"C:\\Fixture\\Game.exe\" --profile=user").AllowDesktopConfig == 0,
    "managed Xbox executable uses game layout too");
Check(fresh.appid == cuphead && fresh.Exe == "\"" + helper + "\"" && fresh.LaunchOptions == options,
    "controller policy preserves launcher identity and raw game arguments");

foreach (int previousDesktop in new[] { 0, 1 })
{
    var prior = Create(); prior.appid = unchecked((int)0x91ABCDEFu); prior.Index = 34;
    prior.StartDir = @"C:\Original Launcher"; prior.AllowDesktopConfig = previousDesktop;
    byte[] before = VDFParser.VDFSerializer.Serialize(new[] { prior });
    var updated = Create(prior);
    Check(updated.AllowDesktopConfig == 0 && updated.appid == prior.appid && updated.Index == 34,
        $"re-import desktop={previousDesktop} repairs layout without changing existing app ID/index");
    Check(updated.StartDir == prior.StartDir && updated.LaunchOptions == options && updated.Icon == "selected-icon.ico",
        $"re-import desktop={previousDesktop} retains launch directory, arguments and selected icon");
    Check(before.SequenceEqual(VDFParser.VDFSerializer.Serialize(new[] { prior })), "building replacement never mutates input entry");
}

var custom = Create();
custom.Icon = @"C:\User Art\selected.ico"; custom.Tags = ["Favorites", "My collection"];
custom.LastPlayTime = 7654321; custom.IsHidden = 1; custom.ShortcutPath = "user-shortcut";
custom.OpenVR = 1; custom.Devkit = 1; custom.DevkitGameID = "user-kit";
var relink = Create(custom);
using (var metadata = new MemoryStream(VDFParser.VDFSerializer.Serialize([relink])))
{
    var saved = VDFParser.VDFParser.Parse(metadata)[0];
    Check(saved.Icon == custom.Icon && saved.Tags.SequenceEqual(custom.Tags) && saved.LastPlayTime == custom.LastPlayTime,
        "real VDF reimport retains user icon, collections and last-play timestamp");
    Check(saved.IsHidden == 1 && saved.ShortcutPath == custom.ShortcutPath && saved.OpenVR == 1 && saved.Devkit == 1 && saved.DevkitGameID == "user-kit",
        "unrelated shortcut metadata survives reimport serialization");
}
relink.Tags[0] = "Changed fixture";
Check(custom.Tags[0] == "Favorites", "replacement collection array cannot mutate existing user tags");

bool Matches(VDFEntry entry) => UwpShortcutArguments.Matches(entry.LaunchOptions, aumid);
var legacy = Create(); legacy.LaunchOptions = aumid + " Cuphead.exe --user-option";
var distinct = Create(); distinct.appid = 23; distinct.LaunchOptions = "Other.Package!App";
Check(!XboxSteamShortcut.HasAmbiguousMatches([[], [legacy, distinct]], Matches), "single legacy identity is safe across empty and unrelated profiles");
Check(!XboxSteamShortcut.HasAmbiguousMatches([[legacy], [Create()]], Matches), "same shortcut ID across profiles is unambiguous");
Check(XboxSteamShortcut.HasAmbiguousMatches([[legacy, Create()]], Matches), "legacy and managed duplicate in one profile are rejected");
var conflict = Create(); conflict.appid = 987;
Check(XboxSteamShortcut.HasAmbiguousMatches([[legacy], [conflict]], Matches), "conflicting profile IDs are rejected without choosing a winner");
Check(XboxSteamShortcut.ResolveAppId([[], [legacy, distinct]], Matches, 999) == cuphead,
    "adding a missing profile uses the existing legacy ID instead of generating a conflict");
Check(XboxSteamShortcut.ResolveAppId([[], [distinct]], Matches, 999) == 999,
    "brand-new game keeps its generated ID when no profile owns it");
bool refused = false;
try { XboxSteamShortcut.ResolveAppId([[legacy, Create()]], Matches, 999); }
catch (InvalidOperationException) { refused = true; }
Check(refused, "ambiguous identities cannot reach insertion through the shared resolver");

string fixtureRoot = Path.Combine(Path.GetTempPath(), "playhub-xbox-art-" + Guid.NewGuid().ToString("N"));
try
{
    foreach (string profile in new[] { "alpha", "beta" })
    {
        string user = Path.Combine(fixtureRoot, profile), grid = Path.Combine(user, "config", "grid");
        Directory.CreateDirectory(grid);
        uint artworkId = unchecked((uint)cuphead);
        var owned = new Dictionary<string, string> { ["cover"] = artworkId + "p", ["banner"] = artworkId.ToString(), ["hero"] = artworkId + "_hero", ["logo"] = artworkId + "_logo" };
        foreach (var asset in owned)
        {
            string file = Path.Combine(grid, asset.Value + ".jpeg");
            byte[] original = System.Text.Encoding.UTF8.GetBytes(profile + asset.Key);
            File.WriteAllBytes(file, original);
            bool wrote = false;
            XboxSteamShortcut.ApplyArtworkIfMissing(user, unchecked((uint)cuphead), asset.Key, "downloaded.png", (_, _, _, _) => wrote = true);
            Check(!wrote && File.ReadAllBytes(file).SequenceEqual(original), $"reimport preserves physical {profile}/{asset.Key} regardless of new automatic artwork");
        }
        uint missingId = 44;
        string source = Path.Combine(fixtureRoot, "chosen.png"); File.WriteAllBytes(source, [1, 4, 9]);
        XboxSteamShortcut.ApplyArtworkIfMissing(user, missingId, "hero", source,
            (_, id, _, path) => File.Copy(path, Path.Combine(grid, id + "_hero.png")));
        Check(File.ReadAllBytes(Path.Combine(grid, "44_hero.png")).SequenceEqual(File.ReadAllBytes(source)), "missing artwork still receives selected bytes in isolated physical grid");
    }
}
finally { if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, true); }

// All other construction inputs are identical: only this controller flag
// may differ in the actual serialized shortcut bytes.
var same = Create(); same.AllowDesktopConfig = 1;
byte[] oldBytes = VDFParser.VDFSerializer.Serialize(new[] { same });
byte[] fixedBytes = VDFParser.VDFSerializer.Serialize(new[] { fresh });
Check(oldBytes.Length == fixedBytes.Length && oldBytes.Zip(fixedBytes).Count(pair => pair.First != pair.Second) == 1,
    "binary shortcut regression changes exactly one desktop-config byte");

var unrelated = Create(); unrelated.appid = 42; unrelated.AppName = "Other user's game";
unrelated.AllowDesktopConfig = 1; unrelated.AllowOverlay = 0; unrelated.Tags = new[] { "User collection" };
byte[] unrelatedBefore = VDFParser.VDFSerializer.Serialize(new[] { unrelated });
var library = new[] { unrelated, fresh };
using var stream = new MemoryStream(VDFParser.VDFSerializer.Serialize(library));
var roundTrip = VDFParser.VDFParser.Parse(stream);
Check(roundTrip[1].AllowDesktopConfig == 0 && roundTrip[1].appid == cuphead && roundTrip[1].LaunchOptions == options,
    "actual VDF serialization roundtrip retains game layout, artwork key and arguments");
Check(unrelatedBefore.SequenceEqual(VDFParser.VDFSerializer.Serialize(new[] { roundTrip[0] })),
    "unrelated user's shortcut controller, overlay and collection fields unchanged");

var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root != null && !Directory.Exists(Path.Combine(root.FullName, "Source", "Playhub"))) root = root.Parent;
string service = File.ReadAllText(Path.Combine(root!.FullName, "Source", "Playhub", "Services", "UwpXboxService.cs"));
Check(service.Contains("XboxSteamShortcut.Create(existing, appId") && !service.Contains("AllowDesktopConfig = 1"),
    "real export applies tested policy before both insert and update");
Check(service.Contains("ApplySelectedArtworkForUser(game, user, unchecked((uint)imported.appid))"),
    "artwork integration continues to use the preserved imported shortcut ID");
Console.WriteLine($"Xbox Steam shortcut checks: {passed} passed.");
