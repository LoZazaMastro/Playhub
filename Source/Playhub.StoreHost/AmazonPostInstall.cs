// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Playhub.StoreLibrary;

internal sealed record AmazonSetupImage(bool MicrosoftSigned, string Description, Version Version);
internal sealed record AmazonSetupRuntime(string Name, string Architecture, Version Version, long Bytes, string Company);
internal interface IAmazonSetupProbe
{
    AmazonSetupImage InspectInstaller(string path);
    AmazonSetupRuntime? Runtime(string name, string architecture);
    int RunVerifiedInstaller(string path, IReadOnlyList<string> arguments, CancellationToken token) => throw new StoreOperationException("amazon_setup_execution_unavailable");
}

// Manifest hashes, owner and retained file handles are checked before any setup.
internal static class AmazonPostInstall
{
    private sealed record Step(string Command, string[] Args, int[] ValidReturns);
    private sealed record Artifact(string Path, long Bytes, int Algorithm, string Hash);
    private sealed record Proof(string Command, string Family, IReadOnlyList<AmazonSetupRuntime> Runtimes);
    private sealed record Receipt(int Version, DateTimeOffset VerifiedAt, string ProductId, string InstallPath, Guid OwnerJob, string ManifestHash,
        string? FuelHash, IReadOnlyList<Artifact> Artifacts, IReadOnlyList<Proof> Steps);
    private static readonly Regex VcDescription = new(@"Microsoft Visual C\+\+ (2010|2012|2013).*\b(x86|x64)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NativeCab = new(@"_(d3dx[a-z0-9_]*|d3dcompiler_[0-9]+|d3dcsx_[0-9]+|xinput|xaudio|x3daudio|xact)_(x86|x64)\.cab$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NativeDll = new(@"^(d3dx[a-z0-9_]*|d3dcompiler_[0-9]+|d3dcsx_[0-9]+|xinput[a-z0-9_]*|xaudio[a-z0-9_]*|x3daudio[a-z0-9_]*|xactengine[a-z0-9_]*|xapofx[a-z0-9_]*)\.dll$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static void Verify(string id, string root, Guid job, AmazonManifest manifest, string journal,
        CancellationToken token, IAmazonSetupProbe? probe = null)
    {
        probe ??= new AmazonWindowsSetupProbe();
        StoreCliProvider.ValidateId(id); root = new StoreInstallFileSystem().CanonicalDestination(root);
        using var directories = StoreOwnedInstallFiles.PinRoot(root);
        using var owner = StoreOwnedInstallFiles.OpenReadFile(Path.Combine(root, ".playhub-install-owner"));
        using var ownerReader = new StreamReader(owner, leaveOpen: true);
        if (job == Guid.Empty || owner.Length != 36 || ownerReader.ReadToEnd() != job.ToString("D")) throw Failure("ownership_lost");
        var artifacts = new List<Artifact>(); var proofs = new List<Proof>(); long checkedBytes = 0;
        var map = manifest.Files.ToDictionary(f => f.RelativePath.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        var fuels = map.Values.Where(f => f.RelativePath.Equals("fuel.json", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (fuels.Length > 1) throw Failure("metadata_invalid");
        byte[] ReadArtifact(string name, long maximum)
        {
            token.ThrowIfCancellationRequested(); name = name.Replace('\\', '/'); AmazonManifest.ValidateRelative(name);
            if (!map.TryGetValue(name, out var file) || file.Size < 0 || file.Size > maximum || checkedBytes > 256L * 1024 * 1024 - file.Size) throw Failure("file_unverified");
            var path = AmazonManifest.Contained(root, file.RelativePath); PinParents(root, path, directories);
            using var stream = StoreOwnedInstallFiles.OpenReadFile(path);
            if (stream.Length != file.Size) throw Failure("file_changed");
            var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
            var digest = file.Algorithm == 0 ? SHA256.HashData(bytes) : Shake128.HashData(bytes, 32);
            if (!CryptographicOperations.FixedTimeEquals(digest, file.Hash)) throw Failure("file_changed");
            checkedBytes += file.Size; artifacts.Add(new(name, file.Size, file.Algorithm, Convert.ToHexString(digest))); return bytes;
        }
        string? fuelHash = null;
        if (fuels.Length == 1)
        {
            var fuel = ReadArtifact(fuels[0].RelativePath, AmazonLaunchMetadata.MaximumBytes); fuelHash = Convert.ToHexString(SHA256.HashData(fuel));
            var document = AmazonLaunchMetadata.ReadDocument(fuel);
            foreach (var step in ParseSteps(document))
            {
                var bytes = ReadArtifact(step.Command, 64 * 1024 * 1024);
                var installer = AmazonManifest.Contained(root, step.Command);
                using var retained = StoreOwnedInstallFiles.OpenReadFile(installer);
                var image = probe.InspectInstaller(installer);
                if (!image.MicrosoftSigned) throw Failure("publisher_unverified");
                var ranInstaller = false;
                AmazonSetupRuntime Runtime(string name, string architecture, Version minimum)
                {
                    try { return RequireRuntime(probe, name, architecture, minimum, 1); }
                    catch (StoreOperationException error) when (error.Code == "amazon_setup_runtime_missing" && !ranInstaller)
                    {
                        ranInstaller = true;
                        var exit = probe.RunVerifiedInstaller(installer, step.Args, token);
                        if (!step.ValidReturns.Contains(exit)) throw Failure("installer_failed");
                        return RequireRuntime(probe, name, architecture, minimum, 1);
                    }
                }
                var vc = VcDescription.Match(image.Description);
                if (vc.Success && Path.GetFileName(installer).Equals("vcredist_" + vc.Groups[2].Value + ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var major = vc.Groups[1].Value switch { "2010" => 10, "2012" => 11, "2013" => 12, _ => 0 };
                    if (image.Version.Major != major || !step.Args.SequenceEqual(new[] { "/q", "/norestart" }, StringComparer.OrdinalIgnoreCase)) throw Failure("unsupported");
                    var architecture = vc.Groups[2].Value.ToLowerInvariant();
                    var runtimeProofs = new[] { "msvcr" + major + "0.dll", "msvcp" + major + "0.dll" }.Select(name => Runtime(name, architecture, image.Version)).ToArray();
                    proofs.Add(new(step.Command, "vc" + major + "-" + architecture, runtimeProofs));
                }
                else if (Path.GetFileName(installer).Equals("DXSETUP.exe", StringComparison.OrdinalIgnoreCase) && image.Version == new Version(4, 9, 0, 904) && step.Args.SequenceEqual(new[] { "/silent" }, StringComparer.OrdinalIgnoreCase))
                {
                    var main = AmazonLaunchMetadata.Parse(fuel);
                    _ = ReadArtifact(main.Executable, 64 * 1024 * 1024);
                    using var mainImage = StoreOwnedInstallFiles.OpenReadFile(AmazonManifest.Contained(root, main.Executable));
                    using var pe = new PEReader(mainImage, PEStreamOptions.LeaveOpen);
                    if (pe.PEHeaders.CorHeader != null) throw Failure("unsupported"); // managed DirectX/GAC is a separate adapter
                    var parent = step.Command.Replace('\\', '/'); parent = parent[..(parent.LastIndexOf('/') + 1)];
                    var cabs = map.Values.Where(f => f.RelativePath.StartsWith(parent, StringComparison.OrdinalIgnoreCase) &&
                        !f.RelativePath[parent.Length..].Contains('/') && NativeCab.IsMatch(Path.GetFileName(f.RelativePath))).ToArray();
                    if (cabs.Length is < 1 or > 256) throw Failure("unsupported");
                    var required = new Dictionary<string, AmazonSetupRuntime>(StringComparer.OrdinalIgnoreCase);
                    foreach (var cab in cabs)
                    {
                        var architecture = NativeCab.Match(Path.GetFileName(cab.RelativePath)).Groups[2].Value.ToLowerInvariant();
                        if (architecture == "x64" && !Environment.Is64BitOperatingSystem) continue;
                        var contents = ReadArtifact(cab.RelativePath, 16 * 1024 * 1024);
                        foreach (var (name, _) in CabinetFiles(contents))
                        {
                            if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                            if (!NativeDll.IsMatch(name)) throw Failure("unsupported");
                            // Versioned SDK DLL names select the ABI. A security
                            // update or Windows XInput shim can be smaller than
                            // the old CAB binary, so byte size is not a version.
                            // XInput 9.1.0 also ships in Windows Vista+ and uses
                            // the OS 6.x version line instead of the SDK 9.x line.
                            var minimum = name.Equals("xinput9_1_0.dll", StringComparison.OrdinalIgnoreCase) ? new Version(6, 0, 0, 0) : new Version(9, 0, 0, 0);
                            var runtime = Runtime(name, architecture, minimum);
                            required[architecture + "/" + name] = runtime;
                        }
                    }
                    if (required.Count == 0) throw Failure("unsupported");
                    proofs.Add(new(step.Command, "directx-native-june2010", required.Values.OrderBy(p => p.Architecture + "/" + p.Name, StringComparer.Ordinal).ToArray()));
                }
                else if (Path.GetFileName(installer).Equals("vcredist_x86.exe", StringComparison.OrdinalIgnoreCase)
                    && image.Description.Trim().Equals("Win32 Cabinet Self-Extractor", StringComparison.OrdinalIgnoreCase)
                    && image.Version == new Version(6, 0, 2900, 2180)
                    && step.Args.SequenceEqual(new[] { "/Q" }, StringComparer.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        StorePrerequisiteInstaller.Apply(root, job, Convert.ToHexString(manifest.RawHash) + ":" + fuelHash,
                            [new(step.Command, bytes.Length, "SHA256", Convert.ToHexString(SHA256.HashData(bytes)))],
                            [new(step.Command, step.Args, step.ValidReturns)], journal + ".native.json", token, probe.RunVerifiedInstaller);
                    }
                    catch (StoreOperationException error) when (error.Code == "prerequisite_installer_failed") { throw Failure("installer_failed"); }
                    proofs.Add(new(step.Command, "microsoft-vc-self-extractor", []));
                }
                else throw Failure("unsupported");
                _ = bytes; // The manifest checksum was verified before classification.
            }
        }
        token.ThrowIfCancellationRequested();
        var receipt = new Receipt(1, DateTimeOffset.UtcNow, id, root, job, Convert.ToHexString(manifest.RawHash), fuelHash, artifacts, proofs);
        var temporary = journal + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, receipt); stream.Flush(true); }
            File.Move(temporary, journal, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static IReadOnlyList<Step> ParseSteps(JsonObject root)
    {
        if (!root.TryGetPropertyValue("PostInstall", out var node)) return [];
        if (node is not JsonArray array || array.Count > 32) throw Failure("metadata_invalid");
        var result = new List<Step>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in array)
        {
            if (item is not JsonObject step || step["Command"] is not JsonValue value || !value.TryGetValue<string>(out var command) || command == null) throw Failure("metadata_invalid");
            command = command.Replace('\\', '/'); AmazonManifest.ValidateRelative(command);
            if (!command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !seen.Add(command)) throw Failure("metadata_invalid");
            var args = step["Args"] is JsonArray arguments ? arguments.Select(v => v is JsonValue text && text.TryGetValue<string>(out var argument) && argument != null ? argument : throw Failure("metadata_invalid")).ToArray() : throw Failure("metadata_invalid");
            if (args.Length > 64 || args.Any(a => a.IndexOfAny(['\0','\r','\n']) >= 0) || args.Sum(a => (long)a.Length + 3) > 8192) throw Failure("metadata_invalid");
            var returns = new List<int>();
            if (step.TryGetPropertyValue("ValidReturns", out var declared))
            {
                if (declared is not JsonArray codes || codes.Count is < 1 or > 16) throw Failure("metadata_invalid");
                foreach (var code in codes)
                {
                    if (code is not JsonValue number || !number.TryGetValue<double>(out var d) || !double.IsFinite(d) || d != Math.Truncate(d) || d < int.MinValue || d > int.MaxValue) throw Failure("metadata_invalid");
                    returns.Add((int)d);
                }
            }
            else returns.Add(0);
            result.Add(new(command, args, returns.ToArray()));
        }
        return result;
    }
    private static AmazonSetupRuntime RequireRuntime(IAmazonSetupProbe probe, string name, string architecture, Version minimum, long bytes)
    {
        var runtime = probe.Runtime(name, architecture);
        if (runtime == null || !runtime.Architecture.Equals(architecture, StringComparison.OrdinalIgnoreCase) || !runtime.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            runtime.Version < minimum || runtime.Bytes < bytes)
        {
            var error = Failure("runtime_missing"); error.Data["Runtime"] = architecture + "/" + name; error.Data["Minimum"] = minimum.ToString(); error.Data["Observed"] = runtime?.Version.ToString() ?? "absent"; throw error;
        }
        return runtime;
    }
    // Microsoft CAB CFFILE metadata only: no extraction, activation or code load.
    internal static IReadOnlyList<(string Name, long Size)> CabinetFiles(byte[] bytes)
    {
        if (bytes.Length < 36 || !bytes.AsSpan(0, 4).SequenceEqual("MSCF"u8)) throw Failure("cabinet_invalid");
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)); var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28));
        if (count > 4096 || offset > bytes.Length - 16) throw Failure("cabinet_invalid");
        var result = new List<(string, long)>(); var current = (int)offset;
        for (var n = 0; n < count; n++)
        {
            if (current < 0 || current > bytes.Length - 17) throw Failure("cabinet_invalid");
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(current)); var end = Array.IndexOf(bytes, (byte)0, current + 16, Math.Min(512, bytes.Length - current - 16));
            if (end < 0) throw Failure("cabinet_invalid");
            var name = Encoding.UTF8.GetString(bytes, current + 16, end - current - 16);
            if (name.Length == 0 || name.Any(c => c < 32 || c is '/' or '\\' or ':')) throw Failure("cabinet_invalid");
            result.Add((name, size)); current = end + 1;
        }
        return result;
    }
    private static void PinParents(string root, string file, StoreOwnedInstallFiles.DirectoryPins pins)
    { for (var p = Path.GetDirectoryName(file)!; p.Length > root.Length; p = Path.GetDirectoryName(p)!) pins.Add(p); }
    private static StoreOperationException Failure(string reason) => new("amazon_setup_" + reason);
}

