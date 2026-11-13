using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Playhub.GameSession;

internal interface IExplorerProtocolLease : IDisposable
{
    bool Current { get; }
    void ShellExecute(string uri);
}

internal static class ExplorerProtocolDispatch
{
    internal static void Epic(string uri) => Epic(uri, WindowsExplorerProtocolLease.Open);

    // The URI is the immutable installed-metadata URI, not an executable or a
    // command line. Explorer invokes its normal registered handler in its own
    // process context, outside the Steam-tracked wrapper's launch ancestry.
    internal static void Epic(string uri, Func<IExplorerProtocolLease> open)
    {
        if (uri.Length > 4096 || uri.Any(char.IsControl)
            || !Uri.TryCreate(uri, UriKind.Absolute, out var target)
            || target.Scheme != "com.epicgames.launcher" || target.Host != "apps"
            || target.UserInfo.Length != 0 || target.Port != -1 || target.Fragment.Length != 0
            || target.AbsolutePath.Length <= 1 || target.Query != "?action=launch&silent=true")
            throw new ArgumentException("The installed Epic launch URI is invalid.", nameof(uri));
        using var explorer = open();
        if (!explorer.Current) throw new InvalidOperationException("The Windows desktop changed before the Epic launch request.");
        explorer.ShellExecute(uri);
        // A successful request is not a game-start or process-ownership proof.
        // Existing EpicProtocolSession observation/provenance supplies those.
    }
}

internal sealed class WindowsExplorerProtocolLease : IExplorerProtocolLease
{
    private readonly SafeFileHandle process;
    private readonly nint shellWindow;
    private readonly uint pid;
    private readonly long birth;
    private readonly List<object> com = new();
    private object? application;
    private bool disposed;

    private WindowsExplorerProtocolLease(SafeFileHandle process, nint window, uint pid, long birth)
    { this.process = process; shellWindow = window; this.pid = pid; this.birth = birth; }

