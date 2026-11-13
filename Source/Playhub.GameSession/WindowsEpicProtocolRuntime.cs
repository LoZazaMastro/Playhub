using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Playhub.Shared;

namespace Playhub.GameSession;

internal sealed class WindowsEpicProtocolRuntime : IEpicProtocolRuntime, IEpicProtocolBindingRuntime, IDisposable
{
    private HashSet<string> allowed = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> payloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly uint session;
    private readonly bool observationEnabled;
    private int observationFailures;
    private readonly EpicInstalledLaunch launch;
    private EpicProtocolCompanion? companion;
    public bool SnapshotComplete { get; private set; }
    public void AfterDispatch(long startedAt, bool beforeComplete)
    {
        if (beforeComplete) companion = new EpicProtocolCompanion(launch, startedAt, payload =>
        {
            // Publish a new immutable observation set, after normal URI dispatch.
            // Retained causal provenance remains the worker's separate gate.
            var images = new HashSet<string>(Volatile.Read(ref allowed), StringComparer.OrdinalIgnoreCase) { payload.Executable };
            Volatile.Write(ref allowed, images);
            var roles = new HashSet<string>(Volatile.Read(ref payloads), StringComparer.OrdinalIgnoreCase) { payload.Executable };
            Volatile.Write(ref payloads, roles);
        });
    }
    public void ObserveCandidates(IReadOnlyList<EpicObservedCandidate> candidates) => companion?.Observe(candidates);
    public void Dispose() => companion?.Dispose();
    internal WindowsEpicProtocolRuntime(EpicInstalledLaunch launch)
    {
        this.launch = launch;
        observationEnabled = Api.ProcessIdToSessionId((uint)Environment.ProcessId, out session);
        foreach (string image in EpicProtocolSession.ObservationImages(launch,
            directory => Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))) allowed.Add(image);
        // Observation only, not a custom ownership grant or a launch target.
        // Fortnite's recorded Bootstrapper/EAC chain is still launched by Epic.
        if (launch.Namespace == "fn" && launch.AppName == "Fortnite")
        {
            string payload = Path.Combine(launch.InstallDirectory, "FortniteGame", "Binaries", "Win64", "FortniteClient-Win64-Shipping.exe");
            if (allowed.Contains(payload)) payloads.Add(payload);
        }
        else if (EpicLaunchCatalog.Supported(launch.Namespace, launch.CatalogItemId, launch.AppName)) payloads.Add(launch.GameExecutable);
    }
    public long Tick => Environment.TickCount64;
    public long FileTime => DateTime.UtcNow.ToFileTimeUtc();
    public void Dispatch(string uri)
    {
        if (!string.Equals(uri, launch.LaunchUri, StringComparison.Ordinal))
            throw new ArgumentException("The Epic URI differs from the installed launch metadata.", nameof(uri));
        // Broker the normal handler through the existing desktop Explorer so a
        // newly started launcher and its helpers do not become Steam children.
        ExplorerProtocolDispatch.Epic(uri);
    }
    public void Delay(int milliseconds) => Thread.Sleep(milliseconds);
    public void Log(string message) => UwpSessionLog.Write(message);
    public IReadOnlyList<IEpicObservedProcess> Snapshot(EpicInstalledLaunch launch)
    {
        SnapshotComplete = observationEnabled;
        if (!observationEnabled) return Array.Empty<IEpicObservedProcess>();
        var observedImages = Volatile.Read(ref allowed); var observedPayloads = Volatile.Read(ref payloads);
        var result = new List<IEpicObservedProcess>();
        var seen = new HashSet<uint>();
        try
        {
            foreach (string name in observedImages.Select(path => Path.GetFileNameWithoutExtension(path)!).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var candidates = Process.GetProcessesByName(name);
                try
                {
                    foreach (var candidate in candidates)
                    {
                        uint pid = (uint)candidate.Id;
                        if (!seen.Add(pid)) continue;
                        if (seen.Count > 128) throw new InvalidOperationException("Epic process candidates exceed their bound.");
                        try
                        {
                            var process = Observed.Open(pid, session, observedImages, observedPayloads);
                            if (process is not null) result.Add(process);
                        }
                        catch (Exception error) when (error is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
                        {
                            // Protected/unknown processes never enable a binding,
                            // and never trigger a raw-executable launch fallback.
                            SnapshotComplete = false;
                            if (++observationFailures <= 8) Log("Epic process observation unavailable; no ownership was inferred.");
                        }
                    }
                }
                finally { foreach (var candidate in candidates) candidate.Dispose(); }
            }
            return result;
        }
        catch { foreach (var process in result) process.Dispose(); throw; }
    }
    private sealed class Observed : IEpicObservedProcess
    {
        private readonly SafeFileHandle handle;
        public uint Pid { get; }
        public long Birth { get; }
        public string Image { get; }
        public bool IsPayload { get; }
        private Observed(SafeFileHandle handle, uint pid, long birth, string image, bool payload)
        { this.handle = handle; Pid = pid; Birth = birth; Image = image; IsPayload = payload; }
        internal static Observed? Open(uint pid, uint session, HashSet<string> allowed, HashSet<string> payloads)
        {
            if (!Api.ProcessIdToSessionId(pid, out uint actualSession) || actualSession != session) return null;
            var handle = Api.OpenProcess(0x101000, false, pid);
            try
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (Api.WaitForSingleObject(handle, 0) == 0) return null;
                var image = new StringBuilder(32768); uint length = (uint)image.Capacity;
                if (!Api.QueryFullProcessImageName(handle, 0, image, ref length)
                    || !Api.GetProcessTimes(handle, out long birth, out _, out _, out _))
                    throw new InvalidOperationException("Epic candidate identity is unavailable.");
                string path = Path.GetFullPath(image.ToString());
                if (!allowed.Contains(path)) return null;
                if (!Api.OpenProcessToken(handle, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
                using (token)
                using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
                using (var self = WindowsIdentity.GetCurrent())
                    if (identity.User is null || self.User is null || !identity.User.Equals(self.User)) return null;
                var result = new Observed(handle, pid, birth, path, payloads.Contains(path));
                if (result.State == EpicObservedState.Exited) return null;
                handle = null!; return result;
            }
            finally { handle?.Dispose(); }
        }
        public EpicObservedState State
        {
            get
            {
                if (handle.IsClosed || handle.IsInvalid) return EpicObservedState.Unknown;
                uint wait = Api.WaitForSingleObject(handle, 0);
                if (wait == 0) return EpicObservedState.Exited;
                return wait == 258 && Api.GetProcessId(handle) == Pid
                    && Api.GetProcessTimes(handle, out long birth, out _, out _, out _) && birth == Birth
                    ? EpicObservedState.Alive : EpicObservedState.Unknown;
            }
        }
        public long PresentationWindow
        {
            get
            {
                if (!IsPayload || State != EpicObservedState.Alive) return 0;
                nint found = 0;
                Api.EnumProc callback = (window, ignored) =>
                {
                    _ = Api.GetWindowThreadProcessId(window, out uint owner);
                    if (owner == Pid && Api.GetAncestor(window, 2) == window && Api.IsWindowVisible(window) && !Api.IsIconic(window)
                        && Api.GetWindowRect(window, out var rect) && rect.Right - rect.Left >= 80 && rect.Bottom - rect.Top >= 60
                        && Api.DwmGetWindowAttribute(window, 14, out uint cloaked, 4) == 0 && cloaked == 0) found = window;
                    return true;
                };
                Api.EnumWindows(callback, 0);
                return State == EpicObservedState.Alive ? found.ToInt64() : 0;
            }
        }
        public void Dispose() => handle.Dispose();
    }
    private static class Api
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        internal delegate bool EnumProc(nint window, nint parameter);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeFileHandle OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] internal static extern uint GetProcessId(SafeFileHandle process);
        [DllImport("kernel32.dll")] internal static extern uint WaitForSingleObject(SafeFileHandle process, uint ms);
        [DllImport("kernel32.dll")] internal static extern bool GetProcessTimes(SafeFileHandle process, out long birth, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll")] internal static extern bool ProcessIdToSessionId(uint pid, out uint session);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern bool QueryFullProcessImageName(SafeFileHandle process, uint flags, StringBuilder path, ref uint length);
        [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool OpenProcessToken(SafeFileHandle process, uint access, out SafeFileHandle token);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint parameter);
        [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window, uint flags);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(nint window);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window, out Rect rectangle);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint window, uint attribute, out uint value, uint bytes);
    }
}
