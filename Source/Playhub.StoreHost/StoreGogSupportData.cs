// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Security.Cryptography;
using System.Text.Json;

namespace Playhub.StoreLibrary;

// Interpret data-copy instructions, never execute goggame scripts or commands.
// The retained CDN manifest authorizes every script/source byte. Destination
// ownership and the private receipt delimit both installation and removal.
internal static class StoreGogSupportData
{
    private sealed record Copy(string Source, string Target, bool Overwrite);
    private sealed record Receipt(int Version, string ProductId, string InstallPath, Guid OwnerJob,
        string ScriptHash, IReadOnlyList<Copy> Copies, IReadOnlyList<string> Directories);
    private const int MaxFiles = 8192;
    private const long MaxBytes = 2L * 1024 * 1024 * 1024;

    internal static void Apply(string receiptPath, string root, string id, Guid ownerJob, string language,
        bool scriptRequired, IReadOnlyList<StoreOwnedInstallFile> manifest, CancellationToken token)
    {
        root = new StoreInstallFileSystem().CanonicalDestination(root);
        using var pins = StoreOwnedInstallFiles.PinRoot(root);
        using var owner = OwnedRoot(root, ownerJob);
        var files = FileMap(root, manifest);
        var scriptName = "goggame-" + id + ".script";
        var scriptPath = StoreOwnedInstallFiles.PathFor(root, scriptName);
        if (!files.TryGetValue(scriptName, out var script))
        {
            if (scriptRequired || File.Exists(scriptPath) || File.Exists(receiptPath)) throw new StoreOperationException("gog_support_script_missing");
            return;
        }
        if (script.Size is < 1 or > 1048576) throw new StoreOperationException("gog_support_script_invalid");
        using var scriptFile = StoreOwnedInstallFiles.OpenReadFile(scriptPath);
        StoreOwnedInstallFiles.VerifyContents(scriptFile, script, token);
        scriptFile.Position = 0; var scriptHash = Convert.ToHexString(SHA256.HashData(scriptFile)); scriptFile.Position = 0;
        using var document = JsonDocument.Parse(scriptFile);
        var data = document.RootElement;
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("productId", out var product) || product.ToString() != id ||
            !data.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array || actions.GetArrayLength() > 512)
            throw new StoreOperationException("gog_support_script_invalid");

