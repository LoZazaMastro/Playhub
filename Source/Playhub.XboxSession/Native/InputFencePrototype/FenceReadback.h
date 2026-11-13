// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <array>
#include <cstdint>
namespace fence {
// Observation only. This data never grants Shown, source or input authority.
struct alignas(8) Readback {
 std::uint32_t magic=0x52464850,version=2,size=128,sequence{};
 std::array<std::uint64_t,4> session{};
 std::uint64_t gameBirth{},producerBirth{};
 std::uint32_t gamePid{},producerPid{},flags{},stateReads{},exReads{},keyReads{},valveReads{},guideSeen{},captures{},gameImageReads{};
 std::uint64_t sampledAt{};std::uint32_t attachStage{},attachError{},rendererMode{},reserved{};
};
static_assert(sizeof(Readback)==128);
enum class RendererMode : std::uint32_t { Unknown=0, Present=1, Absent=2 };
enum class AttachStage : std::uint32_t { None=0, Identity=1, Producer=2, Mapping=3, View=4, XInputModule=5,
 Exports=6, RendererObserve=7, RendererFile=8, RendererPe=9, RendererHash=10, RendererPin=11,
 RendererLoadedIdentity=12, GameImage=13, HookInitialize=14, HookCreate=15, HookEnable=16, Attached=17, RendererChanged=18 };
inline bool RendererMatches(RendererMode mode, std::uintptr_t pinned, std::uintptr_t observed, std::uint32_t error){
 return mode==RendererMode::Absent ? !observed&&error==126 : mode==RendererMode::Present&&pinned&&observed==pinned;
}
struct ModuleRange {std::uintptr_t base{};std::uint32_t size{};};
inline bool Inside(ModuleRange range,const void* address){const auto p=reinterpret_cast<std::uintptr_t>(address);
 return range.base&&range.size&&p>=range.base&&p-range.base<range.size;}
}
