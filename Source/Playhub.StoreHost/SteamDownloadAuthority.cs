// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Diagnostics;
using Microsoft.Win32;

namespace Playhub.StoreLibrary;

public sealed record SteamProcessIdentity(uint Pid, long Birth);
public sealed record SteamObserverLease(Guid OwnerId, DateTimeOffset ExpiresAt);
public sealed class SteamDownloadAuthority
{
    private readonly Func<(bool Known, SteamProcessIdentity? Steam)> steamProcess;
    private readonly Func<uint, long?> birth;
    private readonly Func<DateTimeOffset> now;
    private readonly Action<SteamProcessIdentity>? onRegistered;
    private readonly object gate = new();
    private sealed record Owner(SteamObserverLease Lease, uint ClientPid, long ClientBirth, SteamProcessIdentity Steam, long Sequence, DateTimeOffset Observed);
    private Owner? owner;
    private Guid absence;
    public SteamDownloadAuthority(Func<(bool Known, SteamProcessIdentity? Steam)> steamProcess, Func<uint, long?> birth, Func<DateTimeOffset>? clock = null,
        Action<SteamProcessIdentity>? onRegistered = null)
    { this.steamProcess = steamProcess; this.birth = birth; now = clock ?? (() => DateTimeOffset.UtcNow); this.onRegistered = onRegistered; }
    public SteamObserverLease Register(uint clientPid)
    {
        SteamObserverLease lease;
        SteamProcessIdentity identity;
        lock (gate)
        {
            var actual = steamProcess(); var b = birth(clientPid); var time = now();
            if (!actual.Known || actual.Steam == null || b == null) throw new StoreOperationException("steam_identity_unavailable");
            if (owner != null && time < owner.Lease.ExpiresAt && birth(owner.ClientPid) == owner.ClientBirth && owner.Steam == actual.Steam) throw new StoreOperationException("steam_observer_busy");
            lease = new SteamObserverLease(Guid.NewGuid(), time.AddSeconds(15)); identity = actual.Steam;
            owner = new(lease, clientPid, b.Value, identity, 0, DateTimeOffset.MinValue);
        }
        // Library refresh is an independent background consumer. It must never
        // prevent Steam's download observer from completing its registration.
        try { onRegistered?.Invoke(identity); } catch (Exception e) when (e is not OutOfMemoryException) { }
        return lease;
    }
    public bool Observe(uint clientPid, StoreSteamActivity activity)
    {
        lock (gate)
        {
            var time = now(); var actual = steamProcess();
            if (owner == null || owner.Lease.OwnerId != activity.OwnerId || owner.ClientPid != clientPid || birth(clientPid) != owner.ClientBirth || !actual.Known || actual.Steam != owner.Steam || time >= owner.Lease.ExpiresAt || activity.Sequence <= owner.Sequence || activity.ObservedAt < owner.Observed || activity.ObservedAt > time || time - activity.ObservedAt > TimeSpan.FromSeconds(15)) return false;
            owner = owner with { Lease = owner.Lease with { ExpiresAt = activity.ObservedAt.AddSeconds(15) }, Sequence = activity.Sequence, Observed = activity.ObservedAt }; return true;
        }
    }
    public bool Revoke(uint clientPid, Guid id)
    { lock (gate) { if (owner?.ClientPid != clientPid || owner.Lease.OwnerId != id || birth(clientPid) != owner.ClientBirth) return false; owner = null; return true; } }
    public (StoreSteamActivity? Idle, Guid? Revoke) Absence(long sequence)
    {
        lock (gate)
        {
            var actual = steamProcess();
            if (actual.Known && actual.Steam == null) { if (absence == Guid.Empty) absence = Guid.NewGuid(); return (new(absence, sequence, now(), false), null); }
            var old = absence; absence = Guid.Empty; return (null, old == Guid.Empty ? null : old);
        }
    }
    public static long? ProcessBirth(uint pid)
    { try { using var process = Process.GetProcessById(checked((int)pid)); return process.HasExited ? null : process.StartTime.ToUniversalTime().ToFileTimeUtc(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; } }
    public static (bool Known, SteamProcessIdentity? Steam) ResolveInstalledSteam()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var exe = key?.GetValue("SteamExe") as string;
        if (string.IsNullOrWhiteSpace(exe) && key?.GetValue("SteamPath") is string path) exe = Path.Combine(path, "steam.exe");
        if (string.IsNullOrWhiteSpace(exe) || !Path.IsPathFullyQualified(exe)) return (false, null);
        exe = Path.GetFullPath(exe);
        SteamProcessIdentity? found = null;
        foreach (var process in Process.GetProcessesByName("steam"))
        {
            using (process)
            {
                try
                {
                    if (process.HasExited) continue;
                    var actual = process.MainModule?.FileName; if (actual == null) return (false, null);
                    if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(actual), exe)) continue;
                    if (found != null) return (false, null);
                    found = new(checked((uint)process.Id), process.StartTime.ToUniversalTime().ToFileTimeUtc());
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { return (false, null); }
            }
        }
        return (true, found);
    }
}
