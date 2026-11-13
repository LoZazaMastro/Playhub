// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

internal interface IPackageDebugReference
{
    int Enable(string package);
    int Disable(string package);
}

internal enum PackageLeasePhase { Acquiring = 1, Acquired, Releasing }
internal enum LeaseProcessState { Unknown, Running, Gone }
internal readonly record struct LeaseProcess(uint Pid, long Creation)
{
    internal bool Valid => Pid is > 0 and <= int.MaxValue && Creation > 0;
}
internal sealed record PackageLeaseRecord(int Version, string Package, string Boot, LeaseProcess Owner,
    LeaseProcess Game, PackageLeasePhase Phase);

internal sealed class PackageLifetimeLease : IDisposable
{
    private readonly FileStream _lock;
    private readonly string _journal;
    private readonly IPackageDebugReference _backend;
    private readonly Action<string> _log;
    private readonly Func<LeaseProcess, LeaseProcessState> _processState;
    private readonly PackageLeaseRecord _acquired;
    private bool _disposed;

    private PackageLifetimeLease(FileStream fileLock, string journal, IPackageDebugReference backend,
        Func<LeaseProcess, LeaseProcessState> processState, Action<string> log, PackageLeaseRecord acquired)
        => (_lock, _journal, _backend, _processState, _log, _acquired) = (fileLock, journal, backend, processState, log, acquired);

    internal static PackageLifetimeLease Acquire(string directory, string package, string boot, LeaseProcess owner,
        LeaseProcess game, IPackageDebugReference backend, Func<LeaseProcess, LeaseProcessState> processState,
        Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(package) || string.IsNullOrWhiteSpace(boot) || !owner.Valid || !game.Valid || owner == game)
            throw new ArgumentException("A verified package, boot session, owner and game are required.");
        var folder = Directory.CreateDirectory(directory);
        if ((folder.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The Xbox package lifetime journal directory is redirected.");
        string journal = Path.Combine(folder.FullName, "cuphead.json");
        var fileLock = new FileStream(Path.Combine(folder.FullName, "cuphead.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        try
        {
            var acquired = AcquireReference(journal, package, boot, owner, game, backend, processState, log);
            return new PackageLifetimeLease(fileLock, journal, backend, processState, log, acquired);
        }
        catch { fileLock.Dispose(); throw; }
    }

    private static PackageLeaseRecord AcquireReference(string journal, string package, string boot, LeaseProcess owner,
        LeaseProcess game, IPackageDebugReference backend, Func<LeaseProcess, LeaseProcessState> processState, Action<string> log)
    {
        if (Read(journal) is { } previous)
        {
            if (previous.Version != 1 || previous.Package != package || previous.Boot != boot ||
                !previous.Owner.Valid || !previous.Game.Valid || previous.Owner == previous.Game ||
                previous.Phase != PackageLeasePhase.Acquired ||
                processState(previous.Owner) != LeaseProcessState.Gone || processState(previous.Game) != LeaseProcessState.Gone)
                throw new InvalidOperationException("An unresolved Cuphead package lifetime record was retained. No debugger reference was changed.");
            Release(journal, previous, backend);
            log("Package lifetime: recovered one confirmed reference from an exited Cuphead trial.");
        }

        if (processState(owner) != LeaseProcessState.Running || processState(game) != LeaseProcessState.Running)
            throw new InvalidOperationException("The verified owner or Cuphead process exited before the lifetime lease.");
        var intent = new PackageLeaseRecord(1, package, boot, owner, game, PackageLeasePhase.Acquiring);
        var acquired = intent with { Phase = PackageLeasePhase.Acquired };
        Write(journal, intent);
        Invoke(() => backend.Enable(package), "enable");
        try { Write(journal, acquired); }
        catch
        {
            // The reference is ours, but recovery must never repeat its decrement. Publish the
            // releasing state before attempting compensation, just as for ordinary disposal.
            try { Release(journal, acquired, backend); }
            catch (Exception error) { log("Package lifetime: retained unresolved acquisition: " + error.Message); }
            throw;
        }
        log("Package lifetime: acquired one Cuphead reference.");

        return acquired;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        using (_lock)
        {
            try
            {
                if (_processState(_acquired.Game) != LeaseProcessState.Gone)
                {
                    _log("Package lifetime: kept the confirmed reference because the exact Cuphead exit could not be proved.");
                    return;
                }
                Release(_journal, _acquired, _backend);
                _log("Package lifetime: released one Cuphead reference.");
            }
            catch (Exception error) { _log("Package lifetime: retained unresolved release: " + error.Message); }
        }
    }

    private static void Release(string journal, PackageLeaseRecord acquired, IPackageDebugReference backend)
    {
        Write(journal, acquired with { Phase = PackageLeasePhase.Releasing });
        Invoke(() => backend.Disable(acquired.Package), "disable");
        File.Delete(journal);
    }

    private static void Invoke(Func<int> operation, string action)
    {
        // On timeout the outcome is unknown. The durable transition is intentionally left behind
        // so neither this process nor a later run attempts the COM operation a second time.
        int result = Task.Run(operation).WaitAsync(TimeSpan.FromSeconds(4)).GetAwaiter().GetResult();
        if (result != 0) throw new InvalidOperationException($"Windows package lifetime {action} returned 0x{result:X8}.");
    }

    private static PackageLeaseRecord? Read(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length is < 1 or > 4096) throw new InvalidDataException("The Cuphead lifetime record is invalid.");
            return JsonSerializer.Deserialize<PackageLeaseRecord>(file) ?? throw new InvalidDataException("The Cuphead lifetime record is missing.");
        }
        catch (FileNotFoundException) { return null; }
    }

    private static void Write(string path, PackageLeaseRecord entry)
    {
        string pending = Path.Combine(Path.GetDirectoryName(path)!, Path.GetRandomFileName());
        try
        {
            using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, entry);
                file.Flush(flushToDisk: true);
            }
            if (!MoveFileEx(pending, path, 0x01 | 0x08))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The Cuphead lifetime record could not be committed.");
        }
        finally { File.Delete(pending); }
    }

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string destination, uint flags);
}
