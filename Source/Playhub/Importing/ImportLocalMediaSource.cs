using Playhub.Integrations;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using System.Runtime.InteropServices;

namespace Playhub.Importing;

internal sealed class ImportLocalMediaSource : IDisposable
{
    private readonly IRandomAccessStream? _stream;
    private readonly Action<string> _diagnostic;
    private readonly int _pathLength;
    private MediaPlayer? _player;
    private int _disposed;
    internal MediaSource Source { get; }

    private ImportLocalMediaSource(MediaSource source,IRandomAccessStream? stream,int pathLength,Action<string> diagnostic)
    { Source=source;_stream=stream;_pathLength=pathLength;_diagnostic=diagnostic; }

    internal static async Task<ImportLocalMediaSource> OpenAsync(Uri uri,Action<string> diagnostic,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(!uri.IsAbsoluteUri) throw new ArgumentException("An absolute preview URI is required.");
        if(!uri.IsFile)
        {
            if(uri.Scheme is not ("http" or "https") || !uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("Only local preview sources are allowed.");
            return new(MediaSource.CreateFromUri(uri),null,0,diagnostic);
        }
        string path=uri.LocalPath;
        if(!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute preview path is required.");
        ApplicationIntegrationDataStore.GuardPath(path);
        string contentType=Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp3"=>"audio/mpeg", ".m4a"=>"audio/mp4", ".aac"=>"audio/aac", ".wav"=>"audio/wav",
            ".flac"=>"audio/flac", ".ogg"=>"audio/ogg", ".mp4"=>"video/mp4", ".webm"=>"video/webm",
            _=>throw new ArgumentException("Unsupported local preview format.")
        };
        IRandomAccessStream? stream=null;
        try
        {
            string nativePath=path.StartsWith(@"\\?\",StringComparison.Ordinal)?path:path.StartsWith(@"\\",StringComparison.Ordinal)?@"\\?\UNC\"+path[2..]:@"\\?\"+path;
            stream=await Task.Run(()=>OpenNativeStream(nativePath),cancellationToken);
            if(stream.Size is <1 or >1024UL*1024*1024) throw new InvalidDataException("Local preview size exceeds bounds.");
            cancellationToken.ThrowIfCancellationRequested();
            var source=MediaSource.CreateFromStream(stream,contentType);
            diagnostic($"Local media source created pathLength={path.Length} contentType={contentType} bytes={stream.Size}");
            return new(source,stream,path.Length,diagnostic);
        }
        catch(Exception error)
        {
            stream?.Dispose();
            if(error is not OperationCanceledException)diagnostic($"Local media source failed pathLength={path.Length} type={error.GetType().Name} hresult={error.HResult:X8}");
            throw;
        }
    }

    private static IRandomAccessStream OpenNativeStream(string path)
    {
        Guid iid=new("905A0FE1-BC53-11DF-8C49-001E4FC686DA");
        Marshal.ThrowExceptionForHR(CreateRandomAccessStreamOnFile(path,(uint)FileAccessMode.Read,ref iid,out var pointer));
        try { return WinRT.MarshalInterface<IRandomAccessStream>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }
    [DllImport("shcore.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    private static extern int CreateRandomAccessStreamOnFile(string filePath,uint accessMode,ref Guid iid,out IntPtr result);

    internal void AttachDiagnostics(MediaPlayer player)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed)!=0,this);
        if(_player is not null) throw new InvalidOperationException("Preview diagnostics are already attached.");
        _player=player;
        player.MediaOpened+=MediaOpened;player.MediaFailed+=MediaFailed;
        player.PlaybackSession.PlaybackStateChanged+=PlaybackStateChanged;
    }
    private void MediaOpened(MediaPlayer player,object args)
    {
        Report(()=>$"Local media opened pathLength={_pathLength} durationSeconds={player.PlaybackSession.NaturalDuration.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)}");
    }
    private void MediaFailed(MediaPlayer player,MediaPlayerFailedEventArgs args)
    {
        Report(()=>$"Local media failed pathLength={_pathLength} error={args.Error} hresult={args.ExtendedErrorCode?.HResult:X8}");
    }
    private void PlaybackStateChanged(MediaPlaybackSession session,object args)
    {
        Report(()=>$"Local media state pathLength={_pathLength} state={session.PlaybackState}");
    }
    private void Report(Func<string> message)
    {
        if(Volatile.Read(ref _disposed)!=0)return;
        try { _diagnostic(message()); }
        catch(Exception error) when(error is InvalidOperationException or COMException) { }
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0) return;
        var player=Interlocked.Exchange(ref _player,null);
        if(player is not null)
        {
            try
            {
                player.MediaOpened-=MediaOpened;player.MediaFailed-=MediaFailed;
                player.PlaybackSession.PlaybackStateChanged-=PlaybackStateChanged;
            }
            catch(Exception error) when(error is InvalidOperationException or COMException) { }
        }
        try { Source.Dispose(); } finally { _stream?.Dispose(); }
        _diagnostic($"Local media source disposed pathLength={_pathLength}");
    }
}
