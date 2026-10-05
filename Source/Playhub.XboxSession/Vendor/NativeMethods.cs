using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WSGM.PackagedLaunch;

/// <summary>
///     The Windows entry points this launcher calls, grouped by what they are for.
/// </summary>
/// <remarks>
///     Declarations only: every policy decision lives in the caller. The spike's PEB group is gone
///     with the raw environment replacement it served, and so are the module-enumeration and
///     mitigation-policy groups, which only ever fed a transcript.
/// </remarks>
internal static class NativeMethods
{
    // ---- Process access ----
    internal const uint ProcessTerminate = 0x0001;
    internal const uint ProcessVmRead = 0x0010;
    internal const uint ProcessDupHandle = 0x0040;
    internal const uint ProcessSetQuota = 0x0100;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint Synchronize = 0x0010_0000;

    /// <summary>What a remote DLL load needs, so a refusal names the mask it was refused.</summary>
    internal const uint InjectorAccess = 0x0002 | 0x0008 | ProcessVmRead | 0x0020 | 0x0400 | Synchronize;

    // ---- Remote work ----
    internal const uint MemCommit = 0x1000;
    internal const uint MemReserve = 0x2000;
    internal const uint MemRelease = 0x8000;
    internal const uint PageReadWrite = 0x04;
    internal const uint PageExecuteReadWrite = 0x40;
    internal const uint ProcessQueryInformation = 0x0400;
    internal const uint ListModulesAll = 0x03;
    internal const int MaxPath = 260;

    // ---- Process enumeration ----
    internal const uint Th32CsSnapProcess = 0x0000_0002;

    // ---- Job containment ----
    internal const int JobObjectExtendedLimitInformation = 9;
    internal const uint JobObjectLimitKillOnJobClose = 0x2000;
    internal const int JobObjectBasicAccountingInformationClass = 1;

    // ---- Package identity ----
    internal const int ErrorSuccess = 0;
    internal const int ErrorInsufficientBuffer = 122;
    internal const uint PackageFilterHead = 0x0000_0010;
    internal const uint PackageFilterDirect = 0x0000_0020;

    // ---- Token ----
    internal const uint TokenQuery = 0x0008;
    internal const int TokenIntegrityLevel = 25;
    internal const int TokenIsAppContainer = 29;

    // ---- Package activation ----
    internal const int AoNoErrorUi = 0x2;
    internal const int AoNoSplashScreen = 0x4;

    internal const uint WmActivate = 0x0006;
    internal const uint WmSetFocus = 0x0007;
    internal const uint WmNcActivate = 0x0086;
    internal const uint WmActivateApp = 0x001C;
    internal const uint WmClose = 0x0010;
    internal const uint WmDestroy = 0x0002;
    internal const uint WsPopup = 0x8000_0000;
    internal const uint WsExLayered = 0x0008_0000;
    internal const uint WsExToolWindow = 0x0000_0080;
    internal const uint LwaAlpha = 0x0000_0002;
    internal const int SwShowNa = 8;
    internal const int SwRestore = 9;

    // ---- Console ----
    internal const int SwHide = 0;

    /// <summary>The machine type an x64 image is built for.</summary>
    internal const ushort ImageFileMachineAmd64 = 0x8664;

    /// <summary>What <c>IsWow64Process2</c> reports for a process that is not running under WOW64.</summary>
    internal const ushort ImageFileMachineUnknown = 0;

    // ---- Following another launcher's game ----
    internal const uint ProcessCreateProcess = 0x0080;
    internal const int ProcessCommandLineInformation = 60;

    /// <summary>
    ///     Lets a contained process start children outside the job without asking, so only what the
    ///     supervisor recognises and contains is ever in it.
    /// </summary>
    internal const uint JobObjectLimitSilentBreakawayOk = 0x1000;

    // ---- Resolving a folder behind a junction ----
    internal const uint FileShareReadWriteDelete = 0x0000_0007;
    internal const uint OpenExisting = 3;

    /// <summary>Required to open a directory, rather than a file, for its handle.</summary>
    internal const uint FileFlagBackupSemantics = 0x0200_0000;

    /// <summary>The final path with a drive letter, in its normalised form.</summary>
    internal const uint VolumeNameDos = 0x0;

    /// <summary>This process's command line exactly as Windows received it.</summary>
    /// <returns>The raw line, or empty when Windows returned none.</returns>
    internal static string CommandLine()
    {
        return Marshal.PtrToStringUni(GetCommandLineW()) ?? string.Empty;
    }

