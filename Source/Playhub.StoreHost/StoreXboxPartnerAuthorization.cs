// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Text.Json;
namespace Playhub.StoreLibrary;
public sealed partial class StoreXboxAccountProvider
{
    // Credentials are handed directly to a verified local Xbox extension. This
    // method is never a frontend RPC and never serializes its authorization.
    internal Task<T> WithPartnerAuthorizationAsync<T>(long accountRevision,string relyingParty,Func<string,CancellationToken,Task<T>> operation,CancellationToken cancellationToken)
        => WithAccountAuthorizationAsync(accountRevision,async(account,token)=>await operation(await auth.PartnerAuthorizationAsync(account,relyingParty,token),token),cancellationToken);

    private async Task<T> WithAccountAuthorizationAsync<T>(long accountRevision,Func<XboxAccountTokens,CancellationToken,Task<T>> operation,CancellationToken cancellationToken)
    {
        await readGate.WaitAsync(cancellationToken);
        try
        {
            Saved held;XboxAccountTokens? current;CancellationTokenSource linked;
            lock(sync){Load();held=saved??throw new StoreOperationException("xbox_signin_required");current=tokens;linked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,accountStop.Token);}
            using(linked)
            {
                var token=linked.Token;var snapshot=await Engine.GetSnapshotAsync(token);
                if(!snapshot.Accounts.Any(a=>a.Store==StoreKind.Xbox&&a.State==StoreConnectionState.Connected&&a.Revision==accountRevision))throw new StoreOperationException("account_changed");
                if(current is null||current.ExpiresAt<=now().AddSeconds(60)||current.OAuth.ExpiresAt<=now().AddSeconds(60))
                {
                    var oauth=await auth.RefreshAsync(held.RefreshToken,token);current=await auth.AuthenticateAsync(oauth,token);
                    if(current.Xuid!=held.Xuid)throw new StoreOperationException("xbox_account_identity_mismatch");
                    lock(sync){if(closed||!ReferenceEquals(saved,held))throw new StoreOperationException("account_changed");var next=new Saved(StoreXboxAccountAuth.ClientId,oauth.RefreshToken,current.Xuid,current.Gamertag);writeCredential(JsonSerializer.Serialize(next));saved=held=next;tokens=current;}
                }
                lock(sync)if(closed||!ReferenceEquals(saved,held))throw new StoreOperationException("account_changed");
                snapshot=await Engine.GetSnapshotAsync(token);
                if(!snapshot.Accounts.Any(a=>a.Store==StoreKind.Xbox&&a.State==StoreConnectionState.Connected&&a.Revision==accountRevision))throw new StoreOperationException("account_changed");
                var result=await operation(current,token);
                token.ThrowIfCancellationRequested();
                lock(sync)if(closed||!ReferenceEquals(saved,held))throw new StoreOperationException("account_changed");
                snapshot=await Engine.GetSnapshotAsync(token);
                if(!snapshot.Accounts.Any(a=>a.Store==StoreKind.Xbox&&a.State==StoreConnectionState.Connected&&a.Revision==accountRevision))throw new StoreOperationException("account_changed");
                return result;
            }
        }
        finally{readGate.Release();}
    }
}
