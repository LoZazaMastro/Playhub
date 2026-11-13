// Playhub Steam Session. Distributed under GPL-3.0-only.
using B=SteamNativeOverlayInputPresentationBridge;
internal static class SteamReaderNeutralBootstrapChecks
{
    internal static void Run(Action<bool,string> check)
    {
        const ulong game=0x8000000502000000;
        var source=new TritonSource(0,0x1302,123);
        var binding=new SteamRawInputRelayTrial.SessionBinding(game,11,100,20,200,source,new object(),(nint)100,8,20);
        B.Command Make(long expires)=>new("prepare",Guid.NewGuid().ToString("N"),1,new('a',64),game,480,10,100,20,200,1,50,30,300,40,480,1,expires);
        void Pending(Action<B.Command> run)
        {
            using var scope=B.Configure(10,100,game,20,200,authorityCurrent:_=>true);
            var command=Make(Environment.TickCount64+5000);
            check(B.Publish(command),"neutral bootstrap requires authenticated exact prepare");run(command);
        }
        Pending(c=>check(SteamRawInputRelayTrial.PendingNativePrepare(c,binding,()=>true),"current exact prepare permits neutral-only bootstrap"));
        Pending(c=>check(!SteamRawInputRelayTrial.PendingNativePrepare(c,binding with{GamePid=21},()=>true),"different retained game cannot borrow pending prepare"));
        Pending(c=>check(!SteamRawInputRelayTrial.PendingNativePrepare(c,binding,()=>false),"missing current foreground/owner refuses bootstrap"));
        Pending(c=>{
            int calls=0;
            check(!SteamRawInputRelayTrial.PendingNativePrepare(c,binding,()=>{if(++calls==2)B.Publish(c with{Action="cancel",Serial=2});return true;}),
                "cancel in final current getter cannot attach neutral output");
        });
        Pending(c=>{
            int calls=0;
            check(!SteamRawInputRelayTrial.PendingNativePrepare(c,binding,()=>{if(++calls==2)B.Publish(c with{Action="active",Serial=2});return true;}),
                "active/replaced command in final getter is not pending prepare");
        });
        using(var scope=B.Configure(10,100,game,20,200,authorityCurrent:_=>true))
        {
            var c=Make(Environment.TickCount64+100);check(B.Publish(c),"short-lived prepare accepted only while current");int calls=0;
            check(!SteamRawInputRelayTrial.PendingNativePrepare(c,binding,()=>{if(++calls==2)Thread.Sleep(120);return true;}),
                "expiry in final current getter cannot attach neutral output");
        }
        foreach(int loss in new[]{0,1,2,3})
        {
            var output=new Output();using var owner=new SteamInputSessionOutput(()=>output);
            var reader=owner.Borrow(new(binding,333,1000,1),()=>true)!;int currentCalls=0;
            var result=SteamRawInputRelayTrial.HoldNeutralForPause(reader,()=>loss switch
            {0=>true,1=>false,2=>++currentCalls<2,_=>throw new InvalidOperationException("current unavailable")});
            check(output.Reports.All(r=>r==default),"neutral bootstrap never emits a gameplay report");
            check(reader.Released&&(loss==0?result.Reason==RelayExit.OverlayOpened&&result.CleanupConfirmed&&owner.NeutralHeld:result.Reason!=RelayExit.OverlayOpened),
                "owner loss/exception cannot be reported as authenticated pause");
        }
        var held=new Output();using(var owner=new SteamInputSessionOutput(()=>held))
        {
            var reader=owner.Borrow(new(binding,333,1000,1),()=>true)!;
            check(reader.TryHoldNeutral(()=>true),"pending reader can attach neutral-only output");
            bool rejected=false;try{reader.Submit(new(0x1000,0,0,0,0,0,0));}catch(InvalidOperationException){rejected=true;}
            check(rejected&&held.Reports.Count==0,"neutral-only reader cannot forward held buttons");
            check(!reader.TryConnectNeutral(()=>true),"neutral-only lease cannot convert to gameplay connection");
            check(reader.Close()&&owner.NeutralHeld&&held.Closes==0,"neutral retirement retains same owned PDO until session disposal");
        }
        var detached=new SteamRawInputRelayTrial.ReaderOutcome(true,false,true,RelayExit.FocusLost,false,true,false,333,1000,2,true,false,true);
        check(detached.FreshAdmissionRequired&&!detached.CanResume,"terminal detach requests new admission, never stale pause resume");
        foreach(var denied in new[]{detached with{Attested=false},detached with{SourceRemoved=true},detached with{CleanupConfirmed=false},
            detached with{ReaderReleased=false},detached with{ReaderStopped=false},detached with{TerminalScopeLost=false},
            detached with{NeutralHeld=true},detached with{EndReason=RelayExit.Faulted},detached with{EndReason=RelayExit.SessionEnded},
            detached with{EndReason=RelayExit.OverlayOpened},detached with{ReaderPid=0},detached with{ReaderBirth=0},detached with{Epoch=0}})
            check(!denied.FreshAdmissionRequired,"unknown/EOF/fault/removed/inflight cannot schedule fresh admission");
        check(SteamControllerLayoutTrial.CanReadmit(detached,()=>true,()=>true,()=>true,()=>true),"complete fresh scope/source/own0/detach can schedule a new epoch");
        for(int gate=0;gate<4;gate++)
            check(!SteamControllerLayoutTrial.CanReadmit(detached,()=>gate!=0,()=>gate!=1,()=>gate!=2,()=>gate!=3),
                "foreign/unknown or changed source/automatic inflight/PDO live refuse readmission");
        int scopeReads=0;
        check(!SteamControllerLayoutTrial.CanReadmit(detached,()=>++scopeReads<3,()=>true,()=>true,()=>true),"late current target/foreground loss refuses scheduling");
        int sourceReads=0;
        check(!SteamControllerLayoutTrial.CanReadmit(detached,()=>true,()=>++sourceReads<2,()=>true,()=>true),"fresh source drift at final read refuses scheduling");
        int resetReads=0;
        check(!SteamControllerLayoutTrial.CanReadmit(detached,()=>true,()=>true,()=>++resetReads<2,()=>true),"own receipt changes at final read refuses scheduling");
        foreach(string mode in new[]{"opening","missing-witness","source-gone","foreign","eof","identity","pdo-gone","native-fail","provider-throws","owner-disposed"})
        {
            bool scope=true,witness=false;int observations=0;long now=0;
            var native=new SubmitOutput();using var owner=new SteamInputSessionOutput(()=>native);
            var reader=owner.Borrow(new(binding,333,1000,1),()=>scope,()=>
            {
                if(mode=="provider-throws")throw new InvalidOperationException("fake witness getter");
                if(mode=="owner-disposed")owner.Dispose();
                return witness&&mode is "opening" or "owner-disposed"?RelayExit.OverlayOpened:null;
            })!;
            native.BeforeHeld=()=>
            {
                if(mode=="native-fail")return;
                if(mode=="pdo-gone"){native.Owned=false;return;}
                scope=false;witness=mode!="missing-witness";
            };
            native.FailNative=mode=="native-fail";
            RelayObservation Observe()=>new(true,true,false,true,false,source,
                ++observations<=5?default(XboxReport):new XboxReport(1,0,0,0,0,0,0));
            var result=SteamInputRelay.Run(Observe,()=>reader,()=>now,_=>{now+=1000;if(now>8000)throw new InvalidOperationException("fake loop limit");},CancellationToken.None);
            check(result.Reason==(mode=="opening"?RelayExit.OverlayOpened:RelayExit.Faulted),"only authenticated current final-guard opening becomes pause: "+mode);
            check(native.HeldReports==0,"submit refusal/fault never forwards held report: "+mode);
            check(result.SubmitRejection==(mode=="opening"?"paused-current":mode=="native-fail"?"native-submit-failed":mode is "pdo-gone" or "provider-throws"?"not-recorded":"no-longer-current"),
                "bounded submit rejection diagnostic preserves failure kind: "+mode);
        }
    }
    private sealed class SubmitOutput:IGuardedXboxRelayOutput
    {
        internal bool Owned,FailNative;internal int HeldReports;internal Action? BeforeHeld;
        public bool IsOwned=>Owned;
        public bool TryConnectNeutral(Func<bool> current){if(!current())return false;Owned=true;return current();}
        public bool TrySubmitCurrent(XboxReport report,Func<bool> current)
        {
            if(report!=default)BeforeHeld?.Invoke();
            if(!Owned||!current())return false;
            if(report!=default){if(FailNative)throw new NativeRelaySubmitFailed();HeldReports++;}
            return true;
        }
        public void Submit(XboxReport report)=>throw new InvalidOperationException("unguarded fake update");
        public bool Close(){Owned=false;return true;}
    }
    private sealed class Output:IGuardedXboxRelayOutput
    {
        internal readonly List<XboxReport> Reports=[];internal int Closes;private bool connected;
        public bool IsOwned=>connected;
        public bool TryConnectNeutral(Func<bool> current){if(!current())return false;connected=true;return current();}
        public void Submit(XboxReport report)=>Reports.Add(report);
        public bool TrySubmitCurrent(XboxReport report,Func<bool> current){if(!connected||!current())return false;Reports.Add(report);return true;}
        public bool Close(){Closes++;connected=false;return true;}
    }
}
