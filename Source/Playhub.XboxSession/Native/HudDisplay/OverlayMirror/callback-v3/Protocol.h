// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <windows.h>
#include <cstdint>
#include "Policy.h"
namespace proof {
constexpr wchar_t FilePrefix[]=L"PlayhubExactBand-";
constexpr wchar_t MapPrefix[]=L"Local\\PlayhubExactBand-";
constexpr unsigned PathCapacity=1024;
struct alignas(8) Shared {
 std::uint64_t magic;std::uint32_t version,size;
 std::uint64_t nonceLow,nonceHigh;
 DWORD controllerPid,shellPid,shellThread,session,integrity,sidLength,sourcePid,sourceThread;
 std::uint64_t controllerBirth,shellBirth,sourceBirth,shellWindow,sourceForeground,issued,deadline;
 std::uint64_t sourceCore,gameBirth,targetWindow;
 DWORD gamePid,gameThread,targetThread;
 RECT sourceRect,targetRect;
 POINT initialCursor;
 BYTE sid[SECURITY_MAX_SID_SIZE];
 wchar_t controllerImage[PathCapacity],shellImage[PathCapacity],modulePath[PathCapacity],station[64],desktop[64],aumid[513],targetClass[128];
 wchar_t operationEvent[96];
 volatile LONG armed,revoked,claimed,inflight,received,rejected,refusalMask;
 DWORD callbackPid,callbackThread,callbackError;
 std::uint64_t callbackAt,callbackNonceLow,callbackNonceHigh;
 volatile LONG operationPermit,inputChanged;
 DWORD bandBefore,bandAfter,bandBeforeError,bandAfterError,bandError;
 LONG bandBeforeRead,bandInvoked,bandReturned,bandAfterRead;
 std::uint64_t bandAt;
 DWORD sourceKind;wchar_t expectedSourceClass[128],expectedGameImage[PathCapacity];
};
static_assert(sizeof(void*)==8,"Only native Windows x64 is allowed.");
static_assert(sizeof(Shared)<16384,"IPC must remain small and fixed.");
}