        var copies = new List<Copy>(); var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long copiedBytes = 0;
        foreach (var action in actions.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            if (action.ValueKind != JsonValueKind.Object) throw new StoreOperationException("gog_support_script_invalid");
            if (action.TryGetProperty("languages", out var languages))
            {
                if (languages.ValueKind != JsonValueKind.Array || languages.GetArrayLength() > 256) throw new StoreOperationException("gog_support_script_invalid");
                if (!languages.EnumerateArray().Any(l => l.ValueKind == JsonValueKind.String && (l.GetString() is "*" or "Neutral" || l.GetString() == language))) continue;
            }
            if (!action.TryGetProperty("install", out var install)) continue; // uninstall-only actions are not installation work
            if (install.ValueKind != JsonValueKind.Object || !install.TryGetProperty("action", out var kind) || kind.ValueKind != JsonValueKind.String ||
                !install.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.Object) throw new StoreOperationException("gog_support_script_invalid");
            if (kind.GetString() != "supportData")
            {
                // These two legacy GOG-DOSBox locator values are optional launcher
                // integration. No registry is written, and arbitrary game registry
                // prerequisites/Execute/unknown actions fail closed.
                if (OptionalLegacyLocator(kind.GetString(), arguments, id)) continue;
                throw new StoreOperationException("gog_support_action_unsupported");
            }
            var type = Text(arguments, "type");
            var target = Expand(Text(arguments, "target"), "{app}", "", root, allowRoot: type == "folder");
            if (type is not ("file" or "folder")) throw new StoreOperationException("gog_support_action_unsupported");
            if (arguments.TryGetProperty("overwrite", out var overwrite) && overwrite.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new StoreOperationException("gog_support_script_invalid");
            if (type == "folder" && target.Length != 0) directories.Add(target);
            if (!arguments.TryGetProperty("source", out var sourceValue))
            {
                if (type != "folder") throw new StoreOperationException("gog_support_script_invalid");
                continue;
            }
            if (sourceValue.ValueKind != JsonValueKind.String) throw new StoreOperationException("gog_support_script_invalid");
            var source = Expand(sourceValue.GetString()!, "{supportDir}", "gog-support/" + id, root, allowRoot: true);
            var selected = type == "file" ? files.Values.Where(f => Equal(f.RelativePath, source)).ToArray() :
                files.Values.Where(f => f.RelativePath.StartsWith(source + "/", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (selected.Length == 0) throw new StoreOperationException("gog_support_source_missing");
            foreach (var entry in selected)
            {
                var destination = type == "file" ? target : Join(target, entry.RelativePath[(source.Length + 1)..]);
                _ = Destination(root, destination);
                if (!targets.Add(destination) || copies.Count >= MaxFiles || copiedBytes > MaxBytes - entry.Size) throw new StoreOperationException("gog_support_script_invalid");
                copiedBytes += entry.Size; copies.Add(new(entry.RelativePath, destination, overwrite.ValueKind == JsonValueKind.True));
            }
        }
        var expected = new Receipt(1, id, root, ownerJob, scriptHash, copies.OrderBy(c => c.Target, StringComparer.Ordinal).ToArray(), directories.Order(StringComparer.Ordinal).ToArray());
        var previous = ReadReceipt(receiptPath, root, id, ownerJob);
        if (previous != null && JsonSerializer.Serialize(previous) != JsonSerializer.Serialize(expected)) throw new StoreOperationException("gog_support_receipt_changed");
        // Validate every source and pre-existing destination before the first copy.
        foreach (var copy in expected.Copies)
        {
            PinParents(root, copy.Source, pins, false);
            using var sourceFile = StoreOwnedInstallFiles.OpenReadFile(StoreOwnedInstallFiles.PathFor(root, copy.Source));
            StoreOwnedInstallFiles.VerifyContents(sourceFile, files[copy.Source], token);
            var destination = Destination(root, copy.Target);
            if (!File.Exists(destination)) continue;
            PinParents(root, copy.Target, pins, false);
            using var current = StoreOwnedInstallFiles.OpenReadFile(destination);
            var wasOwned = previous?.Copies.Any(c => Equal(c.Source, copy.Source) && Equal(c.Target, copy.Target)) == true;
            VerifyTarget(current, copy, files, wasOwned, token);
        }
        // Persist the authorized recipe first, so interruption after a copied file
        // cannot orphan its ownership or turn the next retry into foreign adoption.
        WriteReceipt(receiptPath, expected);
        foreach (var directory in expected.Directories) PinParents(root, directory + "/placeholder", pins, true);
        foreach (var copy in expected.Copies)
        {
            token.ThrowIfCancellationRequested(); PinParents(root, copy.Target, pins, true);
            var destination = Destination(root, copy.Target);
            using var sourceFile = StoreOwnedInstallFiles.OpenReadFile(StoreOwnedInstallFiles.PathFor(root, copy.Source));
            StoreOwnedInstallFiles.VerifyContents(sourceFile, files[copy.Source], token); sourceFile.Position = 0;
            if (File.Exists(destination))
            {
                using var current = StoreOwnedInstallFiles.OpenReadFile(destination);
                try { StoreOwnedInstallFiles.VerifyContents(current, files[copy.Source], token); continue; }
                catch (StoreOperationException e) when (e.Code == "installed_files_modified") { }
            }
            var temporary = destination + ".playhub-support-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { sourceFile.CopyTo(output); output.Flush(true); }
                token.ThrowIfCancellationRequested();
                // Only the preflight-authorized downloaded/previously-owned target
                // may be replaced; an unrecognized file appearing meanwhile vetoes.
                if (File.Exists(destination))
                {
                    using var retained = StoreOwnedInstallFiles.OpenWriteFile(destination);
                    VerifyTarget(retained, copy, files, previous != null, token);
                    sourceFile.Position = 0; retained.Position = 0; sourceFile.CopyTo(retained); retained.SetLength(sourceFile.Length); retained.Flush(true);
                }
                else File.Move(temporary, destination, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        foreach (var copy in expected.Copies)
        {
            using var copied = StoreOwnedInstallFiles.OpenReadFile(Destination(root, copy.Target));
            StoreOwnedInstallFiles.VerifyContents(copied, files[copy.Source], token);
        }
    }
    private static void VerifyTarget(Stream stream, Copy copy, IReadOnlyDictionary<string, StoreOwnedInstallFile> files, bool wasOwned, CancellationToken token)
    {
        if (wasOwned)
        {
            try { StoreOwnedInstallFiles.VerifyContents(stream, files[copy.Source], token); return; }
            catch (StoreOperationException e) when (e.Code == "installed_files_modified") { }
        }
        var original = files.GetValueOrDefault(copy.Target) ?? throw new StoreOperationException("gog_support_destination_conflict");
        StoreOwnedInstallFiles.VerifyContents(stream, original, token);
        if (!copy.Overwrite) StoreOwnedInstallFiles.VerifyContents(stream, files[copy.Source], token);
    }

    internal static IReadOnlyList<StoreOwnedInstallFile> ReadOwnedFiles(string receiptPath, string root, string id, Guid ownerJob, IReadOnlyList<StoreOwnedInstallFile> manifest)
    {
        var receipt = ReadReceipt(receiptPath, root, id, ownerJob); if (receipt == null) return [];
        var files = FileMap(root, manifest); var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var result = new List<StoreOwnedInstallFile>();
        foreach (var copy in receipt.Copies)
        {
            _ = Destination(root, copy.Target);
            if (!copy.Source.StartsWith("gog-support/" + id + "/", StringComparison.OrdinalIgnoreCase) || !files.TryGetValue(copy.Source, out var source) || !targets.Add(copy.Target)) throw new StoreOperationException("gog_support_receipt_invalid");
            result.Add(source with { RelativePath = copy.Target });
        }
        return result;
    }
    internal static IReadOnlyList<StoreOwnedInstallFile> MergeFiles(IReadOnlyList<StoreOwnedInstallFile> manifest, IReadOnlyList<StoreOwnedInstallFile> copied)
    {
        var replaced = copied.Select(f => Normalize(f.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return manifest.Where(f => !replaced.Contains(Normalize(f.RelativePath))).Concat(copied).ToArray();
    }
    internal static void RemoveEmptyOwnedDirectories(string receiptPath, string root, string id, Guid ownerJob)
    {
        var receipt = ReadReceipt(receiptPath, root, id, ownerJob); if (receipt == null) return;
        using var pins = StoreOwnedInstallFiles.PinRoot(root);
        foreach (var relative in receipt.Directories.OrderByDescending(d => d.Length))
        {
            var path = Destination(root, relative);
            _ = new StoreInstallFileSystem().CanonicalDestination(path);
            try { Directory.Delete(path, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    private static FileStream OwnedRoot(string root, Guid ownerJob)
    {
        if (ownerJob == Guid.Empty) throw new StoreOperationException("destination_ownership_lost");
        var owner = StoreOwnedInstallFiles.OpenReadFile(Path.Combine(root, ".playhub-install-owner"));
        using var reader = new StreamReader(owner, leaveOpen: true);
        if (owner.Length > 64 || reader.ReadToEnd() != ownerJob.ToString("D")) { owner.Dispose(); throw new StoreOperationException("destination_ownership_lost"); }
        return owner;
    }
    private static Dictionary<string, StoreOwnedInstallFile> FileMap(string root, IReadOnlyList<StoreOwnedInstallFile> manifest)
    {
        if (manifest.Count is < 1 or > 100000) throw new StoreOperationException("gog_support_manifest_invalid");
        var result = new Dictionary<string, StoreOwnedInstallFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest)
        {
            var name = Normalize(file.RelativePath); _ = StoreOwnedInstallFiles.PathFor(root, name); StoreOwnedInstallFiles.ValidateChecksum(file);
            if (file.HashAlgorithm == null && file.Chunks == null || !result.TryAdd(name, file with { RelativePath = name })) throw new StoreOperationException("gog_support_manifest_invalid");
        }
        return result;
    }
    private static Receipt? ReadReceipt(string path, string root, string id, Guid ownerJob)
    {
        if (!File.Exists(path)) return null;
        using var stream = StoreOwnedInstallFiles.OpenReadFile(path);
        if (stream.Length > 4 * 1024 * 1024) throw new StoreOperationException("gog_support_receipt_invalid");
        var receipt = JsonSerializer.Deserialize<Receipt>(stream) ?? throw new StoreOperationException("gog_support_receipt_invalid");
        if (receipt.Version != 1 || receipt.ProductId != id || receipt.OwnerJob != ownerJob || receipt.InstallPath == null || !Equal(receipt.InstallPath, root) || receipt.ScriptHash == null || receipt.ScriptHash.Length != 64 || !receipt.ScriptHash.All(Uri.IsHexDigit) ||
            receipt.Copies == null || receipt.Directories == null || receipt.Copies.Count > MaxFiles || receipt.Directories.Count > MaxFiles) throw new StoreOperationException("gog_support_receipt_invalid");
        return receipt;
    }
    private static void WriteReceipt(string path, Receipt receipt)
    {
        var temporary = path + ".new-" + Guid.NewGuid().ToString("N");
        try { using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, receipt); stream.Flush(true); } File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void PinParents(string root, string relative, StoreOwnedInstallFiles.DirectoryPins pins, bool create)
    {
        var path = StoreOwnedInstallFiles.PathFor(root, relative); var parts = Path.GetRelativePath(root, Path.GetDirectoryName(path)!).Split(Path.DirectorySeparatorChar);
        var current = root;
        foreach (var part in parts)
        {
            if (part == ".") continue; current = Path.Combine(current, part);
            if (create && !Directory.Exists(current)) Directory.CreateDirectory(current);
            pins.Add(current);
        }
    }
    private static bool OptionalLegacyLocator(string? kind, JsonElement args, string id) => kind == "setRegistry" &&
        args.TryGetProperty("root", out var root) && root.GetString() == "HKEY_LOCAL_MACHINE" &&
        args.TryGetProperty("subkey", out var subkey) && (subkey.GetString() == "Software\\GOG.com\\Games\\{productID}" || subkey.GetString() == "Software\\GOG.com\\Games\\" + id) &&
        args.TryGetProperty("valueName", out var value) && value.GetString() is "DOSBOXFOLDER" or "DOSBOXCONF";
    private static string Text(JsonElement data, string name) => data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new StoreOperationException("gog_support_script_invalid");
    private static string Expand(string value, string macro, string prefix, string root, bool allowRoot)
    {
        value = Normalize(value);
        if (value != macro && !value.StartsWith(macro + "/", StringComparison.Ordinal)) throw new StoreOperationException("gog_support_path_invalid");
        var suffix = value.Length == macro.Length ? "" : value[(macro.Length + 1)..];
        var relative = Join(prefix, suffix);
        if (relative.Length == 0) { if (allowRoot) return ""; throw new StoreOperationException("gog_support_path_invalid"); }
        _ = StoreOwnedInstallFiles.PathFor(root, relative); return relative;
    }
    private static string Destination(string root, string target)
    {
        if (target.Split('/','\\').Any(p => p.StartsWith(".playhub", StringComparison.OrdinalIgnoreCase) || p.Equals("gog-support", StringComparison.OrdinalIgnoreCase) || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || ReservedName(p)) || target.StartsWith("goggame-", StringComparison.OrdinalIgnoreCase)) throw new StoreOperationException("gog_support_path_invalid");
        return StoreOwnedInstallFiles.PathFor(root, target);
    }
    private static bool ReservedName(string part)
    {
        var name = part.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) || name.Equals("PRN", StringComparison.OrdinalIgnoreCase) || name.Equals("AUX", StringComparison.OrdinalIgnoreCase) || name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && name[3] is >= '1' and <= '9';
    }
    private static string Normalize(string value) => value.Replace('\\', '/');
    private static string Join(string first, string second) => first.Length == 0 ? second : second.Length == 0 ? first : first + "/" + second;
    private static bool Equal(string first, string second) => StringComparer.OrdinalIgnoreCase.Equals(Normalize(first), Normalize(second));
}
