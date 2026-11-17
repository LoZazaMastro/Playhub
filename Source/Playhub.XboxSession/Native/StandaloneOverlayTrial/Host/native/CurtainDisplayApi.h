// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <windows.h>
#include <cstdint>
namespace playhub::curtain {
// Private, fixed-size in-process API. No endpoint accepts this structure.
struct WirePulse {
 std::uint32_t size,version,purpose,appId,ownerPid,gamePid,framePid,frameThread,steamPid,steamThread,session,reserved;
 std::uint64_t generation,requestEpoch,ownerBirth,gameBirth,frameBirth,gameWindow,steamBirth,steamWindow,sequence,observedAt,expiresAt;
 std::int32_t gameRect[4],steamRect[4];char requestId[33],sourceReceipt[33],ownerBuild[65];
 wchar_t steamClass[256],gameClass[256],steamImage[4096],gameImage[4096],expectedFrameAumid[513];
};
struct WireSnapshot {
 std::uint32_t size,version,phase,cleanupComplete,stage,error;std::int32_t result;std::uint32_t flags;
 std::uint64_t generation,destination,observedAt;
};
static_assert(sizeof(WirePulse)==18736);static_assert(sizeof(WireSnapshot)==56);
}
extern "C" {
__declspec(dllexport) int WINAPI ConfigureCurtainDisplayV1(const wchar_t* callback,const wchar_t* privateRoot);
__declspec(dllexport) int WINAPI QueueCurtainDisplayV1(std::uint32_t action,const playhub::curtain::WirePulse* pulse,std::uint32_t bytes);
__declspec(dllexport) int WINAPI ReadCurtainDisplayV1(playhub::curtain::WireSnapshot* snapshot,std::uint32_t bytes);
}
