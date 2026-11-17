// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Playhub.StoreLibrary;

public sealed partial class StoreAmazonProvider
{
    private sealed record LaunchDefinition(string Executable, string Directory, string[] Arguments, Dictionary<string, string> Environment);

    public Task<ProviderLaunch?> ResolveLaunchAsync(string id, string installPath, CancellationToken token)
    {
        StoreCliProvider.ValidateId(id); token.ThrowIfCancellationRequested();
        if (!gate.TryWait()) return Task.FromResult<ProviderLaunch?>(null);
        try
        {
            using var runtime = PinRuntime();
            var launch = ReadLaunch(id, installPath, token);
            return Task.FromResult<ProviderLaunch?>(new(host, Path.GetDirectoryName(host)!, ["--launch", "amazon", id], launch.Executable));
        }
        catch (Exception e) when (LaunchReadFailure(e)) { return Task.FromResult<ProviderLaunch?>(null); }
        finally { gate.Release(); }
    }

    public Task<StorePrivateLaunchPlan> PrepareLaunchAsync(string id, string installPath, CancellationToken token)
    {
        StoreCliProvider.ValidateId(id); token.ThrowIfCancellationRequested();
        if (!gate.TryWait()) throw new StoreOperationException("provider_busy");
        try
        {
            using var runtime = PinRuntime();
            var launch = ReadLaunch(id, installPath, token);
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new StorePrivateLaunchPlan(Guid.NewGuid(), new(StoreKind.Amazon, id), launch.Executable,
                launch.Directory, launch.Arguments, launch.Environment, DateTimeOffset.UtcNow.AddSeconds(30)));
        }
        catch (StoreOperationException) { throw; }
        catch (Exception e) when (LaunchReadFailure(e)) { throw new StoreOperationException("amazon_launch_unverified"); }
        finally { gate.Release(); }
    }

    // Compatibility entry for the public descriptor. Normal Steam launches use
    // GameSession's private plan so its usual lifetime/input/curtain path is kept.
    public async Task<int> LaunchAsync(string id, CancellationToken token)
    {
        StoreCliProvider.ValidateId(id);
        if (!gate.TryWait()) throw new StoreOperationException("provider_busy");
        string? path;
        try { path = InstalledPath(id); } finally { gate.Release(); }
        if (path is null) throw new StoreOperationException("installation_unverified");
        var plan = await PrepareLaunchAsync(id, path, token);
        token.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(plan.GameExecutable) { UseShellExecute = false, WorkingDirectory = plan.WorkingDirectory };
        foreach (var arg in plan.Arguments) info.ArgumentList.Add(arg);
        foreach (var pair in plan.Environment) info.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(info) ?? throw new StoreOperationException("game_start_failed");
        await process.WaitForExitAsync(token); return process.ExitCode;
    }

    private void BindLaunchLibrary(string json)
    {
        var account = Account(JsonNode.Parse(StoreCredentialVault.Read(vault))!);
        var binding = new { accountIdentity = account.AccountIdentity, librarySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))) };
        var file = Path.Combine(native, "library-account.json");
        File.WriteAllText(file + ".new", JsonSerializer.Serialize(binding)); File.Move(file + ".new", file, true);
    }

    private LaunchDefinition ReadLaunch(string id, string installPath, CancellationToken token, bool verifyExecutable = true)
    {
        using var library = OpenLaunchFile(AmazonManifest.Contained(native, "library.json"), 16 * 1024 * 1024);
        using var binding = OpenLaunchFile(AmazonManifest.Contained(native, "library-account.json"), 4096);
        using var credentials = OpenLaunchFile(vault, 1024 * 1024);
        var account = Account(JsonNode.Parse(StoreCredentialVault.Read(vault))!);
        var libraryBytes = ReadLaunchFile(library);
        using var owner = ParseLaunchJson(ReadLaunchFile(binding));
        if (owner.RootElement.GetProperty("accountIdentity").GetString() != account.AccountIdentity ||
            owner.RootElement.GetProperty("librarySha256").GetString() != Convert.ToHexString(SHA256.HashData(libraryBytes)))
            throw new StoreOperationException("amazon_launch_account_changed");
        using var catalog = ParseLaunchJson(libraryBytes);
        var owned = catalog.RootElement.EnumerateArray().Where(g => g.GetProperty("product").GetProperty("id").GetString() == id).ToArray();
        if (owned.Length != 1) throw new StoreOperationException("game_not_owned");
        var entitlement = LaunchText(owned[0].GetProperty("id").GetString());
        var sku = LaunchText(owned[0].GetProperty("product").GetProperty("sku").GetString());
        using var installed = OpenLaunchFile(AmazonManifest.Contained(native, "installed.json"), 4 * 1024 * 1024);
        using var records = ParseLaunchJson(ReadLaunchFile(installed));
        var matches = records.RootElement.EnumerateArray().Where(g => g.GetProperty("id").GetString() is { } record && (record == id || record == entitlement)).ToArray();
        if (matches.Length != 1) throw new StoreOperationException("installed_metadata_invalid");
        var directory = matches[0].GetProperty("path").GetString();
        _ = LaunchText(matches[0].GetProperty("version").GetString());
        if (directory == null || !Path.IsPathFullyQualified(directory) || !Path.IsPathFullyQualified(installPath) ||
            !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(directory), Path.GetFullPath(installPath))) throw new StoreOperationException("installed_path_conflict");
        directory = Path.GetFullPath(directory);
        using var raw = OpenLaunchFile(AmazonManifest.Contained(native, "manifests/" + id + ".raw"), AmazonManifest.MaximumBytes);
        var manifest = parseManifest(ReadLaunchFile(raw));
        var fuels = manifest.Files.Where(f => f.RelativePath.Equals("fuel.json", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (fuels.Length != 1) throw new StoreOperationException("amazon_fuel_unverified");
        using var fuel = OpenLaunchFile(AmazonManifest.Contained(directory, fuels[0].RelativePath), AmazonLaunchMetadata.MaximumBytes);
        VerifyLaunchFile(fuel, fuels[0]);
        var metadata = AmazonLaunchMetadata.Parse(ReadLaunchFile(fuel));
        // Old installations are compatible only after the same actual metadata
        // and current runtime postconditions verify; never trust an old receipt
        // or run a setup program implicitly during launch/status reads.
        if (verifyExecutable) VerifyPostInstall(id, directory, manifest, token);
        var images = manifest.Files.Where(f => f.RelativePath.Equals(metadata.Executable, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (images.Length != 1) throw new StoreOperationException("amazon_launch_image_unverified");
        var image = AmazonManifest.Contained(directory, images[0].RelativePath);
        using var executable = OpenLaunchFile(image, long.MaxValue);
        if (executable.Length != images[0].Size) throw new StoreOperationException("amazon_launch_image_unverified");
        if (verifyExecutable) VerifyLaunchFile(executable, images[0]);
        // Signed manifest, fuel and exact image authorize this launch. Whole-tree
        // integrity belongs to installation verification and explicit removal,
        // not every library/status refresh or a game's user-modified assets.
        var working = string.IsNullOrEmpty(metadata.WorkingDirectory) || metadata.WorkingDirectory == "." ? directory : AmazonManifest.Contained(directory, metadata.WorkingDirectory);
        if (!Directory.Exists(working)) throw new StoreOperationException("launch_working_directory_invalid");
        // These are Nile's documented configuration paths, not an SDK-provisioned
        // or DRM-compatibility claim. No proprietary SDK is bundled/downloaded here.
        var sdk = AmazonManifest.Contained(native, "SDK/Amazon Games Services");
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FUEL_DIR"] = AmazonManifest.Contained(sdk, "Legacy"),
            ["AMAZON_GAMES_SDK_PATH"] = AmazonManifest.Contained(sdk, "AmazonGamesSDK"),
            ["AMAZON_GAMES_FUEL_ENTITLEMENT_ID"] = entitlement,
            ["AMAZON_GAMES_FUEL_PRODUCT_SKU"] = sku,
            ["AMAZON_GAMES_FUEL_DISPLAY_NAME"] = LaunchText(account.DisplayName ?? "", allowEmpty: true)
        };
        _ = AmazonManifest.Contained(directory, images[0].RelativePath); // Final path/reparse checks under all retained metadata/image handles.
        _ = AmazonManifest.Contained(directory, fuels[0].RelativePath);
        token.ThrowIfCancellationRequested();
        return new(image, working, metadata.Arguments, env);
    }
    private static string LaunchText(string? value, bool allowEmpty = false)
    {
        if (value is null || (!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Length > 2048 || value.IndexOfAny(['\0', '\r', '\n']) >= 0) throw new StoreOperationException("amazon_launch_metadata_invalid");
        return value;
    }
    private static bool LaunchReadFailure(Exception error) => error is IOException or UnauthorizedAccessException or StoreOperationException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException;
    private static FileStream OpenLaunchFile(string path, long maximum)
    {
        for (var p = path; p != null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new StoreOperationException("launch_path_linked");
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > maximum) { file.Dispose(); throw new StoreOperationException("amazon_launch_metadata_invalid"); }
        return file;
    }
    private static byte[] ReadLaunchFile(FileStream stream)
    {
        stream.Position = 0; var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
    private static void VerifyLaunchFile(FileStream stream, AmazonFile file)
    {
        stream.Position = 0;
        var actual = file.Algorithm == 0 ? SHA256.HashData(stream) : Shake128.HashData(stream, 32);
        if (stream.Length != file.Size || !CryptographicOperations.FixedTimeEquals(actual, file.Hash)) throw new StoreOperationException("amazon_launch_image_unverified");
    }
    private static JsonDocument ParseLaunchJson(byte[] bytes)
    {
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24 });
        void Check(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var p in value.EnumerateObject()) { if (!names.Add(p.Name)) throw new StoreOperationException("amazon_launch_metadata_invalid"); Check(p.Value); } }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Check(item);
        }
        try { Check(document.RootElement); return document; } catch { document.Dispose(); throw; }
    }
}
