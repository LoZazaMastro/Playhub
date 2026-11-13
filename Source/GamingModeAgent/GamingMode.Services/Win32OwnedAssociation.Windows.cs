using System.IO;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Playhub.DeckyHost;

namespace GamingMode.Services;

// Native operations are individually injectable. Tests exercise this retained
// adapter with non-owning fake SafeProcessHandles; they never call user32/kernel.
internal interface IOwnedProcessApi
{
    SafeProcessHandle Open(uint pid);
    OwnedProcessFacts? Read(SafeProcessHandle handle, uint pid);
    bool Alive(SafeProcessHandle handle, OwnedProcessFacts identity);
}
internal sealed class RetainedOwnedWin32Process : IOwnedWin32Process
{
    private readonly IOwnedProcessApi api;
    private readonly SafeProcessHandle handle;
    public OwnedProcessFacts Identity { get; }
    private RetainedOwnedWin32Process(IOwnedProcessApi api, SafeProcessHandle handle, OwnedProcessFacts identity)
        => (this.api, this.handle, Identity) = (api, handle, identity);
    internal static RetainedOwnedWin32Process? Open(IOwnedProcessApi api, uint pid)
    {
        SafeProcessHandle? handle = null;
        try
        {
            handle = api.Open(pid);
            if (handle.IsInvalid || api.Read(handle, pid) is not { } id || id.Pid != pid || id.Birth <= 0
                || string.IsNullOrWhiteSpace(id.Image) || string.IsNullOrWhiteSpace(id.Sid) || !api.Alive(handle, id)) return null;
            var result = new RetainedOwnedWin32Process(api, handle, id); handle = null; return result;
        }
        finally { handle?.Dispose(); }
    }
    public bool Current
    {
        get
        {
            if (handle.IsClosed || handle.IsInvalid) return false;
            bool live = api.Alive(handle, Identity);
            return live && !handle.IsClosed && !handle.IsInvalid;
        }
    }
    public void Dispose() => handle.Dispose();
}
internal interface IWin32OwnedSystem
{
    IOwnedProcessApi Processes { get; }
    OwnedProcessFacts Self { get; }
    (uint Pid, string Image)? SteamRegistration();
    UwpSteamSessionAssociation.InstalledImages? InstalledImages();
    bool ValveImage(string path);
    bool SteamUi(OwnedProcessFacts ui, OwnedProcessFacts steam, string root, nint window) => false;
    IOwnedEpicProvenance? RetainEpic(Playhub.Shared.EpicLaunchProvenance proof, uint wrapperPid, long wrapperBirth) => null;
    Task<OwnedLogWindow?> Log(string root, CancellationToken cancellation);
    Task<OwnedHandoff?> Handoff(uint wrapperPid, long birth, CancellationToken cancellation);
    (nint Window, uint Pid)? Foreground();
    long Tick { get; }
    DateTime LocalTime { get; }
}
internal sealed class WindowsOwnedAssociationRuntime(IWin32OwnedSystem system) : IWin32OwnedRuntime
{
    internal WindowsOwnedAssociationRuntime() : this(new NativeOwnedAssociationSystem()) { }
    public IOwnedEpicProvenance? RetainEpic(Playhub.Shared.EpicLaunchProvenance proof, uint wrapperPid, long wrapperBirth)
        => system.RetainEpic(proof, wrapperPid, wrapperBirth);
    public long Tick => system.Tick;
    public DateTime LocalTime => system.LocalTime;
    public string WrapperImage => system.InstalledImages()?.Wrapper ?? "";
    public IOwnedWin32Process? Open(uint pid) => RetainedOwnedWin32Process.Open(system.Processes, pid);
    public IOwnedSteamGeneration? OpenSteam()
    {
        if (system.SteamRegistration() is not { } registration || system.InstalledImages() is not { } images) return null;
        IOwnedWin32Process? process = Open(registration.Pid);
        try
        {
            if (process is null || !process.Current || process.Identity.Sid != system.Self.Sid || process.Identity.Session != system.Self.Session
                || !process.Identity.Image.Equals(registration.Image, StringComparison.OrdinalIgnoreCase)
                || !system.ValveImage(registration.Image) || system.SteamRegistration() != registration || !process.Current) return null;
            var result = new Steam(process, registration, images, system); process = null; return result;
        }
        finally { process?.Dispose(); }
    }
    private sealed class Steam(IOwnedWin32Process process, (uint Pid, string Image) registration,
        UwpSteamSessionAssociation.InstalledImages images, IWin32OwnedSystem system) : IOwnedSteamGeneration
    {
        public OwnedProcessFacts Identity => process.Identity;
        public string Root => Path.GetDirectoryName(registration.Image)!;
        private bool disposed;
        public bool Current => !disposed && system.SteamRegistration() == registration && system.InstalledImages() == images
            && process.Current && !disposed && system.SteamRegistration() == registration;
        public void Dispose() { if (disposed) return; disposed = true; process.Dispose(); }
    }
    public Task<OwnedLogWindow?> ReadLogAsync(string root, CancellationToken cancellation) => system.Log(root, cancellation);
    public Task<OwnedHandoff?> ReadHandoffAsync(uint wrapperPid, long birth, CancellationToken cancellation) => system.Handoff(wrapperPid, birth, cancellation);
    public bool SteamUiForeground(IOwnedSteamGeneration generation)
    {
        if (!generation.Current || system.Foreground() is not { } foreground) return false;
        using var ui = Open(foreground.Pid);
        return ui?.Current == true && system.SteamUi(ui.Identity, generation.Identity, generation.Root, foreground.Window)
            && ui.Current && generation.Current && system.Foreground() == foreground
            && ui.Current && generation.Current && system.Foreground() == foreground;
    }
    public (nint Window, uint Pid)? Foreground() => system.Foreground();
}
internal sealed class NativeOwnedAssociationSystem : IWin32OwnedSystem
{
    public IOwnedEpicProvenance? RetainEpic(Playhub.Shared.EpicLaunchProvenance proof, uint wrapperPid, long wrapperBirth)
    {
        var retained = Playhub.Shared.EpicLaunchWindows.RetainFromAuthenticatedWrapper(proof, wrapperPid, wrapperBirth);
        return retained is null ? null : new Epic(retained);
    }
    private sealed class Epic(Playhub.Shared.EpicCaptureScope scope) : IOwnedEpicProvenance
    {
        public bool Current => scope.Current;
        public bool RetainedCurrent => scope.RetainedCurrent;
        public void Dispose() => scope.Dispose();
    }

