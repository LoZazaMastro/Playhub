using GamingMode.Services;
using System.Text.Json;
static class Checks
{
    static int count;
    static void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
    static readonly SteamTaskbarKey Key=new(111,123,134389000000000001,7,222,88,134388000000000001);
    static SteamTaskbarObservation Good(long epoch=1)=>new(Key,epoch,true,true,true,true,true,false,true,false,true);
    sealed class Api:ISteamTaskbarMark
    {
        public List<bool> Calls=[];public int Result;public Action? Before,After;
        public int Mark(nint window,bool value,Func<bool> guard){Before?.Invoke();if(!guard())return -1;Calls.Add(value);After?.Invoke();return Result;}
    }
    static void Main()
    {
        foreach(bool gaming in new[]{false,true})
        {var api=new Api();var lease=new SteamTaskbarFullscreenLease(api);var state=Good();lease.Observe(()=>state);Check(lease.Owned&&api.Calls.SequenceEqual([true]),$"BP allowed in mode {gaming}");lease.Observe(()=>state);Check(api.Calls.Count==1,"held does not repeat TRUE");state=state with{Foreground=false};lease.Observe(()=>state);Check(!lease.Owned&&api.Calls.SequenceEqual([true,false]),"focus loss FALSE");}
        {var a=new Api();a.After=()=>{if(a.Calls.Last())throw new IOException("TRUE applied, response lost");};var l=new SteamTaskbarFullscreenLease(a);l.Observe(()=>Good());Check(l.Owned&&a.Calls.SequenceEqual([true]),"TRUE-then-throw retains paired cleanup");l.Observe(()=>Good());Check(!l.Owned&&a.Calls.SequenceEqual([true,false]),"uncertain TRUE cleaned even while foreground");}
        {var a=new Api{Before=()=>throw new IOException("COM init failed")};var l=new SteamTaskbarFullscreenLease(a);l.Observe(()=>Good());Check(!l.Owned&&a.Calls.Count==0,"failure before adjacent guard reserves nothing");}
        var denied=new Func<SteamTaskbarObservation,SteamTaskbarObservation>[]
        {s=>s with{Exact=false},s=>s with{ModeAllowed=false},s=>s with{MainBigPicture=false},s=>s with{Foreground=false},s=>s with{Visible=false},s=>s with{Cloaked=true},s=>s with{FullMonitor=false},s=>s with{FreshMainProof=false},s=>s with{Key=default}};
        foreach(var change in denied){var a=new Api();var l=new SteamTaskbarFullscreenLease(a);l.Observe(()=>change(Good()));Check(!l.Owned&&a.Calls.Count==0,"negative acquisition");}
        {var a=new Api{Result=-1};var l=new SteamTaskbarFullscreenLease(a);l.Observe(()=>Good());l.Observe(()=>Good());Check(a.Calls.Count==3&&l.Owned,"unknown FALSE retains exact cleanup lease");a.Result=0;l.Release(()=>Good());Check(!l.Owned&&a.Calls.Count==4,"paired cleanup retry succeeds");l.Observe(()=>Good(2));Check(a.Calls.Count==5,"new lifecycle can retry");}
        {var state=Good();var a=new Api{Before=()=>state=state with{Foreground=false}};var l=new SteamTaskbarFullscreenLease(a);l.Observe(()=>state);Check(!l.Owned&&a.Calls.Count==0,"slow COM initialization loses foreground");}
        {var state=Good();var a=new Api();a.After=()=>{if(a.Calls.Last())state=state with{ModeAllowed=false};};var l=new SteamTaskbarFullscreenLease(a);l.Observe(()=>state);Check(!l.Owned&&a.Calls.SequenceEqual([true,false]),"post TRUE mode transition releases");}
        foreach(var change in new Func<SteamTaskbarObservation,SteamTaskbarObservation>[]{s=>s with{ModeAllowed=false},s=>s with{MainBigPicture=false},s=>s with{Visible=false},s=>s with{FullMonitor=false}})
        {var s=Good();var a=new Api();var l=new SteamTaskbarFullscreenLease(a);l.Observe(()=>s);s=change(s);l.Observe(()=>s);Check(!l.Owned&&a.Calls.Last()==false,"owned intent end releases");}
        {var s=Good();var a=new Api();var l=new SteamTaskbarFullscreenLease(a);l.Observe(()=>s);s=s with{Exact=false};l.Release(()=>s);Check(l.Owned&&a.Calls.Count==1,"unknown identity retains cleanup ownership");s=Good() with{Foreground=false};l.Release(()=>s);Check(!l.Owned&&a.Calls.SequenceEqual([true,false]),"retry cleanup does not require foreign foreground");}
        foreach(var s in new[]{Good() with{IdentityEnded=true,Exact=false},Good() with{Key=Key with{Birth=Key.Birth+1}},Good() with{Key=Key with{ShellBirth=Key.ShellBirth+1}}})
        {var a=new Api();var l=new SteamTaskbarFullscreenLease(a);l.Observe(()=>Good());l.Release(()=>s);Check(!l.Owned&&a.Calls.SequenceEqual([true]),"never FALSE a replaced identity");}
        {var s=Good();var a=new Api();var l=new SteamTaskbarFullscreenLease(a);l.Observe(()=>s);a.Before=()=>s=s with{Key=Key with{Birth=Key.Birth+1}};l.Release(()=>s);Check(l.Owned&&a.Calls.Count==1,"release revalidates after slow getter");l.Release(()=>s);Check(!l.Owned,"retired identity released locally");}
        var m=new SteamTaskbarBounds(-1920,0,0,1080);var edge=m with{Bottom=1079};
        Check(SteamTaskbarGeometry.Covers(m,m,m,0),"physical negative-origin monitor");
        Check(!SteamTaskbarGeometry.Covers(m,m,m,0x00c00000),"caption not fullscreen");
        Check(!SteamTaskbarGeometry.Covers(m with{Bottom=1040},m,m,0),"work area not full monitor");
        Check(SteamTaskbarGeometry.BottomPixel(edge,m,edge,0,true),"explicit bottom autohide single pixel");
        Check(!SteamTaskbarGeometry.BottomPixel(edge,m,edge,0,false),"pixel alone not enough");
        Check(!SteamTaskbarGeometry.BottomPixel(edge with{Bottom=1078},m,edge with{Bottom=1078},0,true),"two pixels rejected");
        Check(!SteamTaskbarGeometry.BottomPixel(m with{Left=-1919},m,m with{Left=-1919},0,true),"other edge rejected");
        Check(!SteamTaskbarGeometry.BottomPixel(edge,m,edge,0x40000,true),"resizable one-pixel window rejected");
        Check(!SteamTaskbarGeometry.Covers(default,default,default,0),"empty bounds rejected");
        const string json="{\"nativeMode\":4,\"main\":true,\"focused\":true,\"windowFocus\":true,\"browserId\":35,\"physicalWidth\":3840,\"physicalHeight\":2160,\"ownerHint\":123}";
        SteamTaskbarMainProof? Parse(string text){using var d=JsonDocument.Parse(text);return SteamTaskbarMainProof.Parse(d.RootElement,7,10);}
        var proof=Parse(json);Check(proof is not null&&proof.Epoch==7&&proof.MatchesOwner(123)&&!proof.MatchesOwner(124),"receipt native owner");
        Check(proof!.MatchesBounds(new(0,0,3840,2159)),"physical viewport matches bottom-pixel intent");
        foreach(var item in new[]{json.Replace("\"nativeMode\":4","\"nativeMode\":7"),json.Replace("\"focused\":true","\"focused\":false"),json.Replace("\"windowFocus\":true","\"windowFocus\":false"),json.Replace("\"browserId\":35","\"browserId\":-1"),json.Replace("3840","99"),"{}"})Check(Parse(item) is null,"invalid receipt rejected");
        Console.WriteLine($"{count}/{count} pure taskbar checks PASS");
    }
}
