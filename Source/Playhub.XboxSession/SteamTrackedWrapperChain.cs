// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using Playhub.Shared;

// This is a separate authority from Steam registering the reader itself. Only
// the authenticated Epic sidecar can create it; no wire DTO carries this type.
internal sealed class SteamTrackedWrapperChain : IDisposable
{
    internal interface IEnvironment
    {
        object SteamIdentity { get; }
        bool RootCurrent { get; }
        string SteamImage { get; }
        bool EpicCurrent { get; }
        bool WrapperTracked { get; }
        IEpicRetainedProcess? Open(uint pid);
        IEpicRetainedProcess? OpenSteam(EpicProcessIdentity wrapper);
    }

    private readonly object gate = new();
    private readonly IEnvironment environment;
    private readonly IEpicRetainedProcess wrapper, companion, steamProcess;
    private readonly EpicProcessIdentity wrapperIdentity, companionIdentity, steamIdentity;
    private readonly ulong gameId;
    private readonly uint gamePid;
    private readonly long gameBirth;
    private bool disposed, revoked, checking;

    private SteamTrackedWrapperChain(IEnvironment environment, IEpicRetainedProcess wrapper,
        IEpicRetainedProcess companion, IEpicRetainedProcess steamProcess, ulong gameId, uint gamePid, long gameBirth)
    {
        this.environment = environment; this.wrapper = wrapper; this.companion = companion;
        wrapperIdentity = wrapper.Identity; companionIdentity = companion.Identity;
        this.steamProcess = steamProcess; steamIdentity = steamProcess.Identity;
        this.gameId = gameId; this.gamePid = gamePid; this.gameBirth = gameBirth;
    }

    internal static SteamTrackedWrapperChain? OpenEpic(TrustedSteamFiles steam, Process parent,
        Win32CompanionHandoff handoff, EpicCaptureScope epic)
    {
        try
        {
            if (handoff.Epic is not { } witness || !epic.Retains(witness) || handoff.WrapperPid != parent.Id
                || handoff.WrapperBirth != parent.StartTime.ToFileTimeUtc()) return null;
            using var self = Process.GetCurrentProcess();
            return Open(new NativeEnvironment(steam, parent, handoff.GameId, epic, witness), handoff,
                (uint)self.Id, self.StartTime.ToFileTimeUtc(), AppContext.BaseDirectory);
        }
        catch (Exception error) when (Expected(error)) { return null; }
    }

    private static SteamTrackedWrapperChain? Open(IEnvironment environment, Win32CompanionHandoff handoff,
        uint selfPid, long selfBirth, string directory)
    {
        IEpicRetainedProcess? wrapper = null, companion = null, steamProcess = null;
        try
        {
            if (handoff.Epic is not { } epic || epic.WrapperPid != handoff.WrapperPid
                || epic.WrapperBirth != handoff.WrapperBirth || epic.GamePid != handoff.GamePid
                || epic.GameBirth != handoff.GameBirth || !SamePath(epic.GameExecutable, handoff.Executable)
                || (uint)handoff.GameId != 0x02000000 || handoff.GameId >> 32 < 0x80000000
                || handoff.GamePid == handoff.WrapperPid || selfPid == handoff.WrapperPid
                || selfPid == handoff.GamePid || handoff.GamePid == 0 || handoff.GameBirth < handoff.WrapperBirth
                || handoff.WrapperBirth <= 0 || selfBirth <= 0 || !environment.EpicCurrent || !environment.RootCurrent
                || !environment.WrapperTracked) return null;
            wrapper = environment.Open(handoff.WrapperPid); companion = environment.Open(selfPid);
            if (wrapper is null || companion is null) return null;
            var w = wrapper.Identity; var c = companion.Identity;
            if (w.Pid != handoff.WrapperPid || w.Birth != handoff.WrapperBirth
                || c.Pid != selfPid || c.Birth != selfBirth || c.ParentPid != w.Pid || c.Birth < w.Birth
                || !SamePath(w.Image, Path.Combine(directory, "Playhub.GameSession.exe"))
                || !SamePath(c.Image, Path.Combine(directory, "Playhub.XboxSession.exe"))
                || !EpicLaunchWindows.SameUser(w, c)) return null;
            steamProcess = environment.OpenSteam(w);
            if (steamProcess is null || steamProcess.Identity.Pid == 0 || steamProcess.Identity.Birth <= 0
                || !SamePath(steamProcess.Identity.Image, environment.SteamImage)
                || steamProcess.Identity.Birth > w.Birth || !EpicLaunchWindows.SameUser(steamProcess.Identity, w)
                || steamProcess.Identity.Pid == w.Pid || steamProcess.Identity.Pid == c.Pid) return null;
            var result = new SteamTrackedWrapperChain(environment, wrapper, companion, steamProcess,
                handoff.GameId, handoff.GamePid, handoff.GameBirth);
            wrapper = companion = steamProcess = null;
            if (result.Current) return result;
            result.Dispose(); return null;
        }
        catch (Exception error) when (Expected(error)) { return null; }
        finally { steamProcess?.Dispose(); companion?.Dispose(); wrapper?.Dispose(); }
    }

