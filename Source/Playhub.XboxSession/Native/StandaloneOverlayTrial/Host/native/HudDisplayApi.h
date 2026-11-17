// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <windows.h>
#include <cstdint>
namespace playhub::hud {
// Private, in-process ABI. No pipe/HTTP grants native display authority.
// Read is a copy; only explicit UI-producer Begin/Pulse/Close can drive work.
struct WirePulse {
 std::uint32_t size,version,purpose,appId,agentPid,gamePid,framePid,frameThread,sourceThread,session,enabled,suspended,ready,reserved;
 std::uint64_t generation,collectorEpoch,agentBirth,gameBirth,frameBirth,gameWindow,wpfWindow,wpfInstance,sequence,sampleTick,observedAt,leaseExpiresAt;
 std::int32_t gameRect[4],wpfRect[4];char collectorLease[33],ownerBuild[65];wchar_t wpfClass[256],expectedFrameAumid[513];
};
struct WireSnapshot {
 std::uint32_t size,version,phase,cleanupComplete,stage,error;std::int32_t hresult;std::uint32_t flags;
 std::uint64_t generation,source,destination,shownSequence,observedAt,dwmUpdates;
};
static_assert(sizeof(WirePulse)==1824);static_assert(sizeof(WireSnapshot)==80);
}
extern "C" {
__declspec(dllexport) int WINAPI ConfigureHudDisplayV1(const wchar_t* callback,const wchar_t* runtimeRoot);
__declspec(dllexport) int WINAPI QueueHudDisplayV1(std::uint32_t action,const playhub::hud::WirePulse*,std::uint32_t bytes);
__declspec(dllexport) int WINAPI ReadHudDisplayV1(playhub::hud::WireSnapshot*,std::uint32_t bytes);
}
