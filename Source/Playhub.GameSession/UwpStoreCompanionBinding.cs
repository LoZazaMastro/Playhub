// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using Playhub.StoreLibrary;

namespace Playhub.GameSession;

// Private transport only: created from the broker's verified one-shot launch
// plan. It never comes from an artwork/library snapshot or a command-line path.
internal sealed record UwpStoreCompanionBinding(string ProductId, Guid LaunchId, DateTimeOffset ExpiresAt,
    string Aumid, string ManifestExecutable, string GameExecutable, string PackageDirectory)
{
    internal static UwpStoreCompanionBinding Create(StorePrivateLaunchPlan plan, DateTimeOffset now)
    {
        if (plan.Key is not { Store: StoreKind.Xbox } || plan.XboxAumid is null || plan.Arguments is null || plan.Arguments.Count != 0
            || plan.Environment is null || plan.Environment.Count != 0 || !Path.IsPathFullyQualified(plan.GameExecutable) || !Path.IsPathFullyQualified(plan.WorkingDirectory))
            throw new InvalidDataException("The Xbox companion requires the broker's registered activation plan.");
        var relative = Path.GetRelativePath(plan.WorkingDirectory, plan.GameExecutable);
        var binding = new UwpStoreCompanionBinding(plan.Key.Id, plan.LaunchId, plan.ExpiresAt, plan.XboxAumid,
            relative, plan.GameExecutable, plan.WorkingDirectory);
        if (!binding.MatchesApplication(plan.XboxAumid, relative, now))
            throw new InvalidDataException("The Xbox companion launch binding is invalid or expired.");
        return binding;
    }

    internal bool MatchesApplication(string aumid, string executable, DateTimeOffset now) =>
        ProductId is { Length: 12 } && ProductId.All(c => char.IsAsciiLetterOrDigit(c) && !char.IsAsciiLetterLower(c))
        && LaunchId != Guid.Empty && ExpiresAt > now && ExpiresAt <= now.AddSeconds(35)
        && Aumid is not null && ManifestExecutable is not null && GameExecutable is not null && PackageDirectory is not null
        && Aumid == aumid && IsApplication(Aumid, ManifestExecutable)
        && ManifestExecutable.Equals(executable, StringComparison.OrdinalIgnoreCase)
        && SamePath(GameExecutable, Path.Combine(PackageDirectory, ManifestExecutable))
        && Contained(GameExecutable, PackageDirectory);

    internal bool MatchesBroker(StoreLaunchDescriptor descriptor, string wrapper, string registeredExecutable, DateTimeOffset now, string? physicalBrokerExecutable = null) =>
        MatchesApplication(Aumid, ManifestExecutable, now)
        && descriptor.Key == new StoreGameKey(StoreKind.Xbox, ProductId)
        && descriptor.IsInstalled && descriptor.IsLaunchable && descriptor.ErrorCode is null
        && descriptor.AccountRevision >= 0 && descriptor.LibraryRevision >= 0
        && SamePath(descriptor.ExecutablePath, wrapper)
        && SamePath(descriptor.WorkingDirectory, Path.GetDirectoryName(wrapper))
        && descriptor.Arguments is not null && descriptor.Arguments.SequenceEqual(new[] { "--store-library", "xbox", ProductId }, StringComparer.Ordinal)
        && SamePath(descriptor.GameExecutablePath, GameExecutable)
        && SamePath(registeredExecutable, physicalBrokerExecutable ?? GameExecutable);

    private static bool IsApplication(string aumid, string executable)
    {
        var parts = aumid.Split('!');
        return parts.Length == 2 && parts.All(part => !string.IsNullOrWhiteSpace(part)) && aumid.Length <= 512 && !aumid.Contains('\0')
            && !string.IsNullOrWhiteSpace(executable) && executable.Length <= 4096 && !Path.IsPathRooted(executable)
            && !executable.Contains(':') && !executable.Contains('\0') && executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && executable.Replace('/', '\\').Split('\\').All(part => part is not (".." or "." or ""));
    }
    private static bool Contained(string path, string directory)
    {
        try { return Path.IsPathFullyQualified(path) && Path.IsPathFullyQualified(directory)
            && Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }
    private static bool SamePath(string? left, string? right)
    {
        try { return left is not null && right is not null && Path.IsPathFullyQualified(left) && Path.IsPathFullyQualified(right)
            && Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }
}
