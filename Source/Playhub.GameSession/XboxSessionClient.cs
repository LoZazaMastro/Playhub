using System.Diagnostics;

namespace Playhub.GameSession;

internal static class XboxSessionClient
{
    internal static int Run(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The Xbox session component could not start.");
        process.WaitForExit();
        return process.ExitCode;
    }
}
