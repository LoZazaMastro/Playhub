// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

namespace Playhub.StoreLibrary;

public enum StoreKind { Epic, Gog, Xbox, Amazon }
public enum StoreConnectionState { Disconnected, Connecting, Connected, Expired, Unavailable }
public enum StoreInstallKind { Direct, ExternalHandoff, Unavailable }
public enum StoreJobState { Queued, BlockedSteam, Starting, Downloading, Pausing, PausedByUser, PausedBySteam, Installing, Verifying, Completed, Canceling, Canceled, Failed, Interrupted, ExternalHandoff }
public enum StoreJobAction { Pause, Resume, Cancel, DownloadNow }

public sealed record StoreGameKey(StoreKind Store, string Id);
public sealed record StoreCapabilities(StoreInstallKind InstallKind, bool CanConnect, bool CanRefresh, bool CanPause, bool CanResume, bool CanCancel, string? UnavailableReason = null);
public sealed record StoreAccount(StoreKind Store, StoreConnectionState State, long Revision, string? DisplayName, StoreCapabilities Capabilities, string? ErrorCode = null);
public sealed record StoreLibraryGame(StoreGameKey Key, string Title, long AccountRevision, StoreCapabilities Capabilities, bool IsInstalled, bool IsLaunchable, string? InstallPath = null, string? ArtworkKey = null, long? DownloadBytes = null, string? UnavailableReason = null, string? ArtworkUrl = null, IReadOnlyList<string>? ArtworkFallbackUrls = null, string? ArtworkKind = null, string? LibraryKind = null,string? InstallProvider=null);
public sealed record StoreDownloadJob(Guid JobId, StoreGameKey Key, string Title, StoreJobState State, long Revision, long AccountRevision, string InstallPath, long DownloadedBytes, long? TotalBytes, double? BytesPerSecond, DateTimeOffset UpdatedAt, bool CanPause, bool CanResume, bool CanCancel, string? ReasonCode = null, string? ErrorCode = null, double? ProgressPercent = null, bool CanDownloadNow = false, bool CanRemove = false, string? LibraryKind = null, string? RemovalKind = null, string? Language = null);
public sealed record StoreLibrarySnapshot(long Revision, DateTimeOffset ObservedAt, IReadOnlyList<StoreAccount> Accounts, IReadOnlyList<StoreLibraryGame> Games, IReadOnlyList<StoreDownloadJob> Jobs);
public sealed record StoreConnectSession(Guid SessionId, StoreKind Store, Uri AuthorizationUri, DateTimeOffset ExpiresAt, bool RequiresAuthorizationCode);
public sealed record StoreCommandResult(bool Accepted, string? ErrorCode, StoreLibrarySnapshot Snapshot, Guid? JobId = null);
public sealed record StoreInstallLocation(string RootPath, string DisplayName, long FreeBytes, bool IsReady);
public sealed record StoreInstallPreparation(StoreGameKey Key, long AccountRevision, long Revision, Guid? SelectionNonce, DateTimeOffset ExpiresAt, long? RequiredBytes, string? SuggestedPath, string? ConfirmedPath, IReadOnlyList<StoreInstallLocation> Locations, StoreInstallKind InstallKind, string? ErrorCode = null, IReadOnlyList<string>? Languages = null, string? Language = null);
public sealed record StoreEnqueueRequest(StoreGameKey Key, long ExpectedAccountRevision, string InstallPath, Guid RequestId, Guid SelectionNonce, long ExpectedPreparationRevision, string? Language = null);
public sealed record StoreJobCommand(Guid JobId, long ExpectedRevision, StoreJobAction Action, Guid RequestId,StoreGameKey? ExpectedKey=null,long? ExpectedAccountRevision=null,string? ExpectedInstallPath=null);
public sealed record StoreHistorySelection(Guid JobId,long ExpectedJobRevision,StoreGameKey Key,long ExpectedAccountRevision);
public sealed record StoreClearCompletedRequest(long ExpectedRevision, Guid RequestId,IReadOnlyList<StoreHistorySelection>? Selections=null);
public sealed record StoreClearCanceledRequest(long ExpectedRevision, Guid RequestId, Guid? JobId = null,IReadOnlyList<StoreHistorySelection>? Selections=null);
public sealed record StoreRemoveHistoryRequest(long ExpectedRevision, Guid RequestId, Guid JobId,long? ExpectedJobRevision=null,StoreGameKey? ExpectedKey=null,long? ExpectedAccountRevision=null);
public sealed record StoreCancelCleanupRequest(Guid JobId, long ExpectedRevision, Guid RequestId, bool DeletePartial = true,StoreGameKey? ExpectedKey=null,long? ExpectedAccountRevision=null,string? ExpectedInstallPath=null);
public sealed record StoreLaunchDescriptor(StoreGameKey Key, long AccountRevision, long LibraryRevision, bool IsInstalled, bool IsLaunchable, string? ExecutablePath, string? WorkingDirectory, IReadOnlyList<string> Arguments, string? ErrorCode = null, string? GameExecutablePath = null);