    internal static IExplorerProtocolLease Open()
    {
        if (!OperatingSystem.IsWindows() || Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("The Epic launch request needs the Windows desktop apartment.");
        nint window = Api.GetShellWindow();
        _ = Api.GetWindowThreadProcessId(window, out uint pid);
        if (window == 0 || pid == 0) throw new InvalidOperationException("The Windows desktop is unavailable for the Epic launch request.");
        var process = Api.OpenProcess(0x101000, false, pid);
        WindowsExplorerProtocolLease? lease = null;
        try
        {
            if (process.IsInvalid || Api.WaitForSingleObject(process, 0) != 258
                || !Api.GetProcessTimes(process, out long birth, out _, out _, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The Windows desktop identity is unavailable.");
            if (!Api.ProcessIdToSessionId(pid, out uint explorerSession)
                || !Api.ProcessIdToSessionId((uint)Environment.ProcessId, out uint ownSession) || explorerSession != ownSession)
                throw new InvalidOperationException("The Windows desktop belongs to another session.");
            var image = new StringBuilder(32768); uint length = (uint)image.Capacity;
            if (!Api.QueryFullProcessImageName(process, 0, image, ref length)
                || !Path.GetFullPath(image.ToString()).Equals(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Windows desktop is not hosted by Explorer.");
            if (!Api.OpenProcessToken(process, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            using (var own = WindowsIdentity.GetCurrent())
                if (identity.User is null || own.User is null || !identity.User.Equals(own.User))
                    throw new InvalidOperationException("The Windows desktop belongs to another user.");
            lease = new(process, window, pid, birth);
            process = null!;
            lease.BindDesktop();
            return lease;
        }
        catch { lease?.Dispose(); throw; }
        finally { process?.Dispose(); }
    }

    private T Keep<T>(T value) where T : class { com.Add(value); return value; }
    private void BindDesktop()
    {
        // Microsoft: desktop view -> background automation -> Application ->
        // IShellDispatch2.ShellExecute. Creating Shell.Application directly can
        // instantiate it in the caller and loses Explorer's launch context.
        // https://devblogs.microsoft.com/oldnewthing/20131118-00/?p=2643
        var windows = Keep((IShellWindows)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), throwOnError: true)!)!);
        object location = null!, root = null!; // VT_EMPTY; pvarLocRoot is unused and must be empty.
        var desktop = Keep(windows.FindWindowSW(ref location, ref root, 8, out int desktopWindow, 1));
        if (!WindowOwned(unchecked((nint)(uint)desktopWindow))) throw new InvalidOperationException("Explorer desktop automation ownership changed.");
        var provider = (IComServiceProvider)desktop;
        Guid service = new("4C96BE40-915C-11CF-99D3-00AA004AE837"), browserId = typeof(IShellBrowser).GUID;
        Marshal.ThrowExceptionForHR(provider.QueryService(ref service, ref browserId, out object browserObject));
        var browser = (IShellBrowser)Keep(browserObject);
        Marshal.ThrowExceptionForHR(browser.GetWindow(out nint browserWindow));
        if (!WindowOwned(browserWindow)) throw new InvalidOperationException("Explorer browser ownership changed.");
        Marshal.ThrowExceptionForHR(browser.QueryActiveShellView(out var view));
        Keep(view);
        Guid dispatch = new("00020400-0000-0000-C000-000000000046");
        Marshal.ThrowExceptionForHR(view.GetItemObject(0, ref dispatch, out object folder));
        Keep(folder);
        application = Keep(folder.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, folder, null)
            ?? throw new InvalidOperationException("Explorer shell automation is unavailable."));
        if (!Current) throw new InvalidOperationException("The Windows desktop changed while preparing the Epic launch request.");
    }

    private bool WindowOwned(nint window)
    { _ = Api.GetWindowThreadProcessId(window, out uint owner); return window != 0 && owner == pid; }
    public bool Current => !disposed && application is not null && !process.IsClosed && !process.IsInvalid
        && Api.GetShellWindow() == shellWindow && WindowOwned(shellWindow)
        && Api.WaitForSingleObject(process, 0) == 258 && Api.GetProcessId(process) == pid
        && Api.GetProcessTimes(process, out long actualBirth, out _, out _, out _) && actualBirth == birth;

    public void ShellExecute(string uri)
    {
        if (!Current) throw new InvalidOperationException("The Windows desktop changed before the Epic launch request.");
        // No arguments, working directory, environment, elevation, or executable
        // override: Epic's registered handler owns auth, bootstrap and anti-cheat.
        application!.GetType().InvokeMember("ShellExecute", BindingFlags.InvokeMethod, null, application,
            new object[] { uri, "", "", "", 1 });
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; application = null;
        for (int index = com.Count - 1; index >= 0; index--)
            if (Marshal.IsComObject(com[index]))
                try { Marshal.ReleaseComObject(com[index]); } catch (InvalidComObjectException) { }
        com.Clear(); process.Dispose();
    }

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        int Count { get; }
        [return: MarshalAs(UnmanagedType.IDispatch)] object Item([In, MarshalAs(UnmanagedType.Struct)] object index);
        [return: MarshalAs(UnmanagedType.IUnknown)] object NewEnum();
        void Register([MarshalAs(UnmanagedType.IDispatch)] object value, int hwnd, int windowClass, out int cookie);
        void RegisterPending(int thread, [In, MarshalAs(UnmanagedType.Struct)] ref object location, [In, MarshalAs(UnmanagedType.Struct)] ref object root, int windowClass, out int cookie);
        void Revoke(int cookie);
        void OnNavigate(int cookie, [In, MarshalAs(UnmanagedType.Struct)] ref object location);
        void OnActivated(int cookie, [MarshalAs(UnmanagedType.VariantBool)] bool active);
        [return: MarshalAs(UnmanagedType.IDispatch)] object FindWindowSW([In, MarshalAs(UnmanagedType.Struct)] ref object location, [In, MarshalAs(UnmanagedType.Struct)] ref object root, int windowClass, out int hwnd, int options);
        void OnCreated(int cookie, [MarshalAs(UnmanagedType.IUnknown)] object value);
        void ProcessAttachDetach([MarshalAs(UnmanagedType.VariantBool)] bool attach);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    { [PreserveSig] int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value); }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        [PreserveSig] int GetWindow(out nint hwnd);
        [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);
        [PreserveSig] int InsertMenusSB(nint menu, nint widths);
        [PreserveSig] int SetMenuSB(nint menu, nint hole, nint window);
        [PreserveSig] int RemoveMenusSB(nint menu);
        [PreserveSig] int SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int EnableModelessSB([MarshalAs(UnmanagedType.Bool)] bool enable);
        [PreserveSig] int TranslateAcceleratorSB(nint message, ushort id);
        [PreserveSig] int BrowseObject(nint item, uint flags);
        [PreserveSig] int GetViewStateStream(uint mode, out nint stream);
        [PreserveSig] int GetControlWindow(uint id, out nint window);
        [PreserveSig] int SendControlMsg(uint id, uint message, nuint wParam, nint lParam, out nint result);
        [PreserveSig] int QueryActiveShellView(out IShellView view);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        [PreserveSig] int GetWindow(out nint hwnd);
        [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);
        [PreserveSig] int TranslateAccelerator(nint message);
        [PreserveSig] int EnableModeless([MarshalAs(UnmanagedType.Bool)] bool enable);
        [PreserveSig] int UIActivate(uint state);
        [PreserveSig] int Refresh();
        [PreserveSig] int CreateViewWindow(nint previous, nint settings, nint browser, nint rect, out nint hwnd);
        [PreserveSig] int DestroyViewWindow();
        [PreserveSig] int GetCurrentInfo(nint settings);
        [PreserveSig] int AddPropertySheetPages(uint reserved, nint callback, nint parameter);
        [PreserveSig] int SaveViewState();
        [PreserveSig] int SelectItem(nint item, uint flags);
        [PreserveSig] int GetItemObject(uint item, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);
    }

    private static class Api
    {
        [DllImport("user32.dll")] internal static extern nint GetShellWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeFileHandle OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] internal static extern uint GetProcessId(SafeFileHandle process);
        [DllImport("kernel32.dll")] internal static extern uint WaitForSingleObject(SafeFileHandle process, uint milliseconds);
        [DllImport("kernel32.dll")] internal static extern bool GetProcessTimes(SafeFileHandle process, out long birth, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll")] internal static extern bool ProcessIdToSessionId(uint pid, out uint session);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern bool QueryFullProcessImageName(SafeFileHandle process, uint flags, StringBuilder path, ref uint size);
        [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool OpenProcessToken(SafeFileHandle process, uint access, out SafeFileHandle token);
    }
}
