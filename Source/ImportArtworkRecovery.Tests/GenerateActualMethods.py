"""Extract production control-flow verbatim; substitute only filesystem roots/Steam write boundary."""
from pathlib import Path
import os
import re

root = Path(__file__).resolve().parents[1]
source = Path(os.environ.get('PLAYHUB_IMPORT_ARTWORK_SOURCE', str(root / 'Playhub/Services/UwpXboxService.cs')))
text = source.read_text('utf-8-sig')
names = ['PopulateSteamGridDbCoversAsync','SearchSteamGridDbGamesAsync','ReadReleaseYear','RefreshSteamGridDbCoverAsync','GetSteamGridDbArtworkAsync','GetOfficialSteamArtworkAsync',
         'ResolveSteamAppIdAsync','NormalizeTitle','RemoteFileExistsAsync','DownloadAndApplySteamGridDbArtworkAsync',
         'TryDownloadSteamGridDbCoverAsync','FindSteamGridDbGameIdAsync','NormalizeArtworkType','NormalizeImageExtension',
         'SetSelectedArtworkPath','FindExistingImage','PopulateMissingSteamGridDbArtworkAsync','GetSelectedArtworkPath']
methods = []
for name in names:
    match = re.search(r'^    (?:public|private) [^\n]*\b' + name + r'\s*\(', text, re.M)
    if not match:
        raise RuntimeError('Missing actual method: ' + name)
    start = match.start(); brace = text.index('{', match.end()); at = brace; depth = 0
    while at < len(text):
        if text[at] == '{': depth += 1
        elif text[at] == '}':
            depth -= 1
            if depth == 0: break
        at += 1
    if depth: raise RuntimeError('Unbalanced actual method: ' + name)
    methods.append(text[start:at+1])
header = '''#nullable enable
using System.Net; using System.Text; using System.Text.Json; using Playhub.Models;
namespace Playhub.Services;
public sealed record SteamGridArtworkOption(string Url,string PreviewUrl,int Width,int Height,string? Provider=null,string? AuthorName=null,string? AuthorSteamId=null);
public sealed record SteamGridGameOption(int Id,string Name,int? ReleaseYear,bool Verified);
public static class AppPaths { public static string LocalDataRoot {get;set;}=""; }
public sealed partial class UwpXboxService {
private readonly HttpClient _http;
public UwpXboxService(HttpMessageHandler handler) { _http=new HttpClient(handler); }
public string CoverFormat {get;set;}=global::Playhub.Services.CoverFormat.Vertical;
public Task RunMissing(List<UwpGameEntry> games,string key)=>PopulateMissingSteamGridDbArtworkAsync(games,key);
public Task RunCovers(List<UwpGameEntry> games,string key)=>PopulateSteamGridDbCoversAsync(games,key);
private static bool ApplyArtworkToExistingSteamShortcuts(UwpGameEntry game,string type,string path)=>File.Exists(path);
'''
destination = Path(__file__).parent / 'ActualMethods.Generated.cs'
destination.write_text(header + '\n'.join(methods) + '\n}', encoding='utf-8')
print('Extracted ' + str(len(methods)) + ' actual production methods from ' + str(source))
