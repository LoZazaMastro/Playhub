using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static class AppContainerBrokerChecks
{
    private const ulong FixtureGame = (0xF0010203ul << 32) | 0x02000000;

    internal static void Run(Action<bool, string> check)
    {
        int pid = int.MaxValue - Environment.ProcessId;
        int duplicates = 0;
        using var broker = new OverlayBrokerTransport(pid, FixtureGame, (source, rights) =>
        {
            Interlocked.Increment(ref duplicates);
            return DuplicateLocal(source, rights);
        });
        using var client = new Client(broker);
        string baseName = $"GameOverlay_InputEventStream_{pid}";
        string eventName = $"{baseName}_written-IPCWrapper";
        var eventResult = client.Exchange(5, eventName, flags: 1);
        using var ownEvent = new SafeWaitHandle(eventResult.Handle, true);
        using var peerEvent = EventWaitHandle.OpenExisting(eventName);
        check(eventResult.Error == 0 && !ownEvent.IsInvalid && SetEvent(ownEvent) && peerEvent.WaitOne(500),
            "Broker sends a usable real Win32 event through the anonymous request transport");
        var opened = client.Exchange(6, eventName, access: 0x1F0003);
        using var openedEvent = new SafeWaitHandle(opened.Handle, true);
        check(opened.Error == 0 && !openedEvent.IsInvalid, "Event all-access request is reduced to signal and synchronize rights");

        string mappingName = $"{baseName}_mem-IPCWrapper";
        var mapping = client.Exchange(1, mappingName, access: 4, low: 4096);
        using var mappingHandle = new SafeWaitHandle(mapping.Handle, true);
        using var peerMapping = MemoryMappedFile.OpenExisting(mappingName);
        using var peerView = peerMapping.CreateViewAccessor(0, 4096);
        peerView.Write(0, 0x24681357);
        nint shared = MapViewOfFile(mappingHandle.DangerousGetHandle(), 6, 0, 0, 4096);
        if (shared == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try { check(mapping.Error == 0 && Marshal.ReadInt32(shared) == 0x24681357, "Broker duplicates a working read/write handle for the exact named mapping"); }
        finally { UnmapViewOfFile(shared); }

        string mutexName = $"{baseName}_mutex-IPCWrapper";
        var mutex = client.Exchange(3, mutexName);
        using var mutexHandle = new SafeWaitHandle(mutex.Handle, true);
        using var peerMutex = Mutex.OpenExisting(mutexName);
        bool owned = peerMutex.WaitOne(500);
        if (owned) peerMutex.ReleaseMutex();
        check(mutex.Error == 0 && !mutexHandle.IsInvalid && owned, "Broker creates an unowned named mutex and returns a real handle");
        int before = duplicates;
        var rejected = new[]
        {
            client.Exchange(5, $"GameOverlay_InputEventStream_{pid - 1}_written-IPCWrapper"),
            client.Exchange(5, $"SteamOverlayRunning_{FixtureGame - 1}-IPCWrapper"),
            client.Exchange(5, "Global\\SteamWebHelper_GPUProcRenderEvent"),
            client.Exchange(7, "RendererLog"),
            client.Exchange(1, mappingName, access: 4, low: uint.MaxValue),
            client.Exchange(1, mappingName, access: 0x40, low: 4096),
            client.Exchange(2, mappingName, access: 0x20),
            client.Exchange(5, mappingName),
            client.Exchange(5, eventName, version: 2),
            client.Exchange(5, eventName, reserved: 1),
            client.Exchange(5, new string('a', 256), terminateName: false),
        };
        check(rejected.All(result => result.Handle == 0 && result.Error == OverlayBrokerTransport.Denied) && duplicates == before,
            "Broker denies foreign names and game IDs, file operation, executable mapping, excessive allocation and malformed wire data");
    }

    internal static int InitializeNativeFixture(string path)
    {
        using var broker = new OverlayBrokerTransport(Environment.ProcessId, FixtureGame, DuplicateLocal);
        var handles = broker.Handles;
        string[] variables = ["WSGM_UWP_BRIDGE_MAPPING", "WSGM_UWP_BRIDGE_REQUEST", "WSGM_UWP_BRIDGE_RESPONSE"];
        nint[] values = [handles.Mapping, handles.Request, handles.Response];
        for (int i = 0; i < variables.Length; i++) Environment.SetEnvironmentVariable(variables[i], values[i].ToInt64().ToString("X"));
        var library = NativeLibrary.Load(Path.GetFullPath(path));
        // The fixture process exits immediately afterward; leave the initialized bridge mapped until then.
        var initialize = Marshal.GetDelegateForFunctionPointer<InitializeBridge>(NativeLibrary.GetExport(library, "InitializeBridge"));
        uint result = initialize(0);
        if (result != 0)
        {
            Console.Error.WriteLine($"Native bridge fixture returned {result}.");
            return 1;
        }
        // Read the Win32 environment. .NET can cache its own copy of environment values.
        return variables.All(name => GetEnvironmentVariable(name, new StringBuilder(32), 32) == 0) ? 0 : 2;
    }

    private sealed class Client : IDisposable
    {
        private readonly nint _view;
        private readonly EventWaitHandle _request = new(false, EventResetMode.AutoReset);
        private readonly EventWaitHandle _response = new(false, EventResetMode.AutoReset);
        internal Client(OverlayBrokerTransport broker)
        {
            var handles = broker.Handles;
            _view = MapViewOfFile(handles.Mapping, 6, 0, 0, OverlayBrokerTransport.Size);
            if (_view == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _request.SafeWaitHandle = new SafeWaitHandle(handles.Request, false);
            _response.SafeWaitHandle = new SafeWaitHandle(handles.Response, false);
        }

        internal (nint Handle, int Error) Exchange(uint operation, string name, uint access = 0, uint flags = 0, uint low = 0,
            int version = OverlayBrokerTransport.Version, int reserved = 0, bool terminateName = true)
        {
            Marshal.WriteInt32(_view, 0, version);
            Marshal.WriteInt32(_view, 4, (int)operation);
            Marshal.WriteInt32(_view, 8, (int)access);
            Marshal.WriteInt32(_view, 12, (int)flags);
            Marshal.WriteInt32(_view, 16, 0);
            Marshal.WriteInt32(_view, 20, unchecked((int)low));
            Marshal.WriteInt32(_view, 36, reserved);
            byte[] characters = Encoding.Unicode.GetBytes(terminateName ? name + '\0' : name);
            Marshal.Copy(characters, 0, _view + OverlayBrokerTransport.NameOffset, characters.Length);
            _request.Set();
            if (!_response.WaitOne(2000)) throw new TimeoutException("The broker fixture did not answer its anonymous event.");
            return (new nint(Marshal.ReadInt64(_view, OverlayBrokerTransport.ResultOffset)), Marshal.ReadInt32(_view, OverlayBrokerTransport.ErrorOffset));
        }

        public void Dispose()
        {
            UnmapViewOfFile(_view);
            _response.Dispose();
            _request.Dispose();
        }
    }

    private static nint DuplicateLocal(nint source, uint rights)
    {
        using var process = Process.GetCurrentProcess();
        if (!DuplicateHandle(process.Handle, source, process.Handle, out nint duplicate, rights, false, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return duplicate;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint InitializeBridge(nint ignored);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint MapViewOfFile(nint mapping, uint access, uint high, uint low, nuint size);
    [DllImport("kernel32.dll")] private static extern bool UnmapViewOfFile(nint address);
    [DllImport("kernel32.dll")] private static extern bool SetEvent(SafeWaitHandle handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(nint sourceProcess, nint source, nint targetProcess, out nint target, uint access, bool inherit, uint flags);
    [DllImport("kernel32.dll", EntryPoint = "GetEnvironmentVariableW", CharSet = CharSet.Unicode)]
    private static extern uint GetEnvironmentVariable(string name, StringBuilder output, uint size);
}
