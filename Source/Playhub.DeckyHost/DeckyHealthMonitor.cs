using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace Playhub.DeckyHost;

internal interface IDeckyHealthMonitor : IDisposable
{
    Task<DeckyProcessTree.Identity?> ReadFailure(CancellationToken cancellation);
    bool Confirm(DeckyProcessTree.Identity identity);
    void Pause();
}

internal sealed class DeckyHealthMonitor(DeckyProcessTree.Identity root) : IDeckyHealthMonitor
{
    private readonly DeckyHealthPolicy _policy = new(Environment.TickCount64);
    private readonly bool _supportedEndpoint = DeckyHealthPolicy.SupportsEndpoint(
        Environment.GetEnvironmentVariable("SERVER_HOST"), Environment.GetEnvironmentVariable("SERVER_PORT"));
    private readonly HttpClient _http = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromMilliseconds(400) };
    private Process? _runtime;
    private DeckyProcessTree.Identity? _identity;
    private long _next;

    public async Task<DeckyProcessTree.Identity?> ReadFailure(CancellationToken cancellation)
    {
        if (!_supportedEndpoint) return null; // Preserve explicitly customized upstream endpoints.
        long now = Environment.TickCount64;
        if (now < _next) return null;
        _next = now + DeckyHealthPolicy.ProbeIntervalMilliseconds;
        if (_runtime is null)
        {
            var children = DeckyProcessTree.Snapshot().Values.Where(candidate => candidate.ParentPid == root.Pid
                && DeckyProcessTree.Depth(candidate, root, new Dictionary<int, DeckyProcessTree.Identity>()) == 1).ToArray();
            if (children.Length != 1) { _policy.Clear(); return null; }
            var child = OpenRuntime(children[0]);
            if (child is null) { _policy.Clear(); return null; }
            _runtime = child; _identity = children[0];
        }
        var identity = _identity!;
        if (!ReadCpu(_runtime, identity, out long cpu))
        { _runtime.Dispose(); _runtime = null; _identity = null; _policy.Clear(); return null; }
        var sample = new DeckyHealthSample(now, cpu, identity);
        if (!_policy.ObserveCpu(sample)) return null;
        bool? absent = ListenerAbsent();
        // A foreign or unreadable listener is a conflict, never permission to
        // stop our tree. No networking is done in the normal idle fast path.
        if (absent != true) { _policy.ObserveNetwork(sample, absent, false); return null; }
        bool failed;
        try
        {
            using var response = await _http.GetAsync("http://127.0.0.1:1337/", HttpCompletionOption.ResponseHeadersRead, cancellation);
            failed = false; // Any HTTP status proves the server event loop runs.
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        { cancellation.ThrowIfCancellationRequested(); failed = true; }
        return _policy.ObserveNetwork(sample, absent, failed) && Confirm(identity) ? identity : null;
    }

    public bool Confirm(DeckyProcessTree.Identity identity) => _identity == identity && _runtime is not null
        && _policy.IsFresh(identity, Environment.TickCount64) && ReadCpu(_runtime, identity, out _)
        && DeckyProcessTree.Read(_runtime.SafeHandle, identity.Pid) == identity && ListenerAbsent() == true;

    public void Pause() => _policy.Clear();

    internal static Process? OpenRuntime(DeckyProcessTree.Identity identity, Func<int, Process>? open = null,
        Func<Process, DeckyProcessTree.Identity?>? read = null)
    {
        Process? process = null;
        try
        {
            process = (open ?? Process.GetProcessById)(identity.Pid);
            if ((read ?? (candidate => DeckyProcessTree.Read(candidate.SafeHandle, candidate.Id)))(process) != identity)
                return null;
            var proven = process; process = null; return proven;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return null; }
        finally { process?.Dispose(); }
    }

    private static bool ReadCpu(Process process, DeckyProcessTree.Identity identity, out long cpu)
    {
        cpu = 0;
        try
        {
            if (!GetProcessTimes(process.SafeHandle, out long birth, out long exited, out long kernel, out long user)
                || birth != identity.Birth || exited != 0) return false;
            cpu = checked(kernel + user); return true;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or OverflowException)
        { return false; }
    }

    // All LISTEN rows count, including foreign/IPv6 listeners. Never infer an
    // absent server from a failed HTTP response or an inaccessible TCP table.
    internal static bool? ListenerAbsent()
    {
        try
        {
            foreach (int family in new[] { 2, 23 })
            {
                int bytes = 0;
                uint status = GetExtendedTcpTable(0, ref bytes, false, family, 3, 0); // OWNER_PID_LISTENER
                if (status != 122 || bytes < 4 || bytes > 4 * 1024 * 1024) return null;
                nint table = Marshal.AllocHGlobal(bytes);
                try
                {
                    if (GetExtendedTcpTable(table, ref bytes, false, family, 3, 0) != 0) return null;
                    bool? absent = TableAbsent(table, bytes, family);
                    if (absent != true) return absent;
                }
                finally { Marshal.FreeHGlobal(table); }
            }
            return true;
        }
        catch (Exception error) when (error is ArgumentException or OverflowException) { return null; }
    }

    internal static bool? TableAbsent(nint table, int bytes, int family)
    {
        if (table == 0 || bytes < 4 || family is not (2 or 23)) return null;
        int count = Marshal.ReadInt32(table), rowSize = family == 2 ? 24 : 56;
        if (count < 0 || count > (bytes - 4) / rowSize) return null;
        for (int index = 0; index < count; index++)
        {
            nint row = table + 4 + index * rowSize;
            int stateOffset = family == 2 ? 0 : 48;
            int portOffset = family == 2 ? 8 : 20;
            int port = (ushort)IPAddress.NetworkToHostOrder(Marshal.ReadInt16(row, portOffset));
            if (Marshal.ReadInt32(row, stateOffset) == 2 && port == 1337) return false;
        }
        return true;
    }

    public void Dispose() { _http.Dispose(); _runtime?.Dispose(); }
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(Microsoft.Win32.SafeHandles.SafeProcessHandle handle,
        out long creation, out long exit, out long kernel, out long user);
    [DllImport("iphlpapi.dll", SetLastError = true)] private static extern uint GetExtendedTcpTable(nint table, ref int bytes,
        [MarshalAs(UnmanagedType.Bool)] bool sorted, int family, int tableClass, uint reserved);
}
