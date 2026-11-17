// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <cstdint>
namespace view {
// Owned-controller diagnostics only; never read from the foreign detour.
// Stages retain the first failed existing guard, with no repeated native query.
enum class ArmStage:std::uint32_t {None,Accepted,NotInstalled,WrongController,Unauthenticated,NotFresh,WrongPreparationPurpose,
 SessionIdentity,CaptureHandles,CapturePurpose,GameUser,WindowIdentity,HostIdentity,HostedAumid,HostedGui,HostedCore,
 NativeClass,NativeCleanGui,NonceReuse,NonceRandom,NonceProperty,OwnSource,OwnIdentity,OwnVisibleEnabled,OwnDesktop,
 OwnBandApi,OwnBandRead,OwnBandMismatch,HostedCurrent,NativeWindow,NativeCurrentCleanGui,NativeDesktop,Expired};
struct ArmDiagnostic {
 std::uint64_t serial{},generation{},previousGeneration{},observedAt{},expiresAt{};ArmStage stage=ArmStage::None;bool preparation{};
 std::uint32_t sourcePid{},sourceThread{},ownPid{},ownThread{},expectedBand{},observedBand{};bool bandObserved{};
};
inline void NoteArmRefusal(ArmDiagnostic* d,ArmStage stage)noexcept{if(d&&d->stage==ArmStage::None)d->stage=stage;}
inline const char* ArmStageName(ArmStage stage)noexcept{
 switch(stage){
#define PLAYHUB_ARM_STAGE(x) case ArmStage::x:return #x
 PLAYHUB_ARM_STAGE(None);PLAYHUB_ARM_STAGE(Accepted);PLAYHUB_ARM_STAGE(NotInstalled);PLAYHUB_ARM_STAGE(WrongController);
 PLAYHUB_ARM_STAGE(Unauthenticated);PLAYHUB_ARM_STAGE(NotFresh);PLAYHUB_ARM_STAGE(WrongPreparationPurpose);PLAYHUB_ARM_STAGE(SessionIdentity);
 PLAYHUB_ARM_STAGE(CaptureHandles);PLAYHUB_ARM_STAGE(CapturePurpose);PLAYHUB_ARM_STAGE(GameUser);PLAYHUB_ARM_STAGE(WindowIdentity);
 PLAYHUB_ARM_STAGE(HostIdentity);PLAYHUB_ARM_STAGE(HostedAumid);PLAYHUB_ARM_STAGE(HostedGui);PLAYHUB_ARM_STAGE(HostedCore);
 PLAYHUB_ARM_STAGE(NativeClass);PLAYHUB_ARM_STAGE(NativeCleanGui);PLAYHUB_ARM_STAGE(NonceReuse);PLAYHUB_ARM_STAGE(NonceRandom);
 PLAYHUB_ARM_STAGE(NonceProperty);PLAYHUB_ARM_STAGE(OwnSource);PLAYHUB_ARM_STAGE(OwnIdentity);PLAYHUB_ARM_STAGE(OwnVisibleEnabled);
 PLAYHUB_ARM_STAGE(OwnDesktop);PLAYHUB_ARM_STAGE(OwnBandApi);PLAYHUB_ARM_STAGE(OwnBandRead);PLAYHUB_ARM_STAGE(OwnBandMismatch);
 PLAYHUB_ARM_STAGE(HostedCurrent);PLAYHUB_ARM_STAGE(NativeWindow);PLAYHUB_ARM_STAGE(NativeCurrentCleanGui);PLAYHUB_ARM_STAGE(NativeDesktop);PLAYHUB_ARM_STAGE(Expired);
#undef PLAYHUB_ARM_STAGE
 }return "Unknown";
}

struct Facts {bool authenticated,callerPinned,callerExecutable,exactSource,exactOwn,alive,sameGeneration,notExpired;};
struct TargetIdentity {std::uint32_t pid;std::uint64_t birth;std::uintptr_t surface,cookie;};
inline bool SameTarget(const TargetIdentity& a,const TargetIdentity& b){return a.pid&&a.birth&&a.surface&&a.cookie&&a.pid==b.pid&&a.birth==b.birth&&a.surface==b.surface&&a.cookie==b.cookie;}
inline bool Route(const Facts& f){return f.authenticated&&f.callerPinned&&f.callerExecutable&&f.exactSource&&f.exactOwn&&f.alive&&f.sameGeneration&&f.notExpired;}
inline constexpr std::uint64_t PreparationLifetimeMs=5000;
inline bool FreshRequest(std::uint64_t previous,std::uint64_t incoming,std::uint64_t now,std::uint64_t expiry){return incoming>previous&&now>0&&expiry>now&&expiry-now<=2000;}
// Only the explicit authenticated preparation entrypoint may take this fixed,
// non-renewing initial budget. Ordinary Arm and Renew retain their 2s caps.
inline bool FreshPreparationRequest(std::uint64_t previous,std::uint64_t incoming,std::uint64_t now,std::uint64_t expiry){return incoming>previous&&now>0&&expiry>now&&expiry-now<=PreparationLifetimeMs;}
inline bool RenewableRequest(std::uint64_t active,std::uint64_t incoming,std::uint64_t now,std::uint64_t oldExpiry,std::uint64_t nextExpiry,bool sameAuthority){return active>0&&incoming==active&&sameAuthority&&now>0&&now<oldExpiry&&nextExpiry>oldExpiry&&nextExpiry>now&&nextExpiry-now<=2000;}
struct RenewalSource {std::uint32_t pid,thread,coreThread;std::uint64_t birth,core;bool hosted;};
inline bool SameRenewalSource(const RenewalSource& a,const RenewalSource& b){return a.pid&&a.thread&&a.birth&&a.pid==b.pid&&a.thread==b.thread&&a.coreThread==b.coreThread&&a.birth==b.birth&&a.core==b.core&&a.hosted==b.hosted;}
inline bool Contains(std::uintptr_t base,std::uintptr_t size,std::uintptr_t address){return base!=0&&size!=0&&address>=base&&address-base<size;}
inline bool CleanupAllowed(bool disabled,unsigned inFlight,bool removed){return disabled&&inFlight==0&&removed;}
}
