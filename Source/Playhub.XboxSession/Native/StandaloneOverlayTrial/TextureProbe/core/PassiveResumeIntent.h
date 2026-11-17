// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <array>
#include <cstdint>
namespace playhub::resume_intent {
using Id=std::uint64_t;
// Native-owned values only. This type is deliberately NOT an IPC structure or SourceLease.
struct Binding {
 std::array<char,65> sessionId{};
 Id gameId{},ownerPid{},ownerBirth{},gamePid{},gameBirth{},gameWindow{},gameThread{},framePid{},frameBirth{},sourceWindow{},sourceCookie{},session{},desktopCookie{};
 std::array<std::int32_t,4> gameRect{},sourceRect{};
 bool operator==(const Binding& b)const noexcept{
  return sessionId==b.sessionId&&gameId==b.gameId&&ownerPid==b.ownerPid&&ownerBirth==b.ownerBirth&&gamePid==b.gamePid&&gameBirth==b.gameBirth&&gameWindow==b.gameWindow&&gameThread==b.gameThread&&framePid==b.framePid&&frameBirth==b.frameBirth&&sourceWindow==b.sourceWindow&&sourceCookie==b.sourceCookie&&session==b.session&&desktopCookie==b.desktopCookie&&gameRect==b.gameRect&&sourceRect==b.sourceRect;
 }
 bool Valid()const noexcept{
  for(unsigned i=0;i<64;++i)if(!((sessionId[i]>='0'&&sessionId[i]<='9')||(sessionId[i]>='a'&&sessionId[i]<='f')))return false;
  return sessionId[64]==0&&gameId&&ownerPid&&ownerBirth&&gamePid&&gameBirth&&gameWindow&&gameThread&&framePid&&frameBirth&&sourceWindow&&sourceCookie&&session&&desktopCookie&&gameWindow!=sourceWindow&&gameRect[2]>gameRect[0]&&gameRect[3]>gameRect[1]&&sourceRect[2]>sourceRect[0]&&sourceRect[3]>sourceRect[1];
 }
};
struct AcceptedOriginalGuide {
 Binding binding{};Id epoch{},generation{},leaseExpiry{},bandNonceLow{},bandNonceHigh{};
 unsigned resumeDepth{};
};
struct Observation {
 Binding currentBinding{};Id now{},actualForeground{};
 // These facts are independently observed native results, never client booleans.
 bool foregroundReadKnown{},identityCurrent{},desktopCurrent{},promptReadKnown{},promptPresent{},noForeignTransition{};
 bool windowsHidden{},authorityRevoked{},cleanupComplete{},closeRequested{};
};
enum class FailureField{Other,ForegroundBefore};
struct RetirementFailure {FailureField field=FailureField::Other;Id generation{},observed{},expected{},sampleSerial{},retiredSampleSerial{};};
enum class State{Empty,Eligible,Waiting,FreshAdmissionPending,GrantConsumed,Done,Cancelled};
enum class Decision{None,WaitPassive,RequestFreshAdmission,Cancel};
// A one-shot continuation capability. It grants only permission to REQUEST a
// new independently attested admission; it cannot authorize rendering or input.
struct ContinuationGrant {Binding binding{};Id guideEpoch{},retiredGeneration{},minimumGeneration{},admissionStartedAt{},maximumPreparationExpiry{};unsigned resumeDepth=1;};
class Intent final {
 AcceptedOriginalGuide origin{};State state=State::Empty;Id lastEpoch{},lastNow{},gapDeadline{};bool grantTaken{};
 ContinuationGrant grant{};
 bool Safe(const Observation& o)const noexcept{
  return o.now&&o.now>=lastNow&&o.foregroundReadKnown&&o.identityCurrent&&o.desktopCurrent&&o.promptReadKnown&&!o.promptPresent&&o.noForeignTransition&&!o.closeRequested&&o.currentBinding==origin.binding;
 }
 Decision Cancel()noexcept{state=State::Cancelled;return Decision::Cancel;}
public:
 static constexpr Id MaximumGapMs=500,MaximumPreparationMs=5000;
 State Read()const noexcept{return state;}Id Deadline()const noexcept{return gapDeadline;}
 // Called only from the actual native Shown publication belonging to a real
 // accepted Guide epoch. A recovered generation can never arm another ticket.
 bool OnOriginalShown(const AcceptedOriginalGuide& g,const Observation& o,bool nativeShownCurrent)noexcept{
  if(!nativeShownCurrent||!g.binding.Valid()||!g.epoch||g.epoch<=lastEpoch||!g.generation||g.generation==UINT64_MAX||g.resumeDepth||(!g.bandNonceLow&&!g.bandNonceHigh)||g.leaseExpiry<=o.now||!o.now||o.actualForeground!=g.binding.gameWindow||!o.foregroundReadKnown||!o.identityCurrent||!o.desktopCurrent||!o.promptReadKnown||o.promptPresent||!o.noForeignTransition||o.closeRequested||!(o.currentBinding==g.binding))return false;
  lastEpoch=g.epoch;origin=g;state=State::Eligible;lastNow=o.now;gapDeadline=0;grant={};grantTaken=false;return true;
 }
 // Caller has already revoked/disarmed the old generation and hidden BOTH
 // windows. Its actual failed guard result must be bound to that exact retire.
 Decision SuspendAfterRevoke(const RetirementFailure& f,const Observation& o)noexcept{
  if(state!=State::Eligible)return Decision::None;
  if(f.field!=FailureField::ForegroundBefore||f.observed!=0||f.expected!=origin.binding.gameWindow||f.generation!=origin.generation||!f.sampleSerial||f.sampleSerial!=f.retiredSampleSerial||!Safe(o)||o.actualForeground!=0||!o.windowsHidden||!o.authorityRevoked||o.now>=origin.leaseExpiry||o.now>UINT64_MAX-MaximumGapMs)return Cancel();
  lastNow=o.now;gapDeadline=o.now+MaximumGapMs;if(gapDeadline>origin.leaseExpiry)gapDeadline=origin.leaseExpiry;state=State::Waiting;return Decision::WaitPassive;
 }
 Decision Observe(const Observation& o)noexcept{
  if(state!=State::Waiting)return Decision::None;
  if(!Safe(o)||!o.windowsHidden||!o.authorityRevoked||o.now>=gapDeadline||(o.actualForeground!=0&&o.actualForeground!=origin.binding.gameWindow))return Cancel();
  lastNow=o.now;if(!o.actualForeground||!o.cleanupComplete)return Decision::WaitPassive;
  if(o.now>UINT64_MAX-MaximumPreparationMs)return Cancel();
  grant={origin.binding,origin.epoch,origin.generation,origin.generation+1,o.now,o.now+MaximumPreparationMs,1};
  state=State::FreshAdmissionPending;return Decision::RequestFreshAdmission;
 }
 // Native issuer consumes this exactly once. Do NOT translate it into a fake
 // takeGuideRequest=true or reuse the retired SourceLease.
 bool TakeGrant(const Observation& o,ContinuationGrant& out)noexcept{
  if(state!=State::FreshAdmissionPending||grantTaken)return false;
  if(!Safe(o)||o.actualForeground!=origin.binding.gameWindow||!o.windowsHidden||!o.authorityRevoked||!o.cleanupComplete||o.now>=gapDeadline||o.now>UINT64_MAX-MaximumPreparationMs){Cancel();return false;}
  lastNow=o.now;grant.admissionStartedAt=o.now;grant.maximumPreparationExpiry=o.now+MaximumPreparationMs;
  out=grant;grantTaken=true;state=State::GrantConsumed;return true;
 }
 // Completion is a bookkeeping acknowledgement of the NEW native lifecycle,
 // not a positive display/input predicate. All real guards/receipts remain.
 bool Complete(Id epoch,Id newGeneration,Id newNonceLow,Id newNonceHigh)noexcept{
  if(state!=State::GrantConsumed||!grantTaken||epoch!=origin.epoch||newGeneration<grant.minimumGeneration||(!newNonceLow&&!newNonceHigh)||(newNonceLow==origin.bandNonceLow&&newNonceHigh==origin.bandNonceHigh))return false;
  state=State::Done;return true;
 }
 void Revoke()noexcept{if(state!=State::Empty)state=State::Cancelled;}
};
}
