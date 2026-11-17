// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using Windows.Media.Devices;
namespace GamingMode.Services;
public sealed record SpatialFormat(Guid Id,string Name,bool Supported,bool RequiresProviderLicense);
public sealed record SpatialEndpoint(string EndpointId,string NativeId,bool IsDefault,bool Supported,
    Guid Selected,Guid Active,IReadOnlyList<SpatialFormat> Formats);
public sealed record SpatialSetRequest(string EndpointId,Guid ExpectedSelected,Guid Requested,bool RequireDefault=true);
public sealed record SpatialCommandResult(bool Started,bool CleanupComplete,int? ExitCode,string Reason);
public sealed record SpatialSetResult(string State,SpatialEndpoint? Endpoint,bool CommandStarted,bool CleanupComplete);
public interface ISpatialEndpointReader
{
    string? DefaultEndpointId { get; }
    Task<SpatialEndpoint?> ReadAsync(string exactEndpointId,CancellationToken stop);
}
public sealed record SpatialCommandState(bool PackageVerified,bool InFlight,bool CleanupComplete)
{
    public bool CanSet=>PackageVerified&&!InFlight&&CleanupComplete;
}
public interface ISpatialCommand
{
    SpatialCommandState ReadState();
    Task<SpatialCommandResult> RunAsync(string nativeId,Guid format,
        Func<CancellationToken,Task<bool>> finalGuard,CancellationToken stop);
}
public sealed class SpatialAudioService
{
    readonly ISpatialEndpointReader reader;readonly ISpatialCommand command;
    public SpatialAudioService(ISpatialEndpointReader reader,ISpatialCommand command){this.reader=reader;this.command=command;}
    readonly SemaphoreSlim serial=new(1,1);
    public SpatialAudioService(string packageDirectory):this(new WinRtSpatialEndpointReader(),new PinnedSpatialCommand(packageDirectory)){}
    public static void Map(WebApplication app,XboxShellBroker shell,string packageDirectory)
    {
        var service=new SpatialAudioService(packageDirectory);
        app.MapPost("/session/audio/spatial",(RequestDelegate)(async context=>{
            var result=await HandleAsync(context,()=>shell.IsAuthorized(context.Connection.RemoteIpAddress,
                context.Request.Headers["X-Playhub-Shell-Token"].ToString()),service);
            await result.ExecuteAsync(context);
        }));
    }
    internal static async Task<IResult> HandleAsync(HttpContext context,Func<bool> authorized,SpatialAudioService service)
    {
        if(!authorized())return Results.StatusCode(401);
        try
        {
            var body=new byte[4097];int count=0;
            while(count<body.Length){int n=await context.Request.Body.ReadAsync(body.AsMemory(count),context.RequestAborted);if(n==0)break;count+=n;}
            if(count is 0 or >4096)return Results.StatusCode(400);
            using var json=JsonDocument.Parse(body.AsMemory(0,count),new(){MaxDepth=4});
            var request=Parse(json.RootElement);if(request is null)return Results.StatusCode(400);
            if(request.Action=="read")
            {
                string? id=service.reader.DefaultEndpointId;
                var current=id is null?null:await service.reader.ReadAsync(id,context.RequestAborted);
                return Results.Json(service.Dto(current,"read",false,null),Json);
            }
            var set=await service.SetAsync(new(request.EndpointId!,request.Expected,request.Format),context.RequestAborted);
            return Results.Json(service.Dto(set.Endpoint,set.State,set.CommandStarted,set.CleanupComplete),Json);
        }
        catch(JsonException){return Results.StatusCode(400);}
        catch(OperationCanceledException){return Results.StatusCode(408);}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {return Results.Json(new{ok=false,code="unavailable",canSet=false},statusCode:503);}
    }
    static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    internal sealed record Request(string Action,string? EndpointId=null,Guid Expected=default,Guid Format=default);
    internal static Request? Parse(JsonElement value)
    {
        if(value.ValueKind!=JsonValueKind.Object)return null;
        var fields=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var v in value.EnumerateObject())if(v.Value.ValueKind!=JsonValueKind.String
            ||!fields.TryAdd(v.Name,v.Value.GetString()!))return null;
        if(!fields.TryGetValue("action",out string? action))return null;
        if(action=="read")return fields.Count==1?new("read"):null;
        if(action!="set"||fields.Count!=4||!fields.TryGetValue("endpointId",out string? id)||id.Length>1024
            ||!fields.TryGetValue("expectedFormat",out string? expected)||!Guid.TryParse(expected,out var e)
            ||!fields.TryGetValue("format",out string? format)||!Guid.TryParse(format,out var g)
            ||WinRtSpatialEndpointReader.NativeId(id) is null)return null;
        return new("set",id,e,g);
    }
    object Dto(SpatialEndpoint? endpoint,string code,bool started,bool? cleanup)
    {
        var commandState=command.ReadState();
        var options=endpoint?.Formats.Select(f=>new{id=f.Id.ToString("B").ToUpperInvariant(),label=f.Name,
            supported=f.Supported,requiresProviderLicense=f.RequiresProviderLicense}).ToList();
        options??=[];if(endpoint is not null)options.Insert(0,new{id=Guid.Empty.ToString("B").ToUpperInvariant(),label="Off",supported=true,requiresProviderLicense=false});
        return new{ok=endpoint is not null&&code is "read" or "applied-confirmed" or "already-confirmed",code=endpoint is null&&code=="read"?"endpoint-unavailable":code,
            endpointId=endpoint?.EndpointId,selectedFormat=endpoint?.Selected.ToString("B").ToUpperInvariant(),
            activeFormat=endpoint?.Active.ToString("B").ToUpperInvariant(),options,licenseState="unknown",
            canSet=endpoint is not null&&endpoint.IsDefault&&commandState.CanSet,commandStarted=started,
            commandInFlight=commandState.InFlight,cleanupComplete=commandState.CleanupComplete&&cleanup is not false};
    }

    public Task<SpatialEndpoint?> ReadAsync(string endpointId,CancellationToken stop=default)=>reader.ReadAsync(endpointId,stop);
    static bool Same(SpatialEndpoint? p,SpatialSetRequest r,string? native=null)=>p is not null
        &&p.EndpointId==r.EndpointId&&p.Selected==r.ExpectedSelected&&(!r.RequireDefault||p.IsDefault)
        &&(native is null||p.NativeId==native);
    static bool Allowed(SpatialEndpoint p,Guid g)=>g==Guid.Empty||p.Supported
        &&p.Formats.Any(f=>f.Id==g&&f.Supported);
    public async Task<SpatialSetResult> SetAsync(SpatialSetRequest request,CancellationToken stop=default)
    {
        await serial.WaitAsync(stop).ConfigureAwait(false);
        try
        {
            var before=await reader.ReadAsync(request.EndpointId,stop).ConfigureAwait(false);
            var commandState=command.ReadState();
            if(!commandState.CleanupComplete||commandState.InFlight)return new("command-pending-cleanup",before,false,false);
            if(!Same(before,request))return new("context-changed",before,false,true);
            if(!Allowed(before!,request.Requested))return new("format-unsupported",before,false,true);
            if(before!.Selected==request.Requested&&before.Active==request.Requested)return new("already-confirmed",before,false,true);
            // The tool pins are checked before this final fresh endpoint comparison.
            // Windows exposes no atomic external compare/exchange: this is a
            // serialized Playhub operation with a final adjacent read, not an OS lock.
            var sent=await command.RunAsync(before!.NativeId,request.Requested,async c=>{
                var current=await reader.ReadAsync(request.EndpointId,c).ConfigureAwait(false);
                return Same(current,request,before.NativeId)&&Allowed(current!,request.Requested);
            },stop).ConfigureAwait(false);
            if(!sent.Started)return new(sent.Reason,before,false,sent.CleanupComplete);
            if(!sent.CleanupComplete)return new("command-cleanup-unknown",null,true,false);
            if(sent.ExitCode is not 0)return new("command-refused-or-interrupted",await reader.ReadAsync(request.EndpointId,stop).ConfigureAwait(false),true,true);
            SpatialEndpoint? observed=null;
            for(int n=0;n<12;n++)
            {
                observed=await reader.ReadAsync(request.EndpointId,stop).ConfigureAwait(false);
                if(observed is null||observed.EndpointId!=request.EndpointId||observed.NativeId!=before.NativeId
                    ||request.RequireDefault&&!observed.IsDefault)return new("context-changed-after-command",observed,true,true);
                if(observed.Selected==request.Requested&&observed.Active==request.Requested)
                    return new("applied-confirmed",observed,true,true);
                if(n!=11)await Task.Delay(50,stop).ConfigureAwait(false);
            }
            // ExitCode zero is deliberately never a success receipt by itself.
            return new(observed?.Selected==request.Requested?"selected-active-different":"not-confirmed",observed,true,true);
        }
        finally{serial.Release();}
    }
}
public sealed class WinRtSpatialEndpointReader:ISpatialEndpointReader
{
    public string? DefaultEndpointId=>MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
    public static string? NativeId(string exactInterfaceId)
    {
        const string prefix=@"\\?\SWD#MMDEVAPI#";
        if(!exactInterfaceId.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return null;
        var tail=exactInterfaceId[prefix.Length..].Split('#');
        const string render="{0.0.0.00000000}.";
        if(tail.Length!=2||!Guid.TryParseExact(tail[1],"B",out _)
            ||!tail[0].StartsWith(render,StringComparison.Ordinal)||!Guid.TryParseExact(tail[0][render.Length..],"B",out var id))return null;
        return render+id.ToString("B");
    }
    public async Task<SpatialEndpoint?> ReadAsync(string exactEndpointId,CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested();
        string? native=NativeId(exactEndpointId);if(native is null)return null;
        var devices=await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
        stop.ThrowIfCancellationRequested();
        if(devices.Count(d=>d.Id==exactEndpointId&&d.IsEnabled)!=1)return null;
        var c=SpatialAudioDeviceConfiguration.GetForDeviceId(exactEndpointId);
        var formats=new List<(string Name,string Guid,bool License)>{
            ("Windows Sonic for Headphones",SpatialAudioFormatSubtype.WindowsSonic,false),
            ("Dolby Atmos for Headphones",SpatialAudioFormatSubtype.DolbyAtmosForHeadphones,true),
            ("Dolby Atmos for Home Theater",SpatialAudioFormatSubtype.DolbyAtmosForHomeTheater,true),
            ("Dolby Atmos for Speakers",SpatialAudioFormatSubtype.DolbyAtmosForSpeakers,true),
            ("DTS Headphone:X",SpatialAudioFormatSubtype.DTSHeadphoneX,true),
            ("DTS:X Ultra",SpatialAudioFormatSubtype.DTSXUltra,true)};
        // Retrieved read-only from the official OS static getter. The managed
        // 19041 projection lacks this later property; guard actual OS support.
        if(Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Media.Audio.SpatialAudioFormatSubtype","DTSXForHomeTheater"))
            formats.Add(("DTS:X for Home Theater","{10201B4A-3322-4967-BF40-2CAA9BAFCA44}",true));
        var selected=Guid.Parse(c.DefaultSpatialAudioFormat);var active=Guid.Parse(c.ActiveSpatialAudioFormat);
        var supported=formats.Select(f=>new SpatialFormat(Guid.Parse(f.Guid),f.Name,c.IsSpatialAudioFormatSupported(f.Guid),f.License)).ToArray();
        stop.ThrowIfCancellationRequested();
        return new(c.DeviceId,native,MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default)==exactEndpointId,
            c.IsSpatialAudioSupported,selected,active,supported);
    }
}
// The production factory is fixed to Process.Start; the internal boundary lets
// offline tests exercise the complete RunAsync cleanup path without a live setter.
internal interface ISpatialOwnedProcess:IDisposable
{
    bool HasExited{get;}
    int ExitCode{get;}
    Task WaitAsync(CancellationToken stop);
    Task DrainAsync();
    void Kill();
}
public sealed class PinnedSpatialCommand:ISpatialCommand
{
    static readonly IReadOnlyDictionary<string,string> Pins=new Dictionary<string,string>{
        ["svcl.exe"]="0C4738296D253495BC995DA7B5938E27B306C8F204DC9F7D4D5D455F1D8D3E38",
        ["readme.txt"]="E81816966040C7F2353007FF38C76E3B88999BBB82C21B9D8AEE57237EC3B626",
        ["svcl.chm"]="2B5FB3A94D19C223779F782A62127B791D1241C977D3C902DB6E6D0F4AA4ED89"};
    static readonly IReadOnlyDictionary<string,long> Sizes=new Dictionary<string,long>{{"svcl.exe",166400},{"readme.txt",7927},{"svcl.chm",15100}};
    readonly string packageDirectory;
    readonly Func<ProcessStartInfo,ISpatialOwnedProcess?> startOwned;
    readonly SemaphoreSlim serial=new(1,1);
    readonly object ownership=new();ISpatialOwnedProcess? owned;bool inFlight;
    public PinnedSpatialCommand(string directory):this(directory,WindowsOwnedProcess.Start){}
    internal PinnedSpatialCommand(string directory,Func<ProcessStartInfo,ISpatialOwnedProcess?> inertStart)
    {packageDirectory=directory;startOwned=inertStart;}
    bool PackageReady()
    {
        try{return Path.IsPathFullyQualified(packageDirectory)&&Pins.All(p=>File.Exists(Path.Combine(packageDirectory,p.Key))
            &&new FileInfo(Path.Combine(packageDirectory,p.Key)).Length==Sizes[p.Key]
            &&Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(packageDirectory,p.Key)))).Equals(p.Value,StringComparison.Ordinal));}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){return false;}
    }
    static bool Exited(ISpatialOwnedProcess process)
    {try{return process.HasExited;}catch(Exception e)when(e is InvalidOperationException or System.ComponentModel.Win32Exception){return false;}}
    // Called under ownership. Never dispose a process still used by RunAsync.
    void ReapExited()
    {if(!inFlight&&owned is {} process&&Exited(process)){owned=null;process.Dispose();}}
    public SpatialCommandState ReadState()
    {
        bool package=PackageReady();
        lock(ownership){ReapExited();return new(package,inFlight,!inFlight&&owned is null);}
    }
    public static IReadOnlyList<string>? Arguments(string nativeId,Guid requested)
    {
        const string prefix="{0.0.0.00000000}.";
        if(!nativeId.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParseExact(nativeId[prefix.Length..],"B",out var id))return null;
        return new[]{"/SetSpatial",prefix+id.ToString("B"),requested==Guid.Empty?"":requested.ToString("B")};
    }
    public async Task<SpatialCommandResult> RunAsync(string nativeId,Guid format,
        Func<CancellationToken,Task<bool>> finalGuard,CancellationToken stop)
    {
        await serial.WaitAsync(stop).ConfigureAwait(false);var pins=new List<FileStream>();ISpatialOwnedProcess? child=null;
        try
        {
            lock(ownership)
            {
                ReapExited();if(owned is not null)return new(false,false,null,"previous-command-not-drained");
                inFlight=true;
            }
            var argv=Arguments(nativeId,format);if(argv is null)return new(false,true,null,"invalid-endpoint");
            if(!Path.IsPathFullyQualified(packageDirectory))return new(false,true,null,"package-invalid");
            foreach(var entry in Pins)
            {
                var f=new FileStream(Path.Combine(packageDirectory,entry.Key),FileMode.Open,FileAccess.Read,FileShare.Read);pins.Add(f);
                if(f.Length!=Sizes[entry.Key]||!Convert.ToHexString(SHA256.HashData(f)).Equals(entry.Value,StringComparison.Ordinal))return new(false,true,null,"package-invalid");
            }
            var start=new ProcessStartInfo(Path.Combine(packageDirectory,"svcl.exe")){
                WorkingDirectory=packageDirectory,UseShellExecute=false,CreateNoWindow=true,
                RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string arg in argv)start.ArgumentList.Add(arg);
            if(!await finalGuard(stop).ConfigureAwait(false))return new(false,true,null,"context-changed");
            stop.ThrowIfCancellationRequested();
            child=startOwned(start);if(child is null)return new(false,true,null,"command-start-failed");
            lock(ownership)owned=child;
            try
            {
                await child.WaitAsync(stop).WaitAsync(TimeSpan.FromSeconds(3),stop).ConfigureAwait(false);
                await child.DrainAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                return new(true,true,child.ExitCode,"exited");
            }
            catch(Exception e)when(e is TimeoutException or OperationCanceledException)
            {
                await Cleanup(child).ConfigureAwait(false);
                return new(true,Exited(child),null,e is TimeoutException?"command-timeout":"command-cancelled");
            }
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {return new(child is not null,child is null||Exited(child),null,"command-unavailable");}
        finally
        {
            if(child is not null&&!Exited(child))await Cleanup(child).ConfigureAwait(false);
            lock(ownership)
            {
                inFlight=false;
                if(child is not null&&Exited(child)){if(ReferenceEquals(owned,child))owned=null;child.Dispose();}
                // Otherwise retain this EXACT process/handle. ReadState and the
                // next RunAsync can clear it only after its real exit is observed.
            }
            foreach(var f in pins)f.Dispose();serial.Release();
        }
    }
    static async Task Cleanup(ISpatialOwnedProcess child)
    {
        try{if(!Exited(child))child.Kill();await child.WaitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);}
        catch(Exception x)when(x is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException){}
    }
    sealed class WindowsOwnedProcess:ISpatialOwnedProcess
    {
        readonly Process process;readonly Microsoft.Win32.SafeHandles.SafeProcessHandle handle;readonly Task drain;
        WindowsOwnedProcess(Process p){process=p;handle=p.SafeHandle;drain=Task.WhenAll(Drain(p.StandardOutput),Drain(p.StandardError));}
        internal static ISpatialOwnedProcess? Start(ProcessStartInfo info)=>Process.Start(info) is {} p?new WindowsOwnedProcess(p):null;
        public bool HasExited=>!handle.IsInvalid&&process.HasExited;
        public int ExitCode=>process.ExitCode;
        public Task WaitAsync(CancellationToken stop)=>process.WaitForExitAsync(stop);
        public Task DrainAsync()=>drain;
        public void Kill()=>process.Kill();
        public void Dispose()=>process.Dispose();
        static async Task Drain(StreamReader reader)
        {try{var buffer=new char[256];while(await reader.ReadAsync(buffer).ConfigureAwait(false)>0){}}catch(Exception e)when(e is IOException or ObjectDisposedException){}}
    }
}