    // Returns a pointer into the process's own memory that must not be freed.
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCommandLineW();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint GetFinalPathNameByHandleW(IntPtr file, StringBuilder? path, uint length, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetExitCodeProcess(SafeHandle process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(
        uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageNameW(
        IntPtr process, uint flags, StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessTimes(
        IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr VirtualAllocEx(
        IntPtr process, IntPtr address, UIntPtr size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteProcessMemory(
        IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr written);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateRemoteThread(
        IntPtr process, IntPtr attributes, UIntPtr stackSize, IntPtr start, IntPtr parameter,
        uint flags, IntPtr threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    internal static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr LoadLibraryW(string path);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool K32EnumProcessModulesEx(
        IntPtr process, [Out] IntPtr[] modules, uint size, out uint needed, uint filter);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint K32GetModuleFileNameExW(
        IntPtr process, IntPtr module, StringBuilder fileName, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32W entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32W entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(
        IntPtr job, int informationClass, IntPtr information, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryInformationJobObject(
        IntPtr job, int informationClass, IntPtr information, uint length, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetPackageFamilyName(
        IntPtr process, ref uint length, StringBuilder? packageFamilyName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder? packageFullName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetPackagePathByFullName(
        string packageFullName, ref uint length, StringBuilder? path);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int FindPackagesByPackageFamily(
        string packageFamilyName,
        uint packageFilters,
        ref uint count,
        IntPtr packageFullNames,
        ref uint bufferLength,
        IntPtr buffer,
        IntPtr packageProperties);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(
        IntPtr token, int informationClass, IntPtr information, uint length, out uint returnLength);

    // Both return interior pointers into the SID, never a value: declaring the count as an int
    // would truncate the pointer on x64 and read garbage.
    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);

    [DllImport("ole32.dll")]
    internal static extern int CoAllowSetForegroundWindow(
        [MarshalAs(UnmanagedType.IUnknown)] object unknown, IntPtr reserved);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern ushort RegisterClassExW(ref WndClassExW windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateWindowExW(
        uint exStyle, IntPtr className, string? windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int GetMessageW(out Msg message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr DispatchMessageW(ref Msg message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(IntPtr window, uint key, byte alpha, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(IntPtr window, StringBuilder className, int count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr window);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler? handler,
        [MarshalAs(UnmanagedType.Bool)] bool add);

    [DllImport("ntdll.dll")]
    internal static extern int NtQueryInformationProcess(
        IntPtr process, int informationClass, IntPtr information, int length, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint GetShortPathNameW(string longPath, StringBuilder? shortPath, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ProcessEntry32W
    {
        internal uint dwSize;
        internal uint cntUsage;
        internal uint th32ProcessID;
        internal IntPtr th32DefaultHeapID;
        internal uint th32ModuleID;
        internal uint cntThreads;
        internal uint th32ParentProcessID;
        internal int pcPriClassBase;
        internal uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectBasicLimitInformation
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal UIntPtr MinimumWorkingSetSize;
        internal UIntPtr MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal UIntPtr Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectExtendedLimitInformationData
    {
        internal JobObjectBasicLimitInformation BasicLimitInformation;
        internal IoCounters IoInfo;
        internal UIntPtr ProcessMemoryLimit;
        internal UIntPtr JobMemoryLimit;
        internal UIntPtr PeakProcessMemoryUsed;
        internal UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JobObjectBasicAccountingInformation
    {
        internal long TotalUserTime;
        internal long TotalKernelTime;
        internal long ThisPeriodTotalUserTime;
        internal long ThisPeriodTotalKernelTime;
        internal uint TotalPageFaultCount;
        internal uint TotalProcesses;
        internal uint ActiveProcesses;
        internal uint TotalTerminatedProcesses;
    }

    // ---- Process Lifetime Management ----
    // The interface a debugger attaches with. A package marked for debugging is exempt from PLM
    // suspension, which is the only reason a packaged game freezes behind an overlay.
    [ComImport]
    [Guid("F27C3930-8029-4AD1-94E3-3DBA417810C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPackageDebugSettings
    {
        [PreserveSig]
        int EnableDebugging(
            [MarshalAs(UnmanagedType.LPWStr)] string packageFullName,
            [MarshalAs(UnmanagedType.LPWStr)] string? debuggerCommandLine,
            IntPtr environment);

        [PreserveSig]
        int DisableDebugging([MarshalAs(UnmanagedType.LPWStr)] string packageFullName);

        [PreserveSig]
        int Suspend([MarshalAs(UnmanagedType.LPWStr)] string packageFullName);

        [PreserveSig]
        int Resume([MarshalAs(UnmanagedType.LPWStr)] string packageFullName);

        [PreserveSig]
        int TerminateAllProcesses([MarshalAs(UnmanagedType.LPWStr)] string packageFullName);
    }

    [ComImport]
    [Guid("B1AEC16F-2383-4852-B0E9-8F0B1DC66B4D")]
    internal class PackageDebugSettings
    {
    }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            int options,
            out uint processId);

        // The remaining two slots exist only to keep the vtable layout correct.
        [PreserveSig]
        int ActivateForFile(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string? verb,
            out uint processId);

        [PreserveSig]
        int ActivateForProtocol(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    internal class ApplicationActivationManager
    {
    }

    // ---- The foreground proxy's window ----
    internal delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WndClassExW
    {
        internal uint cbSize;
        internal uint style;
        internal IntPtr lpfnWndProc;
        internal int cbClsExtra;
        internal int cbWndExtra;
        internal IntPtr hInstance;
        internal IntPtr hIcon;
        internal IntPtr hCursor;
        internal IntPtr hbrBackground;
        internal IntPtr lpszMenuName;
        internal IntPtr lpszClassName;
        internal IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        internal IntPtr hwnd;
        internal uint message;
        internal IntPtr wParam;
        internal IntPtr lParam;
        internal uint time;
        internal int ptX;
        internal int ptY;
    }

    internal delegate bool EnumWindowsProc(IntPtr window, IntPtr param);

    internal delegate bool ConsoleCtrlHandler(uint controlType);

    [StructLayout(LayoutKind.Sequential)]
    internal struct UnicodeString
    {
        internal ushort Length;
        internal ushort MaximumLength;
        internal IntPtr Buffer;
    }
}
