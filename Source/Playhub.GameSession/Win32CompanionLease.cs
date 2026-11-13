using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Playhub.GameSession;

// The live wrapper retains this exact game handle. Direct children keep
// their parent proof; protocol games carry a separately sealed launch witness.
// Consumers also authenticate the pipe server PID and process birth.
internal sealed record Win32CompanionIdentity(uint WrapperPid, long WrapperBirth, uint GamePid,
    long GameBirth, ulong GameId, string Executable, Playhub.Shared.EpicLaunchProvenance? Epic = null)
{
    internal byte[]? SerializeBounded()
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(this);
        if (json.Length >= 32767) return null;
        byte[] payload = new byte[json.Length + 1];json.CopyTo(payload,0);payload[^1]=(byte)'\n';return payload;
    }
    internal string PipeName => FormattableString.Invariant($"Playhub.Win32Session.{WrapperPid}.{WrapperBirth}");
}

internal sealed class Win32CompanionLease : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SafeFileHandle _game;
    private readonly Process _sidecar;
    private readonly Task _publication;
    private readonly Task _returnArm;

    private Win32CompanionLease(SafeFileHandle game, Process sidecar, Win32CompanionIdentity identity, byte[] payload, Func<bool>? provenanceCurrent)
    {
        _game = game; // Borrowed from the launch owner; disposed after this lease.
        _sidecar = sidecar;
        _publication = Publish(identity, payload, provenanceCurrent);
        _returnArm = Task.Run(async () =>
        {
            try
            {
                using var client = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { UseProxy = false })
                    { Timeout = TimeSpan.FromMilliseconds(800) };
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode");
                await XboxShellBrokerClient.ArmGameReturnAsync(directory, client, (uint)(identity.GameId >> 32),
                    identity.GamePid, _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception error) { UwpSessionLog.Write("Win32 return handoff unavailable: " + error.Message); }
        });
    }

    internal static Win32CompanionLease? TryStart(SafeFileHandle game, uint pid, string executable,
        Playhub.Shared.EpicLaunchProvenance? epic = null, Func<bool>? provenanceCurrent = null)
    {
        bool enabled = typeof(Win32CompanionLease).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(metadata => metadata.Key == "PlayhubWin32SteamCompanion" && metadata.Value == "true");
        if (!enabled || !TryReadGameId(Environment.GetEnvironmentVariable("SteamGameId"), out ulong gameId)) return null;
        string helper = Path.Combine(AppContext.BaseDirectory, "Playhub.XboxSession.exe");
        if (!File.Exists(helper) || !File.Exists(Path.Combine(AppContext.BaseDirectory, "PlayhubSteamCompanion.dll"))) return null;
        Process? child = null;
        try
        {
            using var wrapper = Process.GetCurrentProcess();
            string expectedWrapper = Path.Combine(AppContext.BaseDirectory, "Playhub.GameSession.exe");
            if (!string.Equals(Environment.ProcessPath, expectedWrapper, StringComparison.OrdinalIgnoreCase)
                || game.IsInvalid || GetProcessId(game) != pid || WaitForSingleObject(game, 0) != 258
                || !GetProcessTimes(game, out long birth, out _, out _, out _)) return null;
            string image = ReadImage(game);
            if (!image.Equals(Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase)) return null;
            if (epic is not null && (provenanceCurrent?.Invoke() != true || epic.WrapperPid != wrapper.Id
                || epic.WrapperBirth != wrapper.StartTime.ToFileTimeUtc() || epic.GamePid != pid || epic.GameBirth != birth
                || !epic.GameExecutable.Equals(image, StringComparison.OrdinalIgnoreCase))) return null;
            var identity = new Win32CompanionIdentity((uint)wrapper.Id, wrapper.StartTime.ToFileTimeUtc(), pid, birth, gameId, image, epic);
            var payload = identity.SerializeBounded();
            if (payload is null) return null;
            child = Process.Start(new ProcessStartInfo(helper, "--win32-companion-sidecar")
            {
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                StandardInputEncoding = new UTF8Encoding(false),
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("The Win32 companion did not start.");
            bool allowed = AllowSetForegroundWindow((uint)child.Id);
            child.StandardInput.Write(Encoding.UTF8.GetString(payload));
            child.StandardInput.Close();
            UwpSessionLog.Write($"Win32 companion handoff wrapper={identity.WrapperPid} game={pid} birth={birth} gameId={gameId} foreground-allowed={allowed}");
            return new Win32CompanionLease(game, child, identity, payload, provenanceCurrent);
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            try { child?.StandardInput.Close(); } catch (IOException) { }
            child?.Dispose();
            UwpSessionLog.Write("Win32 companion unavailable: " + error.Message);
            return null;
        }
    }

    internal static bool TryReadGameId(string? value, out ulong gameId) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out gameId)
        && (uint)gameId == 0x02000000 && (gameId >> 32) >= 0x80000000;

    private async Task Publish(Win32CompanionIdentity identity, byte[] payload, Func<bool>? provenanceCurrent)
    {
        try
        {
            while (!_stop.IsCancellationRequested && WaitForSingleObject(_game, 0) == 258 && (provenanceCurrent?.Invoke() ?? true))
            {
                using var pipe = new NamedPipeServerStream(identity.PipeName, PipeDirection.Out, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                if (WaitForSingleObject(_game, 0) != 258 || !(provenanceCurrent?.Invoke() ?? true)) return;
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                bounded.CancelAfter(300);
                try { await pipe.WriteAsync(payload, bounded.Token).ConfigureAwait(false); }
                catch (Exception error) when (!_stop.IsCancellationRequested && error is IOException or OperationCanceledException) { }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { UwpSessionLog.Write("Win32 identity publication unavailable: " + error.Message); }
    }

    private static string ReadImage(SafeFileHandle game)
    {
        var path = new StringBuilder(32768);
        uint length = (uint)path.Capacity;
        if (!QueryFullProcessImageName(game, 0, path, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return Path.GetFullPath(path.ToString());
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { Task.WhenAll(_publication, _returnArm).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        // The native worker watches this exact game handle and wrapper owner.
        // Never terminate a game or an unrelated process during teardown.
        try { _sidecar.WaitForExit(1500); } catch (InvalidOperationException) { }
        _sidecar.Dispose();
        _stop.Dispose();
    }

    [DllImport("kernel32.dll")] private static extern uint GetProcessId(SafeFileHandle process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeFileHandle process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeFileHandle process, out long birth, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(SafeFileHandle process, uint flags, StringBuilder image, ref uint length);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint pid);
}
