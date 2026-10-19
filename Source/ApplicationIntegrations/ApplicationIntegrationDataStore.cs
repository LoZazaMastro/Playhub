using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Playhub.Integrations;

public static class ApplicationIntegrationIdentity
{
    public static string Create(string scope, string sourceId)
    {
        if (scope != "pc" || string.IsNullOrWhiteSpace(sourceId) || sourceId.Length > 2000 || sourceId.Any(char.IsControl))
            throw new ArgumentException("A stable PC game source identity is required.");
        return scope + ":" + sourceId;
    }
    public static string Validate(string identity)
    {
        int separator = identity?.IndexOf(':') ?? -1;
        if (separator <= 0) throw new ArgumentException("A stable source identity is required.");
        return Create(identity![..separator], identity[(separator + 1)..]);
    }
}

/// <summary>Stores artwork choices for exact imported PC game identities.</summary>
public sealed class ApplicationIntegrationDataStore
{
    private readonly string _root;
    private const int MaxBytes = 4 * 1024 * 1024;
    public ApplicationIntegrationDataStore(string root) { _root = Path.GetFullPath(root); }
    public static string Key(string identity) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ApplicationIntegrationIdentity.Validate(identity)))).ToLowerInvariant();
    private string Draft(string identity) => Path.Combine(_root, "games", Key(identity) + ".json");

    public async Task<JsonObject> ReadAsync(string identity, CancellationToken ct = default)
    {
        ApplicationIntegrationIdentity.Validate(identity);
        var value = await ReadFileAsync(Draft(identity), ct);
        if (value.Count == 0) return new JsonObject { ["identity"] = identity, ["integrations"] = new JsonObject() };
        if (value["identity"]?.GetValue<string>() != identity) throw new InvalidDataException("Draft identity does not match its key.");
        return value;
    }

    public async Task<JsonObject> ReadCategoryAsync(string identity, string category, CancellationToken ct = default)
    {
        ValidateCategory(category);
        return (await ReadAsync(identity, ct))["integrations"]?[category]?.DeepClone() as JsonObject ?? new JsonObject();
    }

    public async Task<JsonObject> PatchAsync(string identity, string category, JsonObject fields, CancellationToken ct = default)
    {
        ValidateCategory(category);
        string path = Draft(identity);
        using var held = await LockAsync(path, ct);
        var draft = await ReadAsync(identity, ct);
        var integrations = draft["integrations"] as JsonObject ?? new JsonObject();
        if (draft["integrations"] is not JsonObject) draft["integrations"] = integrations;
        var current = integrations[category] as JsonObject;
        if (current is null) integrations[category] = current = new JsonObject();
        foreach (var field in fields) current[field.Key] = field.Value?.DeepClone();
        await WriteAsync(path, draft, ct);
        return (JsonObject)current.DeepClone();
    }

    private static void ValidateCategory(string category)
    {
        if (category != "artwork") throw new ArgumentException("Choose Artwork.");
    }
    private static async Task<JsonObject> ReadFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return new JsonObject();
        GuardPath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Integration data exceeds the limit.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, ct);
        return JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Integration data must be a JSON object.");
    }
    private static async Task<FileStream> LockAsync(string path, CancellationToken ct)
    {
        GuardPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(20, deadline.Token); }
        }
    }
    private static async Task WriteAsync(string path, JsonObject data, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(data.ToJsonString());
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Integration data exceeds the limit.");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            GuardPath(path);
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static void GuardPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Integration data cannot follow a redirected directory.");
    }
}
