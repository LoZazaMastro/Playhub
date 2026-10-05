using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Playhub.Emulation.Workbench;

/// <summary>Uses Artwork's composition geometry and Steam artwork API, retaining its editable original.</summary>
public sealed class PerfectHeroService(Func<string, CancellationToken, Task<JsonNode?>>? evaluate = null)
{
    public Task<JsonNode?> RestoreLogoAsync(uint appId, CancellationToken ct)
    {
        if (appId == 0) throw new ArgumentException("Imported app is required.");
        return (evaluate ?? SteamPluginBridge.EvaluateAsync)("(async()=>{const appId=" + appId + ";" + """
const call=(method,...args)=>window.DeckyBackend.call('loader/call_plugin_method','Playhub Artworks',method,...args);
const info=await call('get_setting',`perfect_hero_info_${appId}`,null);
const actual=await call('get_local_asset_info',appId,'hero','');
// Retain an intact composition. Only repair the stale hidden layer after a plain replacement.
if(!info||info.sha256&&info.sha256===actual?.sha256)return {ok:true,unchanged:true};
let position=await call('get_setting',`logo_position_backup_${appId}`,null);
if(!position||position.nWidthPct<1||position.nHeightPct<1)position={pinnedPosition:'BottomLeft',nWidthPct:50,nHeightPct:50};
const app=window.appStore?.GetAppOverviewByAppID?.(appId);
if(app&&window.appDetailsStore?.SaveCustomLogoPosition)await window.appDetailsStore.SaveCustomLogoPosition(app,position);
else await window.SteamClient.Apps.SetCustomLogoPositionForApp(appId,JSON.stringify({nVersion:1,logoPosition:position}));
await call('clear_perfect_hero_state',appId,true);return {ok:true,restored:true};
})()
""", ct);
    }

    public Task<JsonNode?> ApplyAsync(uint appId, JsonObject game, CancellationToken ct) => ApplyTargetAsync(appId, game, "hero", ct);

