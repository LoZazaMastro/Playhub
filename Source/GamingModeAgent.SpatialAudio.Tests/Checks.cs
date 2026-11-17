// SPDX-License-Identifier: MIT
using GamingMode.Services;
internal static class Checks
{
    static int count;static void Need(bool b,string reason){++count;if(!b)throw new Exception(reason);}
    static readonly Guid A=Guid.Parse("{A289735D-FA3E-4E35-9D7D-B6F896ACB2E7}");
    static readonly Guid B=Guid.Parse("{B53D940C-B846-4831-9F76-D102B9B725A0}");
    const string E=@"\\?\SWD#MMDEVAPI#{0.0.0.00000000}.{763476ef-bf8c-4ab6-a7e0-883f124f0c8f}#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    const string N="{0.0.0.00000000}.{763476ef-bf8c-4ab6-a7e0-883f124f0c8f}";
    sealed class Reader:ISpatialEndpointReader
    {
        public SpatialEndpoint? Value=new(E,N,true,true,A,A,new[]{new SpatialFormat(A,"Atmos",true,true),new SpatialFormat(B,"Sonic",true,false)});
        public string? DefaultEndpointId=>E;
        public int Reads;public Task<SpatialEndpoint?> ReadAsync(string e,CancellationToken c){c.ThrowIfCancellationRequested();++Reads;return Task.FromResult(Value?.EndpointId==e?Value:null);}
    }
    sealed class Command(Reader reader):ISpatialCommand
    {
        public bool Package=true;public bool CommandInFlight=false;public bool Clean=true;public SpatialCommandState ReadState()=>new(Package,CommandInFlight,Clean);
        public int Attempts,Started;public Action? Before,After;public bool Cleanup=true,Change=true,Active=true;public int Exit;
        public Task<SpatialCommandResult> RunAsync(string n,Guid g,Func<CancellationToken,Task<bool>> guard,CancellationToken stop)=>Run(n,g,guard,stop);
        async Task<SpatialCommandResult> Run(string n,Guid g,Func<CancellationToken,Task<bool>> guard,CancellationToken stop)
        {
            ++Attempts;Before?.Invoke();if(!await guard(stop))return new(false,true,null,"context-changed");++Started;
            if(Change)reader.Value=reader.Value!with{Selected=g,Active=Active?g:reader.Value!.Active};After?.Invoke();
            Clean=Cleanup;return new(true,Cleanup,Exit,"simulated");
        }
    }
    static async Task Main(string[] args)
    {
        Need(WinRtSpatialEndpointReader.NativeId(E)==N,"actual saved WinRT render ID maps exactly");
        Need(WinRtSpatialEndpointReader.NativeId(E.Replace("00000000","00000001")) is null,"capture ID refuses");
        Need(WinRtSpatialEndpointReader.NativeId("DefaultRenderDevice") is null,"no default alias");
        Need(PinnedSpatialCommand.Arguments(N,Guid.Empty) is ["/SetSpatial",N,""] ,"Off is exact empty argv, not speaker Stereo");
        Need(PinnedSpatialCommand.Arguments(N,B) is {} argv&&argv[2]==B.ToString("B"),"GUID argv exact");
        foreach(string bad in new[]{"DefaultRenderDevice","*","Speakers",N+" /Mute AllAppVolume",N+";x"})Need(PinnedSpatialCommand.Arguments(bad,B) is null,"reject wildcard/friendly/injected endpoint");
        foreach(string mode in new[]{"success","off","stale","unknown","unsupported","changed-before","default-before","native-before","default-after","no-change","selected-only","cleanup","nonzero","already"})
        {
            var r=new Reader();var c=new Command(r);var service=new SpatialAudioService(r,c);var desired=mode=="off"?Guid.Empty:B;
            var expected=mode=="stale"?B:A;if(mode=="unknown")desired=Guid.NewGuid();
            if(mode=="unsupported")r.Value=r.Value!with{Supported=false};
            if(mode=="changed-before")c.Before=()=>r.Value=r.Value!with{Selected=B};
            if(mode=="default-before")c.Before=()=>r.Value=r.Value!with{IsDefault=false};
            if(mode=="native-before")c.Before=()=>r.Value=r.Value!with{NativeId=N.Replace("763476ef","11111111")};
            if(mode=="default-after")c.After=()=>r.Value=r.Value!with{IsDefault=false};
            if(mode=="no-change")c.Change=false;if(mode=="selected-only")c.Active=false;if(mode=="cleanup")c.Cleanup=false;
            if(mode=="nonzero")c.Exit=1;if(mode=="already")desired=A;
            var got=await service.SetAsync(new(E,expected,desired));
            string state=mode switch{"success" or "off"=>"applied-confirmed","stale" or "changed-before" or "default-before" or "native-before"=>"context-changed","unknown" or "unsupported"=>"format-unsupported","default-after"=>"context-changed-after-command","no-change"=>"not-confirmed","selected-only"=>"selected-active-different","cleanup"=>"command-cleanup-unknown","nonzero"=>"command-refused-or-interrupted",_=>"already-confirmed"};
            Need(got.State==state,"actual service classification "+mode+" got "+got.State);
            Need(c.Started==(mode is "stale" or "unknown" or "unsupported" or "changed-before" or "default-before" or "native-before" or "already"?0:1),"no command on invalid CAS/capability "+mode);
        }
        using(var invalid=System.Text.Json.JsonDocument.Parse("{\"action\":\"read\",\"format\":\"bad\"}"))Need(SpatialAudioService.Parse(invalid.RootElement) is null,"read cannot carry write fields");
        foreach(string invalid in new[]{"{}","[]","{\"action\":\"read\",\"action\":\"read\"}","{\"action\":false}","{\"action\":\"set\",\"endpointId\":\"DefaultRenderDevice\"}"})
            using(var j=System.Text.Json.JsonDocument.Parse(invalid))Need(SpatialAudioService.Parse(j.RootElement) is null,"strict malformed/duplicate parser");
        using(var j=System.Text.Json.JsonDocument.Parse("{\"action\":\"read\"}"))Need(SpatialAudioService.Parse(j.RootElement)?.Action=="read","strict read parsed");
        {
            var r=new Reader();var c=new Command(r);var service=new SpatialAudioService(r,c);
            async Task<Microsoft.AspNetCore.Http.IResult> Route(string json,bool authorized=true){var context=new Microsoft.AspNetCore.Http.DefaultHttpContext();context.Request.Body=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));return await SpatialAudioService.HandleAsync(context,()=>authorized,service);}
            var unauthorized=await Route("{not JSON",false);Need(unauthorized is Microsoft.AspNetCore.Http.IStatusCodeHttpResult{StatusCode:401}&&r.Reads==0,"auth precedes parsing/data read");
            foreach(string invalid in new[]{"{","{\"action\":\"read\",\"extra\":\"x\"}",new string('x',4097)})Need(await Route(invalid) is Microsoft.AspNetCore.Http.IStatusCodeHttpResult{StatusCode:400},"actual endpoint body/fields/depth bounded");
            var read=(Microsoft.AspNetCore.Http.IValueHttpResult)await Route("{\"action\":\"read\"}");var dto=System.Text.Json.JsonSerializer.SerializeToElement(read.Value);
            Need(dto.GetProperty("ok").GetBoolean()&&dto.GetProperty("canSet").GetBoolean()&&dto.GetProperty("selectedFormat").GetString()==A.ToString("B").ToUpperInvariant(),"actual read DTO status known");
            Need(dto.GetProperty("licenseState").GetString()=="unknown"&&dto.GetProperty("options").GetArrayLength()==3,"support never claims licensed");
            c.Package=false;read=(Microsoft.AspNetCore.Http.IValueHttpResult)await Route("{\"action\":\"read\"}");dto=System.Text.Json.JsonSerializer.SerializeToElement(read.Value);Need(!dto.GetProperty("canSet").GetBoolean()&&dto.GetProperty("ok").GetBoolean(),"missing helper disables writes without hiding readback");c.Package=true;
            string json=System.Text.Json.JsonSerializer.Serialize(new{action="set",endpointId=E,expectedFormat=A.ToString("B"),format=B.ToString("B")});
            var set=(Microsoft.AspNetCore.Http.IValueHttpResult)await Route(json);dto=System.Text.Json.JsonSerializer.SerializeToElement(set.Value);Need(dto.GetProperty("ok").GetBoolean()&&dto.GetProperty("code").GetString()=="applied-confirmed"&&c.Started==1,"actual endpoint delegates exact set and confirmed readback");
        }
        if(args.Length==1)await Ownership(args[0]);
        // Exercise the actual package verifier and final guard without ever
        // entering Process.Start or issuing any real SetSpatial command.
        if(args.Length==1)
        {
            var real=new PinnedSpatialCommand(args[0]);bool checkedGuard=false;
            var refused=await real.RunAsync(N,B,_=>{checkedGuard=true;return Task.FromResult(false);},default);
            Need(checkedGuard&&!refused.Started&&refused.CleanupComplete&&refused.Reason=="context-changed","actual pinned package validates then final refusal never executes");
        }
        Console.WriteLine(count+" spatial prototype checks PASS; fake endpoint/command only, actual package verification no Process.Start");
    }
    sealed class OwnedProcess:ISpatialOwnedProcess
    {
        public bool Exit;public bool ThrowOnExitRead;public int Kills,Disposes;public Action? OnWait;
        public bool HasExited=>ThrowOnExitRead?throw new System.ComponentModel.Win32Exception():Exit;
        public int ExitCode=>0;
        public Task WaitAsync(CancellationToken c)
        {var run=OnWait;OnWait=null;run?.Invoke();return Exit?Task.CompletedTask:c.IsCancellationRequested?Task.FromCanceled(c):Task.FromException(new TimeoutException());}
        public Task DrainAsync()=>Task.CompletedTask;
        public void Kill(){++Kills;/* Inert kernel boundary: access denied / process not yet drained. */}
        public void Dispose(){++Disposes;}
    }
    static async Task Ownership(string package)
    {
        foreach(string mode in new[]{"timeout","cancel"})
        {
            var reader=new Reader();var held=new OwnedProcess();int starts=0;bool observedFlight=false;using var cancel=new CancellationTokenSource();
            PinnedSpatialCommand? command=null;
            command=new(package,info=>{
                ++starts;Need(!info.UseShellExecute&&info.CreateNoWindow,"actual ProcessStartInfo hidden/no shell");
                Need(info.ArgumentList.SequenceEqual(new[]{"/SetSpatial",N,B.ToString("B")}),"actual fixed process argv");
                if(starts==1){if(mode=="cancel")cancel.Cancel();return held;}
                reader.Value=reader.Value!with{Selected=B,Active=B};return new OwnedProcess{Exit=true};
            });
            var service=new SpatialAudioService(reader,command);
            async Task<System.Text.Json.JsonElement> RouteRead()
            {
                var context=new Microsoft.AspNetCore.Http.DefaultHttpContext();context.Request.Body=new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"action\":\"read\"}"));
                var result=(Microsoft.AspNetCore.Http.IValueHttpResult)await SpatialAudioService.HandleAsync(context,()=>true,service);
                return System.Text.Json.JsonSerializer.SerializeToElement(result.Value);
            }
            held.OnWait=()=>{var dto=RouteRead().GetAwaiter().GetResult();observedFlight=dto.GetProperty("commandInFlight").GetBoolean()&&!dto.GetProperty("canSet").GetBoolean()&&!dto.GetProperty("cleanupComplete").GetBoolean();};
            var result=await service.SetAsync(new(E,A,B),cancel.Token);
            Need(observedFlight,"actual read DTO stays nonwritable/cleanupunknown during actual RunAsync");
            Need(result.State=="command-cleanup-unknown"&&!result.CleanupComplete&&starts==1&&held.Kills>0,"actual RunAsync retains exact undrained owned process "+mode);
            for(int n=0;n<2;n++)
            {
                var dto=await RouteRead();Need(dto.GetProperty("ok").GetBoolean()&&!dto.GetProperty("canSet").GetBoolean()&&!dto.GetProperty("cleanupComplete").GetBoolean(),"READ after unknown cannot reenable "+mode);
            }
            Need(!command.ReadState().CanSet&&command.ReadState().PackageVerified,"verified package is not cleanup proof");
            var blocked=await command.RunAsync(N,B,_=>Task.FromResult(true),default);
            Need(!blocked.Started&&!blocked.CleanupComplete&&starts==1,"actual next RunAsync blocked by same retained process");
            var serviceBlocked=await service.SetAsync(new(E,A,A));Need(serviceBlocked.State=="command-pending-cleanup"&&starts==1,"even already matching user request cannot clear pending process");
            held.ThrowOnExitRead=true;Need(!command.ReadState().CleanupComplete,"unknown owned handle read remains failclosed");held.ThrowOnExitRead=false;
            held.Exit=true;
            var after=await RouteRead();Need(after.GetProperty("canSet").GetBoolean()&&after.GetProperty("cleanupComplete").GetBoolean()&&held.Disposes==1,"only actual SAME owned process exit releases readiness");
            var applied=await service.SetAsync(new(E,A,B));Need(applied.State=="applied-confirmed"&&starts==2,"new command permitted after real owned exit and fresh CAS/readback");
        }
    }

}
