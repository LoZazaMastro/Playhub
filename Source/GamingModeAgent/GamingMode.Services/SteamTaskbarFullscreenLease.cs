// GPL-3.0-only. Declares only a verified Steam Big Picture window fullscreen.
namespace GamingMode.Services;
internal readonly record struct SteamTaskbarKey(nint Window,uint Pid,long Birth,uint Thread,nint Shell,uint ShellPid,long ShellBirth)
{internal bool Valid=>Window!=0&&Pid>0&&Birth>0&&Thread>0&&Shell!=0&&ShellPid>0&&ShellBirth>0;}
internal readonly record struct SteamTaskbarBounds(int Left,int Top,int Right,int Bottom);
internal readonly record struct SteamTaskbarObservation(SteamTaskbarKey Key,long Epoch,bool Exact,bool ModeAllowed,
    bool MainBigPicture,bool Foreground,bool Visible,bool Cloaked,bool FullMonitor,bool BottomAutohidePixel,bool FreshMainProof,bool IdentityEnded=false)
{
    internal bool FullscreenIntent=>FullMonitor||BottomAutohidePixel;
    internal bool Hold=>Key.Valid&&Exact&&ModeAllowed&&MainBigPicture&&Foreground&&Visible&&!Cloaked&&FullscreenIntent;
    internal bool Acquire=>Hold&&FreshMainProof;
}
internal interface ISteamTaskbarMark { int Mark(nint window,bool fullscreen,Func<bool> adjacentGuard); }
internal sealed class SteamTaskbarFullscreenLease(ISteamTaskbarMark api)
{
    private SteamTaskbarKey? owned;private (SteamTaskbarKey Key,long Epoch)? attempted;private bool driving,uncertainAcquire;
    internal bool Owned=>owned is not null;
    internal bool Attempted(SteamTaskbarObservation value)=>attempted==(value.Key,value.Epoch);
    internal string Status {get;private set;}="idle";
    internal void Observe(Func<SteamTaskbarObservation> read)
    {
        if(driving)return;driving=true;
        try
        {
            var first=read();
            if(owned is {} held)
            {
                if(first.IdentityEnded||(first.Key.Valid&&first.Key!=held)){owned=null;Status="identity-ended";return;}
                if(!first.Exact){Status="identity-unknown";return;}
                if(uncertainAcquire||!first.Hold)ReleaseCore(read);
                return;
            }
            if(!first.Acquire||attempted==(first.Key,first.Epoch)){Status="refused";return;}
            var last=read();
            if(last.Key!=first.Key||last.Epoch!=first.Epoch||!last.Acquire){Status="refused";return;}
            attempted=(first.Key,first.Epoch);
            int result=api.Mark(first.Key.Window,true,()=>
            {
                var value=read();
                if(value.Key!=first.Key||value.Epoch!=first.Epoch||!value.Acquire)return false;
                // Reserve paired cleanup BEFORE the external call. A transport
                // exception after TRUE may hide a completed Shell mutation.
                owned=first.Key;uncertainAcquire=true;return true;
            });
            if(result!=0){Status="request-not-confirmed";if(owned is not null)ReleaseCore(read);return;}
            uncertainAcquire=false;owned=first.Key;Status="requested";
            var after=read();
            if(after.IdentityEnded||(after.Key.Valid&&after.Key!=first.Key)){owned=null;Status="identity-ended";return;}
            if(!after.Exact){Status="identity-unknown";return;}
            if(!after.Hold)ReleaseCore(read);
        }
        catch{Status=owned is null?"request-not-confirmed":"release-unconfirmed";}
        finally{driving=false;}
    }
    internal void Release(Func<SteamTaskbarObservation> read)
    {
        if(driving)return;driving=true;try{ReleaseCore(read);}finally{driving=false;}
    }
    private void ReleaseCore(Func<SteamTaskbarObservation> read)
    {
        if(owned is not {} held)return;
        try
        {
            var current=read();
            if(current.IdentityEnded||(current.Key.Valid&&current.Key!=held)){owned=null;Status="identity-ended";return;}
            if(!current.Exact){Status="identity-unknown";return;}
            int result=api.Mark(held.Window,false,()=>{var last=read();return last.Key==held&&last.Exact;});
            if(result==0){owned=null;uncertainAcquire=false;Status="released";}else Status="release-unconfirmed";
        }
        catch{Status="release-unconfirmed";}
    }
}
internal static class SteamTaskbarGeometry
{
    internal static bool Covers(SteamTaskbarBounds w,SteamTaskbarBounds m,SteamTaskbarBounds d,long style)=>w==m&&m.Right>m.Left&&m.Bottom>m.Top
        &&(style&0x00c40000)==0&&d.Left>=w.Left&&d.Top>=w.Top&&d.Right<=w.Right&&d.Bottom<=w.Bottom
        &&(long)d.Left-w.Left<=2&&(long)d.Top-w.Top<=2&&(long)w.Right-d.Right<=2&&(long)w.Bottom-d.Bottom<=2;
    // A distinct bottom-edge autohide intent, not a generic fullscreen tolerance.
    internal static bool BottomPixel(SteamTaskbarBounds w,SteamTaskbarBounds m,SteamTaskbarBounds d,long style,bool autoHideBottom)=>
        autoHideBottom&&(style&0x00c40000)==0&&w.Left==m.Left&&w.Right==m.Right&&w.Top==m.Top
        &&m.Right>m.Left&&(long)m.Bottom-m.Top>=120&&(long)w.Bottom==(long)m.Bottom-1&&w==d;
}
