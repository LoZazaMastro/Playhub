using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GamingMode.Services;
internal static class WindowsXboxGameBar
{
    internal static GameBarGuideResult ToggleVerified(uint appId)
    {
        var deadline = Stopwatch.StartNew();
        nint foreground = OverlayWindowTools.GetForegroundWindowHandle();
        uint pid = (uint)OverlayWindowTools.WindowProcessId(foreground);
        if (pid == 0) return new(false, false, "unverifiedForeground");
        var sessions = UwpSteamSessionAssociation.FindForApp(appId);
        return GameBarInteractionGuard.Run(sessions.Count > 0,
            () => sessions.Any(s => s.GamePid == pid),
            () => Windows.Gaming.UI.GameBar.IsInputRedirected,
            () => SendVerifiedChord(foreground, deadline));
    }
    private static GameBarGuideResult SendVerifiedChord(nint foreground, Stopwatch deadline)
    {
        // Keep the verified foreground boundary adjacent to the single input chord.
        if (deadline.ElapsedMilliseconds >= 600 || OverlayWindowTools.GetForegroundWindowHandle() != foreground) return new(false, false, "foregroundChanged");
        Input[] chord = [ Key(0x5B, false), Key(0x47, false), Key(0x47, true), Key(0x5B, true) ];
        uint sent = SendInput((uint)chord.Length, chord, Marshal.SizeOf<Input>());
        if (sent != chord.Length)
        {
            Input[] release = [Key(0x47, true), Key(0x5B, true)];
            SendInput(2, release, Marshal.SizeOf<Input>());
            return new(false, false, "inputRejected");
        }
        return new(true, true, "opened");
    }
    private static Input Key(ushort key, bool up) => new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Flags = up ? 2u : 0u } } };
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public Data Data; }
    [StructLayout(LayoutKind.Explicit)] private struct Data
    {
        [FieldOffset(0)] public Keyboard Keyboard;
        [FieldOffset(0)] public Mouse Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public nint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public nint Extra; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
