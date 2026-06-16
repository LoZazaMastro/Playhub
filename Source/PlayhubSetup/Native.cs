using System;
using System.Runtime.InteropServices;

namespace PlayhubSetup;

/// <summary>Crea collegamenti .lnk usando WScript.Shell (nessuna dipendenza esterna).</summary>
public static class Shortcuts
{
    public static void Create(string shortcutPath, string targetPath, string workingDir,
        string? iconPath = null, string? arguments = null)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type is null) return;

        dynamic? shell = Activator.CreateInstance(type);
        if (shell is null) return;

        try
        {
            var sc = shell.CreateShortcut(shortcutPath);
            sc.TargetPath = targetPath;
            sc.WorkingDirectory = workingDir;
            if (!string.IsNullOrEmpty(arguments)) sc.Arguments = arguments;
            if (!string.IsNullOrEmpty(iconPath)) sc.IconLocation = iconPath + ",0";
            sc.Description = "Playhub";
            sc.Save();
            Marshal.FinalReleaseComObject(sc);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}

/// <summary>Effetti finestra Win11: dark mode, sfondo acrilico, angoli arrotondati.</summary>
public static class WindowEffects
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
    private const int DWMWCP_ROUND = 2;

    /// <summary>
    /// Applica tema scuro + acrilico + angoli tondi. Su Windows non compatibili
    /// le singole chiamate falliscono in modo silenzioso (l'app resta usabile).
    /// </summary>
    public static void ApplyDarkAcrylic(IntPtr hwnd)
    {
        TrySet(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        TrySet(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW);
        TrySet(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
    }

    private static void TrySet(IntPtr hwnd, int attribute, int value)
    {
        try { DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)); }
        catch { }
    }
}
