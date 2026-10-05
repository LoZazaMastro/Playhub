using Playhub.Services;

var passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); passed++; }
async Task<Exception?> Failure(Func<Task> action) { try { await action().WaitAsync(TimeSpan.FromSeconds(3)); return null; } catch (Exception error) { return error; } }
var closed = new Fixture();
Check(await SteamLibraryEditSession.RunAsync(() => Task.FromResult(7), closed) == 7 && closed.Commands.Count == 0, "Already closed Steam remains closed");

var running = new Fixture { Running = true, ShutdownPolls = 3 };
var wroteWhileRunning = false;
await SteamLibraryEditSession.RunAsync(() => { wroteWhileRunning = running.Running; return Task.FromResult(1); }, running);
Check(!wroteWhileRunning && running.Polls == 3 && running.Commands.SequenceEqual(new[] { "-shutdown", "" }), "Write waits for real closure, then restarts desktop Steam once");
var gaming = new Fixture { Running = true, BigPicture = true };
await SteamLibraryEditSession.RunAsync(() => Task.FromResult(1), gaming);
Check(gaming.Commands.SequenceEqual(new[] { "-shutdown", "-gamepadui" }), "Big Picture mode is restored");

var active = new Fixture { Running = true, ActiveGame = true };
var edits = 0;
Check(await Failure(async () => { await SteamLibraryEditSession.RunAsync(() => { edits++; return Task.FromResult(1); }, active); }) is IOException && edits == 0 && active.Commands.Count == 0, "Active game rejects import before every shutdown command");
var unreadable = new Fixture { Running = true, GameStateError = new UnauthorizedAccessException() };
Check(await Failure(async () => { await SteamLibraryEditSession.RunAsync(() => Task.FromResult(1), unreadable); }) is UnauthorizedAccessException && unreadable.Commands.Count == 0, "Unreadable game state never sends shutdown");

var brokenPath = new Fixture { PathError = new IOException("path failure") };
await Failure(async () => { await SteamLibraryEditSession.RunAsync(() => Task.FromResult(1), brokenPath); });
Check(await SteamLibraryEditSession.RunAsync(() => Task.FromResult(4), new Fixture()).WaitAsync(TimeSpan.FromSeconds(3)) == 4, "Path resolution failure releases import gate");
var brokenLaunch = new Fixture { Running = true, ShutdownError = new IOException("start failure") };
await Failure(async () => { await SteamLibraryEditSession.RunAsync(() => Task.FromResult(1), brokenLaunch); });
Check(await SteamLibraryEditSession.RunAsync(() => Task.FromResult(4), new Fixture()).WaitAsync(TimeSpan.FromSeconds(3)) == 4, "Shutdown launch failure releases import gate");

var importError = new IOException("original write failure");
var failedWrite = new Fixture { Running = true };
Check(ReferenceEquals(await Failure(async () => { await SteamLibraryEditSession.RunAsync<int>(() => throw importError, failedWrite); }), importError) && failedWrite.Running, "Failed write restores Steam and preserves original exception");
var restartError = new Fixture { Running = true, RestartError = new IOException("restart failure") };
Check(await SteamLibraryEditSession.RunAsync(() => Task.FromResult(42), restartError) == 42 && restartError.Warnings.Count == 1, "Restart failure keeps successful import result and emits diagnostic warning");
var bothErrors = new Fixture { Running = true, RestartError = new IOException("restart failure") };
Check(ReferenceEquals(await Failure(async () => { await SteamLibraryEditSession.RunAsync<int>(() => throw importError, bothErrors); }), importError) && bothErrors.Warnings.Count == 1, "Restart failure cannot replace original import exception");

var blocked = new Fixture { Running = true, ShutdownPolls = int.MaxValue };
edits = 0;
Check(await Failure(async () => { await SteamLibraryEditSession.RunAsync(() => { edits++; return Task.FromResult(1); }, blocked); }) is TimeoutException && edits == 0 && blocked.Polls == 120 && blocked.Commands.Count == 1, "30 second shutdown limit never writes files or kills Steam after timeout");
var reopened = new Fixture { Running = true, AnotherAppReopens = true };
await SteamLibraryEditSession.RunAsync(() => Task.FromResult(1), reopened);
Check(reopened.Commands.SequenceEqual(new[] { "-shutdown" }), "Concurrent external reopen avoids duplicate launch after lock delay");
var retry = new Fixture { Running = true, IgnoredRestarts = 1 };
await SteamLibraryEditSession.RunAsync(() => Task.FromResult(1), retry);
Check(retry.Commands.Count == 3 && retry.Running && retry.Warnings.Count == 0, "Steam singleton race retries reopen once");
var silentFailure = new Fixture { Running = true, IgnoredRestarts = 2 };
Check(await SteamLibraryEditSession.RunAsync(() => Task.FromResult(8), silentFailure) == 8 && silentFailure.Warnings.Count == 1 && silentFailure.Commands.Count == 3, "Silent reopen failure is bounded and diagnosed");

var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
var entered = false;
var first = SteamLibraryEditSession.RunAsync(() => release.Task, new Fixture());
var second = SteamLibraryEditSession.RunAsync(() => { entered = true; return Task.FromResult(2); }, new Fixture());
Check(!entered, "Concurrent imports cannot enter the writer together");
release.SetResult(1);
await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
Check(entered && second.Result == 2, "Queued import resumes after first write and cleanup");
Console.WriteLine($"{passed} isolated library session checks passed; no real Steam process or registry touched.");

sealed class Fixture : ISteamLibraryRuntime
{
    public bool Running, BigPicture, ActiveGame, AnotherAppReopens;
    public int ShutdownPolls, Polls, IgnoredRestarts;
    public Exception? PathError, ShutdownError, RestartError, GameStateError;
    public List<string> Commands { get; } = new();
    public List<string> Warnings { get; } = new();
    public (bool Running, bool BigPicture) ReadState() => (Running, BigPicture);
    public bool HasActiveGame() => GameStateError is {} error ? throw error : ActiveGame;
    public string SteamExecutable() => PathError is {} error ? throw error : @"X:\fixture\steam.exe";
    public bool FileExists(string path) => true;
    public void Launch(string executable, string arguments, bool hidden)
    {
        Commands.Add(arguments);
        if (arguments == "-shutdown")
        {
            if (!hidden) throw new Exception("Shutdown helper must be hidden");
            if (ShutdownError is {} error) throw error;
            if (ShutdownPolls == 0) Running = false;
        }
        else
        {
            if (RestartError is {} error) throw error;
            Running = IgnoredRestarts-- <= 0;
        }
    }
    public Task Delay(int ms)
    {
        if (ms == 250 && ++Polls >= ShutdownPolls) Running = false;
        if (ms == 1200 && AnotherAppReopens) Running = true;
        return Task.CompletedTask;
    }
    public void Warn(string message) => Warnings.Add(message);
}

namespace Playhub.Services
{
    // Production adapter is linked to compile, but every test injects Fixture.
    // These stubs fail if any test accidentally reaches a real launch pathway.
    internal static class UwpHookSteamManager { public static string GetSteamFolder() => throw new Exception("Native folder lookup forbidden in fixtures"); }
    internal static class ProcessService { public static void StartDetached(string executable, string arguments, bool hidden) => throw new Exception("Native process launch forbidden in fixtures"); }
}
