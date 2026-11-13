// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "HudDisplayEngine.h"
namespace playhub::hud {
// Caller supplies synchronization. Reservation persists across dequeue and the
// first slow native Begin, so a second producer cannot enter before publication.
struct Mailbox {
 std::optional<Pulse> latest,begin;std::optional<Authority> admitted;Id closedThrough{};bool close{};
 bool Queue(unsigned action,const Pulse& p,Id now){const auto& a=p.authority;if(action==3){if(!admitted||*admitted!=a)return false;closedThrough=std::max(closedThrough,a.generation);latest.reset();begin.reset();close=true;return true;}if((action!=1&&action!=2)||a.generation<=closedThrough||!Fresh(p,now))return false;if(action==1){if(admitted||close)return false;admitted=a;begin=p;latest=p;return true;}if(!admitted||*admitted!=a||!latest||p.sequence<latest->sequence)return false;latest=p;return true;}
 void Closed(Id generation){if(admitted&&admitted->generation==generation){closedThrough=std::max(closedThrough,generation);admitted.reset();latest.reset();begin.reset();}}
};
}
