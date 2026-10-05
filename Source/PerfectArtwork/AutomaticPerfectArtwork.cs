using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Playhub.Integrations;

/// <summary>Explicit post-export composition, using only freshly resolved artwork and verified delivery.</summary>
public sealed class AutomaticPerfectArtwork(string root)
{
    public async Task<JsonObject> DeliverHeroAsync(string identity, bool requested,
        Func<CancellationToken,Task<IReadOnlyDictionary<string,string>>> readActual,
        Func<string,CancellationToken,Task> assignHero,
        Func<bool,CancellationToken,Task> setSeparateLogoHidden,
        Func<string,CancellationToken,Task<string?>>? legacyPristine = null,
        CancellationToken ct = default,
        Func<string,string,string,PerfectArtworkLayout,CancellationToken,Task<PerfectArtworkResult>>? compose = null)
    {
        ApplicationIntegrationIdentity.Validate(identity);
        if(!requested)return new JsonObject { ["delivered"]=false,["detail"]="not_requested" };
        var store=new ApplicationIntegrationDataStore(root);
        var state=await store.ReadCategoryAsync(identity,"artwork",ct);
        var actual=await readActual(ct);
        if(!actual.TryGetValue("hero",out var hero)||!actual.TryGetValue("logo",out var logo)||!File.Exists(hero)||!File.Exists(logo))
            return new JsonObject { ["delivered"]=false,["detail"]="hero_and_logo_required" };
        string heroHash=await HashAsync(hero,ct),logoHash=await HashAsync(logo,ct);
        var prior=state["perfect_hero"] as JsonObject;
        bool confirmed=string.Equals(prior?["sha256"]?.ToString(),heroHash,StringComparison.OrdinalIgnoreCase)&&
            string.Equals(prior?["logoSha256"]?.ToString(),logoHash,StringComparison.OrdinalIgnoreCase);
        PerfectArtworkResult? result=null;
        if(!confirmed)
        {
            var layout=prior?["layout"]?.Deserialize<PerfectArtworkLayout>()?.Normalize()??new PerfectArtworkLayout();
            string? pristine=legacyPristine is null?null:await legacyPristine(hero,ct);
            result=compose is null
                ? await new PerfectArtworkCompositor(root).ComposeAsync(identity,"hero",hero,logo,ct,pristine,layout)
                : await compose(identity,hero,logo,layout,ct);
            // No stale acquisition can overwrite an artwork selected while composition was running.
            var fresh=await readActual(ct);
            if(!fresh.TryGetValue("hero",out var currentHero)||!fresh.TryGetValue("logo",out var currentLogo)||
                !string.Equals(await HashAsync(currentHero,ct),heroHash,StringComparison.OrdinalIgnoreCase)||
                !string.Equals(await HashAsync(currentLogo,ct),logoHash,StringComparison.OrdinalIgnoreCase))
                throw new IOException("The Steam artwork changed during composition.");
            if(!string.Equals(await HashAsync(result.Path,ct),result.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new IOException("The prepared composition changed.");
            await assignHero(result.Path,ct);
            var delivered=await readActual(ct);
            if(!delivered.TryGetValue("hero",out var deliveredHero)||
                !string.Equals(await HashAsync(deliveredHero,ct),result.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new IOException("The composed artwork readback failed.");
        }
        else
        {
            // A matching output never excuses a missing or changed preserved original.
            string preserved=prior?["pristineSourcePath"]?.ToString()??throw new IOException("The original artwork is unavailable.");
            string directory=Path.GetFullPath(Path.Combine(root,"compositions",ApplicationIntegrationDataStore.Key(identity),"hero"))+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(preserved).StartsWith(directory,StringComparison.OrdinalIgnoreCase)||
                !string.Equals(await HashAsync(preserved,ct),prior?["sourceSha256"]?.ToString(),StringComparison.OrdinalIgnoreCase))
                throw new IOException("The original artwork changed.");
        }
        var finalState=await store.ReadCategoryAsync(identity,"artwork",ct);
        bool hidden=ArtworkLogoPolicy.ManualOverride(finalState)??true;
        await setSeparateLogoHidden(hidden,ct);
        await store.PatchAsync(identity,"artwork",new JsonObject { ["hero"]=result?.Path??hero,["logoHiddenRequested"]=hidden },ct);
        return new JsonObject { ["delivered"]=true,["acquired"]=true,["detail"]="composition_verified" };
    }

    private static async Task<string> HashAsync(string path,CancellationToken ct)
    {
        ApplicationIntegrationDataStore.GuardPath(path);
        var file=new FileInfo(path);
        if(!file.Exists||file.Length is < 4 or > 32*1024*1024)throw new IOException("The artwork is unavailable.");
        return Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path,ct)));
    }
}
