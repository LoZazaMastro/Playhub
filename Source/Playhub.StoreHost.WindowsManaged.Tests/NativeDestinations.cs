using System.Text.Json;
using Playhub.StoreLibrary;
static partial class Checks
{
    static void LanguageOptions()
    {
        using var info=JsonDocument.Parse("""{"size":{"*":{"disk_size":10,"download_size":8},"en-US":{"disk_size":20,"download_size":15},"it-IT":{"disk_size":30,"download_size":24}}}""");
        var options=StoreCliProvider.GogInstallOptions(info.RootElement,"it");Require(options.Language=="it-IT"&&options.Size.RequiredBytes==40&&options.Size.DownloadBytes==32&&options.Languages.Length==2,"GOG selected language counts exact language plus shared depots");
        Require(StoreCliProvider.GogInstallOptions(info.RootElement,"unavailable").Language=="en-US","unavailable preferred locale selects actual English depot");
        using var content=JsonDocument.Parse("""{"namespace":"correct","pages":[{"data":{"requirements":{"languages":["AUDIO: English |","TEXT: English, French, German, Italian, Chinese - Simplified"]}}}]}""");
        var languages=StoreCliProvider.ReadEpicContentLanguages(content.RootElement,"correct");Require(languages.Contains("it-IT")&&languages.Contains("en-US")&&languages.Contains("zh-CN")&&languages.Length==5,"Epic offered languages come from text support of exact namespace");
        Require(StoreCliProvider.ReadEpicContentLanguages(content.RootElement,"different").Length==0,"similar store slug cannot inject another games language list");
    }
    const string Volume=@"\\?\Volume{11111111-2222-3333-4444-555555555555}";
    static async Task NativeDestinations()
    {
        Require(StoreWindowsInstallControl.ValidVolume(Volume)&&!StoreWindowsInstallControl.ValidVolume(@"F:\")&&!StoreWindowsInstallControl.ValidVolume(Volume+"\\other"),"only canonical volume identities accepted");
        string file=Path.GetTempFileName();
        try{
            int calls=0;
            var native=new StoreWindowsInstallControl(file,(_,args,_,_)=>{calls++;Require(args.Contains("locations")&&!args.Contains("install"),"volume discovery cannot start a transfer");return Task.FromResult((0,JsonSerializer.Serialize(new{protocol=1,kind="locations",locations=new[]{new{volumeName=Volume,rootPath=@"F:\",freeBytes=1000,isReady=true}}})));});
            var result=await native.GetVolumesAsync(default);Require(result.Count==1&&result[0].RootPath==@"F:\"&&calls==1,"native volume metadata parsed without arbitrary folder selection");
            var invalid=new StoreWindowsInstallControl(file,(_,_,_,_)=>Task.FromResult((0,JsonSerializer.Serialize(new{protocol=1,kind="locations",locations=new[]{new{volumeName=Volume,rootPath=@"F:\foreign",freeBytes=1000,isReady=true}}}))));
            bool refused=false;try{await invalid.GetVolumesAsync(default);}catch(StoreOperationException e)when(e.Code=="windows_store_destination_unavailable"){refused=true;}Require(refused,"invalid native location cannot reach selection UI");
        }finally{File.Delete(file);}
    }
    static async Task NativeCancel()
    {
        await using var f=new Fixture();var legacy=new Runtime();await f.Start(legacy);await Wait(async()=>{legacy.Next.Release();return (await f.Job()).State==StoreJobState.ExternalHandoff;});
        var old=await f.Job();Require((await f.Engine.CancelCleanupAsync(new(old.JobId,old.Revision,Guid.NewGuid(),false))).Accepted,"old request dismissed before new native attempt");
        var native=new DestinationRuntime();f.Engine.WindowsManagedRuntime=native;f.Engine.WindowsManagedDelay=(_,ct)=>Task.Delay(5,ct);
        var owned=await f.Provider.GetXboxOwnedLibraryAsync();var denied=await f.Provider.OpenXboxOwnedInstallPageAsync(new(Product,owned.Revision,Guid.NewGuid(),"invalid"));Require(!denied.Accepted&&native.Starts==0,"unregistered disk cannot invoke Windows installation");
        var started=await f.Provider.OpenXboxOwnedInstallPageAsync(new(Product,owned.Revision,Guid.NewGuid(),Volume));Require(started.Accepted,"explicit registered volume starts native path");
        await Wait(()=>Task.FromResult(native.Starts==1));await Wait(async()=>(await f.Engine.GetSnapshotAsync()).Jobs.Single().DownloadedBytes==25);
        var job=(await f.Engine.GetSnapshotAsync()).Jobs.Single();Require(job.InstallPath==@"F:\"&&job.TotalBytes==100&&job.BytesPerSecond==5&&job.ProgressPercent==25,"actual bytes speed and selected disk persist in queue");
        Require((await f.Engine.CancelCleanupAsync(new(job.JobId,job.Revision,Guid.NewGuid(),false))).Accepted,"native cancel acknowledged");
        Require((await f.Engine.GetSnapshotAsync()).Jobs.Count==0,"native row disappears before OS cancellation drains");await Wait(()=>Task.FromResult(native.Cancels==1));
        Require(native.LastVolume==Volume,"exact selected volume forwarded to install control");
    }
    sealed class DestinationRuntime:IWindowsStoreManagedRuntime,IWindowsStoreInstallDestinationRuntime
    {
        internal int Starts,Cancels;internal string? LastVolume;readonly TaskCompletionSource<int?> stopped=new(TaskCreationOptions.RunContinuationsAsynchronously);public bool Available=>true;
        public Task<WindowsXboxPackage?> FindPackageAsync(string family,CancellationToken token)=>Task.FromResult<WindowsXboxPackage?>(null);
        public Task<int?> RunInstallAsync(string id,Func<WindowsStoreManagedProgress,Task> progress,CancellationToken token)=>throw new Exception("legacy CLI must not run");
        public Task<IReadOnlyList<WindowsStoreInstallVolume>> GetInstallVolumesAsync(CancellationToken token)=>Task.FromResult<IReadOnlyList<WindowsStoreInstallVolume>>([new(Volume,@"F:\",10000,true)]);
        public async Task<int?> RunOnVolumeAsync(string id,string family,string? volume,bool launch,Func<WindowsStoreManagedProgress,Task> progress,CancellationToken token){Require(id==Product&&family==Family&&launch,"native session bound to requested exact product family");LastVolume=volume;Interlocked.Increment(ref Starts);await progress(new("downloading",25,25,100,5));return await stopped.Task.WaitAsync(token);}
        public Task CancelInstallAsync(string id,CancellationToken token){Require(id==Product,"cancel targets only requested game");Interlocked.Increment(ref Cancels);stopped.TrySetResult(5);return Task.CompletedTask;}
    }
}
