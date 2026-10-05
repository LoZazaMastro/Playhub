using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace GamingMode.Services;

// Il motore e' sorvegliato finche' Steam e' presente. L'attesa del debugger
// rimane nel motore, mentre qui si limitano soltanto gli avvii del processo.
// Un descrittore esplicito disabilitato non deve attivare l'autoscoperta.

public sealed record ChainFreeEngineDescriptor(string ExecutablePath, IReadOnlyList<string> Arguments)
{
    public const string FileName = "chain-free-engine.json";

    // L'autoscoperta serve soltanto quando manca un file esplicito: una scelta
    // enabled=false dell'utente non deve essere scavalcata.
    public static ChainFreeEngineDescriptor? Discover(IEnumerable<string> installRoots, string userProfile, Action<string>? log = null)
    {
        string? lastMissing = null;
        foreach (string root in installRoots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var found = DiscoverIn(root, userProfile, missing => lastMissing = missing);
            if (found is not null) { log?.Invoke("Chain Free Engine found in " + Path.Combine(root, "ChainFreeEngine") + "."); return found; }
        }
        log?.Invoke("Chain Free Engine: not configured (missing " + (lastMissing ?? "installation") + ")");
        return null;
    }

    // Si preferisce l'installazione dell'utente prima di quelle di sistema.
    public static IEnumerable<string> DefaultInstallRoots(string agentDirectory)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Programs", "Playhub");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Playhub");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Playhub");
        yield return Path.Combine(local, "GamingMode");
        // In sviluppo l'agente puo' essere in <install>\Plugins\Gaming Mode\gaming-mode-win-x64.
        string directory = agentDirectory;
        for (int level = 0; level < 4; level++)
        {
            string? parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent)) break;
            directory = parent;
            yield return directory;
        }
    }

    private static ChainFreeEngineDescriptor? DiscoverIn(string installRoot, string userProfile, Action<string> missing)
    {
        try
        {
            string engineDirectory = Path.Combine(installRoot, "ChainFreeEngine");
            string executable = Path.Combine(engineDirectory, "Playhub.ChainFreeEngine.exe");
            string bundle = Path.Combine(engineDirectory, "Renderer", "playhub-standalone.js");
            string python = Path.Combine(engineDirectory, "Python", "python.exe");
            string pluginRoot = Path.Combine(userProfile, "homebrew", "plugins", "gaming-mode");
            string settings = Path.Combine(userProfile, "homebrew", "settings", "gaming-mode");
            string pluginBuild = Path.Combine(pluginRoot, "dist");
            foreach ((string path, bool directory) in new[]
            {
                (executable, false), (bundle, false), (python, false),
                (pluginRoot, true), (settings, true), (pluginBuild, true),
            })
            {
                if (directory ? Directory.Exists(path) : File.Exists(path)) continue;
                missing(path);
                return null;
            }
            return new ChainFreeEngineDescriptor(executable, new[]
            {
                "--workspace", engineDirectory,
                "--bundle", bundle,
                "--plugin-root", pluginRoot,
                "--settings-dir", settings,
                "--python", python,
                "--plugin-build", pluginBuild,
                "--allow-installed-plugin",
            });
        }
        catch (Exception error) { missing(error.Message); return null; }
    }

    // Un file non utilizzabile o disabilitato non autorizza un avvio implicito.
    public static ChainFreeEngineDescriptor? Read(string path, Action<string>? log = null)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("enabled", out JsonElement enabled) && enabled.ValueKind == JsonValueKind.False) return null;
            string executable = root.TryGetProperty("executable", out JsonElement value) ? value.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(executable)) return null;
            executable = Path.GetFullPath(executable);
            if (!File.Exists(executable)) { log?.Invoke("Chain Free Engine: executable not found at " + executable); return null; }
            List<string> arguments = new();
            if (root.TryGetProperty("arguments", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
                foreach (JsonElement argument in list.EnumerateArray())
                    if (argument.ValueKind == JsonValueKind.String && argument.GetString() is string text && text.Length > 0)
                        arguments.Add(text);
            return new ChainFreeEngineDescriptor(executable, arguments);
        }
        catch (Exception error) { log?.Invoke("Chain Free Engine: descriptor unreadable (" + error.Message + ")"); return null; }
    }
}

