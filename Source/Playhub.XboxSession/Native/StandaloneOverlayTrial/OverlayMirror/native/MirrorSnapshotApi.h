// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "MirrorPresentation.h"
#include <cstring>
#include <type_traits>
namespace playhub::mirror {
static_assert(sizeof(Snapshot)==224,"Mirror snapshot V1 wire size changed");
static_assert(std::is_standard_layout_v<Snapshot>&&std::is_trivially_copyable_v<Snapshot>,"Readonly snapshot must remain a fixed native ABI");
// The existing authenticated status pipe may copy this snapshot. This function
// neither starts presentation nor mints controller/layout authorization.
inline bool ReadPresentationSnapshot(const Presentation& p,Snapshot* out,std::uint32_t bytes){if(!out||bytes!=sizeof(Snapshot)||out->version!=1||out->size!=sizeof(Snapshot))return false;auto current=p.Read();std::memcpy(out,&current,sizeof(current));return true;}
}
