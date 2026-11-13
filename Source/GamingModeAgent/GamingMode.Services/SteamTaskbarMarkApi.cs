// GPL-3.0-only. Uses the documented ITaskbarList2; never writes appbar preferences.
using System.Runtime.InteropServices;
namespace GamingMode.Services;
internal sealed class SteamTaskbarMarkApi:ISteamTaskbarMark,IDisposable
{
    [ComImport,Guid("56FDF344-FD6D-11D0-958A-006097C9A090")]private class TaskbarCom{}
    [ComImport,Guid("602D4995-B13A-429B-A66E-1935E44F4317"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface ITaskbar2
    {
        [PreserveSig]int HrInit();[PreserveSig]int AddTab(nint h);[PreserveSig]int DeleteTab(nint h);
        [PreserveSig]int ActivateTab(nint h);[PreserveSig]int SetActiveAlt(nint h);
        [PreserveSig]int MarkFullscreenWindow(nint h,[MarshalAs(UnmanagedType.Bool)]bool fullscreen);
    }
    private ITaskbar2? taskbar;
    public int Mark(nint window,bool fullscreen,Func<bool> adjacentGuard)
    {
        if(taskbar is null)
        {
            var candidate=(ITaskbar2)new TaskbarCom();int result=candidate.HrInit();
            if(result!=0){Marshal.ReleaseComObject(candidate);return result;}taskbar=candidate;
        }
        // COM initialization may yield. A release needs the exact identities,
        // while acquisition also needs fresh real foreground/Main proof.
        if(!adjacentGuard())return unchecked((int)0x80004004);
        return taskbar.MarkFullscreenWindow(window,fullscreen);
    }
    public void Dispose(){if(taskbar is {} held){taskbar=null;Marshal.ReleaseComObject(held);}}
}