    public async Task<JsonNode?> ApplyTargetAsync(uint appId, JsonObject game, string target, CancellationToken ct)
    {
        if (target is not ("hero" or "banner")) throw new ArgumentException("Unknown composition target.", nameof(target));
        if (appId == 0) throw new ArgumentException("Imported app is required.");
        var hero = game["steamArtwork"]?[target]?.GetValue<string>();
        var logo = game["steamArtwork"]?["logo"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(hero) || string.IsNullOrWhiteSpace(logo) || !File.Exists(hero) || !File.Exists(logo))
            return new JsonObject { ["ok"] = false, ["reason"] = "hero_and_logo_required" };
        async Task<byte[]> Read(string path)
        {
            if (new FileInfo(path).Length > 25 * 1024 * 1024) throw new IOException("Artwork is too large.");
            var bytes = await File.ReadAllBytesAsync(path, ct);
#if PLAYHUB_NATIVE_INTEGRATIONS
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(stream))
            {
                writer.WriteBytes(bytes); await writer.StoreAsync().AsTask(ct); writer.DetachStream();
            }
            stream.Seek(0);
            var image = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask(ct);
            if (image.PixelWidth < 2 || image.PixelHeight < 2 || image.PixelWidth > 8192 || image.PixelHeight > 8192) throw new IOException("Invalid artwork dimensions.");
#else
            using var stream = new MemoryStream(bytes); using var image = System.Drawing.Image.FromStream(stream);
            if (image.Width < 2 || image.Height < 2 || image.Width > 8192 || image.Height > 8192) throw new IOException("Invalid artwork dimensions.");
#endif
            return bytes;
        }
        var h = await Read(hero); var l = await Read(logo);
        var input = new JsonObject { ["target"] = target == "banner" ? "grid_l" : "hero", ["width"] = target == "banner" ? 1926 : 3840, ["height"] = target == "banner" ? 900 : 1240, ["artworkType"] = target == "banner" ? 0 : 1, ["appId"] = appId, ["hero"] = Convert.ToBase64String(h), ["logo"] = Convert.ToBase64String(l),
            ["sourcePath"] = Path.GetFullPath(hero), ["sourceHash"] = Convert.ToHexString(SHA256.HashData(h.Concat(l).ToArray())) };
        return await (evaluate ?? SteamPluginBridge.EvaluateAsync)(BuildExpression(input), ct);
    }

    internal static string BuildExpression(JsonObject input) => "(async()=>{const p=" + input.ToJsonString() + ";" + """
const call=(method,...args)=>window.DeckyBackend.call('loader/call_plugin_method','Playhub Artworks',method,...args);
const apps=window.SteamClient?.Apps;
if(!apps?.SetCustomArtworkForApp||!apps?.SetCustomLogoPositionForApp||!window.DeckyBackend?.call)throw Error('artwork_unavailable');
const current=await call('get_local_asset_info',p.appId,p.target,'');
const previous=await call('get_setting',`perfect_${p.target}_info_${p.appId}`,null);
if(previous?.sourceHash===p.sourceHash&&previous?.sha256&&previous.sha256===current?.sha256)return {ok:true,existing:true};
const load=data=>new Promise((resolve,reject)=>{const i=new Image();i.onload=()=>resolve(i);i.onerror=()=>reject(Error('invalid_artwork'));i.src='data:image/png;base64,'+data});
// When re-editing a composition, read the plugin's pristine source rather than baking its old logo in again.
let original=null,heroData=p.hero;
if(previous?.sha256&&previous.sha256===current?.sha256){
original=await call('prepare_perfect_source_transfer',p.appId,p.target,'');
if(!original?.token)throw Error('original_not_preserved');
try {
const size=Number(original.size),step=Number(original.chunk_size);
if(!Number.isInteger(size)||size<1||size>25*1024*1024||!Number.isInteger(step)||step<1)throw Error('invalid_artwork');
let binary='';for(let offset=0;offset<size;offset+=step)binary+=atob(await call('read_artwork_transfer_chunk',original.token,offset));
heroData=btoa(binary);
} finally {await call('release_artwork_transfer',original.token);}
}
const [hero,logo]=await Promise.all([load(heroData),load(p.logo)]);
const sample=document.createElement('canvas');sample.width=logo.naturalWidth;sample.height=logo.naturalHeight;
const sc=sample.getContext('2d',{willReadFrequently:true});sc.drawImage(logo,0,0);
const rgba=sc.getImageData(0,0,sample.width,sample.height).data;
let x0=sample.width,y0=sample.height,x1=-1,y1=-1;
for(let y=0;y<sample.height;y++)for(let x=0;x<sample.width;x++)if(rgba[(y*sample.width+x)*4+3]>8){x0=Math.min(x0,x);x1=Math.max(x1,x);y0=Math.min(y0,y);y1=Math.max(y1,y)}
if(x1<x0||y1<y0)throw Error('empty_logo');
const sw=x1-x0+1,sh=y1-y0+1,c=document.createElement('canvas');c.width=p.width;c.height=p.height;
const ctx=c.getContext('2d');ctx.fillStyle='#000';ctx.fillRect(0,0,p.width,p.height);ctx.imageSmoothingEnabled=true;ctx.imageSmoothingQuality='high';
const r=Math.max(p.width/hero.naturalWidth,p.height/hero.naturalHeight),w=hero.naturalWidth*r,h=hero.naturalHeight*r;
ctx.drawImage(hero,(p.width-w)/2,(p.height-h)/2,w,h);
const lr=Math.min(p.width*.28/sw,p.height*.72/sh),lw=sw*lr,lh=sh*lr;
ctx.shadowColor='rgba(0,0,0,.55)';ctx.shadowBlur=Math.round(p.width*.006);ctx.shadowOffsetY=Math.round(p.width*.003);
ctx.drawImage(logo,x0,y0,sw,sh,p.width*.25-lw/2,p.height*.5-lh/2,lw,lh);
const data=c.toDataURL('image/jpeg',.92).split(',')[1];
const transfer=await call('prepare_artwork_transfer','',p.sourcePath,'');
if(!transfer?.token)throw Error('original_not_preserved');
try {
const overview=window.appStore?.GetAppOverviewByAppID?.(p.appId);
let position=(overview&&window.appDetailsStore?.GetCustomLogoPosition?.(overview))||{pinnedPosition:'BottomLeft',nWidthPct:50,nHeightPct:50};
if(position.nWidthPct<1||position.nHeightPct<1)position=await call('get_setting',`logo_position_backup_${p.appId}`,null)||{pinnedPosition:'BottomLeft',nWidthPct:50,nHeightPct:50};
await apps.SetCustomArtworkForApp(p.appId,data,'jpg',p.artworkType);
const saved=await call('preserve_perfect_source_from_transfer',p.appId,p.target,transfer.token,!original);
if(!saved?.saved)throw Error('original_not_preserved');
await call('set_setting',`logo_position_backup_${p.appId}`,position);
const hidden={pinnedPosition:'BottomLeft',nWidthPct:.01,nHeightPct:.01};
if(overview&&window.appDetailsStore?.SaveCustomLogoPosition)await window.appDetailsStore.SaveCustomLogoPosition(overview,hidden);
else await apps.SetCustomLogoPositionForApp(p.appId,JSON.stringify({nVersion:1,logoPosition:hidden}));
await call('set_setting',`logo_hidden_${p.appId}`,true);await call('set_setting',`logo_visible_${p.appId}`,false);
await call('set_setting',`perfect_${p.target}_${p.appId}`,true);
const result=await call('get_local_asset_info',p.appId,p.target,'');
await call('set_setting',`perfect_${p.target}_info_${p.appId}`,{version:116,origin:'automatic',withLogo:true,sha256:result?.sha256||'',sourceHash:p.sourceHash});
c.width=0;sample.width=0;return {ok:true,width:p.width,height:p.height,sha256:result?.sha256||''};
} finally { await call('release_artwork_transfer',transfer.token); }
})()
""";
}
