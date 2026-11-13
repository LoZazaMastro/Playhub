// Playhub Xbox Session. Distributed under GPL-3.0-only.
#include "PublicSteamContextPolicy.h"
#include <string>
#include <vector>
#include <iostream>

struct Fake
{
    int32_t pipe = 4, user = 7;
    bool valid = true, friends = true, utils = true, releaseResult = true;
    int checks = 0, failAt = 0;
    uint32_t appId = 0;
    std::vector<std::string> calls;
    bool Current() { return valid && ++checks != failAt; }
    int32_t CreatePipe() { calls.push_back("pipe"); return pipe; }
    int32_t Connect(int32_t p) { if (p != 4) throw 1; calls.push_back("user"); return user; }
    bool Friends(int32_t u, int32_t p, const char* v)
    { if (u != 7 || p != 4 || std::string(v) != "SteamFriends018") throw 2; calls.push_back("friends"); return friends; }
    bool Utils(int32_t p, const char* v)
    { if (p != 4 || std::string(v) != "SteamUtils011") throw 3; calls.push_back("utils"); return utils; }
    uint32_t AppId() { calls.push_back("appid"); return appId; }
    void ReleaseUser(int32_t p, int32_t u) { if (p != 4 || u != 7) throw 4; calls.push_back("release-user"); }
    bool ReleasePipe(int32_t p) { if (p != 4) throw 5; calls.push_back("release-pipe"); return releaseResult; }
};
int main()
{
    using namespace PlayhubPublicSteam;
    int checks = 0;
    auto check = [&](bool ok) { ++checks; if (!ok) throw checks; };
    try
    {
        check(ValidDigest(std::string(64, 'a')));
        check(ValidDigest(std::string(64, 'F')));
        check(!ValidDigest(std::string(63, 'a')));
        check(!ValidDigest(std::string(64, 'g')));
        check(std::string(ClientVersion) == "SteamClient023");
        for (uint32_t app : {0u, 2485417537u, 0xffffffffu})
        {
            Fake f; f.appId = app;
            { ContextLease<Fake> lease(f); check(lease.Read()); check(lease.appId == app); check(lease.flags == 248);
              check(!lease.Read()); lease.Close(); check(lease.released == 3); lease.Close(); }
            check(f.calls == std::vector<std::string>{"pipe","user","friends","utils","appid","release-user","release-pipe"});
        }
        for (int at = 1; at <= 6; ++at)
        {
            Fake f; f.failAt = at;
            { ContextLease<Fake> lease(f); check(!lease.Read()); check(!(lease.flags & AppIdRead)); }
            if (at == 1) check(f.calls.empty());
            else { check(f.calls.back() == "release-pipe"); if (at > 2) check(f.calls[f.calls.size()-2] == "release-user"); }
        }
        for (int32_t p : {0, -1}) { Fake f; f.pipe = p; { ContextLease<Fake> lease(f); check(!lease.Read()); } check(f.calls.size() == 1); }
        for (int32_t u : {0, -1}) { Fake f; f.user = u; { ContextLease<Fake> lease(f); check(!lease.Read()); } check(f.calls.back() == "release-pipe"); check(f.calls.size() == 3); }
        { Fake f; f.utils = false; { ContextLease<Fake> lease(f); check(!lease.Read()); } check(f.calls[f.calls.size()-2] == "release-user"); }
        { Fake f; f.friends = false; ContextLease<Fake> lease(f); check(lease.Read()); check(!(lease.flags & FriendsAvailable)); }
        { Fake f; f.releaseResult = false; ContextLease<Fake> lease(f); check(lease.Read()); lease.Close(); check(lease.released == 1); }
        // The real production policy distinguishes both ways to reach flags0F.
        { Fake f; f.failAt = 2; ContextLease<Fake> lease(f);
          check(!lease.Read()); check((lease.flags | 7u) == 15u);
          check(lease.outcome == ReadOutcome::SessionBeforeConnect);
          check(f.calls == std::vector<std::string>{"pipe"}); }
        { Fake f; f.user = 0; ContextLease<Fake> lease(f);
          check(!lease.Read()); check((lease.flags | 7u) == 15u);
          check(lease.outcome == ReadOutcome::UserUnavailable);
          check(f.calls == std::vector<std::string>{"pipe", "user"}); }
        { Fake f; f.failAt = 3; ContextLease<Fake> lease(f);
          check(!lease.Read()); check((lease.flags | 7u) == 31u);
          check(lease.outcome == ReadOutcome::SessionBeforeFriends); }
        { Fake f; ContextLease<Fake> lease(f); check(lease.Read());
          check(lease.outcome == ReadOutcome::Complete); check(!lease.Read());
          check(lease.outcome == ReadOutcome::AlreadyStarted); }
        for (int failure = 1; failure <= 6; ++failure)
        {
            Fake f; f.failAt = failure; ContextLease<Fake> lease(f); check(!lease.Read());
            constexpr ReadOutcome expected[]{ReadOutcome::SessionBeforePipe, ReadOutcome::SessionBeforeConnect,
                ReadOutcome::SessionBeforeFriends, ReadOutcome::SessionBeforeUtils,
                ReadOutcome::SessionBeforeAppId, ReadOutcome::SessionAfterAppId};
            check(lease.outcome == expected[failure - 1]);
        }
        { Fake f; f.utils = false; ContextLease<Fake> lease(f); check(!lease.Read());
          check(lease.outcome == ReadOutcome::UtilsUnavailable); }
        { int calls = 0; FirstPresentReadGate gate;
          check(gate.AfterCreationPresent(0, [&]() { ++calls; })); check(calls == 1);
          check(!gate.AfterCreationPresent(0, [&]() { ++calls; })); check(calls == 1); }
        for (int32_t status : {int32_t(0x087A0001), int32_t(1), int32_t(0x80004005), int32_t(0x887A0005)})
        { int calls = 0; FirstPresentReadGate gate;
          check(!gate.AfterCreationPresent(status, [&]() { ++calls; })); check(calls == 0);
          check(!gate.AfterCreationPresent(0, [&]() { ++calls; })); check(calls == 0); }
        { int calls = 0; FirstPresentReadGate gate;
          try { gate.AfterCreationPresent(0, [&]() { ++calls; throw 99; }); } catch (int error) { check(error == 99); }
          check(!gate.AfterCreationPresent(0, [&]() { ++calls; })); check(calls == 1); }
        std::cout << checks << " public-context pure checks passed; no Windows/Steam/native calls.\n";
        return 0;
    }
    catch (int failure) { std::cerr << "public-context fixture failed " << failure << '\n'; return 1; }
}
