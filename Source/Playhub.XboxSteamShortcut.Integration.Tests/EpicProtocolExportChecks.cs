using System.Reflection;
using System.Text.Json;
using Playhub.Models;
using Playhub.Services;
using VDFParser.Models;

internal static class EpicProtocolExportChecks
{
    internal static int Run(string root)
    {
        int checks=0;
        void Check(bool condition,string why){if(!condition)throw new Exception(why);checks++;}
        string executable=Path.Combine(root,"Epic Protocol Fixture","FortniteBootstrapper.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);File.WriteAllBytes(executable,[]);
        var game=new UwpGameEntry{Name="Fortnite",IsLocalExecutable=true,LocalExecutablePath=executable,Executable=executable,Aumid=executable,
            EpicNamespace="fn",EpicCatalogItemId="4fe75bbc5a674f4f9b356b5c90567da5",EpicAppName="Fortnite",SourceLaunchArguments="-fixture-metadata-command"};
        string target="\""+executable+"\"";
        const int original=unchecked((int)0xb2345678);
        var direct=new VDFEntry{appid=original,Exe=target,LaunchOptions=game.SourceLaunchArguments,
            StartDir="\"C:\\User directory\"",Icon="user.ico",Tags=["User collection"]};
        string options=UwpXboxService.BuildLaunchOptions(game,direct,target);
        var parsed=CommandLine.Parse(options);
        Check(parsed.Count==5&&parsed[0]=="--epic"&&parsed[1]=="fn"&&parsed[4]==executable,"Epic export selects protocol GameSession entry with exact recorded target identity");
        Check(!options.Contains("fixture-metadata-command")&&!options.Contains("Shipping.exe"),"normal launcher supplies launch/auth chain; no raw arguments or Shipping substitution");
        var managed=XboxSteamShortcut.Create(direct,12,game.Name,"C:\\Playhub\\Playhub.GameSession.exe",UwpXboxService.LocalStartDirectory(game),"",options,true,0);
        Check(managed.appid==original&&managed.StartDir==direct.StartDir&&managed.Icon==direct.Icon&&managed.Tags.SequenceEqual(direct.Tags),"normal Epic repair preserves existing AppID/icon/collections/startdir");
        Check(UwpXboxService.BuildLaunchOptions(game,managed,target)==options,"Epic reimport remains one wrapper");
        var matches=typeof(UwpXboxService).GetMethod("MatchesGame",BindingFlags.NonPublic|BindingFlags.Static)!;
        Check((bool)matches.Invoke(null,[direct,game])!,"existing direct shortcut still matches before repair");
        var legacy=new VDFEntry{appid=original,Exe="\"C:\\Playhub\\Playhub.GameSession.exe\"",LaunchOptions="--game "+target+" "+game.SourceLaunchArguments};
        Check((bool)matches.Invoke(null,[legacy,game])!,"existing --game Epic wrapper matches for controlled repair");
        Check((bool)matches.Invoke(null,[managed,game])!,"new --epic wrapper is recognized on scan/reimport");
        var updated=new UwpGameEntry{Name=game.Name,IsLocalExecutable=true,LocalExecutablePath=Path.Combine(root,"New Version","FortniteBootstrapper.exe"),
            EpicNamespace=game.EpicNamespace,EpicCatalogItemId=game.EpicCatalogItemId,EpicAppName=game.EpicAppName};
        Check((bool)matches.Invoke(null,[managed,updated])!,"exact catalog identity retains AppID across a new installed target path");
        var other=new UwpGameEntry{Name="Other",IsLocalExecutable=true,LocalExecutablePath=executable,EpicNamespace="other",EpicCatalogItemId="item",EpicAppName="app"};
        Check(!(bool)matches.Invoke(null,[managed,other])!,"another Epic catalog cannot adopt the prior wrapper");
        string manifest=Path.Combine(root,"missing-epic-identity.item");
        File.WriteAllText(manifest,JsonSerializer.Serialize(new{InstallLocation=Path.GetDirectoryName(executable),LaunchExecutable=Path.GetFileName(executable),
            DisplayName="Auth game",bRequiresAuth=true,bIsApplication=true,AppCategories=new[]{"games"}}));
        var parse=typeof(EpicGamesService).GetMethod("TryParse",BindingFlags.NonPublic|BindingFlags.Static)!;
        Check(parse.Invoke(null,[manifest]) is null,"unresolvable Epic catalog never silently falls back to raw EXE import");
        return checks;
    }
}
