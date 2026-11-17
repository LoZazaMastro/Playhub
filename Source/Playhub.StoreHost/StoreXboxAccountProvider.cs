// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Playhub.StoreLibrary;

public sealed partial class StoreXboxAccountProvider : IStoreProvider,IStoreTypedSignInProvider,IStoreXboxAccountService,IStoreProtocolExtension,IAsyncDisposable
{
    readonly object sync=new();readonly SemaphoreSlim beginGate=new(1,1),readGate=new(1,1);
    readonly HttpClient http;readonly bool ownsHttp;readonly StoreXboxAccountAuth auth;readonly StoreXboxAccountCollections collection;
    readonly Func<DateTimeOffset> now;readonly Func<string?> readCredential;readonly Action<string?> writeCredential;
    readonly Func<Uri,CancellationToken,Task<bool>> open;
    StoreQueueEngine? engine;Session? session;XboxAccountTokens? tokens;Saved? saved;bool loaded,closed;
    CancellationTokenSource accountStop=new();
    StoreXboxOwnedSnapshot owned=new(0,null,true,Array.Empty<StoreXboxOwnedGame>(),"xbox_signin_required");string? ownedIdentity;
    readonly Dictionary<Guid,StoreXboxSignInState> terminal=[];
    readonly Dictionary<Guid,(string Product,long Revision,StoreXboxHandoffResult Result)> handoffs=[];
    readonly Dictionary<Guid,string?> handoffVolumes=[];
    sealed record Saved(string ClientId,string RefreshToken,string Xuid,string? Gamertag);
    sealed class Session(Guid id,XboxDeviceGrant grant)
    {
        internal readonly Guid Id=id;internal readonly XboxDeviceGrant Grant=grant;
        internal readonly CancellationTokenSource Stop=new();internal int Interval=grant.Interval;
        internal DateTimeOffset NextPoll=DateTimeOffset.MinValue;internal Task<StoreXboxSignInState>? Poll;
    }
    public StoreXboxAccountProvider(string privateDirectory)
    {
        string path=Path.Combine(privateDirectory,"xbox-account.dpapi");
        ownedLibraryCachePath=Path.Combine(Path.GetFullPath(privateDirectory),"xbox-owned-products-v1.json");
        UseWindowsStoreManagedInstalls=true;
        InstalledPackageLookup=WindowsXboxPackageCatalog.FindInstalledGamesAsync;
        readCredential=()=>File.Exists(path)?StoreCredentialVault.Read(path):null;
        writeCredential=value=>{if(value==null){if(File.Exists(path))File.Delete(path);}else StoreCredentialVault.Write(path,value);};
        http=new HttpClient(new SocketsHttpHandler{AllowAutoRedirect=false,UseCookies=false,ConnectTimeout=TimeSpan.FromSeconds(5)}){Timeout=Timeout.InfiniteTimeSpan};
        ownsHttp=true;now=()=>DateTimeOffset.UtcNow;auth=new(http,now);collection=new(auth,now);open=Open;
    }
    internal StoreXboxAccountProvider(HttpClient client,Func<string?> read,Action<string?> write,Func<DateTimeOffset> clock,Func<Uri,CancellationToken,Task<bool>>? opener=null)
    {http=client;readCredential=read;writeCredential=write;now=clock;auth=new(http,now);collection=new(auth,now);open=opener??((_,_)=>Task.FromResult(false));}
    public StoreKind Store=>StoreKind.Xbox;
    public StoreCapabilities Capabilities=>new(StoreInstallKind.ExternalHandoff,true,true,false,false,false);
    // Generic authorization-code APIs are deliberately unsupported for Xbox.
    public Uri AuthorizationUri=>new("https://www.microsoft.com/link");
    internal void Bind(StoreQueueEngine owner){lock(sync){if(engine!=null)throw new InvalidOperationException();engine=owner;}}
    StoreQueueEngine Engine=>engine??throw new StoreOperationException("backend_not_ready");
    static string Identity(string xuid)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("xbox:"+xuid)));
    void Load()
    {
        if(loaded)return;string? text=readCredential();
        if(text==null){loaded=true;return;}if(text.Length>65536)throw new StoreOperationException("credentials_unavailable");
        var value=JsonSerializer.Deserialize<Saved>(text)??throw new StoreOperationException("credentials_unavailable");
        if(value.ClientId!=StoreXboxAccountAuth.ClientId||value.RefreshToken is not {Length:>0 and <=16384}
            ||!System.Text.RegularExpressions.Regex.IsMatch(value.Xuid,"^[0-9]{1,24}$"))throw new StoreOperationException("credentials_unavailable");
        saved=value;loaded=true;
    }
    public Task<ProviderAccount?> GetAccountAsync(CancellationToken cancellationToken)
    {cancellationToken.ThrowIfCancellationRequested();lock(sync){Load();return Task.FromResult(saved==null?null:new ProviderAccount(Identity(saved.Xuid),saved.Gamertag));}}
    public Task<ProviderAccount> ConnectAsync(string authorizationCode,CancellationToken cancellationToken)=>throw new StoreOperationException("xbox_typed_signin_required");
    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();lock(sync)
        {
            writeCredential(null);accountStop.Cancel();saved=null;tokens=null;loaded=true;ownedIdentity=null;
            owned=new(owned.Revision+1,null,true,Array.Empty<StoreXboxOwnedGame>(),"xbox_signin_required");
        }
        return Task.CompletedTask;
    }
    public async Task<StoreXboxSignInSession> BeginXboxSignInAsync(long expectedAccountRevision,CancellationToken cancellationToken=default)
    {
        await beginGate.WaitAsync(cancellationToken);
        Session? held=null;bool reserved=false;
        try
        {
            lock(sync){if(closed)throw new StoreOperationException("backend_closed");if(session!=null)throw new StoreOperationException("xbox_signin_busy");}
            var grant=await auth.BeginAsync(cancellationToken);held=new(Guid.NewGuid(),grant);
            await Engine.ReserveXboxSignInAsync(this,held.Id,expectedAccountRevision,grant.ExpiresAt,cancellationToken);reserved=true;
            cancellationToken.ThrowIfCancellationRequested();
            var remaining=grant.ExpiresAt-now();if(remaining<=TimeSpan.Zero)throw new StoreOperationException("authentication_expired");
            held.Stop.CancelAfter(remaining);
            lock(sync){if(closed)throw new StoreOperationException("backend_closed");session=held;}
            _=ExpireAsync(held);
            return new(held.Id,grant.Page,grant.ExpiresAt,held.Interval);
        }
        catch
        {
            if(held!=null){lock(sync)Complete(held,StoreXboxSignInState.Canceled);if(reserved)await Engine.FinishXboxSignInAsync(this,held.Id,null,"authentication_canceled");}
            throw;
        }
        finally{beginGate.Release();}
    }
    async Task ExpireAsync(Session held)
    {
        try{await Task.Delay(Timeout.InfiniteTimeSpan,held.Stop.Token);}
        catch(OperationCanceledException){}
        bool expires;lock(sync)expires=ReferenceEquals(session,held);
        if(expires)await EndAsync(held,now()>=held.Grant.ExpiresAt?StoreXboxSignInState.Expired:StoreXboxSignInState.Canceled);
    }
    public async Task<StoreXboxSignInResult> PollXboxSignInAsync(Guid sessionId,CancellationToken cancellationToken=default)
    {
        Session? held;Task<StoreXboxSignInState>? work=null;StoreXboxSignInState state=StoreXboxSignInState.Pending;int interval=0;
        lock(sync)
        {
            held=session;
            if(terminal.TryGetValue(sessionId,out var completed))state=completed;
            else if(held==null||held.Id!=sessionId||sessionId==Guid.Empty)state=StoreXboxSignInState.Failed;
            else
            {
                interval=held.Interval;
                if(now()>=held.Grant.ExpiresAt||held.Stop.IsCancellationRequested)work=EndAsync(held,StoreXboxSignInState.Expired);
                else if(held.Poll is {IsCompleted:false})work=held.Poll;
                else if(now()>=held.NextPoll){held.NextPoll=now().AddSeconds(held.Interval);work=held.Poll=Task.Run(()=>PollOwnedAsync(held));}
            }
        }
        if(work!=null)state=await work.WaitAsync(cancellationToken);
        if(state==StoreXboxSignInState.Pending)lock(sync)interval=session?.Id==sessionId?session.Interval:interval;
        if(state!=StoreXboxSignInState.Pending)interval=0;
        return new(sessionId,state,interval,await Engine.GetSnapshotAsync(cancellationToken),StateError(state));
    }
    async Task<StoreXboxSignInState> PollOwnedAsync(Session held)
    {
        try
        {
            var reply=await auth.PollAsync(held.Grant,held.Stop.Token);
            if(reply.State==StoreXboxSignInState.Pending)
            {
                lock(sync)
                {
                    if(!ReferenceEquals(session,held)||held.Stop.IsCancellationRequested)return terminal.GetValueOrDefault(held.Id,StoreXboxSignInState.Canceled);
                    if(reply.SlowDown)held.Interval=Math.Min(30,held.Interval+5);
                    held.NextPoll=now().AddSeconds(held.Interval);
                }
                return StoreXboxSignInState.Pending;
            }
            if(reply.Tokens==null)return await EndAsync(held,reply.State);
            var account=await auth.AuthenticateAsync(reply.Tokens,held.Stop.Token);
            var result=await Engine.FinishXboxSignInAsync(this,held.Id,()=>
            {
                lock(sync)
                {
                    if(closed||!ReferenceEquals(session,held)||held.Stop.IsCancellationRequested||now()>=held.Grant.ExpiresAt)return null;
                    var next=new Saved(StoreXboxAccountAuth.ClientId,account.OAuth.RefreshToken,account.Xuid,account.Gamertag);
                    writeCredential(JsonSerializer.Serialize(next));accountStop.Cancel();accountStop=new();saved=next;tokens=account;loaded=true;
                    ownedIdentity=null;owned=new(owned.Revision+1,null,true,Array.Empty<StoreXboxOwnedGame>(),"library_not_refreshed");
                    Complete(held,StoreXboxSignInState.Connected);return new ProviderAccount(Identity(account.Xuid),account.Gamertag);
                }
            },"authentication_canceled");
            if(result.Accepted)return StoreXboxSignInState.Connected;
            return await EndAsync(held,StoreXboxSignInState.Failed);
        }
        catch(OperationCanceledException){return await EndAsync(held,now()>=held.Grant.ExpiresAt?StoreXboxSignInState.Expired:StoreXboxSignInState.Canceled);}
        catch(Exception e)when(e is not OutOfMemoryException){return await EndAsync(held,StoreXboxSignInState.Failed);}
    }
    void Complete(Session held,StoreXboxSignInState state)
    {
        terminal[held.Id]=state;while(terminal.Count>64)terminal.Remove(terminal.Keys.First());
        if(ReferenceEquals(session,held))session=null;held.Stop.Cancel();
    }
    async Task<StoreXboxSignInState> EndAsync(Session held,StoreXboxSignInState state)
    {
        lock(sync){if(terminal.TryGetValue(held.Id,out var prior))return prior;Complete(held,state);}
        await Engine.FinishXboxSignInAsync(this,held.Id,null,StateError(state));return state;
    }
    public async Task<StoreXboxSignInResult> CancelXboxSignInAsync(Guid sessionId,CancellationToken cancellationToken=default)
    {
        Session? held;StoreXboxSignInState state;
        lock(sync){held=session;state=terminal.GetValueOrDefault(sessionId,StoreXboxSignInState.Failed);}
        if(held?.Id==sessionId)state=await EndAsync(held,StoreXboxSignInState.Canceled);
        return new(sessionId,state,0,await Engine.GetSnapshotAsync(cancellationToken),StateError(state));
    }
    static string? StateError(StoreXboxSignInState state)=>state switch {StoreXboxSignInState.Pending or StoreXboxSignInState.Connected=>null,
        StoreXboxSignInState.Denied=>"authentication_denied",StoreXboxSignInState.Expired=>"authentication_expired",StoreXboxSignInState.Canceled=>"authentication_canceled",_=>"authentication_failed"};
    public async Task<IReadOnlyList<ProviderGame>> ListAsync(CancellationToken cancellationToken)
    {
        await readGate.WaitAsync(cancellationToken);
        Saved? held=null;CancellationTokenSource? accountRead=null;StoreXboxOwnedGame[] previousLocal=[];
        try
        {
            XboxAccountTokens? current;
            lock(sync){Load();LoadOwnedMapping();held=saved??throw new StoreOperationException("xbox_signin_required");current=tokens;previousLocal=owned.Games.Where(g=>g.AccessKind=="installed").ToArray();accountRead=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,accountStop.Token);}
            var token=accountRead.Token;
            if(current==null||current.ExpiresAt<=now().AddSeconds(60))
            {
                var oauth=await auth.RefreshAsync(held.RefreshToken,token);current=await auth.AuthenticateAsync(oauth,token);
                if(current.Xuid!=held.Xuid)throw new StoreOperationException("xbox_account_identity_mismatch");
                lock(sync)
                {
                    if(closed||!ReferenceEquals(saved,held))throw new StoreOperationException("account_changed");
                    var next=new Saved(StoreXboxAccountAuth.ClientId,oauth.RefreshToken,current.Xuid,current.Gamertag);
                    writeCredential(JsonSerializer.Serialize(next));saved=held=next;tokens=current;
                }
            }
            var rows=await collection.ReadAsync(current,token);
            var localPackages=await InstalledPackageLookup(token);
            var localIds=localPackages.Select(p=>p.StoreProductId).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
            IReadOnlyList<StoreXboxOwnedGame> localMetadata=[];
            if(localIds.Length>0)
            {
                try{localMetadata=await collection.InstalledMetadataAsync(localIds,token);}
                catch(StoreOperationException){/* Local registration survives unavailable public artwork metadata. */}
            }
            rows=StoreXboxLocalLibrary.Merge(rows,localPackages,localMetadata);
            lock(sync)
            {
                if(closed||!ReferenceEquals(saved,held))throw new StoreOperationException("account_changed");
                ownedIdentity=Identity(current.Xuid);owned=new(owned.Revision+1,now(),false,Array.AsReadOnly(rows));
                SaveOwnedMapping();
            }
            var pcRows=rows.Where(g=>g.SupportsPc&&g.AccessKind is "owned" or "installed").ToArray();
            var installed=await PackageBatchLookup(pcRows.Select(g=>g.PackageFamilyName).Where(f=>f is not null).Select(f=>f!),token);
            lock(sync)if(closed||!ReferenceEquals(saved,held))throw new StoreOperationException("account_changed");
            var listed=pcRows.Select(g=>
            {
                var package=g.PackageFamilyName is { } family?installed.GetValueOrDefault(family):null;
                return new ProviderGame(g.ProductId,g.Title,g.AccessKind=="owned",g.RequiredBytes,package is {StatusOk:true,IsPlaceholder:false}?package.InstallLocation:null,
                    package?.StatusOk==true&&package.Aumid is not null&&package.ExecutablePath is not null, g.ArtworkUrl, g.ArtworkFallbackUrls,InstallProvider:g.InstallProvider);
            }).Concat(previousLocal.Where(g=>!pcRows.Any(current=>current.ProductId==g.ProductId))
                .Select(g=>new ProviderGame(g.ProductId,g.Title,false,g.RequiredBytes,ArtworkUrl:g.ArtworkUrl,ArtworkFallbackUrls:g.ArtworkFallbackUrls,InstallProvider:g.InstallProvider))).ToArray();
            return await AppendGamePassInstallsAsync(listed,token);
        }
        catch
        {lock(sync)if(ReferenceEquals(saved,held))owned=owned with{IsStale=true,ErrorCode="xbox_library_unavailable"};throw;}
        finally{accountRead?.Dispose();readGate.Release();}
    }
    public Task<StoreXboxOwnedSnapshot> GetXboxOwnedLibraryAsync(CancellationToken cancellationToken=default)
    {cancellationToken.ThrowIfCancellationRequested();lock(sync)return Task.FromResult(owned with{IsStale=owned.IsStale||owned.ObservedAt==null||now()<owned.ObservedAt||now()-owned.ObservedAt>TimeSpan.FromMinutes(5)});}
    public async Task<StoreXboxHandoffResult> OpenXboxOwnedInstallPageAsync(StoreXboxHandoffRequest request,CancellationToken cancellationToken=default)
    {
        var state=await Engine.GetSnapshotAsync(cancellationToken);string? identity;string? family;
        lock(sync)
        {
            if(handoffs.TryGetValue(request.RequestId,out var old))return old.Product==request.ProductId&&old.Revision==request.ExpectedRevision&&handoffVolumes.GetValueOrDefault(request.RequestId)==request.VolumeName?old.Result:Refuse(request,"request_id_reused");
            identity=saved==null?null:Identity(saved.Xuid);
            if(closed||request.RequestId==Guid.Empty||!StoreXboxAccountCollections.ValidId(request.ProductId)||owned.IsStale||owned.Revision!=request.ExpectedRevision
                ||owned.ObservedAt==null||now()<owned.ObservedAt||now()-owned.ObservedAt>TimeSpan.FromMinutes(5)||ownedIdentity!=identity
                ||state.Accounts.Single(a=>a.Store==StoreKind.Xbox).State!=StoreConnectionState.Connected
                ||!owned.Games.Any(g=>g.ProductId==request.ProductId&&g.SupportsPc&&g.AccessKind=="owned")||handoffs.Count>=1024)
                return Refuse(request,"owned_library_changed_or_unavailable");
            var target=owned.Games.Single(g=>g.ProductId==request.ProductId);
            if(StoreXboxInstallProviders.Refusal(target.InstallProvider,target.InstallProviderId) is {} partnerError)return Refuse(request,partnerError);
            handoffs[request.RequestId]=(request.ProductId,request.ExpectedRevision,new(false,"outcome-unknown","handoff_pending",request.ProductId,request.RequestId));
            handoffVolumes[request.RequestId]=request.VolumeName;
            family=owned.Games.Single(g=>g.ProductId==request.ProductId).PackageFamilyName;
        }
        bool invoked=false;StoreXboxHandoffResult result;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock(sync)if(saved==null||Identity(saved.Xuid)!=identity||owned.Revision!=request.ExpectedRevision)
            {var refused=Refuse(request,"account_changed");handoffs[request.RequestId]=(request.ProductId,request.ExpectedRevision,refused);return refused;}
            if(UseWindowsStoreManagedInstalls && family is not null && WindowsXboxPackageCatalog.ValidFamily(family) && Engine.WindowsManagedRuntime.Available)
            {
                var account=state.Accounts.Single(a=>a.Store==StoreKind.Xbox);
                var started=await Engine.BeginWindowsManagedInstallAsync(this,request.ProductId,account.Revision,family,request.RequestId,cancellationToken,volumeName:request.VolumeName);
                result=new(started.Accepted,started.Accepted?"official-install-requested":"not-opened",started.ErrorCode,request.ProductId,request.RequestId);
            }
            else
            {
                if(UseWindowsStoreManagedInstalls){result=Refuse(request,"windows_store_install_not_started");lock(sync)handoffs[request.RequestId]=(request.ProductId,request.ExpectedRevision,result);return result;}
                invoked=true;bool accepted=await open(new Uri("ms-windows-store://pdp/?ProductId="+request.ProductId),cancellationToken);
                result=new(accepted,accepted?"official-page-requested":"not-opened",accepted?null:"official-client-unavailable",request.ProductId,request.RequestId);
            }
        }
        catch(Exception e)when(e is not OutOfMemoryException){result=new(false,invoked?"outcome-unknown":"not-opened",e is StoreOperationException failure?failure.Code:invoked?"official-client-outcome-unknown":"handoff_canceled",request.ProductId,request.RequestId);}
        lock(sync){handoffs[request.RequestId]=(request.ProductId,request.ExpectedRevision,result);return result;}
    }
    static StoreXboxHandoffResult Refuse(StoreXboxHandoffRequest request,string code)=>new(false,"not-opened",code,request.ProductId,request.RequestId);
    static Task<bool> Open(Uri uri,CancellationToken token){token.ThrowIfCancellationRequested();Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true})?.Dispose();return Task.FromResult(true);}
    public Task<ProviderInstallSize> GetInstallSizeAsync(string id,CancellationToken token)=>throw new StoreOperationException("external_install_only");
    internal async Task<object> GetInstallVolumesAsync(long accountRevision,CancellationToken token)
    {
        var snapshot=await Engine.GetSnapshotAsync(token);
        if(!snapshot.Accounts.Any(a=>a.Store==StoreKind.Xbox&&a.State==StoreConnectionState.Connected&&a.Revision==accountRevision)||Engine.WindowsManagedRuntime is not IWindowsStoreInstallDestinationRuntime runtime)
            throw new StoreOperationException("account_changed");
        var locations=await runtime.GetInstallVolumesAsync(token);
        snapshot=await Engine.GetSnapshotAsync(token);
        if(!snapshot.Accounts.Any(a=>a.Store==StoreKind.Xbox&&a.State==StoreConnectionState.Connected&&a.Revision==accountRevision))throw new StoreOperationException("account_changed");
        return new{accountRevision,locations};
    }
    public Task<IStoreTransfer> StartAsync(TransferRequest request,Action<TransferProgress> progress,CancellationToken token)=>throw new StoreOperationException("external_install_only");
    public async Task<(bool Handled,object? Data)> TryHandleAsync(string method,JsonElement args,CancellationToken cancellationToken)=>method switch
    {
        "xbox-account-begin"=>(true,await BeginXboxSignInAsync(args.GetProperty("expectedAccountRevision").GetInt64(),cancellationToken)),
        "xbox-account-poll"=>(true,await PollXboxSignInAsync(args.GetProperty("sessionId").GetGuid(),cancellationToken)),
        "xbox-account-cancel"=>(true,await CancelXboxSignInAsync(args.GetProperty("sessionId").GetGuid(),cancellationToken)),
        "xbox-account-owned"=>(true,await GetXboxOwnedLibraryAsync(cancellationToken)),
        "xbox-install-volumes"=>(true,await GetInstallVolumesAsync(args.GetProperty("expectedAccountRevision").GetInt64(),cancellationToken)),
        "xbox-achievements"=>(true,await ReadXboxAchievementsAsync(args.GetProperty("productId").GetString()??"",args.GetProperty("expectedAccountRevision").GetInt64(),args.TryGetProperty("language",out var language)?language.GetString()??"en-US":"en-US",cancellationToken)),
        "xbox-account-open-owned"=>(true,await OpenXboxOwnedInstallPageAsync(args.Deserialize<StoreXboxHandoffRequest>(StoreLibraryService.JsonOptions)??throw new StoreOperationException("request_invalid"),cancellationToken)),
        _=>(false,null)
    };
    public async ValueTask DisposeAsync()
    {
        Session? held;lock(sync){closed=true;held=session;accountStop.Cancel();}
        if(held!=null)await EndAsync(held,StoreXboxSignInState.Canceled);
        if(ownsHttp)http.Dispose();
    }
}

