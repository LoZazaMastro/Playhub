// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "MirrorPresentation.h"
#include <algorithm>
#include <cstring>
#include <mutex>
#include <type_traits>
namespace playhub::mirror::callsite {
// Original Valve source is physically visible. This is NOT own presentation,
// navigation, input capture or band16 authority; Snapshot224 stays unchanged.
struct NativeVisibleV1 {
 std::uint32_t version=1,size=sizeof(NativeVisibleV1),phase{},reserved{};
 std::array<char,65> sessionId{};std::array<std::uint8_t,7> padding{};
 Id generation{},gameId{},expiresAt{},observedAt{},readAt{},sourceWindow{},sourceCookie{},ownerBirth{},gameBirth{};
 std::uint32_t ownerPid{},gamePid{};
 Id physicalObservedAt{},physicalExpiresAt{},guideEpoch{},receiptFrame{},sourceFrame{};
 std::uint32_t valveOpen{},ownCleanupComplete{},inputAuthority{},visualAuthority{},baselineSamples{},baselineMatched{},outputSamples{},outputMatched{};
};
struct NativeVisibleControlV1 {
 std::uint32_t version=1,size=sizeof(NativeVisibleControlV1),action{},reserved{};
 NativeVisibleV1 expected{};Id requestedExpiresAt{};
};
static_assert(sizeof(NativeVisibleV1)==240&&offsetof(NativeVisibleV1,generation)==88&&offsetof(NativeVisibleV1,physicalObservedAt)==168&&offsetof(NativeVisibleV1,valveOpen)==208&&offsetof(NativeVisibleV1,outputMatched)==236);
static_assert(sizeof(NativeVisibleControlV1)==264&&offsetof(NativeVisibleControlV1,expected)==16&&offsetof(NativeVisibleControlV1,requestedExpiresAt)==256);
static_assert(std::is_standard_layout_v<NativeVisibleV1>&&std::is_trivially_copyable_v<NativeVisibleV1>);
inline bool NativeVisibleCurrent(const NativeVisibleV1& n,Id now){
 return n.version==1&&n.size==sizeof(n)&&n.phase==1&&!n.reserved&&CanonicalSession(n.sessionId)
  &&std::all_of(n.padding.begin(),n.padding.end(),[](auto v){return v==0;})
  &&n.generation&&n.gameId&&(n.gameId&0xffffffffu)==0x02000000u&&(n.gameId>>32)>=0x80000000u
  &&n.sourceWindow&&n.sourceCookie&&n.ownerPid&&n.ownerBirth&&n.gamePid&&n.gameBirth&&n.guideEpoch
  &&n.valveOpen==1&&n.ownCleanupComplete==1&&!n.inputAuthority&&!n.visualAuthority
  &&n.sourceFrame>n.receiptFrame&&n.observedAt&&n.observedAt<=n.readAt&&n.readAt<=now&&now-n.observedAt<=250&&now-n.readAt<=250
  &&n.physicalObservedAt&&n.physicalObservedAt<=n.observedAt&&now>=n.physicalObservedAt&&now-n.physicalObservedAt<=250
  &&n.physicalExpiresAt>now&&n.physicalExpiresAt-n.physicalObservedAt<=250&&n.physicalExpiresAt<=n.expiresAt&&n.expiresAt>now
  &&n.baselineSamples>=16&&n.baselineMatched<=n.baselineSamples&&n.baselineMatched*100ull>=n.baselineSamples*95ull
  &&n.outputSamples>=16&&n.outputMatched<=n.outputSamples&&n.outputMatched*100ull>=n.outputSamples*95ull;
}
inline bool NativeVisibleRetired(const NativeVisibleV1& n,Id now){
 return n.version==1&&n.size==sizeof(n)&&n.phase==2&&!n.reserved&&CanonicalSession(n.sessionId)
  &&std::all_of(n.padding.begin(),n.padding.end(),[](auto v){return v==0;})
  &&n.generation&&n.gameId&&(n.gameId&0xffffffffu)==0x02000000u&&(n.gameId>>32)>=0x80000000u
  &&n.sourceWindow&&n.sourceCookie&&n.ownerPid&&n.ownerBirth&&n.gamePid&&n.gameBirth&&n.guideEpoch
  &&n.ownCleanupComplete==1&&!n.inputAuthority&&!n.visualAuthority&&n.valveOpen<=1
  &&!n.physicalObservedAt&&!n.physicalExpiresAt&&n.observedAt&&n.observedAt<=n.readAt&&n.readAt<=now&&now-n.observedAt<=250&&now-n.readAt<=250;
}
inline bool SameNativeVisible(const NativeVisibleV1&a,const NativeVisibleV1&b){
 return a.version==1&&b.version==1&&a.size==sizeof(a)&&b.size==sizeof(b)&&a.sessionId==b.sessionId&&a.generation&&a.generation==b.generation
  &&a.gameId==b.gameId&&a.sourceWindow==b.sourceWindow&&a.sourceCookie==b.sourceCookie&&a.ownerPid==b.ownerPid&&a.ownerBirth==b.ownerBirth
  &&a.gamePid==b.gamePid&&a.gameBirth==b.gameBirth&&a.guideEpoch==b.guideEpoch;
}
class NativeVisibleMailbox {
 mutable std::mutex mutex;NativeVisibleV1 value{};NativeVisibleControlV1 command{};bool queued{};
public:
 void Publish(const NativeVisibleV1& n){std::lock_guard<std::mutex> lock(mutex);if(queued&&command.action==2){value={};return;}value=n;}
 void Revoke(){std::lock_guard<std::mutex> lock(mutex);value={};}
 NativeVisibleV1 Read(Id now)const{std::lock_guard<std::mutex> lock(mutex);auto n=value;n.readAt=now;return NativeVisibleCurrent(n,now)||NativeVisibleRetired(n,now)?n:NativeVisibleV1{};}
 bool Queue(const NativeVisibleControlV1& c,Id now,Id duration){
  std::lock_guard<std::mutex> lock(mutex);auto n=value;n.readAt=now;
  if(c.version!=1||c.size!=sizeof(c)||c.reserved||queued||!NativeVisibleCurrent(n,now)||!NativeVisibleCurrent(c.expected,now)||!SameNativeVisible(c.expected,n))return false;
  if(c.action==1){if(c.requestedExpiresAt<n.expiresAt||c.requestedExpiresAt<=now||c.requestedExpiresAt-now>duration)return false;}
  else if(c.action!=2||c.requestedExpiresAt)return false;
  command=c;queued=true;if(c.action==2)value={};return true;
 }
 bool Take(NativeVisibleControlV1& c){std::lock_guard<std::mutex> lock(mutex);if(!queued)return false;c=command;queued=false;return true;}
 void Finish(){std::lock_guard<std::mutex> lock(mutex);value={};queued=false;}
};
}

