// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Playhub.StoreLibrary;

internal sealed record StoreInstallOptions(ProviderInstallSize Size,string[] Languages,string? Language);
internal interface IStoreInstallLanguageSource
{
    Task<StoreInstallOptions> GetInstallOptionsAsync(string id,string? preferred,CancellationToken token);
}
internal interface IStorePartialLanguageDownloadSource
{
    Task<StorePartialManifest> ReadLanguagePartialManifestAsync(string id,string? language,CancellationToken token);
}
public sealed partial class StoreCliProvider:IStoreInstallLanguageSource
{
    internal static bool ValidInstallLanguage(string? language)=>language is {Length:>=2 and <=20}&&Regex.IsMatch(language,"^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8}){0,2}$",RegexOptions.CultureInvariant);
    internal static string? ChooseInstallLanguage(IEnumerable<string> values,string? preferred)
    {
        var languages=values.Where(ValidInstallLanguage).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return languages.FirstOrDefault(v=>string.Equals(v,preferred,StringComparison.OrdinalIgnoreCase))??
            (preferred is null?null:languages.FirstOrDefault(v=>v.Split('-')[0].Equals(preferred.Split('-')[0],StringComparison.OrdinalIgnoreCase)))??
            languages.FirstOrDefault(v=>v.Equals("en-US",StringComparison.OrdinalIgnoreCase))??languages.FirstOrDefault(v=>v.Split('-')[0]=="en")??languages.FirstOrDefault();
    }
    async Task<StoreInstallOptions> IStoreInstallLanguageSource.GetInstallOptionsAsync(string id,string? preferred,CancellationToken token)
    {
        ValidateId(id);
        if(Store==StoreKind.Epic){var size=await GetInstallSizeAsync(id,token);var languages=await EpicInstallLanguagesAsync(id,token);return new(size,languages,ChooseInstallLanguage(languages,preferred));}
        using var scope=OpenQuery(token);
        var response=await query(tool,["--auth-config-path",scope.AuthPath,"info",id,"--platform","windows","--skip-dlcs"],scope.Environment,token);
        if(response.Code!=0)throw new StoreOperationException("install_size_unavailable");
        using var document=JsonDocument.Parse(response.Json);return GogInstallOptions(document.RootElement,preferred);
    }
    internal static StoreInstallOptions GogInstallOptions(JsonElement root,string? preferred)
    {
        if(!root.TryGetProperty("size",out var sizes)||sizes.ValueKind!=JsonValueKind.Object||sizes.EnumerateObject().Count()>256)throw new StoreOperationException("install_size_invalid");
        var languages=sizes.EnumerateObject().Select(p=>p.Name).Where(ValidInstallLanguage).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string? selected=ChooseInstallLanguage(languages,preferred);long disk=0,download=0;int count=0;
        foreach(var row in sizes.EnumerateObject().Where(p=>p.Name is "*" or "Neutral"||p.Name==selected))
        {
            if(!row.Value.TryGetProperty("disk_size",out var d)||!d.TryGetInt64(out var db)||db<0||!row.Value.TryGetProperty("download_size",out var b)||!b.TryGetInt64(out var bb)||bb<0)throw new StoreOperationException("install_size_invalid");
            try{disk=checked(disk+db);download=checked(download+bb);}catch(OverflowException){throw new StoreOperationException("install_size_invalid");}count++;
        }
        if(count==0)throw new StoreOperationException("install_size_invalid");return new(new(disk,download),languages,selected);
    }
    async Task<string[]> EpicInstallLanguagesAsync(string id,CancellationToken token)
    {
        try
        {
            var file=Path.Combine(Path.GetDirectoryName(nativeAuth)!,"metadata",id+".json");if(!File.Exists(file)||new FileInfo(file).Length>2*1024*1024)return [];
            using var metadata=JsonDocument.Parse(await File.ReadAllTextAsync(file,token));var root=metadata.RootElement;
            if(root.GetProperty("app_name").GetString()!=id)return [];var game=root.GetProperty("metadata");string? ns=game.GetProperty("namespace").GetString(),title=game.GetProperty("title").GetString();
            if(ns is not{Length:>0 and <=128}||title is not{Length:>0 and <=500})return [];
            var slug=Regex.Replace(title.Normalize(NormalizationForm.FormKD).ToLowerInvariant(),"[^a-z0-9]+","-").Trim('-');if(slug.Length is <1 or >200)return [];
            using var response=await http.GetAsync("https://store-content.ak.epicgames.com/api/en-US/content/products/"+slug,HttpCompletionOption.ResponseHeadersRead,token);
            if(!response.IsSuccessStatusCode||response.Content.Headers.ContentLength>2*1024*1024)return [];
            using var memory=new MemoryStream();await CopyBounded(await response.Content.ReadAsStreamAsync(token),memory,token);
            using var document=JsonDocument.Parse(memory.ToArray());return ReadEpicContentLanguages(document.RootElement,ns);
        }
        catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or HttpRequestException or IOException){return [];}
    }
    internal static string[] ReadEpicContentLanguages(JsonElement root,string expectedNamespace)
    {
        if(!root.TryGetProperty("namespace",out var ns)||ns.GetString()!=expectedNamespace||!root.TryGetProperty("pages",out var pages)||pages.ValueKind!=JsonValueKind.Array)return [];
        var names=new List<string>();
        foreach(var page in pages.EnumerateArray())if(page.TryGetProperty("data",out var data)&&data.TryGetProperty("requirements",out var requirements)&&requirements.TryGetProperty("languages",out var languages)&&languages.ValueKind==JsonValueKind.Array)
            foreach(var value in languages.EnumerateArray())if(value.ValueKind==JsonValueKind.String&&value.GetString() is {Length:>0 and <=8192} text&&!text.StartsWith("AUDIO:",StringComparison.OrdinalIgnoreCase))
                names.AddRange(Regex.Replace(text,"^TEXT:\\s*","",RegexOptions.IgnoreCase).Split([',','|'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries));
        string? Code(string name)=>name.ToLowerInvariant() switch{"chinese - simplified" or "simplified chinese"=>"zh-CN","chinese - traditional" or "traditional chinese"=>"zh-TW","spanish - spain"=>"es-ES","spanish - latin america"=>"es-MX","portuguese - brazil" or "brazilian portuguese"=>"pt-BR","portuguese"=>"pt-PT","english"=>"en-US",_=>CultureInfo.GetCultures(CultureTypes.NeutralCultures).FirstOrDefault(c=>c.EnglishName.Equals(name,StringComparison.OrdinalIgnoreCase)) is {} neutral ? CultureInfo.CreateSpecificCulture(neutral.Name).Name : null};
        return names.Select(Code).OfType<string>().Where(ValidInstallLanguage).Distinct(StringComparer.OrdinalIgnoreCase).Take(128).ToArray();
    }
    sealed record InstalledLanguage(string Id,string Path,string Language);
    string LanguagePath(string id)=>Path.Combine(root,"language-"+id+".json");
    void SaveInstalledLanguage(TransferRequest request)
    {
        if(!ValidInstallLanguage(request.Language))return;var path=LanguagePath(request.Key.Id);var temporary=path+"."+Guid.NewGuid().ToString("N")+".new";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(new InstalledLanguage(request.Key.Id,Path.GetFullPath(request.InstallPath),request.Language!)));File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    string? ReadInstalledLanguage(string id,string installPath)
    {
        try{var path=LanguagePath(id);if(!File.Exists(path)||new FileInfo(path).Length>8192)return null;var choice=JsonSerializer.Deserialize<InstalledLanguage>(File.ReadAllText(path));return choice?.Id==id&&ValidInstallLanguage(choice.Language)&&StringComparer.OrdinalIgnoreCase.Equals(choice.Path,Path.GetFullPath(installPath))?choice.Language:null;}
        catch(Exception e)when(e is JsonException or IOException or UnauthorizedAccessException){return null;}
    }
}
