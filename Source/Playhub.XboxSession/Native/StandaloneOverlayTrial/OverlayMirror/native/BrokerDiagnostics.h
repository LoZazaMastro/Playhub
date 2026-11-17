// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <cstdint>
#include "QuietBandAdmission.h"
#include "../callback-v3/ReadDiagnostic.h"
namespace playhub::mirror {
enum class BrokerStage:std::uint32_t {
 None,InitialTarget,FrameKind,CallbackOpen,CallbackHash,ShellWindow,ShellImage,
 ShellSecurity,Desktop,RetainedSource,RandomNonce,SecurityDescriptor,
 PrivateDirectory,PrivateCopy,SharedObjects,MapView,CoreFocus,SourceImage,
 ControllerImage,TargetClass,CursorRead,TargetNonce,TargetIdentity,InitialSource,
 InitialNoInput,ModuleAbsence,ModuleLoad,CallbackExport,PreHookSource,
 PreHookNoInput,HookInstall,PreSendSource,PreSendNoInput,Deadline,QuietInputBudget,PollRefused
};
inline bool MayDeferNoInput(BrokerStage stage,bool installed,bool hook){return !installed&&!hook&&(stage==BrokerStage::InitialNoInput||stage==BrokerStage::PreHookNoInput);}
inline const char* BrokerStageName(BrokerStage s) {
 switch(s) {
#define PH_STAGE(x,n) case BrokerStage::x:return n
 PH_STAGE(None,"none");PH_STAGE(InitialTarget,"initial-target");PH_STAGE(FrameKind,"frame-kind");
 PH_STAGE(CallbackOpen,"callback-open");PH_STAGE(CallbackHash,"callback-hash");PH_STAGE(ShellWindow,"shell-window");
 PH_STAGE(ShellImage,"shell-image-trust");PH_STAGE(ShellSecurity,"shell-security");PH_STAGE(Desktop,"desktop");
 PH_STAGE(RetainedSource,"retained-source");PH_STAGE(RandomNonce,"random-nonce");PH_STAGE(SecurityDescriptor,"security-descriptor");
 PH_STAGE(PrivateDirectory,"private-directory");PH_STAGE(PrivateCopy,"private-copy");PH_STAGE(SharedObjects,"shared-objects");
 PH_STAGE(MapView,"map-view");PH_STAGE(CoreFocus,"core-focus-game-pid");PH_STAGE(SourceImage,"source-image-class");
 PH_STAGE(ControllerImage,"controller-image");PH_STAGE(TargetClass,"target-class");PH_STAGE(CursorRead,"cursor-read");
 PH_STAGE(TargetNonce,"target-nonce");PH_STAGE(TargetIdentity,"target-identity");PH_STAGE(InitialSource,"initial-source");
 PH_STAGE(InitialNoInput,"initial-no-input");PH_STAGE(ModuleAbsence,"module-absence");PH_STAGE(ModuleLoad,"module-load");
 PH_STAGE(CallbackExport,"callback-export");PH_STAGE(PreHookSource,"pre-hook-source");PH_STAGE(PreHookNoInput,"pre-hook-no-input");
 PH_STAGE(HookInstall,"hook-install");PH_STAGE(PreSendSource,"pre-send-source");PH_STAGE(PreSendNoInput,"pre-send-no-input");
 PH_STAGE(Deadline,"deadline");PH_STAGE(QuietInputBudget,"quiet-input-budget");PH_STAGE(PollRefused,"poll-refused");
#undef PH_STAGE
 }return "unknown";
}
struct BrokerDiagnostic {
 std::uint64_t serial{};BrokerStage stage{};std::uint32_t win32{};
 bool win32Valid{},setupRefused{},pollRefused{},sharedRead{},hookInstalled{};
 std::int32_t received{},rejected{},refusalMask{},permit{},bandInvoked{},bandReturned{};
 std::uint32_t bandError{},quietDeferrals{},noInputFailures{};NoInputFacts blockedInput{};
 std::uint32_t callbackReadReason{},callbackReadBits{},bandBeforeRead{},bandBefore{},bandBeforeError{};
 std::uint64_t callbackIssued{},callbackDeadline{},callbackAt{};
};
// GUI-owner-only diagnostic storage. It authorizes no operation and calls no OS API.
class FirstBrokerFailure {
 BrokerDiagnostic value{};std::uint64_t sequence{};
public:
 void Begin(){value={};value.serial=++sequence;}
 void RestartAttempt(){auto serial=value.serial;auto count=value.quietDeferrals;auto failures=value.noInputFailures;auto input=value.blockedInput;value={};value.serial=serial;value.quietDeferrals=count;value.noInputFailures=failures;value.blockedInput=input;}
 void InputBlocked(const NoInputFacts& input){++value.noInputFailures;value.blockedInput=input;}
 void DeferInput(){++value.quietDeferrals;}
 bool Reject(BrokerStage stage,bool valid=false,std::uint32_t code=0){
  if(value.stage==BrokerStage::None){value.stage=stage;value.win32Valid=valid;value.win32=valid?code:0;value.setupRefused=true;}
  return false;
 }
 void PollRefused(BrokerDiagnostic fields){
  if(value.stage==BrokerStage::None)value.stage=BrokerStage::PollRefused;
  value.pollRefused=true;value.sharedRead=fields.sharedRead;value.hookInstalled=fields.hookInstalled;
  value.received=fields.received;value.rejected=fields.rejected;value.refusalMask=fields.refusalMask;
  value.permit=fields.permit;value.bandInvoked=fields.bandInvoked;value.bandReturned=fields.bandReturned;value.bandError=fields.bandError;
  value.callbackReadReason=fields.callbackReadReason;value.callbackReadBits=fields.callbackReadBits;
  value.bandBeforeRead=fields.bandBeforeRead;value.bandBefore=fields.bandBefore;value.bandBeforeError=fields.bandBeforeError;
  value.callbackIssued=fields.callbackIssued;value.callbackDeadline=fields.callbackDeadline;value.callbackAt=fields.callbackAt;
 }
 BrokerDiagnostic Read()const{return value;}
};
}
