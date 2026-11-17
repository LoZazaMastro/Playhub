// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <cstdint>
namespace playhub::mirror {
struct PresentationCapabilityV1 {
 std::uint32_t version=1,size=sizeof(PresentationCapabilityV1);
 std::uint32_t kind{},destinationBand{},requiresExplorer{},compositorOnly{},exclusiveFullscreenVerified{},reserved{};
};
inline constexpr PresentationCapabilityV1 OrdinaryCompositorCapability(){
 PresentationCapabilityV1 value;value.kind=1;value.destinationBand=1;value.compositorOnly=1;return value;
}
inline constexpr PresentationCapabilityV1 ExactBand16Capability(){
 PresentationCapabilityV1 value;value.kind=16;value.destinationBand=16;value.requiresExplorer=1;return value;
}
static_assert(sizeof(PresentationCapabilityV1)==32);
}
