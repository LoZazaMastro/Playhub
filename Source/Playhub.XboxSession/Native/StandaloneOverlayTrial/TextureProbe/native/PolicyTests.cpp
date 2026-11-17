// Playhub Xbox Session. Distributed under GPL-3.0-only.
#include "FocusPolicy.h"

constexpr GuideFocusRecoveryPolicy OpenedGuide()
{
    GuideFocusRecoveryPolicy guide;
    guide.Arm(1000);
    guide.ForegroundChanged(GuideFocusTarget::Game);
    guide.ForegroundChanged(GuideFocusTarget::Companion);
    guide.OverlayOpened(1100);
    return guide;
}

int main()
{
    // The packed record is observational: no input/foreground API is called.
    using F = AttachedFocusForeground;
    if (ClassifyAttachedFocusForeground(false, false, false) != F::None
        || ClassifyAttachedFocusForeground(true, true, false) != F::Source
        || ClassifyAttachedFocusForeground(true, false, true) != F::Owned
        || ClassifyAttachedFocusForeground(true, false, false) != F::Other
        || ClassifyAttachedFocusForeground(true, true, true) != F::Other) return 54;
    for (bool current : {false, true}) for (bool invoked : {false, true}) for (bool returned : {false, true})
    for (F before : {F::None, F::Source, F::Owned, F::Other}) for (F after : {F::None, F::Source, F::Owned, F::Other})
    {
        const unsigned facts = PackAttachedFocusDiagnostic(current, invoked, returned, before, after);
        if (bool(facts & 1) != current || bool(facts & 2) != invoked || bool(facts & 4) != returned
            || bool(facts & 8) != (before == F::Owned) || bool(facts & 16) != (before == F::Source)
            || bool(facts & 32) != (after == F::Owned) || bool(facts & 64) != (after == F::Source)
            || ((facts >> 8) & 3) != static_cast<unsigned>(before)
            || ((facts >> 10) & 3) != static_cast<unsigned>(after) || (facts & ~0xf7fu)) return 55;
    }
    FocusQueueFacts validQueue{true, true, true, true, true, true, true, true, false};
    if (!CanAttachFocusQueue(validQueue)) return 38;
    auto hostedQueue = validQueue;
    hostedQueue.directGameOwner = false;
    hostedQueue.verifiedFrameOwner = true;
    if (!CanAttachFocusQueue(hostedQueue)) return 39;
    for (int field = 0; field < 7; ++field)
    {
        auto changed = hostedQueue;
        if (field == 0) changed.exactGameAlive = false;
        if (field == 1) changed.sameSession = false;
        if (field == 2) changed.stableSource = false;
        if (field == 3) changed.sourceForeground = false;
        if (field == 4) changed.ownDestination = false;
        if (field == 5) changed.sameDesktop = false;
        if (field == 6) changed.distinctThread = false;
        if (CanAttachFocusQueue(changed)) return 40;
    }
    hostedQueue.verifiedFrameOwner = false;
    if (CanAttachFocusQueue(hostedQueue)) return 41;
    validQueue.verifiedFrameOwner = true;
    if (CanAttachFocusQueue(validQueue)) return 42;
    int switches = 0, confirmations = 0;
    auto switchOwn = [&]() { ++switches; };
    auto confirmOwn = [&]() { ++confirmations; return true; };
    if (TryGuideSelfActivation(false, true, [] { return true; }, switchOwn, confirmOwn)
        || TryGuideSelfActivation(true, false, [] { return true; }, switchOwn, confirmOwn)
        || TryGuideSelfActivation(true, true, [] { return false; }, switchOwn, confirmOwn)
        || switches || confirmations) return 50;
    if (!TryGuideSelfActivation(true, true, [] { return true; }, switchOwn, confirmOwn)
        || switches != 1 || confirmations != 1) return 51;
    if (TryGuideSelfActivation(true, true, [] { return true; }, switchOwn, [] { return false; })
        || switches != 2 || confirmations != 1) return 52;
    // Each exact source guard independently vetoes the native switch.
    for (int field = 0; field < 9; ++field)
    {
        FocusQueueFacts changed{true, true, true, true, true, true, true, false, true};
        if (field == 0) changed.exactGameAlive = false;
        if (field == 1) changed.sameSession = false;
        if (field == 2) changed.stableSource = false;
        if (field == 3) changed.sourceForeground = false;
        if (field == 4) changed.ownDestination = false;
        if (field == 5) changed.sameDesktop = false;
        if (field == 6) changed.distinctThread = false;
        if (field == 7) changed.verifiedFrameOwner = false;
        if (field == 8) changed.directGameOwner = true;
        if (TryGuideSelfActivation(true, true, [&] { return CanAttachFocusQueue(changed); },
            switchOwn, confirmOwn) || switches != 2 || confirmations != 1) return 53;
    }
    int attaches = 0, detaches = 0;
    {
        InputQueueAttachment attachment([&](bool attach) { if (attach) ++attaches; else ++detaches; return true; });
        if (!attachment.Attach()) return 43;
    }
    if (attaches != 1 || detaches != 1) return 44;
    try
    {
        InputQueueAttachment attachment([&](bool attach) { if (attach) ++attaches; else ++detaches; return true; });
        if (!attachment.Attach()) return 45;
        throw 1;
    }
    catch (int) { }
    if (attaches != 2 || detaches != 2) return 46;
    {
        InputQueueAttachment attachment([&](bool attach) { if (!attach) ++detaches; return false; });
        if (attachment.Attach()) return 47;
    }
    if (detaches != 2) return 48;
    {
        int retries = 0;
        InputQueueAttachment attachment([&](bool attach) { if (attach) return true; return ++retries == 2; });
        if (!attachment.Attach() || !attachment.Detach() || retries != 2) return 49;
    }
    SnapshotRestoreQueue restoration;
    if (restoration.Queued()) return 32;
    restoration.Mark();
    for (int tick = 0; tick < 200; ++tick) if (!restoration.Queued()) return 33;
    restoration.Reset();
    if (restoration.Queued()) return 34;
    if (!ValidFrameAumid(nullptr) || !ValidFrameAumid(L"Registered.Package_abc!Game")
        || ValidFrameAumid(L"") || ValidFrameAumid(L"!Game") || ValidFrameAumid(L"Package!")
        || ValidFrameAumid(L"Package!Game!Other") || ValidFrameAumid(L"Package")) return 35;
    wchar_t longIdentity[514]{};
    for (int index = 0; index < 513; ++index) longIdentity[index] = L'x';
    longIdentity[1] = L'!';
    if (ValidFrameAumid(longIdentity)) return 36;
    longIdentity[512] = 0;
    if (!ValidFrameAumid(longIdentity)) return 37;
    if (ShowCompanion(Foreground::Other, true, true)) return 1;
    if (!ShowCompanion(Foreground::Game, true, false)) return 2;
    if (!ShowCompanion(Foreground::Companion, false, true)) return 3;
    if (ShowCompanion(Foreground::Game, false, false)) return 4;
    if (!RestoreGame(Foreground::Companion, true, false, false)) return 5;
    if (RestoreGame(Foreground::Other, true, false, false)) return 6;
    if (RestoreGame(Foreground::Companion, true, false, true)) return 7;
    if (!AllowHotkey(Foreground::Game, false) || AllowHotkey(Foreground::Game, true)) return 8;
    if (AllowHotkey(Foreground::Other, false) || AllowHotkey(Foreground::Companion, false)) return 9;
    if (!AllowOverlayRequest(Foreground::Game, true, true, false, true)
        || AllowOverlayRequest(Foreground::Game, true, false, true, true)
        || AllowOverlayRequest(Foreground::Other, true, true, true, true)
        || AllowOverlayRequest(Foreground::Companion, true, true, true, true)
        || AllowOverlayRequest(Foreground::Game, true, true, true, false)) return 54;
    int replays = 0, releases = 0, freshActivations = 0;
    const bool retainedMenus[]{false, true, true, false};
    for (bool alreadyOpen : retainedMenus)
    {
        // Back can leave Valve.Open true in the retained hidden browser. A
        // fresh authenticated Guide must focus again, without toggling it off.
        if (!AllowOverlayRequest(Foreground::Game, alreadyOpen, true, false, true)) return 55;
        if (PrepareOverlayActivation(alreadyOpen, [] { return true; },
            [&] { ++releases; return true; }, [&] { ++replays; return true; })) ++freshActivations;
    }
    if (freshActivations != 4 || replays != 2 || releases != 2) return 56;
    if (PrepareOverlayActivation(true, [] { return false; },
        [&] { ++releases; return true; }, [&] { ++replays; return true; })
        || releases != 2 || replays != 2) return 57;
    int checks = 0;
    if (PrepareOverlayActivation(true, [&] { return ++checks == 1; },
        [&] { ++releases; return true; }, [&] { ++replays; return true; })
        || checks != 2 || releases != 3 || replays != 2) return 58;
    if (PrepareOverlayActivation(true, [] { return true; }, [] { return false; },
        [&] { ++replays; return true; }) || replays != 2) return 59;
    if (CanLayerCompanion(true, true) || !CanLayerCompanion(true, false) || !CanLayerCompanion(false, false)) return 10;
    if (!CanLayerCompanion(true, true, true)) return 28;
    if (!KeepSnapshotSource(true, true, true, Foreground::Companion, true)) return 29;
    if (KeepSnapshotSource(false, true, true, Foreground::Companion, true)
        || KeepSnapshotSource(true, false, true, Foreground::Companion, true)
        || KeepSnapshotSource(true, true, false, Foreground::Companion, true)
        || KeepSnapshotSource(true, true, true, Foreground::Other, true)
        || KeepSnapshotSource(true, true, true, Foreground::Game, true)
        || KeepSnapshotSource(true, true, true, Foreground::Companion, false)) return 30;
    if (!KeepOwnedOverlayForFrame(Foreground::Companion, true, true, true, 100)) return 11;
    if (KeepOwnedOverlayForFrame(Foreground::Companion, true, true, true, 101)) return 12;
    if (KeepOwnedOverlayForFrame(Foreground::Other, true, true, true, 1)) return 13;
    if (KeepOwnedOverlayForFrame(Foreground::Game, true, true, true, 1)) return 14;
    if (KeepOwnedOverlayForFrame(Foreground::Companion, false, true, true, 1)) return 15;
    if (KeepOwnedOverlayForFrame(Foreground::Companion, true, false, true, 1)) return 16;
    if (KeepOwnedOverlayForFrame(Foreground::Companion, true, true, false, 1)) return 17;
    auto guide = OpenedGuide();
    if (guide.Take(1120, true, true, true)) return 18;
    guide = OpenedGuide();
    guide.ForegroundChanged(GuideFocusTarget::SteamMain);
    if (!guide.Take(1140, true, true, true) || guide.Take(1150, true, true, true)) return 19;
    guide = OpenedGuide();
    guide.ForegroundChanged(GuideFocusTarget::Other);
    guide.ForegroundChanged(GuideFocusTarget::SteamMain);
    if (guide.Take(1140, true, true, true)) return 20;
    guide = OpenedGuide();
    guide.ForegroundChanged(GuideFocusTarget::SteamMain);
    guide.ForegroundChanged(GuideFocusTarget::Game);
    if (guide.Take(1140, true, true, true)) return 21;
    guide = OpenedGuide();
    guide.ForegroundChanged(GuideFocusTarget::SteamMain);
    if (guide.Take(1221, true, true, true)) return 22;
    guide.Arm(1000);
    guide.ForegroundChanged(GuideFocusTarget::Companion);
    if (!guide.OverlayOpened(1290)) return 23;
    guide.ForegroundChanged(GuideFocusTarget::SteamMain);
    if (guide.Take(1301, true, true, true)) return 24;
    guide = OpenedGuide();
    guide.ForegroundChanged(GuideFocusTarget::SteamMain);
    if (guide.Take(1140, false, true, true)) return 25;
    guide = OpenedGuide();
    guide.ForegroundChanged(GuideFocusTarget::SteamMain);
    if (guide.Take(1140, true, false, true)) return 26;
    guide = OpenedGuide();
    guide.ForegroundChanged(GuideFocusTarget::SteamMain);
    if (guide.Take(1140, true, true, false)) return 27;
    return 0;
}
