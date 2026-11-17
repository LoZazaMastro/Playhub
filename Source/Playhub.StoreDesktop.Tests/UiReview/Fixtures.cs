using Playhub.StoreLibrary;
namespace Playhub.Services
{
 internal static class AppPaths{public static string LocalDataRoot="";}
 internal static class Diag{public static void Crash(string name,Exception error)=>File.AppendAllText(Path.Combine(AppPaths.LocalDataRoot,"diag.txt"),error.ToString());}
}
namespace Playhub.StoreLibrary
{
 public sealed class StoreLibraryService:IStoreLibraryService
 {
  public static readonly StoreLibraryService Instance=new();public static StoreLibraryService CreateDefault()=>Instance;
  public static StoreSteamCollectionMigration Migration=new(Guid.NewGuid(),"2.7.0:store-collections-v2","76561198000000001","pending",0,2);
  public Task<StoreSteamCollectionMigration> RequestSteamMigrationAsync(CancellationToken token=default)=>Task.FromResult(Migration);
  public Task<StoreSteamCollectionMigration> GetSteamMigrationStatusAsync(Guid id,CancellationToken token=default)=>Task.FromResult(Migration);
  readonly StoreCapabilities caps=new(StoreInstallKind.Direct,true,true,true,true,true);
  StoreLibrarySnapshot snapshot;
  public int Connects,Disconnects,GameMutations;public bool AllowConnect;
  public StoreKind? PendingStore;public TaskCompletionSource<StoreConnectSession>? PendingConnect;
  public StoreLibraryService(){snapshot=new(1,DateTimeOffset.UtcNow,Enum.GetValues<StoreKind>().Select(s=>new StoreAccount(s,s is StoreKind.Epic or StoreKind.Xbox?StoreConnectionState.Connected:StoreConnectionState.Disconnected,1,s==StoreKind.Gog?null:"Fixture",caps)).ToArray(),[],[]);}
  public void AdvanceAccountName(){snapshot=snapshot with{Revision=snapshot.Revision+1,Accounts=snapshot.Accounts.Select(a=>a.Store==StoreKind.Epic?a with{DisplayName="Fixture changed"}:a).ToArray()};}
  public Task<StoreLibrarySnapshot> GetSnapshotAsync(CancellationToken token=default)=>Task.FromResult(snapshot);
  public Task<StoreConnectSession> BeginConnectAsync(StoreKind store,CancellationToken token=default){Connects++;if(store==PendingStore&&PendingConnect is not null)return PendingConnect.Task.WaitAsync(token);return Task.FromResult(new StoreConnectSession(Guid.NewGuid(),store,new Uri(store==StoreKind.Gog?"https://auth.gog.com/auth":"https://epicgames.com/auth"),DateTimeOffset.UtcNow.AddMinutes(5),true));}
  public Task<StoreCommandResult> CompleteConnectAsync(Guid id,string code,CancellationToken token=default){if(AllowConnect)snapshot=snapshot with{Revision=snapshot.Revision+1,Accounts=snapshot.Accounts.Select(a=>a.Store==StoreKind.Gog?a with{State=StoreConnectionState.Connected}:a).ToArray()};return Task.FromResult(new StoreCommandResult(true,null,snapshot));}
  public Task<StoreCommandResult> DisconnectAsync(StoreKind store,long revision,CancellationToken token=default){Disconnects++;snapshot=snapshot with{Revision=snapshot.Revision+1,Accounts=snapshot.Accounts.Select(a=>a.Store==store?a with{State=StoreConnectionState.Disconnected}:a).ToArray()};return Task.FromResult(new StoreCommandResult(true,null,snapshot));}
  public Task<StoreCommandResult> RefreshAsync(StoreKind store,long revision,CancellationToken token=default)=>Task.FromResult(new StoreCommandResult(true,null,snapshot));
  public Task<StoreInstallPreparation> PrepareInstallAsync(StoreGameKey key,long revision,string? path=null,CancellationToken token=default){GameMutations++;throw new Exception("Forbidden fixture game management");}
  public Task<StoreCommandResult> EnqueueAsync(StoreEnqueueRequest r,CancellationToken token=default){GameMutations++;throw new Exception();}
  public Task<StoreCommandResult> ControlAsync(StoreJobCommand c,CancellationToken token=default){GameMutations++;throw new Exception();}
  public Task<StoreLaunchDescriptor> GetVerifiedLaunchDescriptorAsync(StoreGameKey key,CancellationToken token=default){GameMutations++;throw new Exception();}
 }
 internal enum StoreSignInOutcome{Authorized,RetryRequested,Canceled}
 internal sealed record StoreSignInResult(StoreSignInOutcome Outcome,string? Code);
 internal static class StoreSignInWindow{internal static Task<StoreSignInResult> ShowAsync(StoreConnectSession s,Func<string,string> text,CancellationToken token)=>Task.FromResult(new StoreSignInResult(StoreSignInOutcome.Authorized,"inert-fixture"));}
 internal static class StoreXboxSignInWindow{internal static Task<StoreXboxSignInResult?> ShowAsync(StoreXboxSignInController c,StoreXboxSignInSession s,Func<string,string> text,CancellationToken token)=>throw new Exception("No account window in fixture");}
}
