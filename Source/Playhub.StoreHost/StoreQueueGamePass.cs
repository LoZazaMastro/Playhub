// SPDX-License-Identifier: MIT. An explicit current-account request is separate from purchased ownership.
namespace Playhub.StoreLibrary;
public sealed partial class StoreQueueEngine
{
    internal async Task<StoreCommandResult> StageGamePassGameAsync(StoreXboxAccountProvider provider,StoreXboxOwnedGame product,long revision,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            RequireRunning();
            if(!ReferenceEquals(Provider(StoreKind.Xbox),provider)||!Connected(StoreKind.Xbox,revision)||product.PackageFamilyName is null||!provider.CurrentGamePassFamily(product.ProductId,product.PackageFamilyName))return Result(false,"account_changed");
            var key=new StoreGameKey(StoreKind.Xbox,product.ProductId);
            string libraryKind=provider.CurrentOwnedFamily(product.ProductId,product.PackageFamilyName)?"store":"gamePass";
            if(!games.TryGetValue(key,out var game))games[key]=new(key,product.Title,revision,provider.Capabilities,false,false,ArtworkKey:"xbox:"+product.ProductId,
                DownloadBytes:product.RequiredBytes,ArtworkUrl:product.ArtworkUrl,ArtworkFallbackUrls:product.ArtworkFallbackUrls,ArtworkKind:product.ArtworkUrl is null?null:"cover",LibraryKind:libraryKind,InstallProvider:product.InstallProvider);
            else if(game.AccountRevision!=revision)return Result(false,"account_changed");
            else games[key]=game with{LibraryKind=libraryKind,InstallProvider=product.InstallProvider};
            Changed();return Result(true);
        }
        finally{gate.Release();}
    }
}