    internal bool Current
    {
        get
        {
            lock (gate)
            {
                if (disposed || revoked) return false;
                if (checking) { revoked = true; return false; }
                checking = true;
                try
                {
                    bool current = SameLive(wrapper, wrapperIdentity) && SameLive(companion, companionIdentity)
                        && SameLive(steamProcess, steamIdentity) && environment.EpicCurrent && environment.RootCurrent
                        && SameLive(wrapper, wrapperIdentity) && SameLive(companion, companionIdentity) && SameLive(steamProcess, steamIdentity)
                        && !disposed && !revoked;
                    if (!current) revoked = true;
                    return current;
                }
                catch (Exception error) when (Expected(error)) { revoked = true; return false; }
                finally { checking = false; }
            }
        }
    }

    internal ReaderProof? BindReader(Process child, FileStream pinnedImage, string expectedHash,
        SteamRawInputRelayTrial.SessionBinding binding) => Bind((uint)child.Id,
            child.StartTime.ToFileTimeUtc(), pinnedImage, pinnedImage.Name, expectedHash, binding);

    private ReaderProof? Bind(uint pid, long birth, Stream image, string imagePath, string expectedHash,
        SteamRawInputRelayTrial.SessionBinding binding)
    {
        IEpicRetainedProcess? reader = null;
        try
        {
            if (!Current || !Matches(binding) || pid == wrapperIdentity.Pid || pid == companionIdentity.Pid
                || pid == gamePid || birth < companionIdentity.Birth || expectedHash.Length != 64
                || !expectedHash.All(Uri.IsHexDigit) || !image.CanSeek || !image.CanRead
                || !SamePath(imagePath, Path.Combine(Path.GetDirectoryName(companionIdentity.Image)!, "PlayhubSteamInputReader.exe"))) return null;
            long position = image.Position;
            string digest;
            try { image.Position = 0; digest = Convert.ToHexString(SHA256.HashData(image)); }
            finally { image.Position = position; }
            if (!digest.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) return null;
            reader = environment.Open(pid);
            if (reader is null) return null;
            var identity = reader.Identity;
            if (identity.Pid != pid || identity.Birth != birth || identity.ParentPid != companionIdentity.Pid
                || identity.Birth < companionIdentity.Birth || !SamePath(identity.Image, imagePath)
                || !EpicLaunchWindows.SameUser(identity, companionIdentity) || !SameLive(reader, identity) || !Current) return null;
            var result = new ReaderProof(this, reader, identity, binding);
            reader = null;
            if (result.Current(binding)) return result;
            result.Dispose(); return null;
        }
        catch (Exception error) when (Expected(error)) { return null; }
        finally { reader?.Dispose(); }
    }

    private bool Matches(SteamRawInputRelayTrial.SessionBinding binding) => binding.Valid
        && binding.GameId == gameId && binding.GamePid == gamePid && binding.GameBirth == gameBirth
        && binding.CompanionPid == companionIdentity.Pid && binding.CompanionBirth == companionIdentity.Birth
        && ReferenceEquals(binding.SteamSession, environment.SteamIdentity);

