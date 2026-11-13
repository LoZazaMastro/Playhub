// Inert actual-code tests: no process, Steam, native, UI or driver operation.
internal static class SteamNativeOverlayInputChecks
{
    static int checks;static Action<bool,string> checkSink=null!;
    static void Need(bool value,string name){checks++;checkSink(value,"native input: "+name);}
    sealed class Channel:SteamInputOrderedIssuer.IChannel
    {
        internal SteamInputOrderedIssuer.Receipt? Last;
        internal readonly List<uint> Commands=[];
        internal bool Ack=true;
        public SteamInputOrderedIssuer.Receipt? Start(uint layout,SteamInputOrderedIssuer.Receipt? prior,Func<bool> guard)
        {
            if(prior is {} p?Last is not {} held||!ReferenceEquals(p.Identity,held.Identity)||p.Layout!=held.Layout:Last is not null)return null;
            Last=null;if(!guard())return null;
            Commands.Add(layout);return Last=new(new object(),layout);
        }
        public SteamInputOrderedIssuer.Acknowledgement Read(SteamInputOrderedIssuer.Receipt p)=>Last is {} h
            &&ReferenceEquals(h.Identity,p.Identity)&&h.Layout==p.Layout
            ?Ack?SteamInputOrderedIssuer.Acknowledgement.Acknowledged:SteamInputOrderedIssuer.Acknowledgement.Pending
            :SteamInputOrderedIssuer.Acknowledgement.Unknown;
    }
    sealed class Fixture:IDisposable
    {
        internal long Now=1000;internal bool Authority=true,Current=true,Neutral=true;internal int Queries;
        internal Action? Reenter;
        internal readonly IDisposable Scope;
        internal SteamNativeOverlayInputPresentationBridge.Command Command;
        internal readonly SteamRawInputRelayTrial.SessionBinding Session;
        internal readonly SteamInputSessionOutput.ReaderBinding Reader;
        internal readonly SteamRawInputRelayTrial.ReaderOutcome Paused;
        internal readonly Channel Channel=new();internal readonly SteamInputOrderedIssuer Issuer;
        internal Fixture()
        {
            const ulong game=12796537303900094464;const uint pid=45824;const long birth=134389500000000001;
            Scope=SteamNativeOverlayInputPresentationBridge.Configure(50200,birth-1,game,pid,birth,()=>Now,
                _=>{Queries++;var r=Reenter;Reenter=null;r?.Invoke();return Authority;});
            Command=new("prepare",Guid.NewGuid().ToString("N"),1,new('a',64),game,2979426017,50200,birth-1,
                pid,birth,44684,birth-100,28788,birth+1,1310737,9858785,1,Now+5000);
            Session=new(game,53872,birth+2,pid,birth,new(0,0x1302,0x123),new object(),(nint)600,700,pid);
            Reader=new(Session,46620,birth+3,1);
            Paused=new(true,false,false,RelayExit.OverlayOpened,true,true,false,Reader.ReaderPid,Reader.ReaderBirth,1,true,true);
            Issuer=new(Session,SteamInputOrderedIssuer.InputRoute.OwnedSessionPad,()=>Current,()=>Current,()=>Current,()=>Now,
                _=>false,()=>Neutral,Channel,n=>n.HostPid==Command.HostPid&&n.HostBirth==Command.HostBirth
                    &&n.BrowserId==Command.BrowserId&&n.NativeAppId==Command.NativeAppId
                    &&n.SessionId==Command.SessionId&&SteamNativeOverlayInputPresentationBridge.AuthorityCurrent(Command));
        }
        internal void Publish(string action){Command=Command with{Action=action,Serial=Command.Serial+1,ExpiresAt=Now+5000};Need(SteamNativeOverlayInputPresentationBridge.Publish(Command),"publish exact "+action);}
        internal SteamInputOrderedIssuer.NativeOverlayInputLease Lease()=>new(Guid.ParseExact(Command.OwnerToken,"N"),Command.SessionId,
            Command.Generation,Session.GameId,Command.ExpiresAt,Session,1,Reader,Command.HostPid,Command.HostBirth,Command.BrowserId,
            Command.NativeAppId,Command.ObservedGameId,1);
        internal void Prepare(){Need(SteamNativeOverlayInputPresentationBridge.Publish(Command),"server prepare");}
        public void Dispose()=>Scope.Dispose();
    }
    internal static int Run(Action<bool,string> assertion)
    {
        checks=0;checkSink=assertion;
        RoundTrip();NativeNegatives();MailboxFences();Ordinary();OrdinaryCancel();LateClosedAfterExpiry();AbortBeforeDispatch();OrdinaryMenu();
        checks+=SteamNativeOverlayAbortStatusChecks.Run(checkSink);
        return checks;
    }
    static void AbortBeforeDispatch()
    {
        using(var f=new Fixture())
        {
            f.Prepare();uint app=(uint)(f.Session.GameId>>32);
            Need(f.Issuer.SendGame(app,1,()=>true)&&f.Issuer.GameCommandAcknowledged(app),"abort initial game command ACK");
            bool requested=false;bool result=SteamNativeOverlayInputCycle.Run(f.Command,1,f.Session,f.Paused,f.Issuer,
                ()=>f.Current,()=>f.Neutral,()=>f.Now,ms=>{
                    f.Now+=ms;if(!requested&&SteamNativeOverlayInputPresentationBridge.Read().InputReady){requested=true;f.Publish("abort");}
                },CancellationToken.None);
            var aborted=SteamNativeOverlayInputPresentationBridge.Read();
            Need(result&&requested&&aborted.Aborted&&aborted.CleanupComplete&&aborted.ResumeAllowed&&!aborted.NativeActiveSeen,"Ready then abort releases own0 without synthesizing native FALSE");
            Need(aborted.Command?.Action=="abort"&&aborted.Phase=="aborted","aborted is distinct from native Closed");
            Need(f.Channel.Commands.SequenceEqual(new uint[]{app,769,0}),"abort orders only paired0 on same owned issuer");
            Need(!f.Issuer.SendGame(app,1,()=>true)&&f.Issuer.SendGame(app,2,()=>true),"abort still requires a new released reader epoch");
            f.Publish("abort");Need(SteamNativeOverlayInputPresentationBridge.Read().Aborted,"abort retry is idempotent after automatic ACK");
        }
        using(var f=new Fixture())
        {
            f.Prepare();uint app=(uint)(f.Session.GameId>>32);f.Issuer.SendGame(app,1,()=>true);f.Issuer.GameCommandAcknowledged(app);
            f.Publish("abort");bool result=SteamNativeOverlayInputCycle.Run(f.Command,1,f.Session,f.Paused,f.Issuer,
                ()=>f.Current,()=>f.Neutral,()=>f.Now,ms=>f.Now+=ms,CancellationToken.None);
            Need(result&&SteamNativeOverlayInputPresentationBridge.Read().Aborted,"preparing abort can restore without ever selecting menu layout");
            Need(f.Channel.Commands.SequenceEqual(new uint[]{app,0}),"preparing abort never sends769");
        }
        foreach(string bad in new[]{"token","generation","birth","TRUE","cancel","authority"})
        using(var f=new Fixture())
        {
            f.Prepare();SteamNativeOverlayInputPresentationBridge.Report(f.Command.OwnerToken,1,"menu",true,true,true,false);
            var abort=f.Command with{Action="abort",Serial=2};
            if(bad=="token")abort=abort with{OwnerToken=Guid.NewGuid().ToString("N")};
            if(bad=="generation")abort=abort with{Generation=2};if(bad=="birth")abort=abort with{GameBirth=abort.GameBirth+1};
            if(bad=="TRUE"){f.Publish("active");abort=abort with{Serial=f.Command.Serial+1};}
            if(bad=="cancel"){f.Publish("cancel");abort=abort with{Serial=f.Command.Serial+1};}
            if(bad=="authority")f.Authority=false;
            bool accepted=SteamNativeOverlayInputPresentationBridge.Publish(abort);
            Need(bad=="authority"?accepted:!accepted,"abort fence "+bad);
            if(accepted)SteamNativeOverlayInputPresentationBridge.Report(abort.OwnerToken,abort.Generation,"aborted",true,true,false,true,true);
            Need(!SteamNativeOverlayInputPresentationBridge.Read().Aborted,"no aborted receipt for "+bad);
        }
        using(var f=new Fixture())
        {
            f.Prepare();uint app=(uint)(f.Session.GameId>>32);f.Issuer.SendGame(app,1,()=>true);f.Issuer.GameCommandAcknowledged(app);
            bool requested=false;int held=0;
            bool result=SteamNativeOverlayInputCycle.Run(f.Command,1,f.Session,f.Paused,f.Issuer,()=>f.Current,()=>f.Neutral,()=>f.Now,ms=>{
                f.Now+=ms;var s=SteamNativeOverlayInputPresentationBridge.Read();
                if(!requested&&s.InputReady){requested=true;f.Publish("abort");f.Authority=false;}
                else if(requested&&++held==3){Need(!s.Aborted,"foreign foreground keeps owned pad neutral after0 ACK");f.Authority=true;}
            },CancellationToken.None);
            Need(result&&held>=3&&SteamNativeOverlayInputPresentationBridge.Read().Aborted,"exact game return permits same-pad abort recovery");
        }
        using(var f=new Fixture())
        {
            f.Prepare();SteamNativeOverlayInputPresentationBridge.ReportOrdinary(f.Command.OwnerToken,1);f.Publish("abort");
            SteamNativeOverlayInputPresentationBridge.ReportOrdinary(f.Command.OwnerToken,1,aborted:true);
            var s=SteamNativeOverlayInputPresentationBridge.Read();
            Need(s.Aborted&&s.NoOwnedOverride&&s.ResumeAllowed&&!s.NativeActiveSeen,"ordinary abort owns no override and creates no nativeClosed");
        }
    }
    static void RoundTrip()
    {
        using var f=new Fixture();f.Prepare();uint app=(uint)(f.Session.GameId>>32);
        Need(f.Issuer.SendGame(app,1,()=>true)&&f.Issuer.GameCommandAcknowledged(app),"original game command ACK before native preparation");
        int steps=0;bool sawReady=false;
        bool result=SteamNativeOverlayInputCycle.Run(f.Command,1,f.Session,f.Paused,f.Issuer,()=>f.Current,()=>f.Neutral,
            ()=>f.Now,ms=>{
                f.Now+=ms;var s=SteamNativeOverlayInputPresentationBridge.Read();
                if(!s.InputReady)return;
                sawReady=true;Need(s.ReaderStopped&&s.NeutralHeld&&s.MenuAcknowledged,"draw permission only from real stopped/neutral/769ACK facts");
                if(steps++==0){Need(f.Channel.Commands.SequenceEqual(new uint[]{app,769}),"no Set2 permission before ordered769 ACK");f.Publish("active");}
                else if(steps==2){f.Now+=100;f.Publish("renew");}
                else f.Publish("closed");
            },CancellationToken.None);
        Need(result&&sawReady,"native cycle closed safely");
        Need(f.Channel.Commands.SequenceEqual(new uint[]{app,769,0}),"same issuer exact gameâ†’769â†’automatic0 sequence with no duplicate renewal");
        var closed=SteamNativeOverlayInputPresentationBridge.Read();
        Need(closed.CleanupComplete&&closed.AutomaticAcknowledged&&closed.ResumeAllowed&&!closed.InputReady,"closed is own0ACK, not game-input claim");
        f.Publish("cancel");Need(SteamNativeOverlayInputPresentationBridge.Read().CleanupComplete
            &&SteamNativeOverlayInputPresentationBridge.Read().ResumeAllowed,"cleanup retry after closed ACK is idempotent, never stops a new reader");
        Need(!f.Issuer.SendGame(app,1,()=>true)&&f.Issuer.SendGame(app,2,()=>true),"old reader epoch cannot resume; new epoch can select canonical game");
    }
    static void NativeNegatives()
    {
        foreach(string bad in new[]{"host","birth","browser","authority","reader","stopped","neutral","EOF","focus"})
        {
            using var f=new Fixture();f.Prepare();var l=f.Lease();var r=f.Paused;
            if(bad=="host")l=l with{HostPid=1};if(bad=="birth")l=l with{HostBirth=l.HostBirth+1};if(bad=="browser")l=l with{BrowserId=0};
            if(bad=="authority")f.Authority=false;if(bad=="reader")r=r with{ReaderBirth=r.ReaderBirth+1};
            if(bad=="stopped")r=r with{ReaderStopped=false};if(bad=="neutral")f.Neutral=false;
            if(bad=="EOF")r=r with{ParentScopeRevoked=false};if(bad=="focus")r=r with{EndReason=RelayExit.FocusLost};
            bool accepted=f.Issuer.BeginNative(l)&&f.Issuer.ReaderRetired(f.Reader,r);f.Issuer.Tick();
            Need(!accepted&&!f.Channel.Commands.Contains(769),"no769 for "+bad);
        }
        using(var f=new Fixture())
        {
            f.Prepare();f.Reenter=()=>f.Publish("cancel");
            Need(!f.Issuer.BeginNative(f.Lease())&&!f.Channel.Commands.Contains(769),"cancel during native provider query cannot grant");
        }
    }
    static void MailboxFences()
    {
        using(var f=new Fixture())
        {
            f.Prepare();int q=f.Queries;_ =SteamNativeOverlayInputPresentationBridge.Read();Need(f.Queries==q,"Read never invokes native authority");
            Need(!SteamNativeOverlayInputPresentationBridge.Publish(f.Command with{Action="renew",Serial=2,ExpiresAt=f.Now+5100}),"renew before native TRUE refused");
            Need(!SteamNativeOverlayInputPresentationBridge.Publish(f.Command with{Action="active",Serial=2,GameBirth=f.Command.GameBirth+1}),"adjacent64bit birth cannot match");
            f.Publish("cancel");f.Publish("active");Need(!SteamNativeOverlayInputPresentationBridge.AuthorityCurrent(f.Command),"late native TRUE after cancel is cleanup-only");
            Need(!SteamNativeOverlayInputPresentationBridge.Publish(f.Command with{Action="renew",Serial=f.Command.Serial+1,ExpiresAt=f.Now+5200}),"renew cannot revive cancelled generation");
            f.Publish("closed");SteamNativeOverlayInputPresentationBridge.Report(f.Command.OwnerToken,1,"closed",true,true,false,true,true);
            Need(SteamNativeOverlayInputPresentationBridge.Read().ResumeAllowed,"late exact FALSE may discharge revoked debt");
        }
        using(var f=new Fixture())
        {
            f.Prepare();f.Now=f.Command.ExpiresAt;f.Publish("active");
            Need(!SteamNativeOverlayInputPresentationBridge.Read().InputReady&&!SteamNativeOverlayInputPresentationBridge.AuthorityCurrent(f.Command),"expired owner never regains readiness from late TRUE");
            Need(!SteamNativeOverlayInputPresentationBridge.Publish(f.Command with{Action="prepare",Generation=2,OwnerToken=Guid.NewGuid().ToString("N"),Serial=1,ExpiresAt=f.Now+5000}),"new prepare cannot discard unknown prior cleanup debt");
        }
        using(var f=new Fixture())
        {
            f.Prepare();f.Publish("active");f.Now+=50;f.Publish("renew");Need(SteamNativeOverlayInputPresentationBridge.AuthorityCurrent(f.Command),"live same-owner active renew accepted");
        }
    }
    static void Ordinary()
    {
        using var f=new Fixture();f.Prepare();int step=0;
        var result=SteamNativeOverlayInputCycle.RunOrdinary(f.Session.GameId,f.Session.GamePid,f.Session.GameBirth,()=>true,()=>true,
            ()=>f.Now,ms=>{
                f.Now+=ms;var s=SteamNativeOverlayInputPresentationBridge.Read();
                Need(s.OrdinaryReady&&s.InputMode=="ordinary"&&!s.InputReady&&!s.NeutralHeld&&!s.MenuAcknowledged,"ordinary draw permission contains no fake reader/PDO/769 facts");
                f.Publish(step++==0?"active":"closed");
            },CancellationToken.None);
        Need(result==true&&f.Channel.Commands.Count==0,"ordinary route never writes layout/canonical command");
        var done=SteamNativeOverlayInputPresentationBridge.Read();Need(done.CleanupComplete&&done.NoOwnedOverride&&!done.AutomaticAcknowledged,"ordinary no-owned-debt cleanup is not a fake0ACK");
        Need(!SteamNativeOverlayInputCycle.Pending(f.Session.GameId,f.Session.GamePid,f.Session.GameBirth,out _),"closed ordinary receipt cannot spin or block later game work");
    }
    static void OrdinaryCancel()
    {
        using var f=new Fixture();f.Prepare();
        SteamNativeOverlayInputPresentationBridge.ReportOrdinary(f.Command.OwnerToken,1);
        Need(SteamNativeOverlayInputPresentationBridge.Read().OrdinaryReady,"known no-owned-output/debt permits ordinary drawing");
        f.Publish("cancel");SteamNativeOverlayInputPresentationBridge.ReportOrdinary(f.Command.OwnerToken,1,true);
        var cancelled=SteamNativeOverlayInputPresentationBridge.Read();
        Need(cancelled.CleanupComplete&&!cancelled.ResumeAllowed&&!cancelled.OrdinaryReady,"post-ready cancellation retains native close debt without layout writes");
        Need(!SteamNativeOverlayInputPresentationBridge.Publish(f.Command with{Action="prepare",OwnerToken=Guid.NewGuid().ToString("N"),Generation=2,Serial=1}),"unknown ordinary renderer debt blocks next admission");
        f.Publish("closed");SteamNativeOverlayInputPresentationBridge.ReportOrdinary(f.Command.OwnerToken,1,true);
        Need(SteamNativeOverlayInputPresentationBridge.Read().ResumeAllowed,"actual false after revoked ordinary owner releases debt");
        Need(f.Channel.Commands.Count==0,"unknown inventory/no-owned-output ordinary route never writes a profile");
    }
    static void OrdinaryMenu()
    {
        foreach(string mode in new[]{"normal","cancel","expiry","authority","authority-recovery","inflight","foreign"})
        {
            using var f=new Fixture();f.Prepare();int step=0;
            if(mode is "authority" or "authority-recovery")f.Authority=false;
            if(mode=="inflight")f.Channel.Ack=false;
            var result=SteamNativeOverlayInputCycle.RunOrdinary(f.Session.GameId,f.Session.GamePid,f.Session.GameBirth,
                ()=>true,()=>mode!="foreign",()=>f.Now,ms=>{
                    f.Now+=ms;var s=SteamNativeOverlayInputPresentationBridge.Read();
                    Need(!s.InputReady&&!s.ReaderStopped&&!s.NeutralHeld,"ordinary-menu invents no SC reader/PDO proof");
                    if(mode=="inflight") {Need(!s.MenuLayoutReady&&!s.OrdinaryReady,"pending launcher is not menu readiness");if(++step==2){f.Channel.Ack=true;f.Publish("abort");}return;}
                    if(mode=="authority") {Need(!s.MenuLayoutReady&&!s.OrdinaryReady,"native authority failure cannot grant layout");if(step++==0){f.Authority=true;f.Publish("abort");}return;}
                    if(mode=="authority-recovery")
                    {
                        Need(!s.MenuLayoutReady&&!s.OrdinaryReady&&!f.Channel.Commands.Contains(769),"provider recovery never revives a cleaned owner");
                        if(step++==0)f.Authority=true;else f.Publish("abort");return;
                    }
                    if(step++==0)
                    {
                        Need(s.InputMode=="ordinary-menu"&&s.MenuLayoutReady&&s.OrdinaryReady&&!s.NoOwnedOverride,"own769 receipt is explicit and never called no-owned-override");
                        f.Publish("active");
                        if(mode=="expiry")f.Now=f.Command.ExpiresAt+1;
                    }
                    else if(mode=="cancel"&&step==2)f.Publish("cancel");
                    else f.Publish("closed");
                    if(step>10)throw new Exception("bounded fixture");
                },CancellationToken.None,new(f.Channel,()=>f.Channel.Last));
            Need(mode=="foreign"?result==false:result==true,"ordinary-menu terminal handling "+mode);
            Need(mode is "authority" or "authority-recovery" or "foreign"?f.Channel.Commands.SequenceEqual(new uint[]{0}):f.Channel.Commands.SequenceEqual(new uint[]{769,0}),"only own menu769 then ordered0; no canonical profile "+mode);
            Need(!SteamNativeOverlayInputPresentationBridge.Read().InputReady,"ordinary-menu cleanup cannot fabricate SC readiness");
        }
    }
    static void LateClosedAfterExpiry()
    {
        using var f=new Fixture();f.Prepare();int step=0;bool lowRate=false;
        bool result=SteamNativeOverlayInputCycle.Run(f.Command,1,f.Session,f.Paused,f.Issuer,()=>true,()=>f.Neutral,()=>f.Now,
            ms=>{
                f.Now+=ms;
                if(step++==0){f.Publish("active");f.Now=f.Command.ExpiresAt+1;}
                else if(ms==100)
                {
                    lowRate=true;Need(!f.Issuer.UiInputAllowed&&f.Neutral,"expired authority keeps same PDO neutral after cleanup");
                    Need(f.Channel.Commands.SequenceEqual(new uint[]{769,0}),"expiry cleans own0 once, never reissues769");
                    f.Publish("closed");
                }
            },CancellationToken.None);
        Need(result&&lowRate&&SteamNativeOverlayInputPresentationBridge.Read().ResumeAllowed,"real FALSE after grace resumes exact retained session without ending worker");
        Need(f.Channel.Commands.SequenceEqual(new uint[]{769,0}),"late closure is idempotent with already ACKed0");
    }
}