    public IOwnedProcessApi Processes { get; } = new NativeProcessApi();
    public long Tick => Environment.TickCount64;
    public DateTime LocalTime => DateTime.Now;
    public OwnedProcessFacts Self
    {
        get
        {
            var id = DeckyProcessTree.Read(Environment.ProcessId) ?? throw new IOException("Current process identity unavailable.");
            return Facts(id);
        }
    }
    public UwpSteamSessionAssociation.InstalledImages? InstalledImages() => UwpSteamSessionAssociation.ReadInstalledImages();
    public bool ValveImage(string path) => ValveImageTrust.Verify(path);
    public bool SteamUi(OwnedProcessFacts ui, OwnedProcessFacts steam, string root, nint window)
    {
        var name = new System.Text.StringBuilder(256);
        if (GetClassName(window, name, 256) <= 0) return false;
        var chain = new List<DeckyProcessTree.Identity>(); var seen = new HashSet<int>();
        for (int pid = checked((int)ui.Pid); chain.Count < 5 && seen.Add(pid); ) {
            var item = DeckyProcessTree.Read(pid); if (item is null) return false;
            chain.Add(item); if (item.Pid == steam.Pid) break; pid = item.ParentPid;
        }
        return OwnedSteamUiPolicy.Matches(ui, steam, root, name.ToString(), chain, ValveImage(ui.Image));
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassName(nint window, System.Text.StringBuilder name, int maximum);
    public (uint Pid, string Image)? SteamRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        using var active = key?.OpenSubKey("ActiveProcess");
        if (active?.GetValue("pid") is not int pid || pid <= 0) return null;
        string? exe = key?.GetValue("SteamExe") as string;
        if (string.IsNullOrWhiteSpace(exe) && key?.GetValue("SteamPath") is string path) exe = Path.Combine(path, "steam.exe");
        if (exe is null || exe.Length > 32768 || !Path.IsPathFullyQualified(exe) || exe.StartsWith(@"\\")
            || !Path.GetFileName(exe).Equals("steam.exe", StringComparison.OrdinalIgnoreCase)) return null;
        return ((uint)pid, Path.GetFullPath(exe));
    }
    public async Task<OwnedLogWindow?> Log(string root, CancellationToken cancellation)
    {
        using var file = new FileStream(Path.Combine(root, "logs", "gameprocess_log.txt"), FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        if (!GetFileInformationByHandle(file.SafeFileHandle, out var info)) return null;
        long end = file.Length, start = Math.Max(0, end - 262144); file.Position = start;
        byte[] bytes = new byte[checked((int)(end - start))];
        int used = 0;
        while (used < bytes.Length)
        {
            int n = await file.ReadAsync(bytes.AsMemory(used), cancellation).ConfigureAwait(false);
            if (n == 0) break; used += n;
        }
        if (used != bytes.Length) return null;
        string id = FormattableString.Invariant($"{info.Volume}:{info.IndexHigh}:{info.IndexLow}");
        return new(id, start, end, bytes);
    }
    public async Task<OwnedHandoff?> Handoff(uint wrapperPid, long birth, CancellationToken cancellation)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation); bounded.CancelAfter(180);
        using var pipe = new NamedPipeClientStream(".", FormattableString.Invariant($"Playhub.Win32Session.{wrapperPid}.{birth}"),
            PipeDirection.In, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(bounded.Token).ConfigureAwait(false);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint server) || server != wrapperPid) return null;
        byte[] bytes = new byte[32768]; int used = 0;
        while (used < bytes.Length)
        {
            int n = await pipe.ReadAsync(bytes.AsMemory(used), bounded.Token).ConfigureAwait(false);
            if (n == 0) break; used += n;
        }
        if (used is 0 or >= 32768) return null;
        var identity = JsonSerializer.Deserialize<Win32SteamSessionAssociation.Identity>(bytes.AsSpan(0, used));
        return identity is null ? null : new(server, identity);
    }
    public (nint Window, uint Pid)? Foreground()
    {
        nint hwnd = OverlayWindowTools.GetForegroundWindowHandle();
        if (!OverlayWindowTools.IsPresentGameSurface(hwnd) || GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return null;
        return OverlayWindowTools.GetForegroundWindowHandle() == hwnd ? (hwnd, pid) : null;
    }
    private sealed class NativeProcessApi : IOwnedProcessApi
    {
        public SafeProcessHandle Open(uint pid) => OpenProcess(0x101000, false, pid);
        public OwnedProcessFacts? Read(SafeProcessHandle handle, uint pid)
        {
            var value = DeckyProcessTree.Read(handle, checked((int)pid));
            return value is null ? null : Facts(value);
        }
        public bool Alive(SafeProcessHandle handle, OwnedProcessFacts identity) => !handle.IsClosed && !handle.IsInvalid
            && WaitForSingleObject(handle, 0) == 258 && GetProcessId(handle) == identity.Pid
            && GetProcessTimes(handle, out long birth, out long exit, out _, out _) && exit == 0 && birth == identity.Birth;
    }
    private static OwnedProcessFacts Facts(DeckyProcessTree.Identity id) => new((uint)id.Pid, id.Birth, id.Path, id.Sid, id.Session);
    [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct FileInfo { public uint Attributes; public long Created, Accessed, Written; public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll")] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfo info);
    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeServerProcessId(nint pipe, out uint pid);
    [DllImport("kernel32.dll")] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint ms);
    [DllImport("kernel32.dll")] private static extern uint GetProcessId(SafeProcessHandle handle);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(SafeProcessHandle handle, out long birth, out long exit, out long kernel, out long user);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}
