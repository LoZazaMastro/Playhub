internal static class SteamOrdinaryMirrorMenuChecks
{
    private const ulong Game=0xf123456702000000;
    private const uint Pid=77;
    private const long Birth=100;
    private sealed class Fixture:SteamInputOrderedIssuer.IChannel
    {
        internal long Now=1000;
        internal bool Alive=true,FreshCopy=true,ForeignReceipt=false;
        internal int Steps;
        internal SteamMirrorInputPresentation? Frame;
        internal SteamInputOrderedIssuer.Receipt? Latest;
        internal readonly List<uint> Commands=[];
        internal Action? OnWait,OnRead,OnStart,OnCurrentReceipt;
        internal Func<SteamMirrorInputPresentation?>? ReadCurrent;
        internal readonly CancellationTokenSource Cancel=new();
        internal Fixture()
        {
            Frame=new(new string('a',64),1,Game,20000,1,Now,101,102,11,12,50,Birth,
                (uint)Environment.ProcessId,Pid,3,true,true,true,true,false);
        }
        internal SteamMirrorInputPresentation? Read()
        {
            OnRead?.Invoke();
            return Frame is {} p&&p.Prepared(Now)?FreshCopy?p with{ReadAt=Now}:p:null;
        }
        internal SteamInputOrderedIssuer.Receipt? CurrentReceipt(){OnCurrentReceipt?.Invoke();return Latest;}
        internal bool NoOverride()=>Latest is null||Latest.Value.Layout==0;
        public SteamInputOrderedIssuer.Receipt? Start(uint layout,SteamInputOrderedIssuer.Receipt? prior,Func<bool> guard)
        {
            OnStart?.Invoke();
            if(ForeignReceipt||prior!=Latest||!guard())return null;
            var receipt=new SteamInputOrderedIssuer.Receipt(new object(),layout);
            Latest=receipt;Commands.Add(layout);return receipt;
        }
        public SteamInputOrderedIssuer.Acknowledgement Read(SteamInputOrderedIssuer.Receipt receipt)
            =>receipt==Latest&&!ForeignReceipt?SteamInputOrderedIssuer.Acknowledgement.Acknowledged:SteamInputOrderedIssuer.Acknowledgement.Unknown;
        internal void Wait(int ms)
        {
            Now+=ms;Steps++;
            if(OnWait is not null){OnWait();return;}
            if(Steps==1)Frame=Frame! with{State=3,ShownCurrent=true,DestinationShown=true};
            if(Steps==3)Close();
        }
        internal void Close()=>Frame=Frame! with{State=0,PreparedCurrent=false,ShownCurrent=false,DestinationShown=false,DwmAttached=false,CleanupComplete=true,ForegroundScope=SteamMirrorForegroundScope.None};
        internal SteamOrdinaryMirrorMenuCycle.Result? Run(SteamOrdinaryMirrorMenuCycle? cycle=null)
            =>(cycle??new(Game,Pid,Birth)).Run(ReadCurrent??Read,()=>Frame,NoOverride,()=>Alive,()=>Now,Wait,Cancel.Token,new(this,CurrentReceipt));
    }

    private sealed class Windows(Fixture fixture):SteamMirrorInputCycle.IMirrorWindows
    {
        public nint Foreground()=>900;
        public uint ThreadProcess(nint window,out uint pid)
        {pid=window==900?Pid:(uint)Environment.ProcessId;return 7;}
        public nint Ancestor(nint window,uint flags)=>window;
        public nint Owner(nint window)=>0;
        public nint Cookie(nint window)=>window==101?11:12;
        public bool Enabled(nint window)=>true;
        public bool Visible(nint window)=>window==101||fixture.Frame?.DestinationShown==true;
    }

    internal static void Run(Action<bool,string> check)
    {
        // Exercise the complete production native-copy/window join. An opening
        // transition can refuse one read without consuming this generation.
        var transition=new Fixture();var sameCycle=new SteamOrdinaryMirrorMenuCycle(Game,Pid,Birth);
        transition.Frame=transition.Frame! with{State=2,ShownCurrent=false,DestinationShown=false};
        var windows=new Windows(transition);bool crossGetters=false;int transitionCopies=0;
        transition.ReadCurrent=()=>SteamMirrorInputCycle.ReadCurrent(Game,Pid,Birth,()=>true,windows,()=>
        {
            if(crossGetters&&++transitionCopies==2)
                transition.Frame=transition.Frame! with{State=3,ShownCurrent=true,DestinationShown=true};
            return transition.Frame is {} value?value with{ReadAt=transition.Now}:null;
        },()=>transition.Now);
        check(transition.Run(sameCycle) is null&&transition.Commands.Count==0&&transition.Steps==0,
            "Prepared cannot dispatch769 or retire the ordinary generation before stable Shown");
        crossGetters=true;transitionCopies=0;
        check(transition.Run(sameCycle) is null&&transition.Commands.Count==0&&transitionCopies==2&&transition.Frame!.Shown(transition.Now),
            "Actual ReadCurrent crossing Prepared-to-Shown refuses only this pre-admission read without layout effects");
        crossGetters=false;
        check(transition.Run(sameCycle) is {MenuCommandStarted:true,AutomaticCleanup:true,EndReason:"presentation-closed"}
            &&transition.Commands.SequenceEqual(new uint[]{769,0}),
            "Same generation admits stable Shown reads once then cleans its single menu receipt on genuine Closed");

        var healthy=new Fixture();var result=healthy.Run();
        check(result is {MenuCommandStarted:true,MenuLauncherAcknowledged:true,AutomaticCleanup:true,EndReason:"presentation-closed"}
            &&healthy.Commands.SequenceEqual(new uint[]{769,0}),"Ordinary Mirror with no SC/reader/PDO sends only menu769 and its exact automatic0");
        check(typeof(SteamOrdinaryMirrorMenuCycle).GetConstructors(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)
            .All(c=>c.GetParameters().All(p=>p.ParameterType!=typeof(TritonSource)&&p.ParameterType!=typeof(SteamRawInputRelayTrial.SessionBinding))),
            "Ordinary Mirror capability never constructs a physical SC or gameplay SessionBinding");

        foreach(var kind in new[]{"game","birth","id","owner","cookie","future","expired","stale-read"})
        {
            var f=new Fixture();f.FreshCopy=false;
            f.Frame=kind switch
            {
                "game"=>f.Frame! with{GamePid=78},"birth"=>f.Frame! with{GameBirth=101},
                "id"=>f.Frame! with{GameId=Game+(1UL<<32)},"owner"=>f.Frame! with{OwnerPid=(uint)Environment.ProcessId+1},
                "cookie"=>f.Frame! with{SourceCookie=0},"future"=>f.Frame! with{ReadAt=1001},
                "expired"=>f.Frame! with{ExpiresAt=1000},_=>f.Frame! with{ReadAt=749}
            };
            check(f.Run() is null&&f.Commands.Count==0,"Ordinary Mirror rejects "+kind+" before any layout/hidden cleanup");
        }
        var absent=new Fixture{Frame=null};check(absent.Run() is null&&absent.Commands.Count==0,"Absent Mirror never resets a layout");
        var cancelled=new Fixture();cancelled.Cancel.Cancel();check(cancelled.Run() is null&&cancelled.Commands.Count==0,"Pre-admission cancellation never changes layout");

        var foreign=new Fixture{Latest=new(new object(),42)};
        check(foreign.Run() is null&&foreign.Commands.Count==0,"Ordinary Mirror never adopts a foreign nonzero profile");
        var lateForeign=new Fixture();lateForeign.OnCurrentReceipt=()=>lateForeign.Latest=new(new object(),42);
        check(lateForeign.Run() is null&&lateForeign.Commands.Count==0,"Late receipt lookup cannot smuggle a foreign layout into admission");

        foreach(var kind in new[]{"source-cookie","destination-cookie","owner-birth","generation","prepared","closed","expired","cancel","game-ended"})
        {
            var f=new Fixture();f.OnWait=()=>
            {
                switch(kind)
                {
                    case "source-cookie":f.Frame=f.Frame! with{SourceCookie=13};break;
                    case "destination-cookie":f.Frame=f.Frame! with{DestinationCookie=14};break;
                    case "owner-birth":f.Frame=f.Frame! with{OwnerBirth=51};break;
                    case "generation":f.Frame=f.Frame! with{Generation=2};break;
                    case "prepared":f.Frame=f.Frame! with{State=2,ShownCurrent=false,DestinationShown=false};break;
                    case "closed":f.Close();break;
                    case "expired":f.Now=20000;break;
                    case "cancel":f.Cancel.Cancel();break;
                    default:f.Alive=false;break;
                }
            };
            var ended=f.Run();
            check(ended is {AutomaticCleanup:true}&&f.Commands.SequenceEqual(new uint[]{769,0}),"Ordinary Mirror withdraws only its own receipt after "+kind);
        }
        var revoked=new Fixture();var worker=new SteamOrdinaryMirrorMenuCycle(Game,Pid,Birth);
        var original=revoked.Frame;revoked.OnWait=()=>revoked.Frame=null;
        check(revoked.Run(worker) is {AutomaticCleanup:true},"Unknown presentation retires its UI layout after exact own0");
        revoked.Frame=original! with{ExpiresAt=30000};revoked.OnWait=null;
        check(revoked.Run(worker) is null&&revoked.Commands.SequenceEqual(new uint[]{769,0}),"Same-generation recovery cannot regrant769 after cleanup");
        revoked.Frame=original! with{Generation=2,ExpiresAt=30000};revoked.Steps=0;
        check(revoked.Run(worker) is {AutomaticCleanup:true}&&revoked.Commands.SequenceEqual(new uint[]{769,0,769,0}),"Fresh exact generation can create a new ordinary menu lease");

        var stolen=new Fixture();stolen.OnWait=()=>stolen.ForeignReceipt=true;
        check(stolen.Run() is {AutomaticCleanup:false}&&stolen.Commands.SequenceEqual(new uint[]{769}),"Foreign receipt replacement refuses hidden automatic overwrite");
        var beforeDispatch=new Fixture();beforeDispatch.OnStart=()=>beforeDispatch.Frame=null;
        check(beforeDispatch.Run() is {MenuCommandStarted:false,AutomaticCleanup:false}&&beforeDispatch.Commands.Count==0,"Final dispatch guard failure never invents a receipt or automatic ACK");
        var lateGetter=new Fixture();int reads=0;
        lateGetter.OnRead=()=>{if(++reads==6)lateGetter.Frame=lateGetter.Frame! with{SourceCookie=99};};
        check(lateGetter.Run() is {MenuCommandStarted:false}&&lateGetter.Commands.Count==0,"Final native-copy getter drift refuses the actual channel dispatch");
        foreach(var kind in new[]{"game-ended","cancelled"})
        {
            var f=new Fixture();int copies=0;
            f.OnRead=()=>{if(++copies==6){if(kind=="game-ended")f.Alive=false;else f.Cancel.Cancel();}};
            check(f.Run() is {MenuCommandStarted:false}&&f.Commands.Count==0,"Last native-copy "+kind+" refuses769 before actual channel dispatch");
        }

        var frame=new Fixture().Frame! with{ObservedAt=1,ReadAt=5000,ExpiresAt=20000};
        check(SteamMirrorInputCycle.MatchAfterGetters(frame,frame,5000) is not null,"Mirror current native readAt admits an old observation residue without inventing a fresh image");
        check(SteamMirrorInputCycle.MatchAfterGetters(frame,frame with{ReadAt=4749},5000) is null,"Mirror stale readAt is rejected despite fresh other fields");
        check(SteamMirrorInputCycle.MatchAfterGetters(frame,frame with{ReadAt=5001},5000) is null,"Mirror future readAt is rejected");
        check(SteamMirrorInputCycle.MatchAfterGetters(frame,frame with{ObservedAt=5001},5000) is null,"Future original observation remains rejected");
    }
}
