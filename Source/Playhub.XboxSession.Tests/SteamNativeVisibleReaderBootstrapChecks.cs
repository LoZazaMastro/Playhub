// Playhub Steam Session. Distributed under GPL-3.0-only.
using Playhub.Shared;
internal static class SteamNativeVisibleReaderBootstrapChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(ReaderZeroChecks.Run()==0,"Actual no-reader-start opening preserves real stopped reader/PDO proof");
        const ulong game=0x8000000502000000;
        var source=new TritonSource(0,0x1302,123);
        var binding=new SteamRawInputRelayTrial.SessionBinding(game,11,100,20,200,source,new object(),(nint)100,8,20);
        SteamMirrorNativeVisibleDto Fresh()
        {
            long now=Environment.TickCount64;
            return new(){Phase=1,SessionId=new('a',64),Generation="3",GameId=game.ToString(),ExpiresAt=(now+2000).ToString(),
                ObservedAt=now.ToString(),ReadAt=now.ToString(),SourceHwnd="500",SourceCookie="99",OwnerPid=11,OwnerBirth="100",GamePid=20,GameBirth="200",
                PhysicalObservedAt=now.ToString(),PhysicalExpiresAt=(now+250).ToString(),GuideEpoch="7",ReceiptFrame="10",SourceFrame="20",
                ValveOpen=true,OwnCleanupComplete=true,BaselineSamples=16,BaselineMatched=16,OutputSamples=16,OutputMatched=16};
        }
        check(SteamRawInputRelayTrial.ReadNativeVisibleBootstrap(Fresh,binding,()=>true)is not null,"Native240 exact current physical source permits neutral bootstrap only");
        check(SteamRawInputRelayTrial.ReadNativeVisibleBootstrap(null,binding,()=>true)is null,"No Native240 callback preserves the default refusal");
        check(SteamRawInputRelayTrial.ReadNativeVisibleBootstrap(Fresh,binding,()=>false)is null,"Foreign/current-frame refusal cannot start neutral reader");
        foreach(var bad in new[]{Fresh()with{OwnerPid=12},Fresh()with{OwnerBirth="101"},Fresh()with{GamePid=21},Fresh()with{GameBirth="201"},
            Fresh()with{GameId=0x8000000602000000UL.ToString()},Fresh()with{Phase=2,PhysicalObservedAt="0",PhysicalExpiresAt="0"},
            Fresh()with{PhysicalExpiresAt="1"},Fresh()with{PhysicalObservedAt="0"},Fresh()with{ValveOpen=false},Fresh()with{OwnCleanupComplete=false},
            Fresh()with{InputAuthority=true},Fresh()with{VisualAuthority=true},Fresh()with{OutputMatched=15},Fresh()with{SourceFrame="10"}})
            check(SteamRawInputRelayTrial.ReadNativeVisibleBootstrap(()=>bad,binding,()=>true)is null,"Missing/foreign/stale/unverified Native240 never grants bootstrap");
        foreach(string mutation in new[]{"generation","guide","source-cookie","source-window","session","owner-birth"})
        {
            int reads=0;var original=Fresh();
            SteamMirrorNativeVisibleDto? Read()=>++reads==1?original:mutation switch
            {"generation"=>Fresh()with{Generation="4"},"guide"=>Fresh()with{GuideEpoch="8"},"source-cookie"=>Fresh()with{SourceCookie="100"},
                "source-window"=>Fresh()with{SourceHwnd="501"},"session"=>Fresh()with{SessionId=new('b',64)},_=>Fresh()with{OwnerBirth="101"}};
            check(SteamRawInputRelayTrial.ReadNativeVisibleBootstrap(Read,binding,()=>true)is null,"Reentrant native replacement invalidates neutral witness: "+mutation);
        }
        int scopeReads=0;
        check(SteamRawInputRelayTrial.ReadNativeVisibleBootstrap(Fresh,binding,()=>++scopeReads<3)is null,"Final frame/source loss vetoes neutral bootstrap");
        var attestation=new SteamRawInputRelayTrial.SessionAttestation();
        check(attestation.Authorize(binding,1,false,true,true)==SteamRawInputRelayTrial.ReaderAuthorization.None,"Physical Native240 is never independent Steam reader authorization");
        check(attestation.Authorize(binding,1,true,true,true)==SteamRawInputRelayTrial.ReaderAuthorization.SteamLog,"Native neutral path retains actual Steam-log reader authorization");
        check(attestation.Authorize(binding,2,false,true,true)==SteamRawInputRelayTrial.ReaderAuthorization.RetainedSession,"A later exact retained reader can carry existing tracking");
        check(attestation.Authorize(binding with{GameBirth=201},3,false,true,true)==SteamRawInputRelayTrial.ReaderAuthorization.None,"Replaced game cannot carry reader attestation");
        var released=new FreshReleasedReaderFrames();
        check(!released.Observe(1,new(0x1000,0,0,0,0,0,0),true),"Held source cannot count as released bootstrap evidence");
        check(!released.Observe(2,default,true)&&released.Observe(3,default,true),"Two real fresh released reports remain required");
        var output=new NeutralOutput();using var session=new SteamInputSessionOutput(()=>output);
        var reader=session.Borrow(new(binding,123,12345,1),()=>false)!;
        var result=SteamRawInputRelayTrial.HoldNeutralForPause(reader,()=>SteamRawInputRelayTrial.ReadNativeVisibleBootstrap(Fresh,binding,()=>true)is not null);
        check(result.Reason==RelayExit.OverlayOpened&&result.CleanupConfirmed&&session.NeutralHeld&&reader.ReleaseConfirmed,"Exact native bootstrap retires reader while retaining the SAME neutral PDO");
        bool rejected=false;try{reader.Submit(new(0x1000,0,0,0,0,0,0));}catch(InvalidOperationException){rejected=true;}
        check(rejected&&output.HeldReports==0&&output.Reports.All(x=>x==default),"Native neutral-only reader never forwards held gameplay input");
        check(!reader.TryConnectNeutral(()=>true),"Retired neutral-only reader cannot become a gameplay lease");
    }
    private sealed class NeutralOutput:IGuardedXboxRelayOutput
    {
        private bool owned;internal int HeldReports;internal readonly List<XboxReport> Reports=[];
        public bool IsOwned=>owned;
        public bool TryConnectNeutral(Func<bool> current){if(!current())return false;owned=true;return current();}
        public bool TrySubmitCurrent(XboxReport report,Func<bool> current){if(!owned||!current())return false;Reports.Add(report);if(report!=default)HeldReports++;return true;}
        public void Submit(XboxReport report)=>throw new InvalidOperationException("Unguarded fixture update");
        public bool Close(){owned=false;return true;}
    }
}
