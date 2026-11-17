// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
// Original Windows adapter. Nile v1.2.0 remains an unmodified GPL dependency.
using System.Net.Http.Headers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Playhub.StoreLibrary;
internal interface IStoreInstallVerifier { Task<bool> IsInstalledAsync(string id, string path, CancellationToken token); }
public sealed partial class StoreAmazonProvider : IStoreProvider, IStoreInstallVerifier
{
    public const string RuntimeSha256 = "6531790C59F78CEA4A8743BF0582D5AFDA7FB887F5C143391D7339AD0F42AB88";
    public StoreKind Store => StoreKind.Amazon;
    public StoreCapabilities Capabilities { get; }
    private readonly string tool, root, native, vault, host;
    private readonly StoreNativeOperationGate gate;
    private readonly HttpClient http;
    private readonly StoreToolQuery query;
    private readonly AmazonWorkerFactory start;
    private readonly Func<byte[], AmazonManifest> parseManifest;
    private readonly Dictionary<string,string> environment;
    private sealed record Login(string Verifier, string Serial, string Client, DateTimeOffset Expires);
    private Login? login;
    public Uri AuthorizationUri
    {
        get
        {
            if(!Capabilities.CanConnect) throw new StoreOperationException("runtime_missing_or_modified");
            var verifier=Base64Url(RandomNumberGenerator.GetBytes(32)); var serial=Guid.NewGuid().ToString("N").ToUpperInvariant();
            var client=Convert.ToHexString(Encoding.ASCII.GetBytes(serial+"#A2UMVHOX7UP4V7")).ToLowerInvariant();
            var value=new Login(verifier,serial,client,DateTimeOffset.UtcNow.AddMinutes(5)); Volatile.Write(ref login,value);
            return new Uri("https://amazon.com/ap/signin?"+Query(new()
            {
                ["openid.ns"]="http://specs.openid.net/auth/2.0",["openid.claimed_id"]="http://specs.openid.net/auth/2.0/identifier_select",
                ["openid.identity"]="http://specs.openid.net/auth/2.0/identifier_select",["openid.mode"]="checkid_setup",
                ["openid.oa2.scope"]="device_auth_access",["openid.ns.oa2"]="http://www.amazon.com/ap/ext/oauth/2",
                ["openid.oa2.response_type"]="code",["openid.oa2.code_challenge_method"]="S256",["openid.oa2.client_id"]="device:"+client,
                ["language"]="en_US",["marketPlaceId"]="ATVPDKIKX0DER",["openid.return_to"]="https://www.amazon.com",
                ["openid.pape.max_auth_age"]="0",["openid.assoc_handle"]="amzn_sonic_games_launcher",["pageId"]="amzn_sonic_games_launcher",
                ["openid.oa2.code_challenge"]=Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            }));
        }
    }
    public StoreAmazonProvider(string bundleDirectory,string stateDirectory,string hostExecutable,HttpClient? client=null,StoreToolQuery? toolQuery=null)
        : this(bundleDirectory,stateDirectory,hostExecutable,client,toolQuery,null,null) { }
    internal StoreAmazonProvider(string bundleDirectory,string stateDirectory,string hostExecutable,HttpClient? client,StoreToolQuery? toolQuery,AmazonWorkerFactory? worker,Func<byte[],AmazonManifest>? manifestParser)
    {
        root=Path.Combine(Path.GetFullPath(stateDirectory),"amazon"); StoreCredentialVault.PrivateDirectory(root);
        native=Path.Combine(root,"native","nile"); Directory.CreateDirectory(native); vault=Path.Combine(root,"credentials.dpapi");
        host=Path.GetFullPath(hostExecutable);tool=Path.Combine(Path.GetFullPath(bundleDirectory),"nile_windows_x86_64.exe");
        gate=new(Path.Combine(root,"native-operation.lock"));
        var verified=File.Exists(tool)&&Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tool)))==RuntimeSha256;
        Capabilities=verified?new(StoreInstallKind.Direct,true,true,false,false,true):new(StoreInstallKind.Unavailable,false,false,false,false,false,"runtime_missing_or_modified");
        environment=Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>().Where(e=>e.Key is string&&e.Value is string).ToDictionary(e=>(string)e.Key,e=>(string)e.Value!,StringComparer.OrdinalIgnoreCase);
        environment["NILE_CONFIG_PATH"]=Path.GetDirectoryName(native)!; environment["PYTHONUTF8"]="1";
        http=client??new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(30) };
        query=toolQuery??StoreToolProcess.QueryAsync; start=worker??AmazonWorker.StartAsync; parseManifest=manifestParser??(bytes=>AmazonManifest.Parse(bytes));
    }
    public async Task<ProviderAccount> ConnectAsync(string authorizationCode,CancellationToken token)
    {
        RequireRuntime(); await gate.WaitAsync(token);
        try
        {
            var pending=Interlocked.Exchange(ref login,null); if(pending==null||pending.Expires<=DateTimeOffset.UtcNow)throw new StoreOperationException("authorization_expired");
            var code=AuthorizationCode(authorizationCode);
            var body=new { auth_data=new { authorization_code=code,client_domain="DeviceLegacy",client_id=pending.Client,code_algorithm="SHA-256",code_verifier=pending.Verifier,use_global_authentication=false },
                registration_data=new { app_name="AGSLauncher for Windows",app_version="1.0.0",device_model="Windows",device_name=(string?)null,device_serial=pending.Serial,device_type="A2UMVHOX7UP4V7",domain="Device",os_version="10.0.19044.0" },requested_extensions=new[]{"customer_info","device_info"},requested_token_type=new[]{"bearer","mac_dms"},user_context_map=new {} };
            using var response=await SendJson("https://api.amazon.com/auth/register",body,token);
            var success=response.RootElement.GetProperty("response").GetProperty("success");
            var uid=success.GetProperty("extensions").GetProperty("customer_info").GetProperty("user_id").GetString();
            if(string.IsNullOrWhiteSpace(uid)||uid.Length>1024)throw new StoreOperationException("authentication_failed");
            var bearer=success.GetProperty("tokens").GetProperty("bearer");
            var seconds=PositiveSeconds(bearer.GetProperty("expires_in"),"amazon_registration_response_invalid");
            if(string.IsNullOrWhiteSpace(bearer.GetProperty("access_token").GetString())||string.IsNullOrWhiteSpace(bearer.GetProperty("refresh_token").GetString()))throw new StoreOperationException("amazon_registration_response_invalid");
            var data=JsonNode.Parse(success.GetRawText())!;data["NILE"]=new JsonObject { ["token_obtain_time"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            data["tokens"]!["bearer"]!["expires_in"]=seconds;
            var account=Account(data);
            StoreCredentialVault.Write(vault,data.ToJsonString());
            return account;
        }
        catch(StoreOperationException){throw;}
        catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException){throw new StoreOperationException("amazon_registration_response_invalid");}
        finally { gate.Release(); }
    }
    public async Task<ProviderAccount?> GetAccountAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);try{return File.Exists(vault)?Account(JsonNode.Parse(StoreCredentialVault.Read(vault))!):null;}finally{gate.Release();}
    }
    public async Task DisconnectAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            Volatile.Write(ref login,null);
            // Local sign-out is authoritative even when Amazon is unreachable.
            // No foreign launcher configuration or installed records are removed.
            if(File.Exists(vault))File.Delete(vault);
            DeleteMaterialized();
        }
        finally{gate.Release();}
    }
    public async Task<IReadOnlyList<ProviderGame>> ListAsync(CancellationToken token)
    {
        RequireRuntime();if(!gate.TryWait())throw new StoreOperationException("provider_busy");
        try
        {
            using var runtime=PinRuntime();
            Materialize();
            var synced=await query(tool,["library","sync","--force"],environment,token);if(synced.Code!=0)throw new StoreOperationException("library_unavailable");
            var listed=await query(tool,["library","list","--json"],environment,token);if(listed.Code!=0)throw new StoreOperationException("library_unavailable");
            using var doc=JsonDocument.Parse(listed.Json);if(doc.RootElement.ValueKind!=JsonValueKind.Array||doc.RootElement.GetArrayLength()>50000)throw new StoreOperationException("library_invalid");
            var games=new List<ProviderGame>();var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in doc.RootElement.EnumerateArray())
            {
                var product=item.GetProperty("product");var id=product.GetProperty("id").GetString()!;StoreCliProvider.ValidateId(id);
                var entitlement=item.GetProperty("id").GetString();if(string.IsNullOrWhiteSpace(entitlement)||entitlement.Length>1024||!seen.Add(id))throw new StoreOperationException("library_invalid");
                var title=product.GetProperty("title").GetString();if(string.IsNullOrWhiteSpace(title)||title.Length>2048)throw new StoreOperationException("library_invalid");
                var artwork=StoreMetadataArtwork.Amazon(product);
                games.Add(new(id,title,true,null,null,false,artwork.Url,artwork.Alternatives));
            }
            // Persist only a fully parsed response, never a partial library.
            Seal(); // Confirm/seal the same authenticated CLI account before binding launch ownership.
            var json=doc.RootElement.GetRawText();
            File.WriteAllText(Path.Combine(native,"library.json"),json); BindLaunchLibrary(json);
            for(var index=0;index<games.Count;index++) if(InstalledPath(games[index].Id) is { } path)
            {
                try { _=ReadLaunch(games[index].Id,path,token);games[index]=games[index] with { InstallPath=path,Launchable=true }; }
                catch(Exception e) when(LaunchReadFailure(e))
                {
                    // A registered local installation remains removable when
                    // missing/modified payload or prerequisites prevent launch.
                    if(Directory.Exists(path))games[index]=games[index] with { InstallPath=path,Launchable=false };
                }
            }
            return games;
        }
        finally{try{Seal();}finally{gate.Release();}}
    }
    public async Task<ProviderInstallSize> GetInstallSizeAsync(string id,CancellationToken token)
    {
        RequireRuntime();if(!gate.TryWait())throw new StoreOperationException("provider_busy");
        try{Materialize();var plan=await Plan(id,token);var bytes=plan.Manifest.Files.Aggregate(0L,(sum,f)=>checked(sum+f.Size));return new(bytes,bytes);}
        finally{try{Seal();}finally{gate.Release();}}
    }
    public async Task<bool> IsInstalledAsync(string id,string path,CancellationToken token)
    {
        if(!gate.TryWait())return false;
        try{return await IsInstalledUnlocked(id,path,token);}finally{gate.Release();}
    }
    private Task<bool> IsInstalledUnlocked(string id,string path,CancellationToken token)
    {
        try
        {
            StoreCliProvider.ValidateId(id);var actual=InstalledPath(id);
            if(actual==null||!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(path),actual))return Task.FromResult(false);
            var manifestPath=Path.Combine(native,"manifests",id+".raw");if(!File.Exists(manifestPath)||new FileInfo(manifestPath).Length>AmazonManifest.MaximumBytes)return Task.FromResult(false);
            return Task.FromResult(parseManifest(File.ReadAllBytes(manifestPath)).Verify(actual,token));
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or StoreOperationException or JsonException or InvalidOperationException or KeyNotFoundException){return Task.FromResult(false);}
    }
    public async Task<IStoreTransfer> StartAsync(TransferRequest request,Action<TransferProgress> progress,CancellationToken token)
    {
        RequireRuntime();StoreCliProvider.ValidateId(request.Key.Id);if(request.Key.Store!=Store||request.JobId==Guid.Empty)throw new StoreOperationException("store_mismatch");
        await gate.WaitAsync(token);
        try
        {
            Materialize();var path=Path.GetFullPath(request.InstallPath);
            if(!Path.IsPathFullyQualified(request.InstallPath)||!File.Exists(Path.Combine(path,".playhub-install-owner"))||new FileInfo(Path.Combine(path,".playhub-install-owner")).Length>64||File.ReadAllText(Path.Combine(path,".playhub-install-owner"))!=request.JobId.ToString("D"))throw new StoreOperationException("destination_ownership_lost");
            var installed=InstalledPath(request.Key.Id);if(installed!=null&&!StringComparer.OrdinalIgnoreCase.Equals(installed,path))throw new StoreOperationException("installed_path_conflict");
            var plan=await Plan(request.Key.Id,token);foreach(var file in plan.Manifest.Files)_=AmazonManifest.Contained(path,file.RelativePath);
            token.ThrowIfCancellationRequested();
            var runtime=PinRuntime();
            FileStream ownership;
            try { ownership=PinDestination(path,request.JobId); }
            catch { runtime.Dispose();throw; }
            IAmazonWorker worker;
            try
            {
                // Nile 1.2.0 compares its cached manifest even after uninstall:
                // an unchanged manifest then skips every file in a new folder.
                // Keep the delta cache only when its exact destination still
                // contains the verified payload. This also repairs interrupted
                // attempts whose CLI wrote installed.json before verification.
                var cached=AmazonManifest.Contained(native,"manifests/"+request.Key.Id+".raw");
                if(File.Exists(cached)&&!await IsInstalledUnlocked(request.Key.Id,path,token))
                {
                    token.ThrowIfCancellationRequested();
                    File.Delete(cached);
                }
                worker=await start(host,tool,["install",request.Key.Id,"--path",path],environment,root,progress,token);
            }
            catch { ownership.Dispose();runtime.Dispose();throw; }
            return new Transfer(worker,async()=>
            {
                var persisted=Path.Combine(native,"manifests",request.Key.Id+".raw");
                if(!File.Exists(persisted)||new FileInfo(persisted).Length>AmazonManifest.MaximumBytes||!CryptographicOperations.FixedTimeEquals(SHA256.HashData(File.ReadAllBytes(persisted)),plan.Manifest.RawHash))return false;
                if(InstalledVersion(request.Key.Id)!=plan.Version||!await IsInstalledUnlocked(request.Key.Id,path,CancellationToken.None))return false;
                using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
                VerifyPostInstall(request.Key.Id,path,plan.Manifest,timeout.Token, allowInstallation: true);
                return true;
            },()=>{ownership.Dispose();runtime.Dispose();try{Seal();}finally{gate.Release();}});
        }
        catch{try{Seal();}finally{gate.Release();}throw;}
    }
    private FileStream PinRuntime()
    {
        FileStream? pinned=null;
        try
        {
            pinned=new FileStream(tool,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(Convert.ToHexString(SHA256.HashData(pinned))!=RuntimeSha256)throw new StoreOperationException("runtime_missing_or_modified");
            return pinned;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or StoreOperationException)
        { pinned?.Dispose();throw new StoreOperationException("runtime_missing_or_modified"); }
    }
    private static FileStream PinDestination(string path,Guid jobId)
    {
        var marker=Path.Combine(path,".playhub-install-owner");
        for(var current=marker;current!=null;current=Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new StoreOperationException("destination_ownership_lost");
        FileStream? pinned=null;
        try
        {
            pinned=new FileStream(marker,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(pinned.Length!=36)throw new StoreOperationException("destination_ownership_lost");
            using var reader=new StreamReader(pinned,Encoding.UTF8,false,128,true);
            if(reader.ReadToEnd()!=jobId.ToString("D"))throw new StoreOperationException("destination_ownership_lost");
            return pinned;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or StoreOperationException)
        { pinned?.Dispose();throw new StoreOperationException("destination_ownership_lost"); }
    }
    private async Task<(string Version,AmazonManifest Manifest)> Plan(string id,CancellationToken token)
    {
        StoreCliProvider.ValidateId(id);using var library=JsonDocument.Parse(File.ReadAllText(Path.Combine(native,"library.json")));
        var matches=library.RootElement.EnumerateArray().Where(g=>g.GetProperty("product").GetProperty("id").GetString()==id).ToArray();
        if(matches.Length!=1)throw new StoreOperationException("game_not_owned");
        var auth=await FreshNative(token);var access=auth["tokens"]!["bearer"]!["access_token"]!.GetValue<string>();
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://gaming.amazon.com/api/distribution/v2/public");
        request.Headers.UserAgent.ParseAdd("AGSLauncher/1.0.0");
        request.Headers.TryAddWithoutValidation("X-Amz-Target","com.amazon.animusdistributionservice.external.AnimusDistributionService.GetGameDownload");request.Headers.TryAddWithoutValidation("x-amzn-token",access);
        request.Headers.TryAddWithoutValidation("UserAgent","com.amazon.agslauncher.win/3.0.9202.1");
        request.Content=new StringContent(JsonSerializer.Serialize(new { entitlementId=matches[0].GetProperty("id").GetString(),Operation="GetGameDownload" }),Encoding.UTF8,"application/json");
        request.Content.Headers.ContentEncoding.Add("amz-1.0");
        using var response=await http.SendAsync(request,token);if(!response.IsSuccessStatusCode)throw new StoreOperationException("manifest_unavailable");
        using var metadata=JsonDocument.Parse(await Bounded(response,1024*1024,token));
        var version=metadata.RootElement.GetProperty("versionId").GetString();var url=metadata.RootElement.GetProperty("downloadUrl").GetString();
        if(string.IsNullOrWhiteSpace(version)||!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length!=0||uri.Fragment.Length!=0||!(uri.Host.EndsWith(".amazonaws.com",StringComparison.OrdinalIgnoreCase)||uri.Host.EndsWith(".amazon.com",StringComparison.OrdinalIgnoreCase)||uri.Host.Equals("pg.distribution.games.a2z.com",StringComparison.OrdinalIgnoreCase)))throw new StoreOperationException("manifest_url_invalid");
        var builder=new UriBuilder(uri){Path=uri.AbsolutePath.TrimEnd('/')+"/manifest.proto"};
        using var download=await http.GetAsync(builder.Uri,HttpCompletionOption.ResponseHeadersRead,token);if(!download.IsSuccessStatusCode)throw new StoreOperationException("manifest_unavailable");
        var manifest=parseManifest(await Bounded(download,AmazonManifest.MaximumBytes,token));return new(version,manifest);
    }
    private async Task<JsonNode> FreshNative(CancellationToken token)
    {
        var data=ReadNative();var bearer=data["tokens"]!["bearer"]!;
        if(data["NILE"]!["token_obtain_time"]!.GetValue<double>()+PositiveSeconds(JsonSerializer.SerializeToElement(bearer["expires_in"]),"authentication_expired")>DateTimeOffset.UtcNow.ToUnixTimeSeconds()+60)return data;
        using var response=await SendJson("https://api.amazon.com/auth/token",new { source_token=bearer["refresh_token"]!.GetValue<string>(),source_token_type="refresh_token",requested_token_type="access_token",app_name="AGSLauncher for Windows",app_version="1.0.0" },token);
        var access=response.RootElement.GetProperty("access_token").GetString();var expires=PositiveSeconds(response.RootElement.GetProperty("expires_in"),"authentication_expired");
        if(string.IsNullOrWhiteSpace(access))throw new StoreOperationException("authentication_expired");
        bearer["access_token"]=access;bearer["expires_in"]=expires;data["NILE"]!["token_obtain_time"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        WriteNative(data["extensions"]!["customer_info"]!["user_id"]!.GetValue<string>(),data);return data;
    }
    private sealed record InstalledRecord(string Path,string Version);
    private string? InstalledPath(string id)=>InstalledRecordFor(id)?.Path;
    private string? InstalledVersion(string id)=>InstalledRecordFor(id)?.Version;
    private InstalledRecord? InstalledRecordFor(string id)
    {
        var file=Path.Combine(native,"installed.json");if(!File.Exists(file))return null;
        string? oldEntitlement=null;var library=Path.Combine(native,"library.json");
        if(File.Exists(library))
        {
            using var owned=JsonDocument.Parse(File.ReadAllText(library));var entry=owned.RootElement.EnumerateArray().Where(g=>g.GetProperty("product").GetProperty("id").GetString()==id).ToArray();
            if(entry.Length==1)oldEntitlement=entry[0].GetProperty("id").GetString();
        }
        using var doc=JsonDocument.Parse(File.ReadAllText(file));var found=doc.RootElement.EnumerateArray().Where(i=>i.GetProperty("id").GetString()==id||oldEntitlement!=null&&i.GetProperty("id").GetString()==oldEntitlement).ToArray();
        if(found.Length==0)return null;if(found.Length!=1)throw new StoreOperationException("installed_metadata_invalid");
        var path=found[0].GetProperty("path").GetString();var version=found[0].GetProperty("version").GetString();
        if(path==null||!Path.IsPathFullyQualified(path)||string.IsNullOrWhiteSpace(version))throw new StoreOperationException("installed_metadata_invalid");return new(Path.GetFullPath(path),version);
    }
    private static ProviderAccount Account(JsonNode auth)
    {
        var customer=auth["extensions"]!["customer_info"]!;var uid=customer["user_id"]!.GetValue<string>();
        if(string.IsNullOrWhiteSpace(uid))throw new StoreOperationException("credentials_invalid");return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uid))),customer["given_name"]?.GetValue<string>());
    }
    private void Materialize()
    {
        if(!File.Exists(vault))throw new StoreOperationException("authentication_required");var data=JsonNode.Parse(StoreCredentialVault.Read(vault))!;
        var uid=data["extensions"]!["customer_info"]!["user_id"]!.GetValue<string>();var name=data["extensions"]!["customer_info"]!["given_name"]?.GetValue<string>();
        File.WriteAllText(Path.Combine(native,"current_user.json"),JsonSerializer.Serialize(new { name,user_id=uid }));WriteNative(uid,data);
    }
    private JsonNode ReadNative()
    {
        using var current=JsonDocument.Parse(File.ReadAllText(Path.Combine(native,"current_user.json")));var uid=current.RootElement.GetProperty("user_id").GetString()!;
        using var aes=Aes.Create();aes.Key=SHA256.HashData(Encoding.UTF8.GetBytes(uid));
        var bytes=File.ReadAllBytes(AuthFile(uid));if(bytes.Length<32||bytes.Length>1024*1024)throw new StoreOperationException("credentials_invalid");
        var iv=aes.DecryptEcb(bytes.AsSpan(0,16),PaddingMode.None);var plain=aes.DecryptCbc(bytes.AsSpan(16),iv,PaddingMode.PKCS7);
        try { var data=JsonNode.Parse(plain)!;if(data["extensions"]!["customer_info"]!["user_id"]!.GetValue<string>()!=uid)throw new StoreOperationException("account_changed");return data; }
        finally{CryptographicOperations.ZeroMemory(plain);}
    }
    private void WriteNative(string uid,JsonNode data)
    {
        using var aes=Aes.Create();aes.Key=SHA256.HashData(Encoding.UTF8.GetBytes(uid));var iv=RandomNumberGenerator.GetBytes(16);var plain=Encoding.UTF8.GetBytes(data.ToJsonString());
        try { File.WriteAllBytes(AuthFile(uid),aes.EncryptEcb(iv,PaddingMode.None).Concat(aes.EncryptCbc(plain,iv,PaddingMode.PKCS7)).ToArray()); }
        finally{CryptographicOperations.ZeroMemory(plain);}
    }
    private string AuthFile(string uid)=>Path.Combine(native,Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(uid))).ToLowerInvariant()+".enc"); // Pinned Nile filename protocol, not a security hash.
    private void Seal()
    {
        if(!File.Exists(Path.Combine(native,"current_user.json")))return;
        var data=ReadNative();if(File.Exists(vault)&&Account(JsonNode.Parse(StoreCredentialVault.Read(vault))!).AccountIdentity!=Account(data).AccountIdentity)throw new StoreOperationException("account_changed");
        StoreCredentialVault.Write(vault,data.ToJsonString());DeleteMaterialized();
    }
    private void DeleteMaterialized()
    {
        var current=Path.Combine(native,"current_user.json");
        if(File.Exists(current))
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(current));var uid=doc.RootElement.GetProperty("user_id").GetString();
            if(uid!=null&&File.Exists(AuthFile(uid)))File.Delete(AuthFile(uid));File.Delete(current);
        }
    }
    private async Task<JsonDocument> SendJson(string uri,object value,CancellationToken token)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,uri) { Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json") };
        request.Headers.UserAgent.ParseAdd("AGSLauncher/1.0.0");
        using var response=await http.SendAsync(request,token);
        if(!response.IsSuccessStatusCode)throw new StoreOperationException((int)response.StatusCode switch { 400 or 401 or 403=>"amazon_authorization_rejected",429=>"amazon_signin_rate_limited",>=500=>"amazon_signin_service_unavailable",_=>"amazon_registration_failed" });
        return JsonDocument.Parse(await Bounded(response,1024*1024,token));
    }
    private static async Task<byte[]> Bounded(HttpResponseMessage response,int maximum,CancellationToken token)
    {
        if(response.Content.Headers.ContentLength>maximum)throw new StoreOperationException("provider_response_too_large");
        using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[16384];int read;
        while((read=await input.ReadAsync(buffer,token))!=0){if(output.Length+read>maximum)throw new StoreOperationException("provider_response_too_large");output.Write(buffer,0,read);}return output.ToArray();
    }
    internal static string AuthorizationCode(string input)
    {
        var code=input.Trim();
        if(Uri.TryCreate(code,UriKind.Absolute,out var uri))
        {
            if(uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length!=0||uri.Fragment.Length!=0||uri.Host is not ("amazon.com" or "www.amazon.com")||uri.AbsolutePath is not ("/" or "/ap/maplanding"))throw new StoreOperationException("authorization_invalid");
            var values=uri.Query.TrimStart('?').Split('&').Select(p=>p.Split('=',2)).Where(p=>p.Length==2&&Uri.UnescapeDataString(p[0])=="openid.oa2.authorization_code").ToArray();
            if(values.Length!=1)throw new StoreOperationException("authorization_invalid");code=Uri.UnescapeDataString(values[0][1]);
        }
        if(string.IsNullOrWhiteSpace(code)||code.Length>4096||code.IndexOfAny(['\0','\r','\n'])>=0||code.Contains("://"))throw new StoreOperationException("authorization_invalid");return code;
    }
    private static double PositiveSeconds(JsonElement value,string error)
    {
        var valid=value.ValueKind==JsonValueKind.Number?value.TryGetDouble(out var seconds):double.TryParse(value.ValueKind==JsonValueKind.String?value.GetString():null,NumberStyles.Float,CultureInfo.InvariantCulture,out seconds);
        if(!valid||!double.IsFinite(seconds)||seconds<=0)throw new StoreOperationException(error);
        return seconds;
    }
    private static string Base64Url(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static string Query(Dictionary<string,string> values)=>string.Join('&',values.Select(v=>Uri.EscapeDataString(v.Key)+"="+Uri.EscapeDataString(v.Value)));
    private void RequireRuntime(){if(!Capabilities.CanRefresh||!File.Exists(tool)||Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tool)))!=RuntimeSha256)throw new StoreOperationException("runtime_missing_or_modified");}
    private sealed class Transfer : IStoreTransfer
    {
        private readonly IAmazonWorker worker;private readonly Action release;public Task<TransferCompletion> Completion {get;}
        public Transfer(IAmazonWorker worker,Func<Task<bool>> verify,Action release){this.worker=worker;this.release=release;Completion=Observe(verify);}
        private async Task<TransferCompletion> Observe(Func<Task<bool>> verify)
        {
            var drained=false;
            try
            {
                var result=await worker.Completion;
                drained=result.Drained;
                if(!result.Drained)return new(false,false,false,"worker_drain_unconfirmed",DrainConfirmed:false);
                if(result.StopRequested)return new(false,false,false,"checkpoint_unsupported",Stopped:true);
                if(result.Forced||result.ExitCode!=0)return new(false,false,false,"worker_failed");
                var valid=await verify();return new(valid,false,valid,valid?null:"installation_unverified");
            }
            catch(StoreOperationException e){return new(false,false,false,drained?e.Code:"worker_drain_unconfirmed",DrainConfirmed:drained);}
            catch{return new(false,false,false,drained?"installation_unverified":"worker_drain_unconfirmed",DrainConfirmed:drained);}
            finally{if(drained)release();}
        }
        public Task RequestCheckpointAsync(CancellationToken token)=>worker.StopAsync(token);
        public ValueTask DisposeAsync()=>worker.DisposeAsync();
    }
}

