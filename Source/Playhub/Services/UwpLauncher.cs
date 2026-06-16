using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Playhub.Services;

public static class UwpLauncher
{
    public static Task LaunchAsync(string aumid, string arguments)
    {
        var manager = new ApplicationActivationManager();
        manager.ActivateApplication(aumid, arguments, ActivateOptions.None, out _);
        return Task.CompletedTask;
    }

    private enum ActivateOptions
    {
        None = 0x00000000,
        DesignMode = 0x00000001,
        NoErrorUI = 0x00000002,
        NoSplashScreen = 0x00000004
    }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        IntPtr ActivateApplication(
            [In] string appUserModelId,
            [In] string arguments,
            [In] ActivateOptions options,
            [Out] out uint processId);

        IntPtr ActivateForFile([In] string appUserModelId, [In] IntPtr itemArray, [In] string verb, [Out] out uint processId);
        IntPtr ActivateForProtocol([In] string appUserModelId, [In] IntPtr itemArray, [Out] out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private sealed class ApplicationActivationManager : IApplicationActivationManager
    {
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        public extern IntPtr ActivateApplication(
            [In] string appUserModelId,
            [In] string arguments,
            [In] ActivateOptions options,
            [Out] out uint processId);

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        public extern IntPtr ActivateForFile([In] string appUserModelId, [In] IntPtr itemArray, [In] string verb, [Out] out uint processId);

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        public extern IntPtr ActivateForProtocol([In] string appUserModelId, [In] IntPtr itemArray, [Out] out uint processId);
    }
}