internal sealed class AmazonWindowsSetupProbe(bool allowInstallation = true) : IAmazonSetupProbe
{
    public int RunVerifiedInstaller(string path, IReadOnlyList<string> arguments, CancellationToken token)
        => allowInstallation ? StorePrerequisiteInstaller.Run(path, arguments, token) : throw new StoreOperationException("amazon_setup_execution_unavailable");
    public AmazonSetupImage InspectInstaller(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return new(AmazonMicrosoftImageTrust.Verify(path), info.FileDescription ?? "", Version(info));
    }
    public AmazonSetupRuntime? Runtime(string name, string architecture)
    {
        if (architecture is not ("x86" or "x64") || name.IndexOfAny(['/', '\\', ':']) >= 0) return null;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var directory = Path.Combine(windows, architecture == "x86" && Environment.Is64BitOperatingSystem ? "SysWOW64" : "System32");
        var path = Path.Combine(directory, name); if (!File.Exists(path)) return null;
        using var pins = StoreOwnedInstallFiles.PinRoot(directory); using var file = StoreOwnedInstallFiles.OpenReadFile(path);
        using var pe = new PEReader(file, PEStreamOptions.LeaveOpen);
        if ((int)pe.PEHeaders.CoffHeader.Machine != (architecture == "x86" ? 0x14c : 0x8664)) return null;
        var info = FileVersionInfo.GetVersionInfo(path);
        return new(name, architecture, Version(info), file.Length, info.CompanyName ?? "");
    }
    private static Version Version(FileVersionInfo info) => new(Math.Max(0, info.FileMajorPart), Math.Max(0, info.FileMinorPart), Math.Max(0, info.FileBuildPart), Math.Max(0, info.FilePrivatePart));
}
