// GPL-3.0-only. A current read-only Steam main-document receipt, independent of CSS theme.
using System.Text.Json;
namespace GamingMode.Services;
internal sealed record SteamTaskbarMainProof(long Epoch,long ObservedAt,int BrowserId,uint? OwnerHint,int PhysicalWidth,int PhysicalHeight)
{
    internal bool MainBigPicture=>BrowserId>0;
    internal bool MatchesBounds(SteamTaskbarBounds b)=>Math.Abs((long)PhysicalWidth-(b.Right-b.Left))<=16&&Math.Abs((long)PhysicalHeight-(b.Bottom-b.Top))<=16;
    internal bool MatchesOwner(uint pid)=>OwnerHint is null or 0||OwnerHint==pid;
    internal static SteamTaskbarMainProof? Parse(JsonElement v,long epoch,long now)
    {
        try
        {
            if(v.GetProperty("nativeMode").GetInt32()!=4||!v.GetProperty("main").GetBoolean()
                ||!v.GetProperty("focused").GetBoolean()||!v.GetProperty("windowFocus").GetBoolean())return null;
            int browser=v.GetProperty("browserId").GetInt32(),w=v.GetProperty("physicalWidth").GetInt32(),h=v.GetProperty("physicalHeight").GetInt32();
            uint? hint=v.TryGetProperty("ownerHint",out var p)&&p.ValueKind==JsonValueKind.Number?p.GetUInt32():null;
            return browser>0&&w is >=100 and <=32768&&h is >=100 and <=32768?new(epoch,now,browser,hint,w,h):null;
        }
        catch(Exception e)when(e is InvalidOperationException or KeyNotFoundException or FormatException or OverflowException){return null;}
    }
    internal const string ReadCurrentMain="""
        (async()=>{try{
          const s=window.SteamUIStore,store=s?.WindowStore,m=store?.GamepadUIMainWindowInstance;
          if(!m||m.IsMainGamepadUIWindow?.()!==true||m.IsGamepadUIOverlayWindow?.()!==false||m.IsAnyVRWindow?.()===true)return null;
          const w=m.BrowserWindow??m.m_BrowserWindow??m.m_popup?.window,d=w?.document,body=d?.body,root=d?.documentElement;
          if(!w||w.closed||!body||!root||s.ActiveWindowInstance!==m)return null;
          const nativeMode=await window.SteamClient.UI.GetUIMode();
          const id=await Promise.resolve(w.SteamClient?.Browser?.GetBrowserID?.());
          const confirmedMode=await window.SteamClient.UI.GetUIMode(),ratio=w.devicePixelRatio;
          if(window.SteamUIStore!==s||s.WindowStore!==store||store.GamepadUIMainWindowInstance!==m
            ||(m.BrowserWindow??m.m_BrowserWindow??m.m_popup?.window)!==w||w.document!==d||d.body!==body||d.documentElement!==root
            ||s.ActiveWindowInstance!==m||m.IsMainGamepadUIWindow?.()!==true||m.IsGamepadUIOverlayWindow?.()!==false||m.IsAnyVRWindow?.()===true||w.closed||nativeMode!==confirmedMode||!Number.isSafeInteger(id)||id<=0||!Number.isFinite(ratio)||ratio<=0||ratio>8)return null;
          return{nativeMode,main:true,browserId:id,focused:d.hasFocus(),windowFocus:body.classList.contains('WindowFocus'),
            ownerHint:m.params?.browserInfo?.m_unPID??null,physicalWidth:Math.round(w.innerWidth*ratio),physicalHeight:Math.round(w.innerHeight*ratio)};
        }catch{return null;}})()
        """;
}
