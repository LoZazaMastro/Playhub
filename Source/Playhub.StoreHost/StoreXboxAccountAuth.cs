// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
// Original protocol implementation. No Unifideck/Playnite source is incorporated.
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace Playhub.StoreLibrary;

internal sealed record XboxDeviceGrant([property: JsonIgnore] string DeviceCode, [property: JsonIgnore] Uri Page, DateTimeOffset ExpiresAt, int Interval)
{ public override string ToString() => "XboxDeviceGrant [redacted]"; }
internal sealed record XboxOAuthTokens([property: JsonIgnore] string AccessToken, [property: JsonIgnore] string RefreshToken, DateTimeOffset ExpiresAt)
{ public override string ToString() => "XboxOAuthTokens [redacted]"; }
internal sealed record XboxAccountTokens([property: JsonIgnore] XboxOAuthTokens OAuth, [property: JsonIgnore] string UserHash,
    [property: JsonIgnore] string Xuid, [property: JsonIgnore] string MarketplaceToken, DateTimeOffset ExpiresAt, [property: JsonIgnore] string? Gamertag,
    [property: JsonIgnore] string? LiveToken = null)
{ public override string ToString() => "XboxAccountTokens [redacted]"; }
internal sealed record XboxTokenPoll(XboxOAuthTokens? Tokens, StoreXboxSignInState State, bool SlowDown = false);

