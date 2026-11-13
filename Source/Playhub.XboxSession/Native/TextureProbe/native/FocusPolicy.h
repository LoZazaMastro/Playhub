// Playhub Xbox Session. Distributed under GPL-3.0-only.
#pragma once
#include <utility>

enum class Foreground { Game, Companion, Other };

// Diagnostic only: these facts never participate in activation policy.
enum class AttachedFocusForeground : unsigned { None = 0, Source = 1, Owned = 2, Other = 3 };
constexpr AttachedFocusForeground ClassifyAttachedFocusForeground(bool present, bool exactSource, bool exactOwned)
{
    if (!present) return AttachedFocusForeground::None;
    if (exactSource && !exactOwned) return AttachedFocusForeground::Source;
    if (exactOwned && !exactSource) return AttachedFocusForeground::Owned;
    return AttachedFocusForeground::Other;
}
constexpr unsigned PackAttachedFocusDiagnostic(bool current, bool invoked, bool returned,
    AttachedFocusForeground beforeDetach, AttachedFocusForeground afterDetach)
{
    return (current ? 1u : 0u) | (invoked ? 2u : 0u) | (returned ? 4u : 0u)
        | (beforeDetach == AttachedFocusForeground::Owned ? 8u : 0u)
        | (beforeDetach == AttachedFocusForeground::Source ? 16u : 0u)
        | (afterDetach == AttachedFocusForeground::Owned ? 32u : 0u)
        | (afterDetach == AttachedFocusForeground::Source ? 64u : 0u)
        | (static_cast<unsigned>(beforeDetach) << 8) | (static_cast<unsigned>(afterDetach) << 10);
}

struct FocusQueueFacts
{
    bool exactGameAlive, sameSession, stableSource, sourceForeground, ownDestination, sameDesktop, distinctThread;
    bool directGameOwner, verifiedFrameOwner;
};

constexpr bool CanAttachFocusQueue(const FocusQueueFacts& proof)
{
    return proof.exactGameAlive && proof.sameSession && proof.stableSource && proof.sourceForeground
        && proof.ownDestination && proof.sameDesktop && proof.distinctThread
        && proof.directGameOwner != proof.verifiedFrameOwner;
}

// A controller's explicit Guide request may ask Windows to activate this
// process's own surface after an otherwise valid queue attachment was denied.
// Never use this path for warmup, a hotkey, a changed foreground or a retry.
template<class Current, class Activate, class Confirm>
bool TryGuideSelfActivation(bool guideRequested, bool attachmentDenied,
    Current current, Activate activate, Confirm confirm)
{
    if (!guideRequested || !attachmentDenied || !current()) return false;
    activate();
    return confirm();
}

template<class Change>
class InputQueueAttachment
{
public:
    explicit InputQueueAttachment(Change change) : change(std::move(change)) { }
    InputQueueAttachment(const InputQueueAttachment&) = delete;
    InputQueueAttachment& operator=(const InputQueueAttachment&) = delete;
    ~InputQueueAttachment() { if (attached) Detach(); }
    bool Attach() { return attached = change(true); }
    bool Detach()
    {
        if (!attached) return true;
        if (change(false) || change(false)) { attached = false; return true; }
        return false;
    }
private:
    Change change;
    bool attached = false;
};

class SnapshotRestoreQueue
{
public:
    constexpr bool Queued() const { return queued; }
    constexpr void Mark() { queued = true; }
    constexpr void Reset() { queued = false; }
private:
    bool queued = false;
};

constexpr bool ValidFrameAumid(const wchar_t* value)
{
    if (!value) return true; // A direct Win32 session has no hosted-frame identity.
    unsigned length = 0, split = 0;
    for (; length <= 512 && value[length]; ++length)
        if (value[length] == L'!')
        {
            if (split || length == 0) return false;
            split = length;
        }
    return length > 0 && length <= 512 && split > 0 && split + 1 < length;
}

