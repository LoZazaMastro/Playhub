using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Playhub.Shared;

namespace Playhub.GameSession;

// The publisher belongs to the existing game lifetime. Its idle state is an
// asynchronous pipe wait, not a timer or a second game watcher.
internal sealed class UwpIdentityPublication : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private readonly Task _returnArm;
    private readonly object _sync = new();
    private NamedPipeServerStream? _listening;
    internal UwpIdentityPublication(UwpSessionIdentity identity, uint appId = 0)
    {
        _worker = Serve(identity);
        _returnArm = appId == 0 ? Task.CompletedTask : Task.Run(async () =>
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode");
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromMilliseconds(800) };
            await XboxShellBrokerClient.ArmGameReturnAsync(directory, client, appId, identity.GamePid, _stop.Token);
        });
    }
    private async Task Serve(UwpSessionIdentity identity)
    {
        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(identity) + "\n");
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var pipe = new NamedPipeServerStream(UwpSessionIdentity.PipeName(identity.WrapperPid, identity.WrapperBirth),
                    PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                lock (_sync) { if (_stop.IsCancellationRequested) return; _listening = pipe; }
                try
                {
                    await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                    await pipe.WriteAsync(payload, _stop.Token).ConfigureAwait(false);
                }
                catch (IOException) when (!_stop.IsCancellationRequested) { /* A client can disconnect without ending the game's publisher. */ }
                finally { lock (_sync) _listening = null; }
            }
        }
        catch (Exception error) when (_stop.IsCancellationRequested && (error is OperationCanceledException or ObjectDisposedException or IOException)) { }
        catch (Exception error) { UwpSessionLog.Write("identity-publication unavailable: " + error.Message); }
        finally { lock (_sync) _listening = null; }
    }
    public void Dispose()
    {
        _stop.Cancel();
        lock (_sync) _listening?.Dispose();
        try { _worker.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        _returnArm.GetAwaiter().GetResult();
        _stop.Dispose();
    }
    internal static IDisposable? Publish(string aumid, IUwpProcess game)
    {
        if (game is not WindowsUwpProcess actual || actual.Birth == 0
            || !string.Equals(actual.PackageFamily, aumid.Split('!')[0], StringComparison.OrdinalIgnoreCase)) return null;
        using var wrapper = Process.GetCurrentProcess();
        return new UwpIdentityPublication(new((uint)wrapper.Id, wrapper.StartTime.ToUniversalTime().ToFileTimeUtc(),
            actual.Id, actual.Birth, aumid, actual.PackageFamily, actual.Executable),
            XboxShellBrokerClient.ShortcutAppId(Environment.GetEnvironmentVariable("SteamGameId")));
    }
}
