using System;
using System.IO;
using System.Diagnostics;
using Playhub.Shared;

namespace PlayhubSetup;

internal enum GamingModeUpdateFailure { MissingPayload, StartFailed, TimedOut, InstallerFailed, PayloadMismatch }

internal sealed class GamingModeAgentUpdateException(GamingModeUpdateFailure failure, Exception? inner = null)
    : Exception("Gaming Mode update requires another setup attempt: " + failure, inner)
{
    internal GamingModeUpdateFailure Failure { get; } = failure;
}

internal interface IGamingModeInstallerProcess : IDisposable
{
    bool WaitForExit(int milliseconds);
    int ExitCode { get; }
    void Abort();
}

internal static class GamingModeAgentUpdate
{
    internal static void Ensure(string package, string installed, string script,
        Func<IGamingModeInstallerProcess?> start, int timeoutMilliseconds = 180_000)
    {
        if (GamingModeInstallationPolicy.HasSameAgentPayload(package, installed)) return;
        if (!File.Exists(package) || !File.Exists(script))
            throw new GamingModeAgentUpdateException(GamingModeUpdateFailure.MissingPayload);
        try
        {
            using var process = start() ?? throw new GamingModeAgentUpdateException(GamingModeUpdateFailure.StartFailed);
            // A timed-out PowerShell process is still running. Never query its
            // ExitCode or declare the old agent to be the newly installed one.
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                // Stop only the exact PowerShell process we created. Never
                // terminate Steam, an agent, or an arbitrary descendant tree.
                process.Abort();
                throw new GamingModeAgentUpdateException(GamingModeUpdateFailure.TimedOut);
            }
            if (process.ExitCode != 0)
                throw new GamingModeAgentUpdateException(GamingModeUpdateFailure.InstallerFailed);
            if (!GamingModeInstallationPolicy.HasSameAgentPayload(package, installed))
                throw new GamingModeAgentUpdateException(GamingModeUpdateFailure.PayloadMismatch);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        { throw new GamingModeAgentUpdateException(GamingModeUpdateFailure.StartFailed, error); }
    }

    internal static IGamingModeInstallerProcess? Start(ProcessStartInfo start)
    {
        var process = Process.Start(start);
        return process is null ? null : new InstallerProcess(process);
    }

    private sealed class InstallerProcess(Process process) : IGamingModeInstallerProcess
    {
        public bool WaitForExit(int milliseconds) => process.WaitForExit(milliseconds);
        public int ExitCode => process.ExitCode;
        public void Abort()
        {
            if (!process.HasExited) process.Kill();
            process.WaitForExit(2_000);
        }
        public void Dispose() => process.Dispose();
    }
}