// L'interfaccia separa la politica di riavvio dai processi reali, cosi' i test
// possono far avanzare l'orologio senza avviare Steam o un eseguibile Windows.
public interface IChainFreeEngineProcess : IDisposable
{
    bool HasExited { get; }
    void Stop();
}

public sealed class ChainFreeEngineService : IDisposable
{
    public static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(10);
    public const int MaxRestartsPerWindow = 3;
    private readonly ChainFreeEngineDescriptor _descriptor;
    private readonly Func<ChainFreeEngineDescriptor, IChainFreeEngineProcess> _start;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string> _log;
    private readonly List<DateTimeOffset> _starts = new();
    private readonly object _sync = new();
    private IChainFreeEngineProcess? _process;
    private bool _gaming;
    private int? _lastSteam;
    private bool _disposed;
    private DateTimeOffset? _retryAfter;

    public ChainFreeEngineService(ChainFreeEngineDescriptor descriptor,
        Func<ChainFreeEngineDescriptor, IChainFreeEngineProcess>? start = null,
        Func<DateTimeOffset>? clock = null, Action<string>? log = null)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _start = start ?? Launch;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = message => { try { log?.Invoke(message); } catch { /* Il log non ferma il polling. */ } };
    }

    private bool AliveLocked()
    {
        try { return _process is not null && !_process.HasExited; }
        catch { return false; }
    }
    public bool Running { get { lock (_sync) return AliveLocked(); } }
    public int StartCount { get { lock (_sync) return _starts.Count; } }
    public DateTimeOffset? RetryAfter { get { lock (_sync) return _retryAfter; } }

    public void SetSteam(int? steamProcessId)
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (steamProcessId is null)
            {
                _gaming = false;
                StopLocked("Steam is not running");
                return;
            }
            // Si ricorda l'ultimo PID anche attraverso l'intervallo senza Steam.
            // Prima veniva azzerato a null e il vero riavvio non puliva il budget.
            if (_lastSteam is int previous && previous != steamProcessId.Value)
            {
                StopLocked("Steam restarted");
                _starts.Clear();
                _retryAfter = null;
            }
            _lastSteam = steamProcessId;
            _gaming = true;
            EnsureLocked();
        }
    }

    public void Poll()
    {
        lock (_sync)
        {
            if (!_disposed && _gaming) EnsureLocked();
        }
    }

    private void EnsureLocked()
    {
        if (AliveLocked()) return;
        if (_process is not null) { _log("Chain Free Engine exited during a Steam session."); Clear(); }
        DateTimeOffset now = _clock();
        _starts.RemoveAll(start => now - start >= RestartWindow);
        if (_starts.Count >= MaxRestartsPerWindow)
        {
            DateTimeOffset next = _starts[0] + RestartWindow;
            if (_retryAfter != next)
                _log("Chain Free Engine: restart budget exhausted; retry after " + next.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") + ".");
            _retryAfter = next;
            return;
        }
        _retryAfter = null;
        // Anche Process.Start fallito consuma un tentativo: altrimenti un file
        // mancante durante l'installazione causava un ciclo senza alcun limite.
        _starts.Add(now);
        try
        {
            _process = _start(_descriptor) ?? throw new IOException("The engine launcher returned no process.");
            _log("Chain Free Engine started.");
        }
        catch (Exception error) { _log("Chain Free Engine could not start (" + error.Message + ")"); Clear(); }
    }

    private void StopLocked(string reason)
    {
        if (_process is null) return;
        try { _process.Stop(); } catch (Exception error) { _log("Chain Free Engine stop failed (" + error.Message + ")"); }
        _log("Chain Free Engine stopped: " + reason + ".");
        Clear();
    }
    private void Clear()
    {
        try { _process?.Dispose(); } catch { /* Processo gia' terminato. */ }
        _process = null;
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            StopLocked("agent shutdown");
        }
    }
    private static IChainFreeEngineProcess Launch(ChainFreeEngineDescriptor descriptor)
    {
        ProcessStartInfo start = new(descriptor.ExecutablePath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(descriptor.ExecutablePath) ?? "",
        };
        foreach (string argument in descriptor.Arguments) start.ArgumentList.Add(argument);
        Process process = Process.Start(start) ?? throw new IOException("The engine did not start.");
        return new EngineProcess(process);
    }
    private sealed class EngineProcess : IChainFreeEngineProcess
    {
        private readonly Process _process;
        public EngineProcess(Process process) => _process = process;
        public bool HasExited { get { try { return _process.HasExited; } catch { return true; } } }
        public void Stop()
        {
            if (HasExited) return;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var signal = EventWaitHandle.OpenExisting("Local\\Playhub.ChainFreeEngine.Stop." + _process.Id);
                    signal.Set();
                    if (_process.WaitForExit(5000)) return;
                }
            }
            catch { /* Versione precedente o processo ancora nella fase iniziale. */ }
            try { _process.Kill(entireProcessTree: true); } catch { /* Terminato durante il controllo. */ }
            try { _process.WaitForExit(5000); } catch { /* Non rimane niente da attendere. */ }
        }
        public void Dispose() { try { _process.Dispose(); } catch { /* Gia' rilasciato. */ } }
    }
}

