// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "NativeVisibleWire.h"
namespace playhub::mirror::callsite {
// This is used by the real GUI loop, not a parallel test model. Only the
// explicitly supplied renderer/context/output receipts may advance the phase.
class NativeVisibleSession {
 enum class Phase {Idle,Cleanup,Active};Phase phase=Phase::Idle;
 SourceLease lease{};NativeVisibleProbe first{},latest{};Id epoch{},receiptFrame{},sourceFrame{},pendingUntil{},minimumQpc{};
 static bool Fresh(const NativeVisibleProbe& p,Id now){return p.observedAt&&p.observedAt<=now&&p.expiresAt>now&&p.expiresAt-p.observedAt<=250
  &&now-p.observedAt<=250&&p.sourceQpc&&p.frameQpc>=p.sourceQpc&&p.samples>=16&&p.matched<=p.samples&&p.matched*100ull>=p.samples*95ull;}
public:
 bool Pending()const noexcept{return phase==Phase::Cleanup;}
 bool Active()const noexcept{return phase==Phase::Active;}
 bool Held()const noexcept{return phase!=Phase::Idle;}
 const SourceLease& Lease()const noexcept{return lease;}
 Id MinimumQpc()const noexcept{return minimumQpc;}
 Id LastPhysicalFrameQpc()const noexcept{return latest.frameQpc;}
 void Revoke()noexcept{phase=Phase::Idle;pendingUntil=minimumQpc=0;}
 NativeVisibleV1 Retired(const SourceLease& current,bool sourceHidden,bool rawOpen,Id now){
  NativeVisibleV1 n;
  if(!sourceHidden||!lease.generation||!SameAuthority(lease,current)){Revoke();return n;}
  n.phase=2;n.sessionId=lease.sessionId;n.generation=lease.generation;n.gameId=lease.gameId;n.expiresAt=lease.expiresAt;
  n.observedAt=n.readAt=now;n.sourceWindow=lease.sourceWindow;n.sourceCookie=lease.sourceCookie;n.ownerBirth=lease.ownerBirth;n.gameBirth=lease.gameBirth;n.ownerPid=lease.ownerPid;n.gamePid=lease.gamePid;
  n.guideEpoch=epoch;n.receiptFrame=receiptFrame;n.sourceFrame=sourceFrame;n.valveOpen=rawOpen?1u:0u;n.ownCleanupComplete=1;
  n.baselineSamples=first.samples;n.baselineMatched=first.matched;n.outputSamples=latest.samples;n.outputMatched=latest.matched;Revoke();return n;
 }
 // Closing is a fresh observation of this exact hidden source, independent
 // of an expired physical-visible receipt. It grants no display/input lease.
 NativeVisibleV1 ObserveHidden(const SourceLease& current,bool owned,bool hidden,bool rawOpen,bool cleanup,Id now){
  if(Held()||!owned||!hidden||!cleanup)return {};
  // Valve input-use flags may stay latched after the real source was hidden.
  // Preserve the raw flag; only this current hidden observation grants phase2.
  return Retired(current,true,rawOpen,now);
 }
 bool Begin(const SourceLease& l,const NativeVisibleProbe& candidate,Id guide,Id acceptedFrame,Id renderedFrame,Id now){
  if(Held()||!Valid(l,now)||!Fresh(candidate,now)||!guide||renderedFrame<=acceptedFrame)return false;
  lease=l;first=candidate;epoch=guide;receiptFrame=acceptedFrame;sourceFrame=renderedFrame;pendingUntil=std::min<Id>(l.expiresAt,now+250);phase=Phase::Cleanup;return true;
 }
 bool ReadyForConfirm(const SourceLease& current,bool cleanup,Id qpc,Id now){
  if(!Pending()||!SameAuthority(lease,current)||!Valid(current,now)||now>=pendingUntil){Revoke();return false;}
  if(!cleanup)return false;
  if(!qpc){Revoke();return false;}
  // A fresh physical frame must follow this exact successful cleanup barrier.
  if(!minimumQpc)minimumQpc=qpc;
  return true;
 }
 bool Confirm(const SourceLease& current,const NativeVisibleProbe& receipt,Id renderedFrame,bool rendererReady,bool cleanup,Id now){
  if(!Pending()||!minimumQpc||!cleanup||!rendererReady||!SameAuthority(lease,current)||!Valid(current,now)||now>=pendingUntil
    ||!Fresh(receipt,now)||receipt.frameQpc<=minimumQpc||renderedFrame<=receiptFrame){Revoke();return false;}
  latest=receipt;sourceFrame=renderedFrame;phase=Phase::Active;return true;
 }
 bool Current(const SourceLease& current,bool rendererReady,bool cleanup,Id now){
  if(!Active()||!rendererReady||!cleanup||!SameAuthority(lease,current)||!Valid(current,now)||!Fresh(latest,now)){Revoke();return false;}return true;
 }
 bool Update(const SourceLease& current,const NativeVisibleProbe& receipt,Id renderedFrame,bool rendererReady,bool cleanup,Id now){
  if(!Current(current,rendererReady,cleanup,now)||!Fresh(receipt,now)||receipt.frameQpc<=latest.frameQpc||renderedFrame<=receiptFrame){Revoke();return false;}
  latest=receipt;sourceFrame=renderedFrame;return true;
 }
 bool Renew(const SourceLease& next,bool rendererReady,bool cleanup,Id now){
  if(!Current(lease,rendererReady,cleanup,now)||!SameAuthority(lease,next)||next.expiresAt<lease.expiresAt||!Valid(next,now))return false;lease=next;latest.expiresAt=std::min<Id>(latest.observedAt+250,next.expiresAt);return true;
 }
 NativeVisibleV1 Read(bool rendererReady,bool cleanup,Id now){
  if(!Current(lease,rendererReady,cleanup,now))return {};
  NativeVisibleV1 n;n.phase=1;n.sessionId=lease.sessionId;n.generation=lease.generation;n.gameId=lease.gameId;n.expiresAt=lease.expiresAt;
  n.observedAt=n.readAt=now;n.sourceWindow=lease.sourceWindow;n.sourceCookie=lease.sourceCookie;n.ownerBirth=lease.ownerBirth;n.gameBirth=lease.gameBirth;n.ownerPid=lease.ownerPid;n.gamePid=lease.gamePid;
  n.physicalObservedAt=latest.observedAt;n.physicalExpiresAt=std::min<Id>(latest.expiresAt,lease.expiresAt);n.guideEpoch=epoch;n.receiptFrame=receiptFrame;n.sourceFrame=sourceFrame;
  n.valveOpen=1;n.ownCleanupComplete=1;n.baselineSamples=first.samples;n.baselineMatched=first.matched;n.outputSamples=latest.samples;n.outputMatched=latest.matched;
  return NativeVisibleCurrent(n,now)?n:NativeVisibleV1{};
 }
};
}

