using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PlayhubLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var baseDir = AppContext.BaseDirectory;
        var appExe = Path.Combine(baseDir, "app", "Playhub.exe");
        if (!File.Exists(appExe))
        {
            MessageBox(IntPtr.Zero, "Non trovo app\\Playhub.exe.", "Playhub", 0x10);
            return;
        }

        Process.Start(new ProcessStartInfo(appExe)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(appExe) ?? baseDir
        });
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
