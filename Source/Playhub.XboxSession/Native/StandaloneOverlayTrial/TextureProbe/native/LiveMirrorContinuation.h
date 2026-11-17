// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "PassiveResumeIntent.h"
#include "MirrorContinuationWire.h"
#include <bcrypt.h>
// Included inside live_mirror after Authority. No public/native-input endpoint.
class NativeContinuation final {
 using RI=playhub::resume_intent::Intent;using O=playhub::resume_intent::Observation;using B=playhub::resume_intent::Binding;
 RI intent;playhub::resume_intent::AcceptedOriginalGuide origin{};std::uint64_t recordedGeneration{},resumedGeneration{},ticketLow{},ticketHigh{},preparationExpiry{};
 Surface& surface;Authority& authority;view::ScopedForegroundView& view;std::shared_ptr<m::IBackend> backend;
 DWORD ownThread=GetCurrentThreadId();HWINEVENTHOOK hook{};HWND watchedGame{};bool foreignSeen{},watchKnown{};std::wstring station,desktop;
 inline static thread_local NativeContinuation* receiver{};
 static void CALLBACK ForegroundEvent(HWINEVENTHOOK,DWORD,HWND hwnd,LONG,LONG,DWORD,DWORD){auto* r=receiver;if(r&&GetCurrentThreadId()==r->ownThread&&r->watchedGame&&hwnd&&hwnd!=r->watchedGame)r->foreignSeen=true;}
 static bool Name(HANDLE object,std::wstring& value){wchar_t name[128]{};DWORD bytes{};if(!object||!GetUserObjectInformationW(object,UOI_NAME,name,sizeof(name),&bytes)||bytes<=sizeof(wchar_t)||bytes>sizeof(name))return false;value=name;return true;}
 bool DesktopCurrent()const{std::wstring a,b,d,e;HDESK input=OpenInputDesktop(0,FALSE,DESKTOP_READOBJECTS);bool ok=input&&Name(GetProcessWindowStation(),a)&&Name(GetThreadDesktop(ownThread),b)&&Name(GetThreadDesktop(DWORD(origin.binding.gameThread)),d)&&Name(input,e)&&a==station&&b==desktop&&d==desktop&&e==desktop;if(input)CloseDesktop(input);return ok;}
 O ObserveNative(bool cleanup)const{
  O o;o.now=GetTickCount64();o.currentBinding=origin.binding;const auto& b=origin.binding;HWND game=reinterpret_cast<HWND>(b.gameWindow);DWORD pid{};RECT gr{},sr{};GUITHREADINFO gui{sizeof(gui)};
  o.foregroundReadKnown=true;o.actualForeground=reinterpret_cast<std::uintptr_t>(view.ActualForeground());o.noForeignTransition=watchKnown&&!foreignSeen;
  o.identityCurrent=authority.Own()&&SameGame(authority.game,DWORD(b.gamePid),b.gameBirth)&&WaitForSingleObject(authority.owner,0)==WAIT_TIMEOUT&&GetCurrentProcessId()==b.ownerPid&&proof::Birth(GetCurrentProcess())==b.ownerBirth
   &&authority.frame&&GetProcessId(authority.frame)==b.framePid&&proof::Birth(authority.frame)==b.frameBirth&&WaitForSingleObject(authority.frame,0)==WAIT_TIMEOUT
   &&GameWindow(game,authority.pid)&&GetWindowThreadProcessId(game,&pid)==b.gameThread&&pid==b.framePid&&GetWindowRect(game,&gr)&&GetWindowRect(surface.window,&sr)
   &&std::array<std::int32_t,4>{gr.left,gr.top,gr.right,gr.bottom}==b.gameRect&&std::array<std::int32_t,4>{sr.left,sr.top,sr.right,sr.bottom}==b.sourceRect;
  o.desktopCurrent=DesktopCurrent();o.promptReadKnown=GetGUIThreadInfo(DWORD(b.gameThread),&gui)!=FALSE;
  HWND popup=GetLastActivePopup(game);o.promptReadKnown=o.promptReadKnown&&popup!=nullptr;
  o.promptPresent=(gui.flags&(GUI_INMOVESIZE|GUI_INMENUMODE|GUI_SYSTEMMENUMODE|GUI_POPUPMENUMODE))||gui.hwndCapture||gui.hwndMenuOwner||gui.hwndMoveSize||(popup!=game&&IsWindowVisible(popup))||!IsWindowEnabled(game);
  o.closeRequested=!authority.valve.Open();o.windowsHidden=!IsWindowVisible(surface.window)&&!backend->IsShown();o.authorityRevoked=!authority.active&&view.ReadCounts().activeGeneration==0;o.cleanupComplete=cleanup&&view.ReadCounts().inFlight==0;
  if(view.ActualForeground()!=reinterpret_cast<HWND>(o.actualForeground))o.foregroundReadKnown=false;
  return o;
 }
public:
 NativeContinuation(Surface& s,Authority& a,view::ScopedForegroundView& v,std::shared_ptr<m::IBackend> b):surface(s),authority(a),view(v),backend(std::move(b)){
  if(!receiver){receiver=this;hook=SetWinEventHook(EVENT_SYSTEM_FOREGROUND,EVENT_SYSTEM_FOREGROUND,nullptr,ForegroundEvent,0,0,WINEVENT_OUTOFCONTEXT);watchKnown=hook!=nullptr;} }
 ~NativeContinuation(){if(hook)UnhookWinEvent(hook);if(receiver==this)receiver=nullptr;}
 void Revoke(){intent.Revoke();}
 bool Waiting()const{return intent.Read()==playhub::resume_intent::State::Waiting;}
 void OnOriginalShown(const m::Snapshot& shown,const m::SourceLease& lease,std::uint64_t epoch,unsigned depth){
  if(recordedGeneration==lease.generation||depth||!watchKnown||shown.state!=m::State::Shown||!shown.shownCurrent||!shown.leaseCurrent||!authority.Current(lease))return;
  origin={};auto& b=origin.binding;b.sessionId=lease.sessionId;b.gameId=lease.gameId;b.ownerPid=lease.ownerPid;b.ownerBirth=lease.ownerBirth;b.gamePid=lease.gamePid;b.gameBirth=lease.gameBirth;b.gameWindow=lease.gameWindow;b.gameThread=lease.frameThread;b.framePid=lease.framePid;b.frameBirth=lease.frameBirth;b.sourceWindow=lease.sourceWindow;b.sourceCookie=lease.sourceCookie;b.session=lease.session;b.desktopCookie=reinterpret_cast<std::uintptr_t>(GetThreadDesktop(ownThread));b.gameRect={lease.gameRect.left,lease.gameRect.top,lease.gameRect.right,lease.gameRect.bottom};b.sourceRect={lease.sourceRect.left,lease.sourceRect.top,lease.sourceRect.right,lease.sourceRect.bottom};
  origin.epoch=epoch;origin.generation=lease.generation;origin.leaseExpiry=lease.expiresAt;origin.bandNonceLow=shown.destinationCookie;origin.bandNonceHigh=shown.destinationWindow;
  if(!Name(GetProcessWindowStation(),station)||!Name(GetThreadDesktop(ownThread),desktop)||station!=L"WinSta0")return;
  watchedGame=reinterpret_cast<HWND>(lease.gameWindow);foreignSeen=false;ticketLow=ticketHigh=0;std::uint64_t nonce[2]{};
  if(BCryptGenRandom(nullptr,reinterpret_cast<PUCHAR>(nonce),sizeof(nonce),BCRYPT_USE_SYSTEM_PREFERRED_RNG)<0||(!nonce[0]&&!nonce[1]))return;ticketLow=nonce[0];ticketHigh=nonce[1];
  auto observed=ObserveNative(false);observed.authorityRevoked=false;observed.windowsHidden=false;
  if(intent.OnOriginalShown(origin,observed,true)){recordedGeneration=lease.generation;resumedGeneration=preparationExpiry=0;}
 }
 bool Suspend(const AuthorityCurrentDiagnostic& failure,std::uint64_t sample,bool cleanup){
  playhub::resume_intent::RetirementFailure f;f.field=failure.field&&std::strcmp(failure.field,"foreground-before")==0?playhub::resume_intent::FailureField::ForegroundBefore:playhub::resume_intent::FailureField::Other;f.generation=failure.generation;f.observed=failure.observed;f.expected=failure.expected;f.sampleSerial=f.retiredSampleSerial=sample;
  return intent.SuspendAfterRevoke(f,ObserveNative(cleanup))==playhub::resume_intent::Decision::WaitPassive;
 }
 bool TakeResume(bool cleanup,playhub::resume_intent::ContinuationGrant& grant){
  auto decision=intent.Observe(ObserveNative(cleanup));if(decision!=playhub::resume_intent::Decision::RequestFreshAdmission)return false;
  return intent.TakeGrant(ObserveNative(cleanup),grant);
 }
 void Resuming(std::uint64_t generation,std::uint64_t expiry){resumedGeneration=generation;preparationExpiry=expiry;}
 void Complete(const m::Snapshot& shown){if(shown.state==m::State::Shown&&shown.shownCurrent&&shown.generation==resumedGeneration)intent.Complete(origin.epoch,shown.generation,shown.destinationCookie,shown.destinationWindow);}
 api::ContinuationSnapshotV1 Snapshot(std::uint64_t now,bool cleanup)const{
  api::ContinuationSnapshotV1 s;auto state=intent.Read();if(state==playhub::resume_intent::State::Empty||state==playhub::resume_intent::State::Eligible)return s;
  if(state==playhub::resume_intent::State::Waiting)s.phase=1;else if(state==playhub::resume_intent::State::FreshAdmissionPending||state==playhub::resume_intent::State::GrantConsumed)s.phase=2;else if(state==playhub::resume_intent::State::Done)s.phase=4;else s.phase=3;
  const auto& b=origin.binding;s.sessionId=b.sessionId;s.guideEpoch=origin.epoch;s.parentGeneration=origin.generation;s.resumedGeneration=resumedGeneration;s.gapDeadline=intent.Deadline();s.observedAt=s.readAt=now;s.ownerPid=std::uint32_t(b.ownerPid);s.gamePid=std::uint32_t(b.gamePid);s.ownerBirth=b.ownerBirth;s.gameBirth=b.gameBirth;s.gameId=b.gameId;s.sourceWindow=b.sourceWindow;s.sourceCookie=b.sourceCookie;s.ticketLow=ticketLow;s.ticketHigh=ticketHigh;s.cleanupComplete=cleanup;s.originalLeaseExpiry=origin.leaseExpiry;s.preparationExpiresAt=preparationExpiry;
  if(s.phase==2&&!s.resumedGeneration)s.phase=1;return s;
 }
};