// Inputs are never logged. AuthorizationCode is exchanged immediately, never returned
// in snapshots, queue persistence or error text; the credential store owns token storage.
public interface IStoreLibraryService
{
    Task<StoreLibrarySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<StoreConnectSession> BeginConnectAsync(StoreKind store, CancellationToken cancellationToken = default);
    Task<StoreCommandResult> CompleteConnectAsync(Guid sessionId, string authorizationCode, CancellationToken cancellationToken = default);
    Task<StoreCommandResult> DisconnectAsync(StoreKind store, long expectedAccountRevision, CancellationToken cancellationToken = default);
    Task<StoreCommandResult> RefreshAsync(StoreKind store, long expectedAccountRevision, CancellationToken cancellationToken = default);
    Task<StoreInstallPreparation> PrepareInstallAsync(StoreGameKey key, long expectedAccountRevision, string? absoluteDestination = null, CancellationToken cancellationToken = default);
    Task<StoreCommandResult> EnqueueAsync(StoreEnqueueRequest request, CancellationToken cancellationToken = default);
    Task<StoreCommandResult> ControlAsync(StoreJobCommand command, CancellationToken cancellationToken = default);
    Task<StoreLaunchDescriptor> GetVerifiedLaunchDescriptorAsync(StoreGameKey key, CancellationToken cancellationToken = default);
}

// Optional protocol capability so older/fake clients keep the existing contract.
public interface IStoreDownloadHistoryService
{
    Task<StoreCommandResult> ClearCompletedAsync(StoreClearCompletedRequest request, CancellationToken cancellationToken = default);
}

// Optional history-only capability; older clients/fakes retain their existing contract.
public interface IStoreCanceledDownloadHistoryService
{
    Task<StoreCommandResult> ClearCanceledAsync(StoreClearCanceledRequest request, CancellationToken cancellationToken = default);
}
public interface IStoreHistoryRemovalService
{
    Task<StoreCommandResult> RemoveHistoryAsync(StoreRemoveHistoryRequest request, CancellationToken cancellationToken = default);
}
public interface IStoreCancelCleanupService
{
    Task<StoreCommandResult> CancelCleanupAsync(StoreCancelCleanupRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Builds a new per-game destination; never creates or adopts a folder.</summary>
public static class StoreInstallDestination
{
    public static string FolderName(string title)
    {
        string name = string.Join(" ", new string(title.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? ' ' : c).ToArray())
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
        if (name.Length > 80) name = name[..(char.IsHighSurrogate(name[79]) ? 79 : 80)].TrimEnd(' ', '.');
        if (name.Length == 0) name = "Game";
        string stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase)) name = "_" + name;
        return name;
    }
    public static string FromLibraryFolder(string parent, string title)
    {
        if (!Path.IsPathFullyQualified(parent)) throw new ArgumentException("Select an absolute library folder.");
        return Path.Combine(Path.GetFullPath(parent), FolderName(title));
    }
    public static string ForDrive(string root, StoreKind store, string title) => FromLibraryFolder(Path.Combine(root, "Games", store switch
    { StoreKind.Epic => "Epic Games", StoreKind.Gog => "GOG", StoreKind.Xbox => "Xbox", StoreKind.Amazon => "Amazon Games", _ => throw new ArgumentException("Unknown store.") }), title);
    public static string? Suggest(StoreKind store, string title, IReadOnlyList<StoreInstallLocation> locations, long requiredBytes)
    {
        var drive = locations.Where(l => l.IsReady && Path.IsPathFullyQualified(l.RootPath)).OrderByDescending(l => l.FreeBytes >= requiredBytes).ThenByDescending(l => l.FreeBytes).FirstOrDefault();
        return drive is null ? null : ForDrive(drive.RootPath, store, title);
    }
    public static string ChangeDrive(string root, string current, StoreKind store, string title)
    {
        if (Path.IsPathFullyQualified(current) && Path.GetPathRoot(current) is { Length: > 0 } old)
            return Path.Combine(root, Path.GetRelativePath(old, current));
        return ForDrive(root, store, title);
    }
}
