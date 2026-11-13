using Playhub.Shared;
using System.Text.Json;
const string root=@"C:\Epic\Game";
string launch=Path.Combine(root,"Binaries","Win64","Bootstrapper.exe"), payload=Path.Combine(root,"Binaries","Win64","ActualGame.exe"),
 config=Path.Combine(root,"Binaries","Win64","EasyAntiCheat","Settings.json"), other=Path.Combine(root,"EasyAntiCheat","Settings.json");
var plan=new EpicInstalledLaunch("provider","catalog","artifact",@"C:\Metadata\fixture.item",new string('a',64),root,launch,"uri");
var files=new Dictionary<string,EpicPayloadFileState>(StringComparer.OrdinalIgnoreCase){{launch,EpicPayloadFileState.PlainFile},{payload,EpicPayloadFileState.PlainFile}};
string json=JsonSerializer.Serialize(new {executable="ActualGame.exe",productid="ignored",account_token="ignored"});
EpicPayloadFileState State(string f)=>files.GetValueOrDefault(f,EpicPayloadFileState.Missing);
EpicLaunchPayload? Read()=>EpicLaunchPayloadResolver.Resolve(plan,State,_=>json);
int count=0;void Check(bool b,string why){if(!b)throw new Exception(why);count++;}
Check(Read()==new EpicLaunchPayload(launch),"no config preserves declared target without classifying it as a game");
files[config]=EpicPayloadFileState.PlainFile;var expected=Read();
Check(expected?.Executable==payload&&expected.ConfigurationPath==config,"official payload descriptor resolves real game instead of bootstrapper");
Check(expected?.ConfigurationSha256?.Length==64&&!expected.Executable.Contains("Bootstrapper"),"immutable descriptor fingerprint bound without returning account fields");
json="{\"executable\":\"../../../../outside.exe\"}";Check(Read() is null,"escaping descriptor refused");
json="{\"executable\":\"C:\\\\outside.exe\"}";Check(Read() is null,"absolute descriptor refused");
json="{\"executable\":\"ActualGame.exe -argument\"}";Check(Read() is null,"descriptor is never argv");
json="{\"executable\":\"ActualGame.exe\"}";files[payload]=EpicPayloadFileState.Missing;Check(Read() is null,"missing payload cannot fall back to bootstrapper");
files[payload]=EpicPayloadFileState.PlainFile;files[config]=EpicPayloadFileState.Unavailable;Check(Read() is null,"unknown or reparse config is not positive absence");
files[config]=EpicPayloadFileState.PlainFile;files[other]=EpicPayloadFileState.PlainFile;Check(Read() is null,"multiple config roots ambiguous");
files.Remove(other);json="[]";Check(Read() is null,"nonobject refused");
json="{\"productid\":\"ignored\"}";Check(Read() is null,"missing executable unavailable");
json="{\"executable\":\"ActualGame.exe\",\"padding\":\""+new string('x',65536)+"\"}";Check(Read() is null,"oversized metadata bounded");
json="{\"executable\":\"ActualGame.exe\"}";var changed=Read();Check(changed?.Executable==expected?.Executable&&changed?.ConfigurationSha256!=expected?.ConfigurationSha256,"config mutation changes lease fingerprint");
Check(EpicLaunchPayloadResolver.Resolve(plan,State,_=>throw new IOException("fixture")) is null,"read failure stays unavailable without raw fallback");
var visited=new List<string>();
FileAttributes Dangling(string path){visited.Add(path);if(path.EndsWith("EasyAntiCheat",StringComparison.OrdinalIgnoreCase))return FileAttributes.Directory|FileAttributes.ReparsePoint;if(path.EndsWith("Settings.json"))throw new FileNotFoundException();return FileAttributes.Directory;}
Check(EpicLaunchPayloadResolver.ReadState(config,Dangling)==EpicPayloadFileState.Unavailable&&!visited.Contains(config),"dangling reparse parent is unavailable before querying its missing leaf");
FileAttributes Missing(string path){if(path.EndsWith("EasyAntiCheat"))throw new DirectoryNotFoundException();return FileAttributes.Directory;}
Check(EpicLaunchPayloadResolver.ReadState(config,Missing)==EpicPayloadFileState.Missing,"positive missing plain parent remains an ordinary absent optional configuration");
Console.WriteLine($"{count} actual Epic payload metadata checks PASS; no process, launch or native API calls.");
