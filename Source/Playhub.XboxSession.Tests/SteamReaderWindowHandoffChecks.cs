internal static class SteamReaderWindowHandoffChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var source=new TritonSource(1,0x1302,52);
        var old=new SteamRawInputRelayTrial.SessionBinding(0xA000000102000000,23,120,27,130,source,new object(),23,7,27);
        var next=old with {Window=24,WindowThread=8};
        var initialRetry=new SteamControllerLayoutTrial.UnadmittedReaderRetry();
        var initialStopped=default(SteamRawInputRelayTrial.ReaderOutcome) with{ReaderStopped=true};
        check(initialRetry.Decide(23,source,initialStopped,true,true)==true&&initialRetry.Decide(23,source,initialStopped,true,true)==true
            &&initialRetry.Decide(23,source,initialStopped,true,true)==true&&initialRetry.Decide(23,source,initialStopped,true,true)==false,
            "unadmitted initial reader retries are bounded to three while the same window and controller remain");
        check(initialRetry.Decide(24,source,initialStopped,true,true)==true,"a genuinely recreated window can begin its own bounded fresh admission");
        foreach(var bad in new[]{initialStopped with{ReaderStopped=false},initialStopped with{Attested=true},initialStopped with{SourceRemoved=true},initialStopped with{NeutralHeld=true},initialStopped with{ReaderPid=5},initialStopped with{StartDeferred=true},initialStopped with{EndReason=RelayExit.Faulted}})
            check(initialRetry.Decide(23,source,bad,true,true)is null,"unknown or already admitted reader cannot use initial retry");
        check(initialRetry.Decide(23,source,initialStopped,false,true)is null&&initialRetry.Decide(23,source,initialStopped,true,false)is null,
            "a held session output or unclean layout cannot be bypassed by initial admission retry");
        var stopped=new SteamRawInputRelayTrial.ReaderOutcome(true,false,false,RelayExit.FocusLost,true,true,true,100,1000,1,true,true);
        var attestation=new SteamRawInputRelayTrial.SessionAttestation();
        check(attestation.Authorize(old,1,true,true)==SteamRawInputRelayTrial.ReaderAuthorization.SteamLog,"window handoff starts from an actually Steam-attested old reader");
        check(!attestation.CanCarry(next,2),"a changed window does not silently inherit the previous reader");
        int connections=0,closes=0;var output=new Output(()=>connections++,()=>closes++);
        using var session=new SteamInputSessionOutput(()=>output);
        var first=session.Borrow(new(old,100,1000,1),()=>true)!;
        check(first.TryConnectNeutral(()=>true)&&first.Close()&&session.NeutralHeld,"old reader releases the SAME owned device to neutral before the change");
        foreach(var bad in new[]{stopped with{ReaderStopped=false},stopped with{ReaderPid=101},stopped with{ReaderBirth=1001},stopped with{Epoch=2},stopped with{SourceRemoved=true},stopped with{CleanupConfirmed=true}})
            check(!session.RebindWindow(bad,next,2,()=>true),"window change refuses unmatched, live, disconnected or already detached old reader");
        foreach(var changed in new[]{next with{GamePid=28},next with{GameBirth=131},next with{CompanionPid=24},next with{CompanionBirth=121},next with{GameId=0xA000000202000000},next with{SteamSession=new object()},next with{Source=source with{Handle=53}}})
            check(!attestation.CanRebindWindow(old,changed,stopped,2)&&!session.RebindWindow(stopped,changed,2,()=>true),"new window cannot change game, wrapper, Steam context or physical controller");
        check(!session.RebindWindow(stopped,next,2,()=>false)&&session.MatchesSession(old),"late current-window refusal preserves the neutral old binding");
        check(attestation.CanRebindWindow(old,next,stopped,2)&&session.RebindWindow(stopped,next,2,()=>true)&&attestation.RebindWindow(old,next,stopped,2),"only stopped authenticated focus pause can transfer to a verified new window in the SAME game lifetime");
        check(attestation.Authorize(next,2,false,false)==SteamRawInputRelayTrial.ReaderAuthorization.None,"changed-window reader still requires its own fresh physical input proof");
        check(attestation.Authorize(next,2,false,true)==SteamRawInputRelayTrial.ReaderAuthorization.RetainedSession,"new window may reuse retained Steam session after stopped-reader proof and fresh physical samples");
        var second=session.Borrow(new(next,101,1001,2),()=>true)!;
        check(second.TryConnectNeutral(()=>true)&&second.ExistingConnection&&connections==1&&closes==0,"recreated window retains the SAME connected Xbox pad without detach or duplicate connection");
        check(second.Close()&&session.NeutralHeld,"second reader retires to neutral normally");
        session.Dispose();check(closes==1&&session.CleanupConfirmed,"only final game teardown detaches the owned session pad");
    }
    sealed class Output(Action connect,Action close):IGuardedXboxRelayOutput
    {
        public bool IsOwned{get;private set;}
        public bool TryConnectNeutral(Func<bool> current){if(!current())return false;connect();return IsOwned=true;}
        public bool TrySubmitCurrent(XboxReport report,Func<bool> current)=>IsOwned&&current();
        public void Submit(XboxReport report){}
        public bool Close(){close();IsOwned=false;return true;}
    }
}