// Il timer non scompare se all'avvio manca il motore. Dopo un'installazione a
// caldo rilegge la configurazione; un'eccezione non interrompe i tick successivi.
public sealed class ChainFreeEngineWatch : IDisposable
{
    private readonly Func<ChainFreeEngineDescriptor?> _resolve;
    private readonly Func<int?> _steamPid;
    private readonly Action<string> _log;
    private readonly object _sync = new();
    private readonly Timer _timer;
    private ChainFreeEngineService? _service;
    private string? _signature;
    private DateTimeOffset _nextResolve;
    private DateTimeOffset _nextHeartbeat;
    private int _polling;
    private bool _disposed;

    public ChainFreeEngineWatch(Func<ChainFreeEngineDescriptor?> resolve, Func<int?> steamPid, Action<string> log)
    {
        _resolve = resolve;
        _steamPid = steamPid;
        _log = message => { try { log(message); } catch { /* Il log e' best effort. */ } };
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }
    private void Tick()
    {
        // Stop puo' attendere la pulizia del CDP: i tick nel frattempo vengono
        // saltati, non accumulati sul thread pool dietro lo stesso lock.
        if (Interlocked.Exchange(ref _polling, 1) != 0) return;
        try
        {
            lock (_sync)
            {
                if (_disposed) return;
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (now >= _nextResolve)
                {
                    _nextResolve = now.AddSeconds(30);
                    try
                    {
                        var descriptor = _resolve();
                        string? signature = descriptor is null ? null : descriptor.ExecutablePath + "\0" + string.Join("\0", descriptor.Arguments);
                        if (signature != _signature)
                        {
                            _service?.Dispose();
                            _service = descriptor is null ? null : new ChainFreeEngineService(descriptor, log: _log);
                            _signature = signature;
                            _log(descriptor is null ? "Chain Free Engine configuration disabled or unavailable." : "Chain Free Engine configuration loaded.");
                        }
                    }
                    catch (Exception error) { _log("Chain Free Engine configuration retry: " + error.Message); }
                }
                // SetSteam include gia' EnsureLocked: il secondo Poll originale
                // raddoppiava controlli e log in caso di budget esaurito.
                try { _service?.SetSteam(_steamPid()); }
                catch (Exception error) { _log("Chain Free Engine watch failed; polling continues: " + error.Message); }
                if (now >= _nextHeartbeat)
                {
                    _nextHeartbeat = now.AddMinutes(1);
                    _log("Chain Free Engine watch alive; configured=" + (_service is not null) + "; running=" + (_service?.Running ?? false) + ".");
                }
            }
        }
        catch (Exception error) { _log("Chain Free Engine watch retry: " + error.Message); }
        finally { Interlocked.Exchange(ref _polling, 0); }
    }
    public void Dispose()
    {
        _timer.Dispose();
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _service?.Dispose();
            _service = null;
        }
    }
}