internal sealed partial class StoreXboxAccountAuth(HttpClient http, Func<DateTimeOffset> now)
{
    // The same public Xbox.com client/consumer authority as the normal Xbox
    // device sign-in. No borrowed user session or application's private secret.
    internal const string ClientId = "1f907974-e22b-4810-a9de-d9647380c97e";
    internal const string Authority = "https://login.microsoftonline.com/consumers/oauth2/v2.0/";
    internal async Task<XboxDeviceGrant> BeginAsync(CancellationToken token)
    {
        var (status, body) = await Send(Authority + "devicecode", new FormUrlEncodedContent(new Dictionary<string,string>
            { ["client_id"] = ClientId, ["scope"] = "XboxLive.signin offline_access" }), token);
        if (status != HttpStatusCode.OK) throw Failed("xbox_signin_unavailable");
        string device = Text(body, "device_code", 16384), code = Text(body, "user_code", 32);
        if (!Regex.IsMatch(code, "^[A-Z0-9-]{4,32}$") || !body.TryGetProperty("expires_in", out var expires)
            || !expires.TryGetInt32(out int seconds) || seconds is < 1 or > 900) throw Failed("xbox_signin_response_invalid");
        if (body.TryGetProperty("verification_uri", out var verify) && verify.GetString() is { } page
            && page is not ("https://www.microsoft.com/link" or "https://microsoft.com/link")) throw Failed("xbox_signin_response_invalid");
        int interval = body.TryGetProperty("interval", out var field) && field.TryGetInt32(out var value) ? value : 5;
        if (interval is < 1 or > 30) throw Failed("xbox_signin_response_invalid");
        return new(device, new Uri("https://www.microsoft.com/link?otc=" + Uri.EscapeDataString(code)), now().AddSeconds(seconds), interval);
    }
    internal async Task<XboxTokenPoll> PollAsync(XboxDeviceGrant grant, CancellationToken token)
    {
        var (status, body) = await Send(Authority + "token", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["client_id"] = ClientId, ["device_code"] = grant.DeviceCode, ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code" }), token);
        if (status == HttpStatusCode.OK) return new(ReadOAuth(body), StoreXboxSignInState.Connected);
        string? error = body.TryGetProperty("error", out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
        return error switch {
            "authorization_pending" => new(null, StoreXboxSignInState.Pending),
            "slow_down" => new(null, StoreXboxSignInState.Pending, true),
            "authorization_declined" or "access_denied" => new(null, StoreXboxSignInState.Denied),
            "expired_token" => new(null, StoreXboxSignInState.Expired),
            _ when (int)status >= 500 || (int)status == 429 => new(null, StoreXboxSignInState.Pending, true),
            _ => new(null, StoreXboxSignInState.Failed)
        };
    }
    internal async Task<XboxOAuthTokens> RefreshAsync(string refreshToken, CancellationToken token)
    {
        var (status, body) = await Send(Authority + "token", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["client_id"] = ClientId, ["refresh_token"] = refreshToken, ["grant_type"] = "refresh_token", ["scope"] = "XboxLive.signin offline_access" }), token);
        if (status != HttpStatusCode.OK) throw Failed("xbox_signin_expired");
        return ReadOAuth(body);
    }
    XboxOAuthTokens ReadOAuth(JsonElement body)
    {
        if (!body.TryGetProperty("expires_in", out var field) || !field.TryGetInt32(out int seconds) || seconds is < 1 or > 86400
            || !Text(body,"token_type",32).Equals("Bearer",StringComparison.OrdinalIgnoreCase)) throw Failed("xbox_signin_response_invalid");
        var scopes=Text(body,"scope",2048).Split(' ',StringSplitOptions.RemoveEmptyEntries);
        if (!scopes.Contains("XboxLive.signin",StringComparer.OrdinalIgnoreCase)) throw Failed("xbox_signin_scope_invalid");
        return new(Text(body,"access_token",16384),Text(body,"refresh_token",16384),now().AddSeconds(seconds));
    }
    internal async Task<XboxAccountTokens> AuthenticateAsync(XboxOAuthTokens oauth, CancellationToken token)
    {
        var (userStatus,user) = await Send("https://user.auth.xboxlive.com/user/authenticate", Json(new {
            Properties=new { AuthMethod="RPS", SiteName="user.auth.xboxlive.com", RpsTicket="d="+oauth.AccessToken },
            RelyingParty="http://auth.xboxlive.com",TokenType="JWT" }),token);
        if(userStatus!=HttpStatusCode.OK)throw Failed("xbox_profile_unavailable");
        string userToken=Text(user,"Token",32768);
        var live=await Xsts(userToken,"http://xboxlive.com",token);
        var store=await Xsts(userToken,"http://mp.microsoft.com/",token);
        var liveClaims=Claims(live);var storeClaims=Claims(store);
        string hash=Text(liveClaims,"uhs",32),xuid=Text(liveClaims,"xid",24);
        if(!Regex.IsMatch(hash,"^[0-9]{1,32}$")||!Regex.IsMatch(xuid,"^[0-9]{1,24}$")
            ||Text(storeClaims,"uhs",32)!=hash||storeClaims.TryGetProperty("xid",out var storeXuid)&&storeXuid.GetString()!=xuid)
            throw Failed("xbox_account_identity_mismatch");
        DateTimeOffset expiry=Expiry(live)<Expiry(store)?Expiry(live):Expiry(store);
        string? tag=liveClaims.TryGetProperty("gtg",out var gamer)&&gamer.ValueKind==JsonValueKind.String?gamer.GetString():null;
        if(tag?.Length>128||tag?.Any(char.IsControl)==true)tag=null;
        return new(oauth,hash,xuid,Text(store,"Token",32768),expiry,tag,Text(live,"Token",32768));
    }
    async Task<JsonElement> Xsts(string userToken,string relyingParty,CancellationToken token)
    {
        var (status,body)=await Send("https://xsts.auth.xboxlive.com/xsts/authorize",Json(new {
            Properties=new { SandboxId="RETAIL",UserTokens=new[]{userToken} },RelyingParty=relyingParty,TokenType="JWT" }),token);
        if(status!=HttpStatusCode.OK||body.TryGetProperty("XErr",out _))throw Failed("xbox_profile_unavailable");
        return body;
    }
    internal async Task<string> PartnerAuthorizationAsync(XboxAccountTokens account,string relyingParty,CancellationToken token)
    {
        if(relyingParty is not ("https://login.battle.net/" or "https://accounts.ea.com/" or "https://ubiservices.ubi.com/"))throw Failed("xbox_partner_identity_invalid");
        var(status,user)=await Send("https://user.auth.xboxlive.com/user/authenticate",Json(new{
            Properties=new{AuthMethod="RPS",SiteName="user.auth.xboxlive.com",RpsTicket="d="+account.OAuth.AccessToken},RelyingParty="http://auth.xboxlive.com",TokenType="JWT"}),token);
        if(status!=HttpStatusCode.OK)throw Failed("xbox_profile_unavailable");
        var ticket=await Xsts(Text(user,"Token",32768),relyingParty,token);var claims=Claims(ticket);
        // Xbox scopes the user hash to the relying party. A partner's hash
        // differs from xboxlive.com's even for the same authenticated account.
        // This ticket is minted from that account's current OAuth token; any
        // explicit Xbox identity must still match before handing it to COM.
        string partnerHash=Text(claims,"uhs",32);
        if(!Regex.IsMatch(partnerHash,"^[0-9]{1,32}$")||claims.TryGetProperty("xid",out var xid)&&xid.GetString()!=account.Xuid)throw Failed("xbox_account_identity_mismatch");
        return "XBL3.0 x="+partnerHash+";"+Text(ticket,"Token",32768);
    }
    JsonElement Claims(JsonElement body)
    {
        if(!body.TryGetProperty("DisplayClaims",out var claims)||!claims.TryGetProperty("xui",out var users)
            ||users.ValueKind!=JsonValueKind.Array||users.GetArrayLength()!=1||users[0].ValueKind!=JsonValueKind.Object)
            throw Failed("xbox_signin_response_invalid");
        _=Expiry(body);return users[0];
    }
    DateTimeOffset Expiry(JsonElement body)
    {
        if(!DateTimeOffset.TryParse(Text(body,"NotAfter",64),out var expiry)||expiry<=now()||expiry>now().AddDays(2))
            throw Failed("xbox_signin_response_invalid");
        return expiry;
    }
    internal async Task<(HttpStatusCode Status,JsonElement Body)> Send(string url,HttpContent? content,CancellationToken token,string? authorization=null,int limit=1024*1024,HttpMethod? method=null,string contractVersion="1",string? language=null)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var request=new HttpRequestMessage(method??HttpMethod.Post,url){Content=content};
        request.Headers.TryAddWithoutValidation("User-Agent","Playhub/2.7 (Xbox account library)");
        request.Headers.TryAddWithoutValidation("x-xbl-contract-version",contractVersion);
        if(language is not null)request.Headers.TryAddWithoutValidation("Accept-Language",language);
        if(authorization!=null)request.Headers.TryAddWithoutValidation("Authorization",authorization);
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        if(response.RequestMessage?.RequestUri?.AbsoluteUri!=url)throw Failed("xbox_endpoint_changed");
        await using var input=await response.Content.ReadAsStreamAsync(deadline.Token);using var output=new MemoryStream();var buffer=new byte[16384];int count;
        while((count=await input.ReadAsync(buffer,deadline.Token))!=0){if(output.Length+count>limit)throw Failed("xbox_response_too_large");output.Write(buffer,0,count);}
        using var document=JsonDocument.Parse(output.ToArray(),new JsonDocumentOptions{MaxDepth=32});
        if(document.RootElement.ValueKind!=JsonValueKind.Object)throw Failed("xbox_response_invalid");
        return(response.StatusCode,document.RootElement.Clone());
    }
    internal static StringContent Json(object value)=>new(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json");
    internal static string Text(JsonElement body,string name,int maximum)
    {
        if(!body.TryGetProperty(name,out var field)||field.ValueKind!=JsonValueKind.String||field.GetString() is not {Length:>0} value
            ||value.Length>maximum||value.Any(char.IsControl))throw Failed("xbox_response_invalid");
        return value;
    }
    internal static StoreOperationException Failed(string code)=>new(code);
}
