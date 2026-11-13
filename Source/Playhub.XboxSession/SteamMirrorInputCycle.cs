// Playhub Xbox Session. Distributed under GPL-3.0-only.
// Runs on the existing managed layout worker. No timers, foreground mutation or RPC.
internal static class SteamMirrorInputCycle
{
    internal readonly record struct Result(bool GameCommandStarted,bool GameCommandAcknowledged,bool CleanupConfirmed);
    internal static Result Run(uint app,long epoch,SteamRawInputRelayTrial.SessionBinding session,
        SteamInputOrderedIssuer issuer,Func<bool> current,Func<bool> ownsGame,
        Func<SteamMirrorInputPresentation?> read,Func<SteamRawInputRelayTrial.ReaderOutcome> probe,
        Action<bool> layoutStarted,Func<long> clock,Action<int> wait,CancellationToken cancellation,
        bool ordinary=false,SteamMirrorOpeningStop? opening=null,
        Func<SteamRawInputRelayTrial.ReaderOutcome,bool?>? nativePause=null)
    {
        bool started=issuer.SendGame(app,epoch,ownsGame);layoutStarted(started);
        if(!started)return new(false,false,false);
        bool delivered=ordinary || Wait(()=>issuer.GameCommandAcknowledged(app),current,clock,wait,900,cancellation);
        if(!delivered)return new(started,false,Release(issuer,clock,wait));
        var reader=ordinary?default:probe();
        if(nativePause?.Invoke(reader) is {} nativeResult)return new(started,delivered,nativeResult);
        if(!ordinary&&!reader.CanResume)return new(started,true,Release(issuer,clock,wait));
        return new(!ordinary&&started,!ordinary&&delivered,ResumePresentation(epoch,session,reader,issuer,current,read,clock,wait,cancellation,ordinary,opening));
    }
    internal static bool ResumePresentation(long epoch,SteamRawInputRelayTrial.SessionBinding session,
        SteamRawInputRelayTrial.ReaderOutcome reader,SteamInputOrderedIssuer issuer,Func<bool> current,
        Func<SteamMirrorInputPresentation?> read,Func<long> clock,Action<int> wait,CancellationToken cancellation,bool ordinary=false,SteamMirrorOpeningStop? opening=null)
    {
        SteamInputOrderedIssuer.MirrorLease? active=null;
        SteamMirrorInputPresentation? source=null;bool debtAutomaticSettled=false;
        // The presentation native lease bounds Prepared waiting; a normal open
        // may renew while active. No reader or pad is recreated in this loop.
        while(current()&&!cancellation.IsCancellationRequested)
        {
            long now=clock();
            if(opening is not null)
            {
                opening.Observe(SteamMirrorInputPresentationBridge.Read(),now);
                if(opening.NativeClosePending)
                {
                    // Focus restoration and visual cleanup do not prove that
                    // Valve closed. Keep this same PDO neutral after our0ACK.
                    if(!debtAutomaticSettled){if(!Release(issuer,clock,wait))return false;debtAutomaticSettled=true;}
                    wait(100);continue;
                }
                if(opening.Blocking)
                {
                    opening.RequestExpiredClose(now,SteamMirrorInputPresentationBridge.ReadWire,SteamMirrorInputPresentationBridge.QueueControl);
                    if(opening.GiveUpWaiting(now))return false;
                }
            }
            var shown=read();now=clock();
            if(shown is null || !shown.Prepared(now))
            {
                if(opening?.Blocking==true){wait(15);continue;}
                break;
            }
            if(source is not null && !source.SamePresentation(shown))
            {
                if(!Release(issuer,clock,wait))return false;
                active=null;source=null;continue;
            }
            source ??=shown;
            if(shown.Shown(now))
            {
                if(active is null)
                {
                    SteamInputSessionOutput.ReaderBinding? binding=ordinary?null:
                        new(session,reader.ReaderPid,reader.ReaderBirth,reader.Epoch);
                    active=new(Guid.NewGuid(),shown.SessionId,shown.Generation,shown.GameId,shown.ExpiresAt,session,epoch,binding);
                    if(!issuer.Begin(active) || !(ordinary?issuer.OrdinarySourceReady():issuer.ReaderRetired(binding!.Value,reader)))break;
                }
                else if(shown.ExpiresAt>active.ExpiresAt)
                {
                    var renewed=active with{ExpiresAt=shown.ExpiresAt};
                    if(!issuer.Renew(renewed))break;active=renewed;
                }
                issuer.Tick();
                if(issuer.State==SteamInputOrderedIssuer.Phase.Uncertain)break;
            }
            wait(15);
        }
        // Revokes UI first and orders our own0 even on AltTab/disconnect/exit.
        // A changed Steam/command owner is never overwritten.
        return Release(issuer,clock,wait);
    }
    internal static bool Release(SteamInputOrderedIssuer issuer,Func<long> clock,Action<int> wait)
    {
        if(!issuer.ReturnAutomatic())return false;
        return Wait(()=>{issuer.Tick();return issuer.AutomaticSettled;},
            ()=>issuer.State!=SteamInputOrderedIssuer.Phase.Uncertain,clock,wait,1500,CancellationToken.None);
    }
    internal static SteamMirrorInputPresentation? ReadCurrent(ulong gameId,uint gamePid,long gameBirth,
        Func<bool> exactGameForeground)
        => ReadCurrent(gameId,gamePid,gameBirth,exactGameForeground,NativeMirrorWindows.Instance,
            SteamMirrorInputPresentationBridge.Read,()=>Environment.TickCount64);
    // The production entrypoint fixes these dependencies. Tests invoke the
    // same complete read/join path with inert window and snapshot providers.
    internal static SteamMirrorInputPresentation? ReadCurrent(ulong gameId,uint gamePid,long gameBirth,
        Func<bool> exactGameForeground,IMirrorWindows windows,Func<SteamMirrorInputPresentation?> read,Func<long> clock)
    {
        nint gameWindow=windows.Foreground();uint gameThread=windows.ThreadProcess(gameWindow,out uint gameOwner);
        var m=read();long now=clock();
        bool Foreground(SteamMirrorInputPresentation value) => value.ForegroundScope switch
        {
            SteamMirrorForegroundScope.Game => exactGameForeground(),
            // This scalar comes only from the authenticated native snapshot.
            // A legacy companion scope or a caller boolean cannot create it.
            SteamMirrorForegroundScope.OwnedOverlay => value.Shown(clock())
                && windows.Foreground()==value.SourceWindow,
            _ => false
        };
        if(m is null || m.GameId!=gameId || m.GamePid!=gamePid || m.GameBirth!=gameBirth || !m.Prepared(now)
            || now-m.ReadAt>250 || m.SourceWindow==m.DestinationWindow || !Foreground(m))return null;
        bool Window(nint h,ulong cookie)
        {
            uint thread=windows.ThreadProcess(h,out uint pid);
            return thread!=0&&pid==m.OwnerPid&&pid==(uint)Environment.ProcessId&&windows.Ancestor(h,2)==h
                &&windows.Owner(h)==0&&unchecked((ulong)windows.Cookie(h))==cookie&&windows.Enabled(h);
        }
        if(!Window(m.SourceWindow,m.SourceCookie)||!Window(m.DestinationWindow,m.DestinationCookie)||!windows.Visible(m.SourceWindow))return null;
        if(m.Shown(now)!=windows.Visible(m.DestinationWindow))return null;
        if(!Foreground(m))return null;
        var last=read();
        var joined=MatchAfterGetters(m,last,clock());
        return joined is not null&&Foreground(joined)&&windows.Foreground()==gameWindow
            &&windows.ThreadProcess(gameWindow,out uint finalOwner)==gameThread&&finalOwner==gameOwner
            &&windows.Foreground()==gameWindow?joined:null;
    }
    internal static SteamMirrorInputPresentation? MatchAfterGetters(SteamMirrorInputPresentation before,
        SteamMirrorInputPresentation? after,long now) => after is not null&&before.SamePresentation(after)
        &&after.ForegroundScope==before.ForegroundScope
        &&after.Prepared(now)&&after.Shown(now)==before.Shown(now)&&now-after.ReadAt<=250?after:null;
    internal interface IMirrorWindows
    {
        nint Foreground();uint ThreadProcess(nint window,out uint pid);nint Ancestor(nint window,uint flags);
        nint Owner(nint window);nint Cookie(nint window);bool Enabled(nint window);bool Visible(nint window);
    }
    private sealed class NativeMirrorWindows:IMirrorWindows
    {
        internal static readonly NativeMirrorWindows Instance=new();
        public nint Foreground()=>GetForegroundWindow();
        public uint ThreadProcess(nint window,out uint pid)=>GetWindowThreadProcessId(window,out pid);
        public nint Ancestor(nint window,uint flags)=>GetAncestor(window,flags);
        public nint Owner(nint window)=>GetWindow(window,4);
        public nint Cookie(nint window)=>GetWindowLongPtrW(window,-21);
        public bool Enabled(nint window)=>IsWindowEnabled(window);
        public bool Visible(nint window)=>IsWindowVisible(window);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd,out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd,uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd,uint command);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint hwnd,int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] private static extern bool IsWindowEnabled(nint hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint hwnd);
    private static bool Wait(Func<bool> ready,Func<bool> current,Func<long> clock,Action<int> wait,int ms,CancellationToken token)
    {
        long deadline=checked(clock()+ms);
        for(int sample=0;sample<64;sample++)
        {
            if(token.IsCancellationRequested||!current())return false;
            bool done=ready();
            if(!current()||clock()>deadline)return false;
            if(done)return true;
            long remaining=deadline-clock();if(remaining<=0)return false;
            wait((int)Math.Min(30,remaining));
        }
        return false;
    }
}
