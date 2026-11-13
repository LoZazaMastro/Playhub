// GPL-3.0-only. Event-driven, Steam-only fullscreen declaration in both session modes.
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
namespace GamingMode.Services;
internal sealed class SteamTaskbarFullscreenService:IDisposable
{
    private readonly FileLogger logger;private readonly string? configuredPath;
    private readonly Thread thread;private readonly NativeEvent callback;private readonly CancellationTokenSource stop=new();
    private Dispatcher? dispatcher;private DispatcherTimer? nativeCheck,retry;
    private SteamTaskbarNativeWindow? window;private SteamTaskbarMainProof? proof;
    private SteamTaskbarMarkApi? api;private SteamTaskbarFullscreenLease? lease;
    private long epoch;private int disposed,modeChanging;private int retryAttempt;
    private bool busy,needProof,queued;private nint lastForeground;
    private static readonly int[] RetryMs=[200,400,900,1500];
    internal SteamTaskbarFullscreenService(FileLogger log,string? steamPath)
    {
        logger=log;configuredPath=steamPath;callback=Event;
        thread=new Thread(Run){IsBackground=true,Name="Playhub Steam taskbar declaration"};thread.SetApartmentState(ApartmentState.STA);thread.Start();
    }
    // Both Gaming Mode and Desktop Mode permit ONLY actual Big Picture intent.
    // A transition revokes the old declaration; it never changes appbar options.
    internal void ModeChanging(){if(Volatile.Read(ref disposed)!=0)return;Volatile.Write(ref modeChanging,1);Wake(true);}
    internal void ModeCompleted(bool gaming){if(Volatile.Read(ref disposed)!=0)return;Volatile.Write(ref modeChanging,0);Wake(true);}
    private void Event(nint h,uint kind,nint hwnd,int obj,int child,uint tid,uint time)
    {
        if(hwnd==0||Volatile.Read(ref disposed)!=0)return;
        if(kind==3)
        {
            if(hwnd!=lastForeground){lastForeground=hwnd;epoch++;proof=null;retryAttempt=0;Wake(true);}else Wake(false);
            return;
        }
        if(window is {} held&&obj==0&&child==0)
        {
            if(kind==0x8001&&hwnd==held.Key.Window)held.WindowRetired=true;
            if(kind==0x8001&&hwnd==held.Key.Shell)held.ShellRetired=true;
            if(hwnd==held.Key.Window||hwnd==held.Key.Shell){epoch++;proof=null;retryAttempt=0;Wake(true);}
        }
    }
    private void Wake(bool main)
    {
        var d=dispatcher;if(d is null||d.HasShutdownStarted||d.HasShutdownFinished)return;
        try{d.BeginInvoke(DispatcherPriority.Background,new Action(()=>{if(main){epoch++;proof=null;needProof=true;retryAttempt=0;}Queue();}));}
        catch(InvalidOperationException){}
    }
    private void Queue()
    {
        if(queued||dispatcher is null||dispatcher.HasShutdownStarted)return;queued=true;
        dispatcher!.BeginInvoke(DispatcherPriority.Background,new Action(async()=>{queued=false;await Reconcile();}));
    }
    private SteamTaskbarObservation Read()=>window?.Read(epoch,Volatile.Read(ref modeChanging)==0&&Volatile.Read(ref disposed)==0,proof)??default;
    private async Task Reconcile()
    {
        if(busy)return;busy=true;
        try
        {
            if(Volatile.Read(ref disposed)!=0){ReleaseAndMaybeStop();return;}
            if(window is not null)
            {
                lease!.Observe(Read);
                if(!lease.Owned&&!Read().Hold){Drop();}
            }
            if(window is null)
            {
                nint fg=SteamTaskbarNativeWindow.GetForegroundWindow();
                if(Volatile.Read(ref modeChanging)!=0||SteamTaskbarNativeWindow.Class(fg)!="SDL_app")return;
                window=SteamTaskbarNativeWindow.Capture(configuredPath);
                if(window is null)return;
                api=new();lease=new(api);needProof=true;
            }
            if(needProof)
            {
                needProof=false;var held=window;long generation=epoch;
                var reader=new SteamThemeReadinessProbe(held.VerifyReaderOwner);
                var result=await reader.ReadBigPictureAsync(generation,stop.Token);
                if(Volatile.Read(ref disposed)!=0){ReleaseAndMaybeStop();return;}
                if(ReferenceEquals(window,held)&&generation==epoch&&!held.WindowRetired&&!held.ShellRetired)proof=result;
            }
            lease!.Observe(Read);
            if(lease.Owned)
            {
                nativeCheck!.Interval=TimeSpan.FromMilliseconds(lease.Status=="release-unconfirmed"?1000:250);
                nativeCheck.Start();retry!.Stop();retryAttempt=0;
            }
            else
            {
                var state=Read();
                // Keep the attempt receipt until a genuine window/focus epoch
                // changes. Dropping it here could retry an uncertain TRUE forever.
                if(lease.Attempted(state)){nativeCheck!.Stop();retry!.Stop();return;}
                // Retry only a still-exact native fullscreen candidate; no idle
                // WebSocket loop, generic window scan or per-frame COM request.
                if(state.ModeAllowed&&state.Exact&&state.Foreground&&state.Visible&&!state.Cloaked&&state.FullscreenIntent&&retryAttempt<RetryMs.Length)
                {retry!.Interval=TimeSpan.FromMilliseconds(RetryMs[retryAttempt++]);retry.Start();}
                else Drop();
            }
        }
        catch(OperationCanceledException)when(stop.IsCancellationRequested){ReleaseAndMaybeStop();}
        catch(Exception error){logger.Error("Steam taskbar fullscreen declaration could not be confirmed.",error);if(lease?.Owned==true)nativeCheck?.Start();else Drop();}
        finally{busy=false;}
    }
    private void Drop()
    {
        nativeCheck?.Stop();retry?.Stop();proof=null;
        api?.Dispose();api=null;lease=null;window?.Dispose();window=null;
    }
    private void ReleaseAndMaybeStop()
    {
        lease?.Release(Read);
        if(lease?.Owned==true){nativeCheck!.Interval=TimeSpan.FromSeconds(1);nativeCheck.Start();return;}
        Drop();dispatcher?.BeginInvokeShutdown(DispatcherPriority.Background);
    }
    private void Run()
    {
        try{RunCore();}
        catch(Exception error){logger.Error("Steam taskbar observer ended without a confirmed declaration.",error);lease?.Release(Read);}
        finally{stop.Dispose();}
    }
    private void RunCore()
    {
        using var dpi=PhysicalDpiScope.Enter();if(dpi is null||Volatile.Read(ref disposed)!=0)return;
        dispatcher=Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        nint foreground=SetWinEventHook(3,3,0,callback,0,0,2),lifecycle=SetWinEventHook(0x8001,0x800c,0,callback,0,0,2);
        if(foreground==0||lifecycle==0){if(foreground!=0)UnhookWinEvent(foreground);if(lifecycle!=0)UnhookWinEvent(lifecycle);logger.Info("Steam taskbar declaration unavailable: window observer missing.");return;}
        nativeCheck=new DispatcherTimer(TimeSpan.FromMilliseconds(250),DispatcherPriority.Background,(_,_)=>{if(disposed!=0)ReleaseAndMaybeStop();else Queue();},dispatcher);nativeCheck.Stop();
        retry=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};retry.Tick+=(_,_)=>{retry.Stop();needProof=true;Queue();};
        lastForeground=SteamTaskbarNativeWindow.GetForegroundWindow();needProof=true;Queue();
        try{Dispatcher.Run();}
        finally{UnhookWinEvent(foreground);UnhookWinEvent(lifecycle);if(lease?.Owned!=true)Drop();}
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;try{stop.Cancel();}catch(ObjectDisposedException){}
        var d=dispatcher;
        if(d is not null&&!d.HasShutdownStarted&&!d.HasShutdownFinished)
            try{d.BeginInvoke(DispatcherPriority.Background,new Action(ReleaseAndMaybeStop));}catch(InvalidOperationException){}
        if(Thread.CurrentThread!=thread&&!thread.Join(750))logger.Info("Steam taskbar declaration cleanup pending on its retained worker.");
    }
    private delegate void NativeEvent(nint hook,uint kind,nint hwnd,int obj,int child,uint thread,uint time);
    [DllImport("user32.dll")]private static extern nint SetWinEventHook(uint min,uint max,nint module,NativeEvent callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")]private static extern bool UnhookWinEvent(nint hook);
}
