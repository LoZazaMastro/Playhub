using System.Security.Cryptography;
using System.Text.Json;

namespace Playhub.StandaloneHost.Migration;

/// <summary>Only reads sources inside an explicitly selected isolated workspace.
/// It never moves, deletes or overwrites those sources. All writes go to new
/// .migration-snapshots or .migration-journal children of that workspace.</summary>
public sealed class MigrationStorage
{
    private const int MaxFiles = 10000;
    private const long MaxFileBytes = 128L * 1024 * 1024;
    private const long MaxTotalBytes = 512L * 1024 * 1024;
    private readonly string root;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record Manifest(string Product, int Schema, MigrationPlan Plan);
    public MigrationStorage(string isolatedWorkspace)
    {
        root = Path.GetFullPath(isolatedWorkspace).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(root, Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A drive root is not an isolated migration workspace.");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The isolated workspace must already exist.");
        CheckAncestors(root);
    }

    public MigrationPlan Plan(IEnumerable<MigrationSource> sources, string candidateInstanceId)
    {
        if (string.IsNullOrWhiteSpace(candidateInstanceId) || candidateInstanceId.Length > 128)
            throw new ArgumentException("An expected candidate instance identity is required.");
        var selected = sources.OrderBy(s => s.Name, StringComparer.Ordinal).ToArray();
        if (selected.Length is < 1 or > 12 || selected.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() != selected.Length)
            throw new ArgumentException("Use one to twelve distinct source names.");
        var directories = new List<string>();
        foreach (var source in selected)
        {
            if (string.IsNullOrWhiteSpace(source.Name) || source.Name.Length > 64) throw new ArgumentException("Invalid source name.");
            var path = Resolve(source.RelativeDirectory);
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("A selected source is missing.");
            if (Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)[0].StartsWith(".migration-", StringComparison.Ordinal))
                throw new ArgumentException("Migration output cannot be a source.");
            if (directories.Any(other => IsWithin(path, other) || IsWithin(other, path)))
                throw new ArgumentException("Source directories must not overlap.");
            directories.Add(path);
        }
        var files = new List<MigrationFile>();
        long total = 0;
        foreach (var directory in directories)
        {
            foreach (var file in Walk(directory))
            {
                using var stream = OpenSource(file);
                if (stream.Length > MaxFileBytes || (total += stream.Length) > MaxTotalBytes || files.Count >= MaxFiles)
                    throw new IOException("The bounded migration snapshot limit was exceeded.");
                files.Add(new(Normalize(Path.GetRelativePath(root, file)), stream.Length, Hash(stream)));
            }
        }
        var ordered = files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray();
        var normalized = selected.Select(s => s with { RelativeDirectory = Normalize(Path.GetRelativePath(root, Resolve(s.RelativeDirectory))) }).ToArray();
        return new(Fingerprint(candidateInstanceId, normalized, ordered), candidateInstanceId, normalized, ordered);
    }

    public void VerifyCurrent(MigrationPlan plan)
    {
        if (plan.Id != Fingerprint(plan.CandidateInstanceId, plan.Sources, plan.Files))
            throw new InvalidDataException("The migration plan was modified.");
        if (Plan(plan.Sources, plan.CandidateInstanceId).Id != plan.Id)
            throw new IOException("Source data changed after planning. Create a fresh plan.");
    }

    public SnapshotReceipt CreateSnapshot(MigrationPlan plan)
    {
        VerifyCurrent(plan);
        string relative = ".migration-snapshots/" + Guid.NewGuid().ToString("N");
        EnsureDirectory(relative);
        foreach (var file in plan.Files)
        {
            using var source = OpenSource(Resolve(file.RelativePath));
            if (source.Length != file.Bytes || Hash(source) != file.Sha256)
                throw new IOException("A source changed while preparing its snapshot.");
            source.Position = 0;
            var destination = relative + "/files/" + file.RelativePath;
            EnsureDirectory(Normalize(Path.GetDirectoryName(destination)!));
            using var output = new FileStream(Resolve(destination), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(output);
            output.Flush(flushToDisk: true);
        }
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new Manifest("PlayhubStandaloneMigrationSnapshot", 1, plan), Json);
        WriteNew(relative + "/manifest.json", manifest);
        var receipt = new SnapshotReceipt(relative, Hash(manifest), plan.Files.Count);
        VerifySnapshot(receipt);
        VerifyCurrent(plan);
        return receipt;
    }

    public void VerifySnapshot(SnapshotReceipt receipt)
    {
        ValidateReceiptDirectory(receipt.RelativeDirectory);
        byte[] bytes = File.ReadAllBytes(Resolve(receipt.RelativeDirectory + "/manifest.json"));
        if (Hash(bytes) != receipt.ManifestSha256) throw new InvalidDataException("Snapshot manifest changed.");
        var manifest = JsonSerializer.Deserialize<Manifest>(bytes) ?? throw new InvalidDataException("Snapshot manifest missing.");
        if (manifest.Product != "PlayhubStandaloneMigrationSnapshot" || manifest.Schema != 1 || manifest.Plan.Files.Count != receipt.FileCount
            || manifest.Plan.Id != Fingerprint(manifest.Plan.CandidateInstanceId, manifest.Plan.Sources, manifest.Plan.Files))
            throw new InvalidDataException("Snapshot identity is invalid.");
        foreach (var file in manifest.Plan.Files)
        {
            // Resolve the original too: a tampered relative path cannot escape the workspace.
            _ = Resolve(file.RelativePath);
            using var stream = OpenSource(Resolve(receipt.RelativeDirectory + "/files/" + file.RelativePath));
            if (stream.Length != file.Bytes || Hash(stream) != file.Sha256)
                throw new InvalidDataException("Snapshot file differs from its manifest.");
        }
    }

    public string CreateJournal(string transactionId)
    {
        if (!Guid.TryParseExact(transactionId, "N", out _)) throw new ArgumentException("Invalid transaction identity.");
        string path = ".migration-journal/" + transactionId;
        EnsureDirectory(path);
        return path;
    }

    public void AppendJournal(string relativeDirectory, int step, string state, HandoverContext context, SnapshotReceipt? snapshot)
    {
        if (!relativeDirectory.Equals(".migration-journal/" + context.TransactionId, StringComparison.Ordinal))
            throw new ArgumentException("Journal identity differs from the transaction.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, context, step, state, snapshot, recordedUtc = DateTimeOffset.UtcNow }, Json);
        WriteNew(relativeDirectory + "/" + step.ToString("D3") + ".json", bytes);
    }

    private IEnumerable<string> Walk(string start)
    {
        int entries = 0;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((start, 0));
        while (pending.TryPop(out var current))
        {
            CheckAncestors(current.Path);
            if (current.Depth > 32) throw new IOException("Source directory nesting limit exceeded.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(current.Path).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (++entries > 30000) throw new IOException("Source entry count limit exceeded.");
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked source entries are not supported.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push((entry, current.Depth + 1));
                else yield return entry;
            }
        }
    }

    private string Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Contains('\0'))
            throw new ArgumentException("A relative workspace path is required.");
        var segments = relative.Replace('\\', '/').Split('/');
        if (segments.Any(s => s is "" or "." or ".." || s.EndsWith(' ') || s.EndsWith('.')))
            throw new ArgumentException("Invalid relative path.");
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithin(full, root) || full.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The path must stay below the isolated workspace.");
        CheckAncestors(full);
        return full;
    }

    private void EnsureDirectory(string relative)
    {
        string path = Resolve(relative);
        Directory.CreateDirectory(path);
        CheckAncestors(path);
    }

    private void WriteNew(string relative, byte[] bytes)
    {
        string path = Resolve(relative);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private FileStream OpenSource(string path)
    {
        CheckAncestors(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static void CheckAncestors(string path)
    {
        for (string? current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Junctions and symbolic links are not supported.");
    }

    private static void ValidateReceiptDirectory(string path)
    {
        var parts = path.Split('/');
        if (parts.Length != 2 || parts[0] != ".migration-snapshots" || !Guid.TryParseExact(parts[1], "N", out _))
            throw new ArgumentException("Invalid snapshot directory.");
    }
    private static bool IsWithin(string child, string parent) => child.Equals(parent, StringComparison.OrdinalIgnoreCase)
        || child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string path) => path.Replace('\\', '/');
    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Fingerprint(string instance, IReadOnlyList<MigrationSource> sources, IReadOnlyList<MigrationFile> files)
        => Hash(JsonSerializer.SerializeToUtf8Bytes(new { candidateInstanceId = instance, sources, files }));
}