    internal sealed class ReaderProof : IDisposable
    {
        private readonly SteamTrackedWrapperChain owner;
        private readonly IEpicRetainedProcess reader;
        private readonly EpicProcessIdentity identity;
        private readonly SteamRawInputRelayTrial.SessionBinding binding;
        private bool disposed, revoked;
        internal ReaderProof(SteamTrackedWrapperChain owner, IEpicRetainedProcess reader,
            EpicProcessIdentity identity, SteamRawInputRelayTrial.SessionBinding binding)
            => (this.owner, this.reader, this.identity, this.binding) = (owner, reader, identity, binding);
        internal bool Current(SteamRawInputRelayTrial.SessionBinding expected)
        {
            if (disposed || revoked || binding != expected) return false;
            try
            {
                bool current = owner.Current && SameLive(reader, identity) && owner.Matches(expected)
                    && owner.Current && SameLive(reader, identity) && !disposed && !revoked && !owner.disposed && !owner.revoked;
                if (!current) revoked = true;
                return current;
            }
            catch (Exception error) when (Expected(error)) { revoked = true; return false; }
        }
        public void Dispose() { if (disposed) return; disposed = true; reader.Dispose(); }
    }

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; steamProcess.Dispose(); companion.Dispose(); wrapper.Dispose(); }
    }
    private static bool SameLive(IEpicRetainedProcess process, EpicProcessIdentity expected)
        => process.Identity == expected && process.Current && process.Identity == expected;
    private static bool SamePath(string left, string right) => EpicLaunchWindows.SamePath(left, right);
    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException
        or InvalidOperationException or Win32Exception or ArgumentException or SecurityException or OverflowException;

    private sealed class NativeEnvironment(TrustedSteamFiles steam, Process parent, ulong gameId,
        EpicCaptureScope epic, EpicLaunchProvenance witness) : IEnvironment
    {
        public object SteamIdentity => steam;
        public bool RootCurrent => steam.UnchangedRoot();
        public string SteamImage => Path.Combine(Path.GetDirectoryName(steam.ComponentPaths.Last())!, "steam.exe");
        public bool EpicCurrent => epic.Retains(witness);
        public bool WrapperTracked => TrialSteamTracking.IsTracked(steam.TrackingLog, parent, gameId);
        public IEpicRetainedProcess? Open(uint pid) => EpicNativeProcess.Open(pid);
        public IEpicRetainedProcess? OpenSteam(EpicProcessIdentity wrapper)
        {
            if (!TrustedSteamFiles.ValveSignature(SteamImage)) return null;
            IEpicRetainedProcess? found = null;
            try
            {
                foreach (var candidate in Process.GetProcessesByName("steam"))
                {
                    using (candidate)
                    {
                        var retained = EpicNativeProcess.Open((uint)candidate.Id);
                        if (retained is null) continue;
                        if (!SamePath(retained.Identity.Image, SteamImage) || !EpicLaunchWindows.SameUser(retained.Identity, wrapper)
                            || retained.Identity.Birth > wrapper.Birth || !retained.Current) { retained.Dispose(); continue; }
                        if (found is not null) { retained.Dispose(); return null; }
                        found = retained;
                    }
                }
                var result = found; found = null; return result;
            }
            finally { found?.Dispose(); }
        }
    }

#if PLAYHUB_TEST_FIXTURE
    internal static SteamTrackedWrapperChain? OpenFixture(IEnvironment environment, Win32CompanionHandoff handoff,
        uint selfPid, long selfBirth, string directory) => Open(environment, handoff, selfPid, selfBirth, directory);
    internal ReaderProof? BindFixture(uint pid, long birth, Stream image, string imagePath, string expectedHash,
        SteamRawInputRelayTrial.SessionBinding binding) => Bind(pid, birth, image, imagePath, expectedHash, binding);
#endif
}
