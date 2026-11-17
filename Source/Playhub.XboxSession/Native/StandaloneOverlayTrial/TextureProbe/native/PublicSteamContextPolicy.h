// Playhub Xbox Session. Distributed under GPL-3.0-only.
#pragma once
#include <cstdint>
#include <string_view>
#include <utility>

namespace PlayhubPublicSteam
{
constexpr char ClientVersion[] = "SteamClient023";
constexpr char FriendsVersion[] = "SteamFriends018";
constexpr char UtilsVersion[] = "SteamUtils011";
constexpr bool ValidDigest(std::string_view text)
{
    if (text.size() != 64) return false;
    for (char c : text) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
    return true;
}

enum Flag : uint32_t { FilePinned = 1, ModuleVerified = 2, ClientAvailable = 4,
    PipeCreated = 8, UserConnected = 16, FriendsAvailable = 32, UtilsAvailable = 64, AppIdRead = 128 };

enum class ReadOutcome : int32_t
{
    NotStarted = 0, AlreadyStarted = 1, SessionBeforePipe = 2, PipeUnavailable = 3,
    SessionBeforeConnect = 4, UserUnavailable = 5, SessionBeforeFriends = 6,
    SessionBeforeUtils = 7, UtilsUnavailable = 8, SessionBeforeAppId = 9,
    SessionAfterAppId = 10, Complete = 11, FirstPresentNotReady = 12
};

// This private timing experiment makes one decision after the creation Present.
// S_OK only: an occluded/status-only Present is not claimed as renderer-ready.
class FirstPresentReadGate
{
public:
    template<class Read> bool AfterCreationPresent(int32_t status, Read&& read)
    {
        if (decided) return false;
        decided = true;
        if (status != 0) return false;
        std::forward<Read>(read)();
        return true;
    }
private:
    bool decided = false;
};

// Backend has only public SDK operations. No account data or activation operation exists here.
template<class Backend> class ContextLease
{
public:
    explicit ContextLease(Backend& backend) : backend(backend) { }
    ContextLease(const ContextLease&) = delete;
    ContextLease& operator=(const ContextLease&) = delete;
    ~ContextLease() { Close(); }
    bool Read()
    {
        if (started) { outcome = ReadOutcome::AlreadyStarted; return false; }
        started = true;
        outcome = ReadOutcome::SessionBeforePipe;
        if (!backend.Current()) return false;
        outcome = ReadOutcome::PipeUnavailable;
        pipe = backend.CreatePipe();
        if (pipe <= 0) { pipe = 0; return false; }
        flags |= PipeCreated;
        outcome = ReadOutcome::SessionBeforeConnect;
        if (!backend.Current()) return false;
        outcome = ReadOutcome::UserUnavailable;
        user = backend.Connect(pipe);
        if (user <= 0) { user = 0; return false; }
        flags |= UserConnected;
        outcome = ReadOutcome::SessionBeforeFriends;
        if (!backend.Current()) return false;
        if (backend.Friends(user, pipe, FriendsVersion)) flags |= FriendsAvailable;
        outcome = ReadOutcome::SessionBeforeUtils;
        if (!backend.Current()) return false;
        outcome = ReadOutcome::UtilsUnavailable;
        if (!backend.Utils(pipe, UtilsVersion)) return false;
        flags |= UtilsAvailable;
        outcome = ReadOutcome::SessionBeforeAppId;
        if (!backend.Current()) return false;
        appId = backend.AppId();
        outcome = ReadOutcome::SessionAfterAppId;
        if (!backend.Current()) return false;
        flags |= AppIdRead;
        outcome = ReadOutcome::Complete;
        return true;
    }
    void Close()
    {
        // Cleanup our handles even if the game/session ended. No global shutdown is used.
        if (user) { backend.ReleaseUser(pipe, user); user = 0; released |= 1; }
        if (pipe) { if (backend.ReleasePipe(pipe)) released |= 2; pipe = 0; }
    }
    uint32_t flags = 0, appId = 0, released = 0;
    ReadOutcome outcome = ReadOutcome::NotStarted;
private:
    Backend& backend;
    int32_t pipe = 0, user = 0;
    bool started = false;
};
}