constexpr bool CanLayerCompanion(bool fillsMonitor, bool topmost, bool fullscreenTrial = false)
{
    // These window properties alone do not distinguish DXGI exclusive mode
    // from borderless independent flip. The trial verifies its own snapshot
    // and retained game identity before trying the real overlay.
    return fullscreenTrial || !fillsMonitor || !topmost;
}

constexpr bool KeepSnapshotSource(bool exactGameAlive, bool exactWindow, bool snapshotReady,
    Foreground front, bool sourceMinimized)
{
    return exactGameAlive && exactWindow && snapshotReady && front == Foreground::Companion && sourceMinimized;
}

constexpr bool ShowCompanion(Foreground front, bool overlayOpen, bool keyPending)
{
    return front != Foreground::Other && (overlayOpen || keyPending);
}

constexpr bool RestoreGame(Foreground front, bool companionShown, bool overlayOpen, bool keyPending)
{
    return front == Foreground::Companion && companionShown && !overlayOpen && !keyPending;
}

constexpr bool AllowHotkey(Foreground front, bool overlayOpen)
{
    return front == Foreground::Game && !overlayOpen;
}

constexpr bool AllowOverlayRequest(Foreground front, bool overlayOpen, bool guideRequested, bool hotkeyPressed, bool composited)
{
    return composited && front == Foreground::Game
        && (guideRequested || (hotkeyPressed && !overlayOpen));
}

template<class Current, class Release, class Replay>
bool PrepareOverlayActivation(bool alreadyOpen, Current current, Release release, Replay replay)
{
    if (!current()) return false;
    // Replaying Shift+Tab into an existing Valve menu would close it.
    if (alreadyOpen ? !release() : !replay()) return false;
    return current();
}

constexpr bool KeepOwnedOverlayForFrame(Foreground front, bool overlayOpen, bool surfaceShown,
    bool retainedFrame, unsigned long long ageMs)
{
    return front == Foreground::Companion && overlayOpen && surfaceShown && retainedFrame && ageMs <= 100;
}

enum class GuideFocusTarget { Game, Companion, SteamMain, Other };

class GuideFocusRecoveryPolicy
{
public:
    constexpr void Arm(unsigned long long now) { phase = Phase::Game; started = now; }
    constexpr void Cancel() { phase = Phase::Idle; }
    constexpr bool Armed() const { return phase != Phase::Idle; }
    constexpr bool OpenObserved() const { return phase == Phase::Open || phase == Phase::Hop; }
    constexpr bool SteamHopObserved() const { return phase == Phase::Hop; }
    constexpr bool WithinGuide(unsigned long long now) const { return Armed() && now - started <= 300; }

    constexpr void ForegroundChanged(GuideFocusTarget target)
    {
        if (!Armed()) return;
        if (target == GuideFocusTarget::Companion && phase == Phase::Game) phase = Phase::Companion;
        else if (target == GuideFocusTarget::SteamMain && phase == Phase::Open) phase = Phase::Hop;
        else if (!(target == GuideFocusTarget::Game && phase == Phase::Game)
            && !(target == GuideFocusTarget::Companion && (phase == Phase::Companion || phase == Phase::Open))
            && !(target == GuideFocusTarget::SteamMain && phase == Phase::Hop)) Cancel();
    }

    constexpr bool OverlayOpened(unsigned long long now)
    {
        if (phase != Phase::Companion || !WithinGuide(now)) return false;
        opened = now;
        phase = Phase::Open;
        return true;
    }

    constexpr bool InRecoveryWindow(unsigned long long now) const
    {
        return OpenObserved() && WithinGuide(now) && now - opened <= 120;
    }

    constexpr bool Take(unsigned long long now, bool exactCurrentSteam, bool sameInput, bool open)
    {
        const bool allowed = SteamHopObserved() && InRecoveryWindow(now) && exactCurrentSteam && sameInput && open;
        Cancel();
        return allowed;
    }

private:
    enum class Phase { Idle, Game, Companion, Open, Hop };
    Phase phase = Phase::Idle;
    unsigned long long started = 0, opened = 0;
};
