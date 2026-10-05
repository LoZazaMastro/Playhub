using Playhub.Importing;
using Windows.Media.Playback;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using System.Diagnostics;

int passed=0;
void Check(bool condition,string name) { if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);passed++; }
byte[] SilentWave()
{
    using var memory=new MemoryStream();using var writer=new BinaryWriter(memory);
    const int data=16000*2*3;
    writer.Write("RIFF"u8.ToArray());writer.Write(data+36);writer.Write("WAVEfmt "u8.ToArray());writer.Write(16);
    writer.Write((short)1);writer.Write((short)1);writer.Write(16000);writer.Write(32000);writer.Write((short)2);writer.Write((short)16);
    writer.Write("data"u8.ToArray());writer.Write(data);writer.Write(new byte[data]);return memory.ToArray();
}
async Task Refuse(Uri uri,string name,CancellationToken token=default)
{
    bool rejected=false;
    try { using var unexpected=await ImportLocalMediaSource.OpenAsync(uri,_=>{},token); }
    catch(Exception error) when(error is IOException or InvalidDataException or ArgumentException or OperationCanceledException or System.Runtime.InteropServices.COMException) { rejected=true; }
    Check(rejected,name);
}
async Task Play(string path,string label,bool video=false)
{
    var diagnostics=new System.Collections.Concurrent.ConcurrentQueue<string>();
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
    var local=await ImportLocalMediaSource.OpenAsync(new Uri(path),diagnostics.Enqueue,timeout.Token);
    var player=new MediaPlayer { AutoPlay=false,IsMuted=true };
    player.SystemMediaTransportControls.IsEnabled=false;
    try
    {
        local.AttachDiagnostics(player);
        var opened=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.MediaOpened+=(_,_)=>opened.TrySetResult();
        player.MediaFailed+=(_,args)=>opened.TrySetException(new Exception("Actual media failed "+args.Error+" "+args.ExtendedErrorCode?.HResult));
        player.Source=local.Source;await opened.Task.WaitAsync(timeout.Token);
        Check(player.PlaybackSession.NaturalDuration.TotalSeconds is >2.8 and <3.2,label+" actual MediaPlayer opens long-path stream and duration");
        Check(player.PlaybackSession.PlaybackState!=MediaPlaybackState.Playing,label+" no automatic playback");
        if(video)Check(player.PlaybackSession.NaturalVideoWidth==1280 && player.PlaybackSession.NaturalVideoHeight==720,"actual H264 MP4 preview reports decoded 720p dimensions");
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.PlaybackSession.PositionChanged+=(_,_)=> { if(player.PlaybackSession.Position.TotalMilliseconds>100)started.TrySetResult(); };
        player.Play();await started.Task.WaitAsync(timeout.Token);
        Check(player.PlaybackSession.Position.TotalMilliseconds>100,label+" muted playback progresses via cloneable stream");
        player.Pause();player.PlaybackSession.Position=TimeSpan.Zero;
        var resumed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.PlaybackSession.PositionChanged+=(_,_)=> { if(player.PlaybackSession.Position.TotalMilliseconds>100)resumed.TrySetResult(); };
        player.Play();await resumed.Task.WaitAsync(timeout.Token);
        Check(player.PlaybackSession.Position.TotalMilliseconds>100,label+" retained player plays after temporary pause");
    }
    finally { player.Pause();player.Source=null;local.Dispose();local.Dispose();player.Dispose(); }
    Check(diagnostics.Count(message=>message.Contains("source disposed"))==1,label+" stream closes idempotently after source detach");
    Check(diagnostics.Any(message=>message.Contains("media opened")) && diagnostics.Any(message=>message.Contains("state=Playing")),label+" actual open/play diagnostics observed");
    Check(diagnostics.All(message=>!message.Contains(Path.GetDirectoryName(path)!,StringComparison.OrdinalIgnoreCase)),label+" diagnostics exclude full paths");
    File.Delete(path);Check(!File.Exists(path),label+" no held file remains after cleanup");
}
string root=Path.Combine(Path.GetTempPath(),"Playhub-local-media-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try
{
    string longRoot=Path.Combine(root,new string('a',85),new string('b',85),new string('c',85));Directory.CreateDirectory(longRoot);
    string wave=Path.Combine(longRoot,"silent-owned-preview.wav");await File.WriteAllBytesAsync(wave,SilentWave());
    Check(wave.Length>327,"actual long preview fixture path");
    using(var legacy=Windows.Media.Core.MediaSource.CreateFromUri(new Uri(wave)))
    using(var legacyPlayer=new MediaPlayer { AutoPlay=false,IsMuted=true })
    {
        legacyPlayer.SystemMediaTransportControls.IsEnabled=false;
        var observation=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        legacyPlayer.MediaOpened+=(_,_)=>observation.TrySetResult("opened");
        legacyPlayer.MediaFailed+=(_,failure)=>observation.TrySetResult("failed error="+failure.Error+" hresult="+failure.ExtendedErrorCode?.HResult.ToString("X8"));
        legacyPlayer.Source=legacy;
        try { Console.WriteLine("OBSERVED original long-path file URI: "+await observation.Task.WaitAsync(TimeSpan.FromSeconds(5))); }
        catch(TimeoutException) { Console.WriteLine("OBSERVED original long-path file URI: no open/fail within 5 seconds"); }
        legacyPlayer.Source=null;
    }
    string shortWave=Path.Combine(root,"source.wav");await File.WriteAllBytesAsync(shortWave,SilentWave());
    if(args.Length>=1)
    {
        string aac=Path.Combine(longRoot,"normalized-owned-preview.m4a");
        var command=new ProcessStartInfo(args[0]) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(string arg in new[]{"-nostdin","-hide_banner","-loglevel","error","-y","-i",shortWave,"-c:a","aac","-b:a","128k",aac})command.ArgumentList.Add(arg);
        using var process=Process.Start(command)!;using var encodeTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));await process.WaitForExitAsync(encodeTimeout.Token);
        Check(process.ExitCode==0,"actual pinned FFmpeg creates synthetic AAC/M4A fixture");await Play(aac,"AAC");
    }
    await Play(wave,"WAV");
    string shortVideo=Path.Combine(root,"video.mp4");await File.WriteAllBytesAsync(shortVideo,Array.Empty<byte>());
    var composition=new MediaComposition();composition.Clips.Add(MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255,20,40,60),TimeSpan.FromSeconds(3)));
    var videoFile=await StorageFile.GetFileFromPathAsync(shortVideo);
    var encoded=await composition.RenderToFileAsync(videoFile,MediaTrimmingPreference.Precise,MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p));
    Check(encoded==TranscodeFailureReason.None,"Windows encodes isolated synthetic H264 MP4 fixture");
    string longVideo=Path.Combine(longRoot,"actual-owned-preview.mp4");File.Copy(shortVideo,longVideo);await Play(longVideo,"MP4",true);
    await Refuse(new Uri("relative.wav",UriKind.Relative),"relative media URI refused");
    await Refuse(new Uri("https://example.com/preview.mp4"),"external network preview refused without request");
    await Refuse(new Uri("http://user:password@localhost/preview.mp4"),"credential-bearing loopback refused");
    string missing=Path.Combine(root,"missing.wav");await Refuse(new Uri(missing),"missing preview fails without creating file");Check(!File.Exists(missing),"read-only native open never creates missing source");
    string empty=Path.Combine(root,"empty.wav");await File.WriteAllBytesAsync(empty,Array.Empty<byte>());await Refuse(new Uri(empty),"empty preview refused");File.Delete(empty);
    string large=Path.Combine(root,"oversized.mp4");using(var file=File.Create(large))file.SetLength(1024L*1024*1024+1);await Refuse(new Uri(large),"oversized preview refused without memory copy");File.Delete(large);
    string unsupported=Path.Combine(root,"wrong.exe");await File.WriteAllBytesAsync(unsupported,SilentWave());await Refuse(new Uri(unsupported),"unsupported local media format refused");
    using var canceled=new CancellationTokenSource();canceled.Cancel();await Refuse(new Uri(shortWave),"canceled source cannot publish",canceled.Token);
    string bad=Path.Combine(longRoot,"invalid-owned-preview.wav");await File.WriteAllBytesAsync(bad,new byte[48]);
    var failureDiagnostics=new System.Collections.Concurrent.ConcurrentQueue<string>();
    var invalid=await ImportLocalMediaSource.OpenAsync(new Uri(bad),failureDiagnostics.Enqueue,CancellationToken.None);
    var invalidPlayer=new MediaPlayer { AutoPlay=false,IsMuted=true };invalidPlayer.SystemMediaTransportControls.IsEnabled=false;
    try
    {
        invalid.AttachDiagnostics(invalidPlayer);var failed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        invalidPlayer.MediaFailed+=(_,_)=>failed.TrySetResult();invalidPlayer.Source=invalid.Source;
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(failureDiagnostics.Any(message=>message.Contains("Local media failed") && message.Contains("hresult=")),"actual failed media records error and HRESULT");
    }
    finally { invalidPlayer.Source=null;invalid.Dispose();invalidPlayer.Dispose(); }
    File.Delete(bad);Check(!File.Exists(bad),"failed decode releases owned stream");
    var localFailed=await ImportLocalMediaSource.OpenAsync(new Uri(shortWave),_=>{},CancellationToken.None);var disposedPlayer=new MediaPlayer();localFailed.AttachDiagnostics(disposedPlayer);disposedPlayer.Dispose();localFailed.Dispose();Check(true,"cleanup tolerates already-disposed player");
    Console.WriteLine($"{passed} local media checks PASS; XAML lifecycle/live gate remains root.");
}
catch(Exception error) { Console.Error.WriteLine($"FAIL {error.GetType().Name} hr={error.HResult:X8}: {error.Message}");Environment.ExitCode=1; }
finally { Directory.Delete(root,true); }
