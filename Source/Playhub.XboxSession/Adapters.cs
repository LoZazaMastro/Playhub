using System.Diagnostics;
namespace WSGM.PackagedLaunch;

internal sealed record ProcessFacts(int Id);

internal static class PackagedLaunchLog
{
    internal static void Info(string message) => Write("info", message);
    internal static void Error(string message) => Write("error", message);
    private static void Write(string level, string message)
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playhub", "logs");
        try
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "xbox-session.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {level} {message}{Environment.NewLine}");
        }
        catch { }
    }
}

internal sealed class PrivilegeJournal
{
    internal void Record(string action, string access, bool granted, int error)
        => PackagedLaunchLog.Info($"action={action}; access={access}; granted={granted}; error={error}");
}

internal static class ProcessInspector
{
    internal static bool? IsNativeX64(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return NativeMethods.IsWow64Process2(process.Handle, out ushort emulation, out ushort architecture)
                ? emulation == 0 && architecture == 0x8664 : null;
        }
        catch { return null; }
    }
}
