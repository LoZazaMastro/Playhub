// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <array>
#include <cstdint>
#include <cstddef>
namespace playhub::mirror::callsite {
struct alignas(8) ContinuationSnapshotV1 {
 std::uint32_t version=1,size=sizeof(ContinuationSnapshotV1),phase{},reserved{};
 std::array<char,65> sessionId{};std::array<std::uint8_t,7> padding{};
 std::uint64_t guideEpoch{},parentGeneration{},resumedGeneration{},gapDeadline{},observedAt{},readAt{};
 std::uint32_t ownerPid{},gamePid{};
 std::uint64_t ownerBirth{},gameBirth{},gameId{},sourceWindow{},sourceCookie{},ticketLow{},ticketHigh{};
 std::uint32_t inputAuthority{},visualAuthority{},cleanupComplete{},reserved2{};
 std::uint64_t originalLeaseExpiry{},preparationExpiresAt{},reserved3{};
};
static_assert(sizeof(ContinuationSnapshotV1)==240);
static_assert(offsetof(ContinuationSnapshotV1,guideEpoch)==88&&offsetof(ContinuationSnapshotV1,ownerPid)==136);
static_assert(offsetof(ContinuationSnapshotV1,inputAuthority)==200&&offsetof(ContinuationSnapshotV1,originalLeaseExpiry)==216);
}
