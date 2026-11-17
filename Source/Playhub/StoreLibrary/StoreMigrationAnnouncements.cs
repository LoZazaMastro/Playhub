// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Text.Json;

namespace Playhub.StoreLibrary;

/// <summary>Presentation receipts only. Never authorizes or completes a Steam migration.</summary>
internal sealed class StoreMigrationAnnouncements(string path)
{
    private readonly List<Guid> announced = Read(path);
    public bool Contains(Guid requestId) => announced.Contains(requestId);
    public void Remember(Guid requestId)
    {
        if (requestId == Guid.Empty || Contains(requestId)) return;
        announced.Add(requestId);
        while (announced.Count > 64) announced.RemoveAt(0);
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(announced));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        finally { if (temporary is not null) try { File.Delete(temporary); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }
    private static List<Guid> Read(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 8192) return [];
            return (JsonSerializer.Deserialize<Guid[]>(File.ReadAllText(path)) ?? []).Where(id => id != Guid.Empty).Distinct().TakeLast(64).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
}
