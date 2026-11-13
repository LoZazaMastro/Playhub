// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "CurtainDisplayEngine.h"
namespace playhub::curtain {
struct PublisherPulse {Authority authority;Id sequence{},observedAt{},expiresAt{};};
inline bool Fresh(const PublisherPulse& p,Id now){return Valid(p.authority)&&p.sequence&&p.observedAt&&p.observedAt<=now&&now-p.observedAt<=250&&p.expiresAt>now&&p.expiresAt>=p.observedAt&&p.expiresAt-p.observedAt<=1500;}
// Caller owns the mutex. Reservation survives dequeue/slow hidden Begin.
struct PublisherMailbox {
 std::optional<PublisherPulse> latest,begin;std::optional<Authority> admitted;Id closedThrough{};bool close{};
 bool Queue(unsigned action,const PublisherPulse& p,Id now){
  if(action==3){if(!admitted||*admitted!=p.authority)return false;closedThrough=(std::max)(closedThrough,p.authority.generation);latest.reset();begin.reset();close=true;return true;}
  if((action!=1&&action!=2)||!Fresh(p,now)||p.authority.generation<=closedThrough)return false;
  if(action==1){if(admitted||close)return false;admitted=p.authority;begin=latest=p;return true;}
  if(!admitted||*admitted!=p.authority||!latest||p.sequence<=latest->sequence)return false;latest=p;return true;
 }
 void Closed(Id generation){if(admitted&&admitted->generation==generation){closedThrough=(std::max)(closedThrough,generation);admitted.reset();latest.reset();begin.reset();}}
};
}
