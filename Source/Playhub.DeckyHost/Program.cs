using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Playhub.DeckyHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is not (2 or 4) || args[0] != "--loader") return 2;
        int? attach = null;
        if (args.Length == 4)
        {
            if (args[2] != "--attach" || !int.TryParse(args[3], out int pid) || pid <= 0) return 2;
            attach = pid;
        }
        try
        {
            string loader = Path.GetFullPath(args[1]);
            if (!DeckyProcessTree.IsLoaderPath(loader) || !File.Exists(loader)) return 2;
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(loader.ToUpperInvariant())));
            using var single = new Mutex(true, "Local\\Playhub.DeckyHost." + key, out bool owns);
            if (!owns) return 0;
            try { using var lifetime = new DeckyLifetime(loader, attach); return lifetime.Run(); }
            finally { single.ReleaseMutex(); }
        }
        catch { return 1; }
    }
}

// One hidden native window and a blocking message pump. No timer, network polling,
// WPF initialization or process-tree-wide termination runs in the resident host.
internal sealed class DeckyLifetime : IDisposable
{
    private const uint RootExited = 0x8001;
    private readonly string _loader;
    private readonly int? _attach;
    private readonly WindowProcedure _procedure;
    private readonly string _className = "Playhub.DeckyLifetime." + Guid.NewGuid().ToString("N");
    private nint _window;
    private Process? _root;
    private DeckyProcessTree.Identity? _identity;
    private string? _runtime;
    private bool _sessionEnded;

    internal DeckyLifetime(string loader, int? attach = null) { _loader = loader; _attach = attach; _procedure = WindowProc; }

    internal int Run()
    {
        // Windows notifies this host before default-priority (0x280) onefile
        // parents. Confirmed session end releases worker DLLs before extraction
        // cleanup. Neither QUERYENDSESSION nor a cancelled ENDSESSION stops Decky.
        if (!SetProcessShutdownParameters(0x3f0, 0)) throw new IOException("Cannot set Decky shutdown order.");
        var cls = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Procedure = _procedure, Instance = GetModuleHandle(null), Name = _className };
        if (RegisterClassEx(ref cls) == 0) throw new IOException("Cannot register Decky lifecycle window.");
        _window = CreateWindowEx(0, _className, "Playhub Decky lifecycle", 0, 0, 0, 0, 0, 0, 0, cls.Instance, 0);
        if (_window == 0) throw new IOException("Cannot create Decky lifecycle window.");
        if (_attach is int attachedPid)
        {
            // Upgrading Playhub must protect a loader already running from an
            // older startup entry, without restarting Steam or its plugins.
            _identity = DeckyProcessTree.FindRoot(attachedPid, _loader)
                ?? throw new IOException("Cannot verify the existing Decky process.");
            _root = Process.GetProcessById(_identity.Pid);
            if (DeckyProcessTree.Read(_root.SafeHandle, _root.Id) != _identity)
                throw new IOException("The existing Decky process has changed.");
        }
        else
        {
            DeckyRuntimeDirectory.CleanupAbandoned(_loader);
            _runtime = DeckyRuntimeDirectory.Create(_loader);
            var start = new ProcessStartInfo(_loader) { WorkingDirectory = Path.GetDirectoryName(_loader)!, UseShellExecute = false };
            foreach (string name in start.Environment.Keys.ToArray())
                if (name.StartsWith("_PYI", StringComparison.OrdinalIgnoreCase) || name.Equals("_MEIPASS2", StringComparison.OrdinalIgnoreCase)) start.Environment.Remove(name);
            start.Environment["PYINSTALLER_RESET_ENVIRONMENT"] = "1";
            start.Environment["TEMP"] = _runtime;
            start.Environment["TMP"] = _runtime;
            _root = Process.Start(start) ?? throw new IOException("Cannot start Decky.");
            _identity = DeckyProcessTree.Read(_root.Id);
        }
        if (_identity is null || !_identity.Path.Equals(_loader, StringComparison.OrdinalIgnoreCase)) throw new IOException("Cannot verify Decky ownership.");
        _root.Exited += RootHasExited;
        _root.EnableRaisingEvents = true;
        int result;
        while ((result = GetMessage(out var message, 0, 0, 0)) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); }
        return result < 0 ? 1 : 0;
    }

    private void RootHasExited(object? sender, EventArgs args) => PostMessage(_window, RootExited, 0, 0);

    private nint WindowProc(nint window, uint message, nuint wparam, nint lparam)
    {
        if (message == 0x11) return 1; // WM_QUERYENDSESSION: consent immediately
        if (message == 0x16) // WM_ENDSESSION
        {
            if (wparam != 0 && !_sessionEnded)
            {
                _sessionEnded = true;
                try { if (_identity is not null) DeckyProcessTree.StopDescendants(_identity); } catch { }
            }
            return 0;
        }
        if (message == RootExited)
        {
            // PyInstaller normally removes _MEI itself. Abandoned extraction is
            // retried only under this host's marked private runtime container.
            try { if (_runtime is not null) DeckyRuntimeDirectory.TryCleanup(_runtime, _loader); } catch { }
            PostQuitMessage(0);
            return 0;
        }
        return DefWindowProc(window, message, wparam, lparam);
    }

    public void Dispose()
    {
        if (_root is not null) { _root.Exited -= RootHasExited; _root.Dispose(); }
        if (_window != 0) { DestroyWindow(_window); _window = 0; }
        UnregisterClass(_className, GetModuleHandle(null));
    }

    private delegate nint WindowProcedure(nint hwnd, uint message, nuint wparam, nint lparam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass { public uint Size, Style; public WindowProcedure Procedure; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? Menu, Name; public nint SmallIcon; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetProcessShutdownParameters(uint level, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")] private static extern ushort RegisterClassEx(ref WindowClass cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")] private static extern nint CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string cls, nint instance);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")] private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint window, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessage(nint window, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
}
