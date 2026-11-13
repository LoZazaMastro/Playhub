using System.Security.Cryptography;
using Playhub.Shared;

internal static class SteamTrackedWrapperChainChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(SteamRawInputRelayTrial.AwaitReader(false,6000,true,false,false,SteamRawInputRelayTrial.ReaderAuthorization.SteamTrackedWrapperChain),
            "authorized Epic reader may wait neutral for release beyond bootstrap timeout");
        check(!SteamRawInputRelayTrial.AwaitReader(false,6000,true,false,false,SteamRawInputRelayTrial.ReaderAuthorization.None),
            "unattested reader still times out without a Steam log or prior proof");
        check(!SteamRawInputRelayTrial.AwaitReader(true,6000,true,false,false,SteamRawInputRelayTrial.ReaderAuthorization.SteamTrackedWrapperChain),
            "EOF or terminal completion ends even authorized neutral waiting");
        check(!SteamRawInputRelayTrial.AwaitReader(false,6000,false,false,false,SteamRawInputRelayTrial.ReaderAuthorization.SteamTrackedWrapperChain),
            "missing native layout cannot borrow extended neutral waiting");
        check(SteamRawInputRelayTrial.AwaitReader(false,6000,true,true,false,SteamRawInputRelayTrial.ReaderAuthorization.None),
            "original Steam-tracked bootstrap wait is unchanged");
        check(SteamRawInputRelayTrial.AwaitReader(false,1000,false,false,false,SteamRawInputRelayTrial.ReaderAuthorization.None),
            "initial bounded unauthenticated bootstrap wait is unchanged");
        using (var r = new Rig())
        {
            using var chain = r.Create(); using var reader = r.Bind(chain!);
            check(chain?.Current == true && reader?.Current(r.Binding) == true, "Epic tracked wrapper retains its exact sidecar and actual hash-pinned reader");
            var policy = new SteamRawInputRelayTrial.SessionAttestation();
            check(policy.Authorize(r.Binding,1,false,true,true,reader) == SteamRawInputRelayTrial.ReaderAuthorization.SteamTrackedWrapperChain,
                "first Epic reader has explicit wrapper-chain authority with Steam tracked false");
            check(!policy.CanCarry(r.Binding,2) && policy.Authorize(r.Binding,2,false,true) == SteamRawInputRelayTrial.ReaderAuthorization.None,
                "Epic epoch cannot carry without a new actual reader capability");
            check(policy.Authorize(r.Binding,2,false,false,true,reader) == SteamRawInputRelayTrial.ReaderAuthorization.None,
                "wrapper chain never replaces fresh native descriptor/report");
            check(policy.Authorize(r.Binding,2,false,true,false,reader) == SteamRawInputRelayTrial.ReaderAuthorization.None,
                "wrapper chain never replaces current game scope");
            reader!.Dispose();
            check(!policy.CanCarry(r.Binding,2,reader) && policy.Authorize(r.Binding,2,false,true,true,reader) == SteamRawInputRelayTrial.ReaderAuthorization.None,
                "retired reader cannot revive a new epoch");
            r.Reader = r.Reader with { Pid=41, Birth=260 };
            using var next = r.Bind(chain!);
            check(next?.Current(r.Binding) == true && policy.Authorize(r.Binding,2,false,true,true,next) == SteamRawInputRelayTrial.ReaderAuthorization.SteamTrackedWrapperChain,
                "next epoch needs its own newly retained reader generation");
            check(!next!.Current(r.Binding with { GameBirth=999 }), "reader proof is bound to the exact game birth");
            check(!next.Current(r.Binding with { Source=new(0,0x1302,999) }), "reader proof cannot migrate to another controller source");
        }
        void OpenDenied(Action<Rig> change,string label)
        {
            using var r = new Rig(); change(r); using var chain = r.Create(); check(chain is null,label);
        }
        OpenDenied(r=>r.Handoff=r.Handoff with { Epic=null }, "ordinary Win32 handoff cannot create Epic bootstrap authority");
        OpenDenied(r=>r.Handoff=r.Handoff with { Epic=r.Handoff.Epic! with { GameBirth=999 } }, "witness game generation mismatch refused");
        OpenDenied(r=>r.Handoff=r.Handoff with { Epic=r.Handoff.Epic! with { WrapperBirth=999 } }, "witness wrapper generation mismatch refused");
        OpenDenied(r=>r.Handoff=r.Handoff with { GameId=480 }, "observed alias cannot replace canonical full game identity");
        OpenDenied(r=>r.Tracked=false, "wrapper must have its own real Steam tracking proof");
        OpenDenied(r=>r.Epic=false, "missing or revoked historical Epic witness refuses bootstrap");
        OpenDenied(r=>r.Root=false, "Steam installation drift refuses bootstrap");
        OpenDenied(r=>r.Companion=r.Companion with { ParentPid=99 }, "sidecar parent must be the actual retained wrapper");
        OpenDenied(r=>r.Companion=r.Companion with { Birth=201 }, "sidecar birth substitution refused");
        OpenDenied(r=>r.Companion=r.Companion with { Image=Path.Combine(Rig.Directory,"other.exe") }, "sidecar image must be the exact installed helper");
        OpenDenied(r=>r.Wrapper=r.Wrapper with { Birth=101 }, "wrapper PID reuse cannot match retained birth");
        OpenDenied(r=>r.Wrapper=r.Wrapper with { Image=Path.Combine(Rig.Directory,"other.exe") }, "wrapper executable path substitution refused");
        OpenDenied(r=>r.Companion=r.Companion with { Sid="other" }, "sidecar cannot cross user SID");
        OpenDenied(r=>r.Companion=r.Companion with { Session=2 }, "sidecar cannot cross Windows session");
        OpenDenied(r=>r.Steam=r.Steam with { Birth=101 }, "Steam generation newer than tracked wrapper is refused");
        OpenDenied(r=>r.Steam=r.Steam with { Image=Path.Combine(Rig.Directory,"fakeSteam.exe") }, "Steam retained image must match trusted installation");
        OpenDenied(r=>r.Steam=r.Steam with { Sid="other" }, "Steam process must be in the same user session");
        OpenDenied(r=>r.SteamAvailable=false, "ambiguous or unavailable signed Steam owner refuses bootstrap");
        void BindDenied(Action<Rig> change,string label,string? hash=null)
        {
            using var r = new Rig(); using var chain = r.Create(); change(r); using var reader=r.Bind(chain!,hash); check(reader is null,label);
        }
        BindDenied(r=>r.Reader=r.Reader with { ParentPid=10 }, "reader must be child of exact sidecar, not merely of the wrapper");
        BindDenied(r=>r.Reader=r.Reader with { Birth=251 }, "reader process birth must match the Process returned by launch");
        BindDenied(r=>r.Reader=r.Reader with { Image=Path.Combine(Rig.Directory,"other.exe") }, "reader executable path substitution refused");
        BindDenied(r=>r.Reader=r.Reader with { Sid="other" }, "reader user mismatch refused");
        BindDenied(r=>r.Reader=r.Reader with { Session=2 }, "reader Windows session mismatch refused");
        BindDenied(_=>{}, "reader content hash mismatch refused",new string('0',64));
        BindDenied(r=>r.Binding=r.Binding with { SteamSession=new object() }, "binding must use same retained trusted Steam instance");
        BindDenied(r=>r.Binding=r.Binding with { CompanionBirth=999 }, "binding sidecar generation mismatch refused");
        using (var r=new Rig())
        {
            using var chain=r.Create();using var reader=r.Bind(chain!);int getter=0;
            r.Opened["reader"].BeforeCurrent=()=>{if(++getter==2)chain!.Dispose();};
            check(!reader!.Current(r.Binding), "dispose during last reader getter must be rejected");
        }
        foreach (var item in new[]{"wrapper","sidecar","steam","reader"})
        {
            using var r=new Rig();using var chain=r.Create();using var reader=r.Bind(chain!);
            var process=r.Opened[item];process.Alive=false;
            check(!reader!.Current(r.Binding), $"{item} death or unknown handle retires forwarding proof");
            process.Alive=true;check(!reader.Current(r.Binding), $"{item} transient recovery cannot revive a retired reader");
        }
        using (var r=new Rig())
        {
            using var chain=r.Create();using var reader=r.Bind(chain!);
            r.Opened["reader"].BeforeCurrent=()=>r.Opened["reader"].Facts=r.Reader with { ParentPid=999 };
            check(!reader!.Current(r.Binding), "reader identity drift during a slow getter is rejected after that getter");
        }
        using (var r=new Rig())
        {
            using var chain=r.Create();using var reader=r.Bind(chain!);
            r.BeforeEpic=()=>chain!.Dispose();
            check(!reader!.Current(r.Binding), "reentrant owner disposal during witness getter cannot grant");
        }
        using (var r=new Rig())
        {
            using var chain=r.Create();using var reader=r.Bind(chain!);
            r.BeforeRoot=()=>r.Opened["steam"].Alive=false;
            check(!reader!.Current(r.Binding), "Steam death during final root getter is caught by retained postcheck");
        }
        using (var r=new Rig())
        {
            using var chain=r.Create();using var reader=r.Bind(chain!);
            r.Epic=false;
            check(!reader!.Current(r.Binding), "Epic witness revocation stops forwarding after initial acceptance");
            r.Epic=true;check(!reader.Current(r.Binding), "old witness recovery does not revive the revoked reader");
        }
        using (var r=new Rig())
        {
            var policy=new SteamRawInputRelayTrial.SessionAttestation();
            check(policy.Authorize(r.Binding,1,true,true)==SteamRawInputRelayTrial.ReaderAuthorization.SteamLog
                &&policy.Authorize(r.Binding,2,false,true)==SteamRawInputRelayTrial.ReaderAuthorization.RetainedSession,
                "existing direct SteamLog admission and retained carry are unchanged");
        }
        using (var r=new Rig())
        {
            var witness=r.Handoff.Epic!;
            using var epic=new EpicCaptureScope(new FakeProcess(r.Wrapper),
                new FakeProcess(new(30,150,witness.GameExecutable,5,"SID",1)),witness,null!);
            check(epic.Retains(witness), "sealed scope matches the immutable handoff witness");
            check(!epic.Retains(witness with { Ticket=new string('b',32) }), "a different historical ticket cannot borrow another scope");
            epic.Dispose();
            check(!epic.Retains(witness), "disposed historical scope cannot grant reader authority");
        }
    }

    private sealed class FakeProcess(EpicProcessIdentity identity) : IEpicRetainedProcess
    {
        internal EpicProcessIdentity Facts=identity;internal bool Alive=true,Disposed;internal Action? BeforeCurrent;
        public EpicProcessIdentity Identity=>Facts;
        public bool Current {get{BeforeCurrent?.Invoke();return Alive&&!Disposed;}}
        public void Dispose()=>Disposed=true;
    }
    private sealed class Rig : SteamTrackedWrapperChain.IEnvironment,IDisposable
    {
        internal static readonly string Directory=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"BootstrapFixture-NoFiles"));
        internal EpicProcessIdentity Wrapper=new(10,100,Path.Combine(Directory,"Playhub.GameSession.exe"),1,"SID",1);
        internal EpicProcessIdentity Companion=new(20,200,Path.Combine(Directory,"Playhub.XboxSession.exe"),10,"SID",1);
        internal EpicProcessIdentity Reader=new(40,250,Path.Combine(Directory,"PlayhubSteamInputReader.exe"),20,"SID",1);
        internal EpicProcessIdentity Steam=new(1,10,Path.Combine(Directory,"steam.exe"),2,"SID",1);
        internal bool Tracked=true,Root=true,Epic=true,SteamAvailable=true;internal Action? BeforeEpic,BeforeRoot;
        internal readonly Dictionary<string,FakeProcess> Opened=new();
        internal Win32CompanionHandoff Handoff;
        internal SteamRawInputRelayTrial.SessionBinding Binding;
        public object SteamIdentity {get;}=new object();
        public bool RootCurrent {get{BeforeRoot?.Invoke();return Root;}}
        public bool EpicCurrent {get{BeforeEpic?.Invoke();return Epic;}}
        public bool WrapperTracked=>Tracked;
        public string SteamImage=>Path.Combine(Directory,"steam.exe");
        internal Rig()
        {
            const ulong id=0x8000000102000000;
            var p=new EpicLaunchProvenance(1,new string('a',32),"ns","item","artifact","manifest",new string('1',64),Directory,
                Path.Combine(Directory,"game.exe"),10,100,5,20,"EpicGamesLauncher.exe",new string('2',64),110,30,150,123,0);
            Handoff=new(10,100,30,150,id,p.GameExecutable,p);
            Binding=new(id,20,200,30,150,new(0,0x1302,123),SteamIdentity,123,99,30);
        }
        public IEpicRetainedProcess? Open(uint pid)
        {
            var (name,facts)=pid==10?("wrapper",Wrapper):pid==20?("sidecar",Companion):("reader",Reader);
            var process=new FakeProcess(facts);Opened[name]=process;return process;
        }
        public IEpicRetainedProcess? OpenSteam(EpicProcessIdentity wrapper)
        {if(!SteamAvailable)return null;var process=new FakeProcess(Steam);Opened["steam"]=process;return process;}
        internal SteamTrackedWrapperChain? Create()=>SteamTrackedWrapperChain.OpenFixture(this,Handoff,20,200,Directory);
        internal SteamTrackedWrapperChain.ReaderProof? Bind(SteamTrackedWrapperChain chain,string? expected=null)
        {
            using var image=new MemoryStream(new byte[]{1,2,3});
            return chain.BindFixture(Reader.Pid,Reader.Pid==41?260:250,image,Path.Combine(Directory,"PlayhubSteamInputReader.exe"),
                expected??Convert.ToHexString(SHA256.HashData(new byte[]{1,2,3})),Binding);
        }
        public void Dispose(){foreach(var process in Opened.Values)process.Dispose();}
    }
}