public sealed partial class StoreQueueEngine
{
    readonly Dictionary<Guid,(StoreXboxAccountProvider Provider,StoreAccount Prior,DateTimeOffset Expiry)> xboxSignIns=[];
    internal async Task ReserveXboxSignInAsync(StoreXboxAccountProvider provider,Guid id,long expectedRevision,DateTimeOffset expiry,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            RequireRunning();var prior=accounts[StoreKind.Xbox];
            if(!providers.TryGetValue(StoreKind.Xbox,out var registered)||!ReferenceEquals(provider,registered)||id==Guid.Empty||xboxSignIns.Count!=0
                ||expiry<=now()||expiry>now().AddMinutes(15)||prior.Revision!=expectedRevision||prior.State==StoreConnectionState.Connecting
                ||active is { } job&&jobs.Single(j=>j.JobId==job).Key.Store==StoreKind.Xbox)throw new StoreOperationException("account_changed");
            xboxSignIns.Add(id,(provider,prior,expiry));accounts[StoreKind.Xbox]=prior with{State=StoreConnectionState.Connecting,Revision=prior.Revision+1};providerOperations++;
            try{Changed();}catch{xboxSignIns.Remove(id);accounts[StoreKind.Xbox]=prior;providerOperations--;throw;}
        }
        finally{gate.Release();}
    }
    internal async Task<StoreCommandResult> FinishXboxSignInAsync(StoreXboxAccountProvider provider,Guid id,Func<ProviderAccount?>? accept,string? error)
    {
        await gate.WaitAsync(CancellationToken.None);
        try
        {
            if(!xboxSignIns.TryGetValue(id,out var held)||!ReferenceEquals(provider,held.Provider))return Result(false,"account_changed");
            xboxSignIns.Remove(id);providerOperations--;
            var current=accounts[StoreKind.Xbox];if(current.State!=StoreConnectionState.Connecting||current.Revision!=held.Prior.Revision+1)return Result(false,"account_changed");
            ProviderAccount? account=null;
            if(now()<held.Expiry&&accept!=null)try{account=accept();}catch(Exception e)when(e is not OutOfMemoryException){error="credential_protection_failed";}
            accounts[StoreKind.Xbox]=held.Prior with{State=account==null?held.Prior.State:StoreConnectionState.Connected,Revision=held.Prior.Revision+2,DisplayName=account?.DisplayName??held.Prior.DisplayName,ErrorCode=account!=null?null:error};
            if(account!=null){accountIdentities[StoreKind.Xbox]=account.AccountIdentity;DropUninstalled(StoreKind.Xbox);}
            Changed();return Result(account!=null,account!=null?null:error);
        }
        finally{gate.Release();}
    }
}
