using Playhub.Shared;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static int Main()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "Playhub-ShellIdentity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string executable = Path.Combine(scratch, "Playhub.exe");
        string icon = Path.Combine(scratch, "Playhub.ico");
        File.WriteAllBytes(executable, [1, 2, 3]);
        File.WriteAllBytes(icon, [1, 2, 3]);
        var writes = new List<(uint Key, string? Value)>();
        ShellIdentity.ApplyWindow((key, value) => writes.Add((key, value)), executable, icon);
        Check(writes.Select(value => value.Key).SequenceEqual(new uint[] { 2, 4, 3, 5 }), "relaunch tuple precedes window ID");
        Check(writes[0].Value == "\"" + executable + "\"", "relaunch has exact quoted executable");
        Check(writes[1].Value == "Playhub", "stable product name");
        Check(writes[2].Value == icon + ",0", "explicit ICO resource index");
        Check(writes[3].Value == "LoZazaMastro.Playhub", "version-independent shared identity");
        Check(ShellIdentity.IsMainTarget(executable, executable), "same canonical main app");
        Check(ShellIdentity.IsMainTarget(executable.ToUpperInvariant(), executable), "Windows case-insensitive main app");
        foreach (string value in new[] { "Playhub.exe", Path.Combine(scratch, "Other.exe"), "steam://open/bigpicture", "", "\"" + executable + "\" --other" })
            Check(!ShellIdentity.IsMainTarget(value, executable), "unrelated shortcut target refused");
        Check(!ShellIdentity.IsMainTarget(Path.Combine(scratch, "GamingMode.exe"), Path.Combine(scratch, "GamingMode.exe")), "agent never adopted as main app");
        Check(!ShellIdentity.IsMainTarget(Path.Combine(scratch, "Playhub Setup.exe"), Path.Combine(scratch, "Playhub Setup.exe")), "setup never adopted as main app");
        Check(!ShellIdentity.UpdateOwnedShortcut(Path.Combine(scratch, "absent.lnk"), executable, icon), "missing shortcut remains absent");
        NativeShortcutChecks(scratch, executable, icon);
        Console.WriteLine($"PASS {_checks} shell-identity checks; no UI, process activation, user shortcuts, registry or cache changes.");
        return 0;
    }

    private static void NativeShortcutChecks(string scratch, string executable, string icon)
    {
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!)!;
        try
        {
            string owned = Path.Combine(scratch, "owned.lnk");
            dynamic shortcut = ((dynamic)shell).CreateShortcut(owned);
            shortcut.TargetPath = executable; shortcut.Arguments = "--keep \"user words\" \"\"";
            shortcut.WorkingDirectory = scratch; shortcut.WindowStyle = 7; shortcut.Hotkey = "CTRL+ALT+F12";
            shortcut.Description = "User description"; shortcut.Save(); string originalHotkey = shortcut.Hotkey; Marshal.ReleaseComObject(shortcut);
            Check(ShellIdentity.UpdateOwnedShortcut(owned, executable, icon, Console.WriteLine), "owned existing shortcut upgraded");
            shortcut = ((dynamic)shell).CreateShortcut(owned);
            Check((string)shortcut.Arguments == "--keep \"user words\" \"\"", "existing arguments preserved");
            Check((string)shortcut.WorkingDirectory == scratch, "working directory preserved");
            Check((int)shortcut.WindowStyle == 7, "window style preserved");
            Check((string)shortcut.Hotkey == originalHotkey, "hotkey preserved");
            Check((string)shortcut.Description == "User description", "description preserved");
            Check(((string)shortcut.IconLocation).Equals(icon + ",0", StringComparison.OrdinalIgnoreCase), "owned icon repaired");
            Marshal.ReleaseComObject(shortcut);
            Check(ReadId(owned) == ShellIdentity.AppId, "actual ShellLink property ID matches main window");
            Check(ShellIdentity.UpdateOwnedShortcut(owned, executable, icon), "upgrade idempotent");
            Check(ReadId(owned) == ShellIdentity.AppId, "id remains stable after repeated update");
            string foreign = Path.Combine(scratch, "foreign.lnk");
            shortcut = ((dynamic)shell).CreateShortcut(foreign);
            shortcut.TargetPath = Path.Combine(scratch, "Other.exe"); shortcut.Arguments = "--user"; shortcut.Save(); Marshal.ReleaseComObject(shortcut);
            byte[] before = SHA256.HashData(File.ReadAllBytes(foreign));
            Check(!ShellIdentity.UpdateOwnedShortcut(foreign, executable, icon), "different target not modified");
            Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(foreign))), "unrelated shortcut bytes preserved");
        }
        finally { Marshal.ReleaseComObject(shell); }
    }

    private static string? ReadId(string path)
    {
        object link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!)!;
        try
        {
            ((IPersistFile)link).Load(path, 0);
            Key key = new() { Format = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
            Marshal.ThrowExceptionForHR(((IStore)link).GetValue(in key, out Value value));
            try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.ReleaseComObject(link); }
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); _checks++; }
    [StructLayout(LayoutKind.Sequential)] private struct Key { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct Value { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Pointer; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IStore { [PreserveSig] int GetCount(out uint count); [PreserveSig] int GetAt(uint index, out Key key); [PreserveSig] int GetValue(in Key key, out Value value); }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref Value value);
}
