// Playhub Xbox Session. Distributed under GPL-3.0-only.
// Same layout worker/channel/PDO as the game relay. No mirror or C++ ABI.
internal static class SteamNativeOverlayInputCycle
{
    internal static void ServeOrdinary(ulong gameId,uint gamePid,long gameBirth,Func<bool> noOwnedOverride,
        Func<bool> current,Func<long> clock,Action<int> wait,CancellationToken cancellation,OrdinaryChannel? menuChannel=null)
    {
        while(current()&&!cancellation.IsCancellationRequested)
        {
            var result=RunOrdinary(gameId,gamePid,gameBirth,noOwnedOverride,current,clock,wait,cancellation,menuChannel);
            if(result==false)return;
            wait(100);
        }
    }
    internal static bool? RunOrdinary(ulong gameId,uint gamePid,long gameBirth,Func<bool> noOwnedOverride,
        Func<bool> current,Func<long> clock,Action<int> wait,CancellationToken cancellation,OrdinaryChannel? menuChannel=null)
    {
        if(menuChannel is not null)return RunOrdinaryMenu(gameId,gamePid,gameBirth,noOwnedOverride,current,clock,wait,cancellation,menuChannel);
        if(!Pending(gameId,gamePid,gameBirth,out var admitted)||admitted is null||!noOwnedOverride())return null;
        long cancelledAt=0;
        while(current()&&!cancellation.IsCancellationRequested)
        {
            var s=SteamNativeOverlayInputPresentationBridge.Read();var c=s.Command;
            if(c is null||c.OwnerToken!=admitted.OwnerToken||c.Generation!=admitted.Generation||!noOwnedOverride())return false;
            if(c.Action=="abort")
            {
                SteamNativeOverlayInputPresentationBridge.ReportOrdinary(c.OwnerToken,c.Generation,aborted:true);
                if(SteamNativeOverlayInputPresentationBridge.Read().ResumeAllowed)return true;
                wait(100);continue;
            }
            if(c.Action is "closed" or "cancel")
            {
                SteamNativeOverlayInputPresentationBridge.ReportOrdinary(c.OwnerToken,c.Generation,true);
                if(SteamNativeOverlayInputPresentationBridge.Read().ResumeAllowed)return true;
                if(cancelledAt==0)cancelledAt=clock();
                wait(clock()-cancelledAt>=1500?100:15);continue;
            }
            if(cancelledAt!=0||c.ExpiresAt<=clock()||!SteamNativeOverlayInputPresentationBridge.AuthorityCurrent(admitted))
            {
                SteamNativeOverlayInputPresentationBridge.ReportOrdinary(c.OwnerToken,c.Generation,true);
                if(cancelledAt==0)cancelledAt=clock();
                wait(clock()-cancelledAt>=1500?100:15);continue;
            }
            SteamNativeOverlayInputPresentationBridge.ReportOrdinary(c.OwnerToken,c.Generation);
            wait(15);
        }
        return false;
    }
    internal sealed record OrdinaryChannel(SteamInputOrderedIssuer.IChannel Channel,Func<SteamInputOrderedIssuer.Receipt?> ReadCurrent);
    internal static bool? RunOrdinaryMenu(ulong gameId,uint gamePid,long gameBirth,Func<bool> noOwnedOverride,
        Func<bool> current,Func<long> clock,Action<int> wait,CancellationToken cancellation,OrdinaryChannel channel)
    {
        if(!Pending(gameId,gamePid,gameBirth,out var admitted)||admitted is null||!noOwnedOverride())return null;
        var latest=channel.ReadCurrent();bool started=false,menuReady=false,cleaned=false,retired=false;
        bool Same(SteamNativeOverlayInputPresentationBridge.Command? c)=>c is not null&&c.OwnerToken==admitted.OwnerToken&&c.Generation==admitted.Generation;
        bool Grant()
        {
            if(!current()||!SteamNativeOverlayInputPresentationBridge.AuthorityCurrent(admitted)||!current())return false;
            var s=SteamNativeOverlayInputPresentationBridge.Read();var c=s.Command;
            return Same(c)&&c!.Action is "prepare" or "active" or "renew"&&c.ExpiresAt>clock();
        }
        bool Cleanup()
        {
            if(cleaned)return true;
            // Only the exact locally retained command may be replaced. No FG
            // requirement is added to withdrawal of our own menu override.
            var zero=channel.Channel.Start(0,latest,()=>true);if(zero is null)return false;latest=zero;
            long until=clock()+1500;
            for(int n=0;n<100&&clock()<=until;n++)
            {
                var status=channel.Channel.Read(zero.Value);
                if(status==SteamInputOrderedIssuer.Acknowledgement.Acknowledged)return cleaned=true;
                if(status!=SteamInputOrderedIssuer.Acknowledgement.Pending)return false;
                wait(15);
            }
            return false;
        }
        try
        {
            while(current()&&!cancellation.IsCancellationRequested)
            {
                var s=SteamNativeOverlayInputPresentationBridge.Read();var c=s.Command;
                if(!Same(c))return false;
                if(c!.Action is "closed" or "cancel" or "abort"||retired||!Grant())
                {
                    // Cleanup is a one-way transition for this exact owner.
                    // A later provider recovery cannot revive its retired769.
                    retired=true;
                    bool automatic=Cleanup();
                    SteamNativeOverlayInputPresentationBridge.ReportOrdinaryMenu(admitted.OwnerToken,admitted.Generation,false,automatic,
                        closed:c.Action is "closed" or "cancel",aborted:c.Action=="abort");
                    if(SteamNativeOverlayInputPresentationBridge.Read().ResumeAllowed)return true;
                    if(!automatic)return false;
                    wait(100);continue;
                }
                if(!started)
                {
                    var receipt=channel.Channel.Start(769,latest,Grant);
                    if(receipt is null)return false;latest=receipt;started=true;
                }
                if(!menuReady)
                {
                    var state=channel.Channel.Read(latest!.Value);
                    if(state is SteamInputOrderedIssuer.Acknowledgement.Failed or SteamInputOrderedIssuer.Acknowledgement.Unknown)return false;
                    menuReady=state==SteamInputOrderedIssuer.Acknowledgement.Acknowledged;
                }
                if(menuReady&&Grant())SteamNativeOverlayInputPresentationBridge.ReportOrdinaryMenu(admitted.OwnerToken,admitted.Generation,true);
                wait(15);
            }
            return false;
        }
        finally
        {
            bool automatic=Cleanup();
            var s=SteamNativeOverlayInputPresentationBridge.Read();
            if(Same(s.Command))SteamNativeOverlayInputPresentationBridge.ReportOrdinaryMenu(admitted.OwnerToken,admitted.Generation,false,automatic,
                closed:s.Command!.Action is "closed" or "cancel",aborted:s.Command.Action=="abort");
        }
    }
    internal static bool Pending(ulong gameId,uint gamePid,long gameBirth,
        out SteamNativeOverlayInputPresentationBridge.Command? admission)
    {
        var s=SteamNativeOverlayInputPresentationBridge.Read();var c=s.Command;
        admission=c;
        return c is not null&&c.FullGameId==gameId&&c.GamePid==gamePid&&c.GameBirth==gameBirth
            &&!(s.CleanupComplete&&s.ResumeAllowed);
    }
    internal static bool Run(SteamNativeOverlayInputPresentationBridge.Command admission,
        long epoch,SteamRawInputRelayTrial.SessionBinding session,SteamRawInputRelayTrial.ReaderOutcome reader,
        SteamInputOrderedIssuer issuer,Func<bool> current,Func<bool> neutralHeld,
        Func<long> clock,Action<int> wait,CancellationToken cancellation,Func<bool>? disconnected=null)
    {
        void Report(string phase,bool menu,bool automatic,bool resume=false)=>
            SteamNativeOverlayInputPresentationBridge.Report(admission.OwnerToken,admission.Generation,phase,
                reader.ReaderStopped&&reader.ReaderReleased,neutralHeld(),menu,automatic,resume);
        bool Same(SteamNativeOverlayInputPresentationBridge.Command c)=>c.OwnerToken==admission.OwnerToken
            &&c.Generation==admission.Generation&&c.SessionId==admission.SessionId&&c.FullGameId==session.GameId
            &&c.GamePid==session.GamePid&&c.GameBirth==session.GameBirth&&c.HostPid==admission.HostPid
            &&c.HostBirth==admission.HostBirth&&c.BrowserId==admission.BrowserId&&c.NativeAppId==admission.NativeAppId
            &&c.ObservedGameId==admission.ObservedGameId;
        bool Cleanup(bool safe)
        {
            bool automatic=SteamMirrorInputCycle.Release(issuer,clock,wait);
            Report(automatic?"closed":"uncertain",false,automatic,safe&&current());
            return automatic&&safe&&SteamNativeOverlayInputPresentationBridge.Read().ResumeAllowed;
        }
        bool AbortBeforeDispatch()
        {
            bool automatic=SteamMirrorInputCycle.Release(issuer,clock,wait);
            if(!automatic){Report("uncertain",false,false);return false;}
            Report("returning",false,true);
            // An abort is not a native FALSE. Keep the same neutral pad until
            // exact game/profile context is back, then create a fresh reader epoch.
            while(!cancellation.IsCancellationRequested&&current())
            {
                var state=SteamNativeOverlayInputPresentationBridge.Read();var c=state.Command;
                if(c is null||!Same(c))return false;
                if(c.Action=="closed")return Cleanup(true);
                if(c.Action!="abort"||state.NativeActiveSeen)return AwaitFalseAfterCleanup();
                if(reader.ReaderStopped&&reader.ReaderReleased&&reader.CanResume&&neutralHeld()&&issuer.ResumeAllowed
                    &&SteamNativeOverlayInputPresentationBridge.AbortAuthorityCurrent(c)&&current())
                {
                    Report("aborted",false,true,true);
                    if(SteamNativeOverlayInputPresentationBridge.Read().Aborted)return true;
                }
                if(disconnected?.Invoke()==true)break;
                wait(100);
            }
            return Cleanup(false);
        }
        bool AwaitFalseAfterCleanup()
        {
            if(!SteamMirrorInputCycle.Release(issuer,clock,wait)){Report("uncertain",false,false);return false;}
            // Acknowledge only our0 now so frontend cancellation can issue Set0.
            // Keep this SAME neutral PDO while the actual false arrives.
            Report("closed",false,true);
            long started=clock();
            while(!cancellation.IsCancellationRequested&&current())
            {
                var s=SteamNativeOverlayInputPresentationBridge.Read();
                if(s.Command is {} c&&Same(c)&&c.Action=="closed")return Cleanup(true);
                if(disconnected?.Invoke()==true)break;
                wait(clock()-started>=1500?100:15);
            }
            return Cleanup(false);
        }
        if(!reader.CanResume||reader.EndReason!=RelayExit.OverlayOpened||!neutralHeld())return Cleanup(false);
        var initial=SteamNativeOverlayInputPresentationBridge.Read();
        if(initial.Command is {} terminal&&Same(terminal))
        {
            if(terminal.Action=="abort")return AbortBeforeDispatch();
            if(terminal.Action=="closed")return Cleanup(initial.NativeActiveSeen||initial.ReadyEver);
            if(terminal.Action=="cancel")return initial.ReadyEver?AwaitFalseAfterCleanup():Cleanup(true);
        }
        var binding=new SteamInputSessionOutput.ReaderBinding(session,reader.ReaderPid,reader.ReaderBirth,reader.Epoch);
        if(!Same(admission)||admission.Serial==0||admission.ExpiresAt<=clock())return Cleanup(false);
        var lease=new SteamInputOrderedIssuer.NativeOverlayInputLease(Guid.ParseExact(admission.OwnerToken,"N"),
            admission.SessionId,admission.Generation,session.GameId,admission.ExpiresAt,session,epoch,binding,
            admission.HostPid,admission.HostBirth,admission.BrowserId,admission.NativeAppId,admission.ObservedGameId,admission.Serial);
        if(!issuer.BeginNative(lease)||!issuer.ReaderRetired(binding,reader))return Cleanup(false);
        long uncertainAt=0;
        while(!cancellation.IsCancellationRequested&&current())
        {
            if(disconnected?.Invoke()==true)return Cleanup(false);
            var snapshot=SteamNativeOverlayInputPresentationBridge.Read();var c=snapshot.Command;long now=clock();
            if(c is null||!Same(c))return Cleanup(false);
            if(c.Action=="abort")return AbortBeforeDispatch();
            if(c.Action=="closed")return Cleanup(snapshot.NativeActiveSeen||snapshot.ReadyEver);
            if(c.Action=="cancel")return snapshot.ReadyEver?AwaitFalseAfterCleanup():Cleanup(true);
            bool trusted=c.Action is "prepare" or "active" or "renew"&&c.ExpiresAt>now
                &&SteamNativeOverlayInputPresentationBridge.AuthorityCurrent(admission)&&current();
            if(!trusted)
            {
                // Revoke and clean our override, but an expired/missing true
                // event is never a Closed proof. Allow only bounded late false.
                if(uncertainAt==0)
                {
                    uncertainAt=now;
                    if(!SteamMirrorInputCycle.Release(issuer,clock,wait)){Report("uncertain",false,false);return false;}
                    Report("returning",false,true);
                }
                wait(clock()-uncertainAt>=1500?100:15);continue;
            }
            if(uncertainAt!=0)
            {
                // Returning focus cannot revive an already retired input lease.
                // Keep the same neutral device until the real false event.
                wait(clock()-uncertainAt>=1500?100:15);continue;
            }
            if(c.ExpiresAt>lease.ExpiresAt)
            {
                var next=lease with{ExpiresAt=c.ExpiresAt};if(!issuer.RenewNative(next))return Cleanup(false);lease=next;
            }
            issuer.Tick();
            if(issuer.State==SteamInputOrderedIssuer.Phase.Uncertain)return Cleanup(false);
            if(issuer.UiInputAllowed)Report("menu",true,false);
            else Report("preparing",false,false);
            wait(15);
        }
        return Cleanup(false);
    }
}
