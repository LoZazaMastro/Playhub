// SPDX-License-Identifier: GPL-3.0-only
// Included after Surface/ValveOverlay. Private default-OFF path; no legacy focus or snapshot.
#pragma once
#include "ScopedForegroundView.h"
#include "ConfiguredRenderer.h"
#include "ForegroundViewPolicy.h"
#include "Identity.h"
#include "LiveMirrorApi.h"
#include "Win32MirrorBackend.h"
#include "TextureProbePolicy.h"
#include <cstdio>
namespace playhub::mirror {std::shared_ptr<IBand16Broker> CreateExactBand16Broker(const wchar_t*,const wchar_t*);}
namespace live_mirror {
namespace m=playhub::mirror;namespace api=playhub::mirror::callsite;
inline api::Mailbox mailbox;
struct EventLog {
 HANDLE file=INVALID_HANDLE_VALUE;unsigned observationRowsWritten{},surfaceRowsWritten{};std::uint64_t validityGenerationWritten{},surfaceEpochWritten=~std::uint64_t(0);
 EventLog(){wchar_t temp[32768]{};DWORD n=GetTempPathW(32768,temp);if(!n||n>=32768)return;
  std::wstring folder=std::wstring(temp)+L"Playhub-Live-Mirror-20261112";
  if(!CreateDirectoryW(folder.c_str(),nullptr)&&GetLastError()!=ERROR_ALREADY_EXISTS)return;
  DWORD attr=GetFileAttributesW(folder.c_str());if(attr==INVALID_FILE_ATTRIBUTES||(attr&FILE_ATTRIBUTE_REPARSE_POINT)||!(attr&FILE_ATTRIBUTE_DIRECTORY))return;
  auto path=folder+L"\\mirror-"+std::to_wstring(GetCurrentProcessId())+L"-"+std::to_wstring(proof::Birth(GetCurrentProcess()))+L".jsonl";
  file=CreateFileW(path.c_str(),GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL|FILE_FLAG_OPEN_REPARSE_POINT,nullptr);
 }
 ~EventLog(){if(file!=INVALID_HANDLE_VALUE)CloseHandle(file);}
 void Write(const char* stage,const m::Snapshot& s,const view::Counts& count,unsigned long long frames,HRESULT present,bool raw){
  if(file==INVALID_HANDLE_VALUE)return;char text[2048]{};
  int size=snprintf(text,sizeof(text),"{\"stage\":\"%s\",\"bootMs\":%llu,\"state\":%u,\"reason\":%u,\"generation\":%llu,\"expiresAt\":%llu,\"sourceHwnd\":%llu,\"destinationHwnd\":%llu,\"prepared\":%s,\"shown\":%s,\"cleanupComplete\":%s,\"actualScope\":%u,\"keyboardAtWritten\":false,\"legacyOpenEventsEmitted\":false,\"presentFrames\":%llu,\"lastPresent\":%d,\"shimRouted\":%llu,\"rawValveOpen\":%s,\"pixelsVerified\":false,\"inputAuthority\":false,\"snapshotUsed\":false,\"focusCalls\":0,\"globalInputSent\":0}\n",stage,static_cast<unsigned long long>(GetTickCount64()),unsigned(s.state),unsigned(s.reason),static_cast<unsigned long long>(s.generation),static_cast<unsigned long long>(s.expiresAt),static_cast<unsigned long long>(s.sourceWindow),static_cast<unsigned long long>(s.destinationWindow),s.preparedCurrent?"true":"false",s.shownCurrent?"true":"false",s.cleanupComplete?"true":"false",s.actualScope,frames,int(present),static_cast<unsigned long long>(count.routed),raw?"true":"false");
  if(size>0&&size<int(sizeof(text))){DWORD written{};WriteFile(file,text,DWORD(size),&written,nullptr);}
 }
 void WriteSurface(const SurfaceWindowDiagnostic& d){
  if(file==INVALID_HANDLE_VALUE)return;char text[2048]{};
  if(surfaceEpochWritten!=d.epoch){surfaceEpochWritten=d.epoch;surfaceRowsWritten=0;}
  int size=snprintf(text,sizeof(text),"{\"stage\":\"surface-window-summary\",\"bootMs\":%llu,\"diagnosticEpoch\":%llu,\"sourceHwnd\":\"%llu\",\"sourceCookie\":\"%llu\",\"ownerThread\":%u,\"ready\":%s,\"enabledOnce\":%s,\"resetDeferred\":%s,\"recordRows\":%u,\"dropped\":%llu,\"seen\":[%llu,%llu,%llu,%llu,%llu,%llu,%llu,%llu,%llu,%llu],\"passThrough\":true,\"inputAuthority\":false}\n",
   static_cast<unsigned long long>(GetTickCount64()),d.epoch,static_cast<unsigned long long>(reinterpret_cast<std::uintptr_t>(d.window)),static_cast<unsigned long long>(d.cookie),d.ownerThread,d.ready?"true":"false",d.enabledOnce?"true":"false",d.resetDeferred?"true":"false",d.rows,d.dropped,d.seen[0],d.seen[1],d.seen[2],d.seen[3],d.seen[4],d.seen[5],d.seen[6],d.seen[7],d.seen[8],d.seen[9]);
  if(size>0&&size<int(sizeof(text))){DWORD written{};WriteFile(file,text,DWORD(size),&written,nullptr);}
  for(unsigned i=surfaceRowsWritten;i<d.rows&&i<32;i++){
   const auto&r=d.records[i];if(!r.completed)break;
   size=snprintf(text,sizeof(text),"{\"stage\":\"surface-window-message\",\"diagnosticEpoch\":%llu,\"serial\":%llu,\"beginBootMs\":%llu,\"endBootMs\":%llu,\"attemptedGeneration\":%llu,\"acceptedGeneration\":%llu,\"sourceHwnd\":\"%llu\",\"sourceCookie\":\"%llu\",\"gameHwnd\":\"%llu\",\"thread\":%u,\"depth\":%u,\"message\":%u,\"wParam\":\"%llu\",\"lParam\":\"%lld\",\"result\":\"%lld\",\"originalLastError\":%u,\"foregroundBefore\":\"%llu\",\"foregroundAfter\":\"%llu\",\"afterKnown\":%s,\"cleanupStarted\":%s,\"positionKnown\":%s,\"positionFlags\":%u,\"insertAfter\":\"%llu\",\"passThrough\":true,\"inputAuthority\":false}\n",
    d.epoch,r.serial,r.begin,r.end,r.attemptedGeneration,r.acceptedGeneration,static_cast<unsigned long long>(reinterpret_cast<std::uintptr_t>(r.window)),static_cast<unsigned long long>(r.cookie),static_cast<unsigned long long>(reinterpret_cast<std::uintptr_t>(r.gameWindow)),r.thread,r.depth,r.message,static_cast<unsigned long long>(r.wparam),static_cast<long long>(r.lparam),static_cast<long long>(r.result),r.originalError,static_cast<unsigned long long>(reinterpret_cast<std::uintptr_t>(r.foregroundBefore)),static_cast<unsigned long long>(reinterpret_cast<std::uintptr_t>(r.foregroundAfter)),r.afterKnown?"true":"false",r.cleanupStarted?"true":"false",r.positionKnown?"true":"false",r.positionFlags,static_cast<unsigned long long>(r.insertAfter));
   if(size>0&&size<int(sizeof(text))){DWORD written{};if(!WriteFile(file,text,DWORD(size),&written,nullptr)||written!=DWORD(size))break;surfaceRowsWritten=i+1;}
  }
 }
 void WriteQuietInput(const m::BrokerDiagnostic& d,std::uint64_t generation){
  if(file==INVALID_HANDLE_VALUE||!d.noInputFailures)return;char text[1024]{};const auto& f=d.blockedInput;
  int size=snprintf(text,sizeof(text),"{\"stage\":\"band-broker-no-input\",\"bootMs\":%llu,\"serial\":%llu,\"generation\":%llu,\"setupStage\":\"%s\",\"inputFailures\":%u,\"deferrals\":%u,\"held\":%s,\"firstHeldVk\":%u,\"cursorAttempted\":%s,\"cursorRead\":%s,\"cursorReadFailed\":%s,\"initialX\":%d,\"initialY\":%d,\"currentX\":%d,\"currentY\":%d,\"cursorMismatch\":%s,\"hookInstalled\":false,\"productionAuthority\":false}\n",
   static_cast<unsigned long long>(GetTickCount64()),static_cast<unsigned long long>(d.serial),static_cast<unsigned long long>(generation),m::BrokerStageName(d.stage),d.noInputFailures,d.quietDeferrals,f.held?"true":"false",f.firstHeldVk,f.cursorAttempted?"true":"false",f.cursorRead?"true":"false",f.cursorAttempted&&!f.cursorRead?"true":"false",f.initialX,f.initialY,f.currentX,f.currentY,f.cursorRead&&(f.initialX!=f.currentX||f.initialY!=f.currentY)?"true":"false");
  if(size>0&&size<int(sizeof(text))){DWORD written{};WriteFile(file,text,DWORD(size),&written,nullptr);}
 }

 void WriteCapture(const view::CaptureRecord& r,const view::CaptureDiagnostic& d){
  if(file==INVALID_HANDLE_VALUE)return;char text[1536]{};
  int size=snprintf(text,sizeof(text),"{\"stage\":\"renderer-capture-call\",\"bootMs\":%llu,\"serial\":%llu,\"generation\":%llu,\"thread\":%u,\"requested\":\"%llu\",\"logicalPrevious\":\"%llu\",\"actualForegroundBefore\":\"%llu\",\"actualForegroundAfter\":\"%llu\",\"callerPinned\":true,\"virtualized\":%s,\"osSetCaptureInvoked\":%s,\"totalSetCalls\":%llu,\"virtualSets\":%llu,\"realSets\":%llu,\"virtualGets\":%llu,\"virtualReleases\":%llu,\"inputAuthority\":false}\n",
   static_cast<unsigned long long>(r.bootMs),static_cast<unsigned long long>(r.serial),static_cast<unsigned long long>(r.generation),r.thread,static_cast<unsigned long long>(r.requested),static_cast<unsigned long long>(r.result),static_cast<unsigned long long>(r.actualBefore),static_cast<unsigned long long>(r.actualAfter),r.virtualized?"true":"false",r.virtualized?"false":"true",static_cast<unsigned long long>(d.setCalls),static_cast<unsigned long long>(d.virtualSets),static_cast<unsigned long long>(d.realSets),static_cast<unsigned long long>(d.virtualGets),static_cast<unsigned long long>(d.virtualReleases));
  if(size>0&&size<int(sizeof(text))){DWORD written{};WriteFile(file,text,DWORD(size),&written,nullptr);}
 }

 void WriteCaptureSummary(const view::CaptureDiagnostic& d){
  if(file==INVALID_HANDLE_VALUE)return;char text[768]{};
  int size=snprintf(text,sizeof(text),"{\"stage\":\"renderer-capture-summary\",\"bootMs\":%llu,\"setCalls\":%llu,\"virtualSets\":%llu,\"realSets\":%llu,\"getCalls\":%llu,\"virtualGets\":%llu,\"releaseCalls\":%llu,\"virtualReleases\":%llu,\"recordRows\":%u,\"inputAuthority\":false}\n",static_cast<unsigned long long>(GetTickCount64()),static_cast<unsigned long long>(d.setCalls),static_cast<unsigned long long>(d.virtualSets),static_cast<unsigned long long>(d.realSets),static_cast<unsigned long long>(d.getCalls),static_cast<unsigned long long>(d.virtualGets),static_cast<unsigned long long>(d.releaseCalls),static_cast<unsigned long long>(d.virtualReleases),d.rows);
  if(size>0&&size<int(sizeof(text))){DWORD written{};WriteFile(file,text,DWORD(size),&written,nullptr);}
 }

 void WriteObservation(const view::observation::Diagnostic& d){
  if(file==INVALID_HANDLE_VALUE)return;char text[2048]{};
  int size=snprintf(text,sizeof(text),"{\"stage\":\"renderer-observation-summary\",\"bootMs\":%llu,\"compiled\":%s,\"installAttempted\":%s,\"ready\":%s,\"installResult\":%d,\"configuredMask\":%u,\"enabledMask\":%u,\"copyKnown\":%s,\"inFlight\":%u,\"recordRows\":%u,\"activeWindowViewCompiled\":%s,\"actualGetActiveNull\":%llu,\"logicalActiveReturns\":%llu,\"dropped\":%llu,\"seen\":[%llu,%llu,%llu,%llu,%llu],\"pinned\":[%llu,%llu,%llu,%llu,%llu],\"admitted\":[%llu,%llu,%llu,%llu,%llu],\"delegated\":[%llu,%llu,%llu,%llu,%llu],\"inputAuthority\":false}\n",
   static_cast<unsigned long long>(GetTickCount64()),d.compiled?"true":"false",d.attempted?"true":"false",d.ready?"true":"false",int(d.installResult),d.configuredMask,d.enabledMask,d.copyKnown?"true":"false",d.inFlight,d.rows,d.activeWindowViewCompiled?"true":"false",d.actualGetActiveNull,d.logicalActiveReturns,static_cast<unsigned long long>(d.dropped),
   d.seen[0],d.seen[1],d.seen[2],d.seen[3],d.seen[4],d.pinned[0],d.pinned[1],d.pinned[2],d.pinned[3],d.pinned[4],d.admitted[0],d.admitted[1],d.admitted[2],d.admitted[3],d.admitted[4],d.delegated[0],d.delegated[1],d.delegated[2],d.delegated[3],d.delegated[4]);
  if(size>0&&size<int(sizeof(text))){DWORD written{};WriteFile(file,text,DWORD(size),&written,nullptr);}
  if(!d.copyKnown)return;
  for(unsigned i=observationRowsWritten;i<d.rows&&i<d.records.size();i++){
   const auto&r=d.records[i];const auto& b=r.before;const auto& a=r.after;
   size=snprintf(text,sizeof(text),"{\"stage\":\"renderer-window-api-observed\",\"api\":%u,\"firstBootMs\":%llu,\"lastBootMs\":%llu,\"repeats\":%llu,\"thread\":%u,\"caller\":\"%llu\",\"requested\":\"%llu\",\"returned\":\"%llu\",\"actualReturned\":\"%llu\",\"logicalReturned\":\"%llu\",\"activeWindowView\":%s,\"passThrough\":%s,\"actualForegroundBefore\":\"%llu\",\"actualForegroundAfter\":\"%llu\",\"generation\":\"%llu\",\"ownHwnd\":\"%llu\",\"ownCookie\":\"%llu\",\"ownerPid\":%u,\"ownerBirth\":\"%llu\",\"ownThread\":%u,\"gamePid\":%u,\"gameBirth\":\"%llu\",\"gameHwnd\":\"%llu\",\"framePid\":%u,\"frameBirth\":\"%llu\",\"frameThread\":%u,\"scopeCurrentAfter\":%s,\"afterGeneration\":\"%llu\",\"afterOwnHwnd\":\"%llu\",\"lastErrorAfterOriginal\":%u,\"lastErrorIsFailureReason\":false,\"suppressed\":false,\"inputAuthority\":false}\n",
    unsigned(r.kind),r.begin,r.end,r.repeats,r.thread,r.caller,r.requested,r.returned,r.returned,r.logicalReturned,r.activeWindowView?"true":"false",r.activeWindowView?"false":"true",r.foregroundBefore,r.foregroundAfter,b.generation,b.ownWindow,b.ownCookie,b.ownerPid,b.ownerBirth,b.ownThread,b.gamePid,b.gameBirth,b.gameWindow,b.framePid,b.frameBirth,b.frameThread,r.currentAfter?"true":"false",a.generation,a.ownWindow,r.lastErrorAfterOriginal);
   if(size>0&&size<int(sizeof(text))){DWORD written{};if(!WriteFile(file,text,DWORD(size),&written,nullptr)||written!=DWORD(size))break;observationRowsWritten=i+1;}
  }
 }

 void WriteAdmission(const char* failure,std::uint64_t attempted,std::uint64_t published,HRESULT result,const view::ArmDiagnostic& d){
  if(file==INVALID_HANDLE_VALUE)return;char text[1536]{};
  int size=snprintf(text,sizeof(text),"{\"stage\":\"admission-refused\",\"bootMs\":%llu,\"failureStage\":\"%s\",\"attemptedGeneration\":%llu,\"publishedGeneration\":%llu,\"armResult\":%d,\"armStage\":\"%s\",\"armSerial\":%llu,\"armGeneration\":%llu,\"previousArmGeneration\":%llu,\"armObservedAt\":%llu,\"armExpiresAt\":%llu,\"preparation\":%s,\"sourcePid\":%u,\"sourceThread\":%u,\"ownPid\":%u,\"ownThread\":%u,\"bandObserved\":%s,\"expectedOwnBand\":%u,\"observedOwnBand\":%u,\"productionAuthority\":false,\"securityGuardRelaxed\":false}\n",
   static_cast<unsigned long long>(GetTickCount64()),failure,static_cast<unsigned long long>(attempted),static_cast<unsigned long long>(published),int(result),view::ArmStageName(d.stage),static_cast<unsigned long long>(d.serial),static_cast<unsigned long long>(d.generation),static_cast<unsigned long long>(d.previousGeneration),static_cast<unsigned long long>(d.observedAt),static_cast<unsigned long long>(d.expiresAt),d.preparation?"true":"false",d.sourcePid,d.sourceThread,d.ownPid,d.ownThread,d.bandObserved?"true":"false",d.expectedBand,d.observedBand);
  if(size>0&&size<int(sizeof(text))){DWORD written{};WriteFile(file,text,DWORD(size),&written,nullptr);}
 }
 void WriteBroker(const m::BrokerDiagnostic& d,std::uint64_t generation){
  if(file==INVALID_HANDLE_VALUE||!d.pollRefused)return;char text[2048]{};
  int size=snprintf(text,sizeof(text),"{\"stage\":\"band-broker-refused\",\"bootMs\":%llu,\"serial\":%llu,\"generation\":%llu,\"firstSetupStage\":\"%s\",\"setupRefused\":%s,\"win32Valid\":%s,\"win32\":%u,\"pollRefused\":true,\"sharedRead\":%s,\"hookInstalled\":%s,\"received\":%d,\"rejected\":%d,\"refusalMask\":%d,\"permit\":%d,\"bandInvoked\":%d,\"bandReturned\":%d,\"bandError\":%u,\"callbackReadRevision\":1,\"callbackReadReason\":%u,\"callbackReadField\":\"%s\",\"callbackReadBits\":%u,\"bandBeforeRead\":%u,\"bandBefore\":%u,\"bandBeforeError\":%u,\"callbackIssued\":%llu,\"callbackDeadline\":%llu,\"callbackAt\":%llu,\"permitSamplePostRetire\":true}\n",
   static_cast<unsigned long long>(GetTickCount64()),static_cast<unsigned long long>(d.serial),static_cast<unsigned long long>(generation),m::BrokerStageName(d.stage),d.setupRefused?"true":"false",d.win32Valid?"true":"false",d.win32,d.sharedRead?"true":"false",d.hookInstalled?"true":"false",d.received,d.rejected,d.refusalMask,d.permit,d.bandInvoked,d.bandReturned,d.bandError,d.callbackReadReason,playhub_band_read::Name(d.callbackReadReason),d.callbackReadBits,d.bandBeforeRead,d.bandBefore,d.bandBeforeError,static_cast<unsigned long long>(d.callbackIssued),static_cast<unsigned long long>(d.callbackDeadline),static_cast<unsigned long long>(d.callbackAt));
  if(size>0&&size<int(sizeof(text))){DWORD written{};WriteFile(file,text,DWORD(size),&written,nullptr);}
 }

};
struct AuthorityCurrentDiagnostic {
 const char* field{};std::uint64_t generation{},observed{},expected{};bool rectangle{};m::Rect actualRect{},expectedRect{};
 bool Equal(const char* name,std::uint64_t actual,std::uint64_t wanted)noexcept{if(actual==wanted)return true;if(!field){field=name;observed=actual;expected=wanted;}return false;}
 bool Fresh(const char* name,std::uint64_t now,std::uint64_t expires)noexcept{if(now<expires)return true;if(!field){field=name;observed=now;expected=expires;}return false;}
 bool Rectangle(const char* name,const RECT& actual,const m::Rect& wanted)noexcept{
  m::Rect value{actual.left,actual.top,actual.right,actual.bottom};if(m::Same(value,wanted))return true;
  if(!field){field=name;rectangle=true;actualRect=value;expectedRect=wanted;}return false;
 }
};
struct Authority final:m::IAuthority {
 Surface& surface;view::ScopedForegroundView& view;const ValveOverlay& valve;HANDLE game,owner,frame{};DWORD pid;std::uint64_t birth;
 m::SourceLease lease{};bool active{},receipt{};std::uint64_t frames{},receiptFrame{},routedBaseline{};HRESULT present=S_FALSE;
 AuthorityCurrentDiagnostic lastCurrentFailure{},lastCurrentSample{};std::uint64_t currentSampleSerial{},lastCurrentSampleSerial{};
 Authority(Surface& s,view::ScopedForegroundView& v,const ValveOverlay& overlay,HANDLE g,HANDLE o,DWORD p,std::uint64_t b):surface(s),view(v),valve(overlay),game(g),owner(o),pid(p),birth(b){}
 ~Authority(){if(frame)CloseHandle(frame);}
 bool Own()const{DWORD process{};return surface.window&&GetWindowThreadProcessId(surface.window,&process)==GetCurrentThreadId()&&process==GetCurrentProcessId()
  &&GetAncestor(surface.window,GA_ROOT)==surface.window&&!GetWindow(surface.window,GW_OWNER)
  &&GetWindowLongPtrW(surface.window,GWLP_USERDATA)==reinterpret_cast<LONG_PTR>(&surface);}
 bool Current(const m::SourceLease& expected)override{
  AuthorityCurrentDiagnostic failure;failure.generation=expected.generation;const auto sample=++currentSampleSerial;
  auto note=[&](bool current){lastCurrentSample=failure;lastCurrentSampleSerial=sample;if(!current&&failure.field&&failure.generation&&lastCurrentFailure.generation!=failure.generation)lastCurrentFailure=failure;return current;};
  if(!(failure.Equal("active",active,1)&&failure.Equal("same-authority",m::SameAuthority(lease,expected),1)
   &&failure.Equal("valid-expected-lease",m::Valid(expected,GetTickCount64()),1)&&failure.Fresh("owner-lease-expiry",GetTickCount64(),lease.expiresAt)
   &&failure.Equal("own-surface",Own(),1)&&failure.Equal("wrapper-wait",WaitForSingleObject(owner,0),WAIT_TIMEOUT)
   &&failure.Equal("game-current-before",SameGame(game,pid,birth),1)&&failure.Equal("frame-present",frame!=nullptr,1)
   &&failure.Equal("frame-pid",GetProcessId(frame),lease.framePid)&&failure.Equal("frame-birth",proof::Birth(frame),lease.frameBirth)
   &&failure.Equal("frame-wait",WaitForSingleObject(frame,0),WAIT_TIMEOUT)))return note(false);
  HWND source=reinterpret_cast<HWND>(lease.gameWindow);DWORD process{};RECT a{},b{};
  return note(failure.Equal("foreground-before",reinterpret_cast<m::Id>(view.ActualForeground()),reinterpret_cast<m::Id>(source))
   &&failure.Equal("game-window",GameWindow(source,pid),1)&&failure.Equal("game-window-thread",GetWindowThreadProcessId(source,&process),lease.frameThread)
   &&failure.Equal("game-window-pid",process,lease.framePid)&&failure.Equal("game-rect-read",GetWindowRect(source,&a)!=FALSE,1)
   &&failure.Rectangle("game-rect",a,lease.gameRect)&&failure.Equal("surface-rect-read",GetWindowRect(surface.window,&b)!=FALSE,1)
   &&failure.Rectangle("surface-rect",b,lease.sourceRect)
   &&failure.Equal("foreground-after",reinterpret_cast<m::Id>(view.ActualForeground()),reinterpret_cast<m::Id>(source))
   &&failure.Equal("game-current-after",SameGame(game,pid,birth),1)&&failure.Fresh("final-expiry",GetTickCount64(),lease.expiresAt));
 }
 AuthorityCurrentDiagnostic ReadCurrentDiagnostic()const noexcept{return lastCurrentFailure;}
 bool RendererReady(const m::SourceLease& expected)override{auto count=view.ReadCounts();bool route=!count.patchDisabled&&count.expectedSurfaceBand==1&&count.activeGeneration==expected.generation&&count.routed>routedBaseline;
  return api::RendererReceiptReady(receipt,frames,receiptFrame,present,valve.Open(),route,Current(expected));}
 bool Capture(const api::ConfigurationV1& cfg,std::uint64_t generation,const wchar_t* aumid){
  HWND source=view.ActualForeground();if(!Own()||!SameGame(game,pid,birth)||!GameWindow(source,pid))return false;
  DWORD framePid{};DWORD thread=GetWindowThreadProcessId(source,&framePid);if(!thread||!framePid)return false;
  HANDLE retained=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,framePid);if(!retained)return false;
  if(frame)CloseHandle(frame);frame=retained;RECT gameRect{},sourceRect{};DWORD session{};
  if(!GetWindowRect(source,&gameRect)||!GetWindowRect(surface.window,&sourceRect)||!ProcessIdToSessionId(GetCurrentProcessId(),&session))return false;
  // Admission (hash/signature/callback retirement + cold browser) has one
  // fixed, non-renewing budget. Shown renewals retain the configured short cap.
  m::SourceLease next;next.sessionId=cfg.sessionId;next.generation=generation;next.gameId=cfg.gameId;next.expiresAt=GetTickCount64()+view::PreparationLifetimeMs;
  next.ownerPid=GetCurrentProcessId();next.ownerBirth=proof::Birth(GetCurrentProcess());next.gamePid=pid;next.gameBirth=birth;
  next.framePid=framePid;next.frameThread=thread;next.frameBirth=proof::Birth(frame);next.sourceThread=GetCurrentThreadId();next.session=session;
  next.gameWindow=reinterpret_cast<m::Id>(source);next.sourceWindow=reinterpret_cast<m::Id>(surface.window);next.sourceCookie=reinterpret_cast<m::Id>(&surface);
  next.gameRect={gameRect.left,gameRect.top,gameRect.right,gameRect.bottom};next.sourceRect={sourceRect.left,sourceRect.top,sourceRect.right,sourceRect.bottom};
  if(aumid&&*aumid){if(wcsnlen_s(aumid,513)>=513)return false;wcscpy_s(next.expectedFrameAumid.data(),next.expectedFrameAumid.size(),aumid);}
  lease=next;active=true;receipt=false;receiptFrame=frames;return Current(lease);
 }
 view::Request ViewRequest()const{view::Request r;r.authenticatedGuide=true;r.sequence=lease.generation;r.expiresAt=lease.expiresAt;r.retainedGame=game;
  r.gamePid=pid;r.gameBirth=birth;r.exactSource=reinterpret_cast<HWND>(lease.gameWindow);r.ownSurface=surface.window;
  r.expectedFrameAumid=lease.expectedFrameAumid.data();r.ownLifetimeCookie=reinterpret_cast<std::uintptr_t>(&surface);r.surfacePurpose=view::SurfacePurpose::OccludedTextureProbeBand1;return r;}
};
#include "LiveMirrorContinuation.h"
inline void WriteValidity(EventLog& log,std::uint64_t generation,const m::SourceLease& lease,
 const AuthorityCurrentDiagnostic& authority,const m::Presentation::StillDiagnostic& still){
 bool authorityKnown=authority.field&&authority.generation==generation,stillKnown=still.field&&still.generation==generation;
 if(log.file==INVALID_HANDLE_VALUE||!generation||log.validityGenerationWritten==generation||(!authorityKnown&&!stillKnown))return;
 char text[2048]{};int size=snprintf(text,sizeof(text),"{\"stage\":\"mirror-current-refused\",\"bootMs\":%llu,\"generation\":%llu,\"ownerPid\":%u,\"ownerBirth\":\"%llu\",\"gamePid\":%u,\"gameBirth\":\"%llu\",\"gameHwnd\":\"%llu\",\"stillKnown\":%s,\"stillField\":\"%s\",\"stillObserved\":\"%llu\",\"stillExpected\":\"%llu\",\"authorityKnown\":%s,\"authorityField\":\"%s\",\"authorityObserved\":\"%llu\",\"authorityExpected\":\"%llu\",\"rectangle\":%s,\"actualRect\":[%d,%d,%d,%d],\"expectedRect\":[%d,%d,%d,%d],\"diagnosticCopiesOnly\":true,\"securityGuardRelaxed\":false}\n",
  static_cast<unsigned long long>(GetTickCount64()),static_cast<unsigned long long>(generation),lease.ownerPid,static_cast<unsigned long long>(lease.ownerBirth),lease.gamePid,static_cast<unsigned long long>(lease.gameBirth),static_cast<unsigned long long>(lease.gameWindow),
  stillKnown?"true":"false",stillKnown?still.field:"not-observed",stillKnown?static_cast<unsigned long long>(still.observed):0ull,stillKnown?static_cast<unsigned long long>(still.expected):0ull,
  authorityKnown?"true":"false",authorityKnown?authority.field:"not-observed",authorityKnown?static_cast<unsigned long long>(authority.observed):0ull,authorityKnown?static_cast<unsigned long long>(authority.expected):0ull,
  authorityKnown&&authority.rectangle?"true":"false",authority.actualRect.left,authority.actualRect.top,authority.actualRect.right,authority.actualRect.bottom,authority.expectedRect.left,authority.expectedRect.top,authority.expectedRect.right,authority.expectedRect.bottom);
 if(size>0&&size<int(sizeof(text))){DWORD written{};if(WriteFile(log.file,text,DWORD(size),&written,nullptr)&&written==DWORD(size))log.validityGenerationWritten=generation;}
}
}
extern "C" __declspec(dllexport) int32_t WINAPI ConfigureMirrorV1(const playhub::mirror::callsite::ConfigurationV1* config,std::uint32_t bytes,const wchar_t* callback,const wchar_t* runtimeRoot){
 if(!config||bytes!=sizeof(*config))return 0;
 return live_mirror::mailbox.Configure(*config,callback,runtimeRoot)?1:0;
}
extern "C" __declspec(dllexport) int32_t WINAPI ReadMirrorSnapshotV1(playhub::mirror::Snapshot* result,std::uint32_t bytes){
 if(!result||bytes!=sizeof(*result)||result->version!=1||result->size!=sizeof(*result))return 0;
 auto snapshot=live_mirror::mailbox.Read(GetTickCount64());std::memcpy(result,&snapshot,sizeof(snapshot));return 1;
}
extern "C" __declspec(dllexport) int32_t WINAPI ReadMirrorContinuationV1(playhub::mirror::callsite::ContinuationSnapshotV1* result,std::uint32_t bytes){
 if(!result||bytes!=sizeof(*result)||result->version!=1||result->size!=sizeof(*result))return 0;
 auto value=live_mirror::mailbox.ReadContinuation(GetTickCount64());if(!value.phase)return 0;std::memcpy(result,&value,sizeof(value));return 1;
}
extern "C" __declspec(dllexport) int32_t WINAPI QueueMirrorControlV1(const playhub::mirror::callsite::ControlV1* command,std::uint32_t bytes){
 if(!command||bytes!=sizeof(*command))return 0;
 return live_mirror::mailbox.Queue(*command,GetTickCount64())?1:0;
}
static int32_t RunLiveMirror(Surface& surface,const ValveOverlay& valve,HANDLE game,HANDLE owner,DWORD pid,std::uint64_t birth,
 const wchar_t* renderer,const wchar_t* frameAumid,Report report,TakeGuideRequest takeGuideRequest){
 namespace m=playhub::mirror;namespace a=playhub::mirror::callsite;
 a::ConfigurationV1 config;std::wstring callback,runtimeRoot;
 if(!live_mirror::mailbox.Start(config,callback,runtimeRoot))return E_ACCESSDENIED;
 live_mirror::EventLog log;view::ScopedForegroundView foreground;
 auto pin=view::OpenConfiguredRenderer(renderer);HRESULT installed=pin?foreground.Install(pin):E_ACCESSDENIED;
 if(FAILED(installed)){live_mirror::mailbox.Finish({});return installed;}
 auto broker=m::CreateExactBand16Broker(callback.c_str(),runtimeRoot.c_str());auto backend=m::CreateWin32MirrorBackend(broker);
 if(!backend){foreground.Close();live_mirror::mailbox.Finish({});return E_ACCESSDENIED;}
 m::Win32Clock clock;live_mirror::Authority authority(surface,foreground,valve,game,owner,pid,birth);m::Presentation presentation(clock,authority,backend);live_mirror::NativeContinuation continuation(surface,authority,foreground,backend);std::uint64_t guideEpoch=0;unsigned recoveryDepth=0;
 surface.windowTrace.Start(surface.window,reinterpret_cast<std::uintptr_t>(&surface),GetCurrentThreadId(),&foreground,
  [](void* state)noexcept{return static_cast<view::ScopedForegroundView*>(state)->ActualForeground();});
 struct SurfaceTraceLifetime {Surface& surface;~SurfaceTraceLifetime(){surface.windowTrace.Stop();}} traceLifetime{surface};
 HANDLE processes[]{game,owner};std::uint64_t generation=0,lastBrokerDiagnostic=0,lastQuietSerial=0;std::uint32_t lastQuietCount=0,lastCaptureRow=0;HRESULT result=S_OK;int lastScope=0;bool reportedForeground=false;texture::AdmissionBarrier admission;
 auto publish=[&](const char* stage){auto state=presentation.Read();live_mirror::mailbox.PublishPair(state,continuation.Snapshot(clock.Now(),state.cleanupComplete!=0));log.Write(stage,state,foreground.ReadCounts(),authority.frames,authority.present,valve.Open());};
 auto close=[&](m::CloseReason why,bool allowPassive=false){surface.windowTrace.Retiring();auto failure=authority.lastCurrentSample;auto sample=authority.lastCurrentSampleSerial;authority.receipt=false;authority.active=false;foreground.Disarm();
  bool hidden=backend->Hide();if(authority.Own())surface.Hide();
  bool passive=allowPassive&&hidden&&continuation.Suspend(failure,sample,presentation.Read().cleanupComplete!=0);
  if(!passive)continuation.Revoke();
  live_mirror::mailbox.RevokePair(why,clock.Now(),continuation.Snapshot(clock.Now(),false));
  presentation.Close(why);admission.Retire(clock.Now());publish(passive?"passive-foreground-gap":"retired");live_mirror::WriteValidity(log,generation,authority.lease,authority.ReadCurrentDiagnostic(),presentation.ReadStillDiagnostic());log.WriteSurface(surface.windowTrace.Read());log.WriteCaptureSummary(foreground.ReadCaptureDiagnostic());log.WriteObservation(foreground.ReadObservationDiagnostic());};

 report(1,0);publish("passive-configured");log.WriteSurface(surface.windowTrace.Read());log.WriteCaptureSummary(foreground.ReadCaptureDiagnostic());log.WriteObservation(foreground.ReadObservationDiagnostic());
 while(SameGame(game,pid,birth)&&WaitForSingleObject(owner,0)==WAIT_TIMEOUT&&IsWindow(surface.window)){
  DWORD waited=MsgWaitForMultipleObjectsEx(2,processes,authority.active?8:33,QS_ALLINPUT,MWMO_INPUTAVAILABLE);
  if(waited==WAIT_OBJECT_0||waited==WAIT_OBJECT_0+1)break;if(waited==WAIT_FAILED){DWORD error=GetLastError();result=error?HRESULT_FROM_WIN32(error):E_FAIL;break;}
  MSG msg{};for(unsigned i=0;i<64&&PeekMessageW(&msg,nullptr,0,0,PM_REMOVE);i++){TranslateMessage(&msg);DispatchMessageW(&msg);}
  // Back/Valve-close wins before any renewal or late show receipt.
  if((presentation.Read().state==m::State::Shown||(recoveryDepth&&authority.active))&&!valve.Open())close(m::CloseReason::Requested);
  // Only this existing authenticated callback can create a mirror generation.
  bool pendingGuide=takeGuideRequest()!=0;if(pendingGuide){continuation.Revoke();report(13,0);}
  if(admission.Consume(pendingGuide,clock.Now())){++guideEpoch;recoveryDepth=0;
   if(presentation.Read().state!=m::State::Closed){close(m::CloseReason::Requested);}
   else{
    HWND actual=foreground.ActualForeground();
    if(SameGame(game,pid,birth)&&GameWindow(actual,pid)&&authority.Own()){
     log.WriteSurface(surface.windowTrace.Read());surface.windowTrace.Attempt(generation+1,actual);
     HRESULT aligned=surface.Align(actual,false);if(SUCCEEDED(aligned))aligned=surface.Draw();
     if(SUCCEEDED(aligned)&&foreground.ActualForeground()==actual)aligned=surface.Align(actual,true);
     if(SUCCEEDED(aligned)&&foreground.ActualForeground()==actual&&authority.Capture(config,++generation,frameAumid)){
      authority.routedBaseline=foreground.ReadCounts().routed;HRESULT armed=foreground.ArmPreparation(authority.ViewRequest());
      // Keep the same short-circuit order and calls. On refusal hide/revoke
      // first, then log only copies; the published wire still reports gen0 if
      // admission never reached Presentation::Begin's lease assignment.
      const char* failure=nullptr;
      if(FAILED(armed))failure="arm-preparation";
      else if(!authority.Current(authority.lease))failure="authority-current";
      else if(!presentation.Begin(authority.lease))failure="presentation-begin";
      if(failure){auto diagnostic=foreground.ReadArmDiagnostic();close(m::CloseReason::NativeFailure);log.WriteAdmission(failure,generation,presentation.Read().generation,armed,diagnostic);}
      else{surface.windowTrace.Accepted(generation);publish("authenticated-generation-preparing");}
     }else{authority.active=false;surface.Hide();}
    }
   }
  }
  a::ControlV1 control;
  if(live_mirror::mailbox.Take(control)){
   auto observed=presentation.Read();
   if(a::CommandCurrent(control,observed,clock.Now(),config.leaseDurationMs)){
    if(control.action==a::Action::Close)close(m::CloseReason::Requested);
    else{
     bool current=authority.Current(authority.lease);
     if(control.action==a::Action::Renew)current=current&&observed.state==m::State::Shown&&authority.RendererReady(authority.lease);
     if(current&&control.requestedExpiresAt>authority.lease.expiresAt){auto next=authority.lease;next.expiresAt=control.requestedExpiresAt;
      if(!presentation.Renew(next))current=false;
      else{auto request=authority.ViewRequest();request.expiresAt=next.expiresAt;
       if(FAILED(foreground.Renew(request)))current=false;else authority.lease=next;}
     }
     if(!current)close(m::CloseReason::AuthorityLost);
     else if(control.action==a::Action::Show){authority.receipt=true;authority.receiptFrame=authority.frames;publish("renderer-receipt-accepted");}
     else publish("lease-renewed");
    }
   }else publish("stale-command-refused");
  }
  if(authority.active){
   if(!authority.Current(authority.lease)){close(clock.Now()>=authority.lease.expiresAt?m::CloseReason::Expired:m::CloseReason::AuthorityLost,clock.Now()<authority.lease.expiresAt);}
   else{authority.present=surface.Draw();if(authority.present==S_OK)++authority.frames;
    if(FAILED(authority.present)){result=authority.present;close(m::CloseReason::NativeFailure);}}
  }
  presentation.Tick();
  {auto capture=foreground.ReadCaptureDiagnostic();while(lastCaptureRow<capture.rows)log.WriteCapture(capture.records[lastCaptureRow++],capture);}
  const auto diagnostic=broker->Diagnostics();
  if(diagnostic.noInputFailures&&(diagnostic.serial!=lastQuietSerial||diagnostic.noInputFailures!=lastQuietCount)){lastQuietSerial=diagnostic.serial;lastQuietCount=diagnostic.noInputFailures;log.WriteQuietInput(diagnostic,generation);}
  if(diagnostic.pollRefused&&diagnostic.serial&&diagnostic.serial!=lastBrokerDiagnostic){lastBrokerDiagnostic=diagnostic.serial;log.WriteBroker(diagnostic,generation);}
  if(authority.active&&authority.receipt&&presentation.Read().state==m::State::Prepared){
   if(presentation.ShowPrepared(authority.lease)){
    if(!authority.RendererReady(authority.lease))close(m::CloseReason::AuthorityLost);
    else{auto shown=presentation.Read();if(recoveryDepth)continuation.Complete(shown);else continuation.OnOriginalShown(shown,authority.lease,guideEpoch,recoveryDepth);publish("shown-after-receipt-open-route-and-new-present");}
   }
  }
  auto observed=presentation.Read();
  if(authority.active&&(observed.state==m::State::Closed||observed.state==m::State::CleanupPending))close(observed.reason,observed.reason==m::CloseReason::SourceLost||observed.reason==m::CloseReason::AuthorityLost);
  if(continuation.Waiting()){playhub::resume_intent::ContinuationGrant grant;
   if(continuation.TakeResume(presentation.Read().cleanupComplete!=0,grant)){
    HWND actual=foreground.ActualForeground();bool started=false;
    if(actual==reinterpret_cast<HWND>(grant.binding.gameWindow)&&authority.Own()&&presentation.Read().state==m::State::Closed){
     HRESULT aligned=surface.Align(actual,false);if(SUCCEEDED(aligned))aligned=surface.Draw();if(SUCCEEDED(aligned)&&foreground.ActualForeground()==actual)aligned=surface.Align(actual,true);
     if(SUCCEEDED(aligned)&&foreground.ActualForeground()==actual&&authority.Capture(config,++generation,frameAumid)){
      authority.lease.expiresAt=std::min(authority.lease.expiresAt,grant.maximumPreparationExpiry);auto request=authority.ViewRequest();request.authenticatedGuide=false;authority.routedBaseline=foreground.ReadCounts().routed;
      auto armed=foreground.ArmContinuation(request,grant);recoveryDepth=1;
      started=SUCCEEDED(armed)&&authority.Current(authority.lease)&&presentation.Begin(authority.lease);
      if(started){continuation.Resuming(generation,authority.lease.expiresAt);surface.windowTrace.Accepted(generation);publish("fresh-continuation-preparing");}
     }
    }
    if(!started)close(m::CloseReason::NativeFailure);
   }
  }
  {auto current=presentation.Read();live_mirror::mailbox.PublishPair(current,continuation.Snapshot(clock.Now(),current.cleanupComplete!=0));}m::DrainRetainedMirrorCleanup();
  HWND actual=foreground.ActualForeground();bool gameForeground=SameGame(game,pid,birth)&&GameWindow(actual,pid);
  int scope=gameForeground?1:0;if(scope!=lastScope){report(11,scope);lastScope=scope;}
  if(gameForeground!=reportedForeground){report(9,gameForeground?1:0);reportedForeground=gameForeground;}
 }
 close(m::CloseReason::Disposed);
 // A committed foreign callback has no documented hard time bound. Keep its
 // exact source/destination and GUI owner alive, hidden and non-forwarding,
 // until its retirement is proven; never unload code under an in-flight call.
 bool pendingLogged=false;
 while(!presentation.Read().cleanupComplete){
  if(!pendingLogged){publish("cleanup-pending-owner-retained");pendingLogged=true;}
  MSG msg{};for(unsigned i=0;i<64&&PeekMessageW(&msg,nullptr,0,0,PM_REMOVE);i++){TranslateMessage(&msg);DispatchMessageW(&msg);}
  presentation.Tick();m::DrainRetainedMirrorCleanup();live_mirror::mailbox.Publish(presentation.Read());
  if(!presentation.Read().cleanupComplete)MsgWaitForMultipleObjectsEx(0,nullptr,100,QS_ALLINPUT,MWMO_INPUTAVAILABLE);
 }
 surface.Hide();HRESULT disabled=foreground.Close();publish("cleaned-patch-disabled-storage-retained");log.WriteCaptureSummary(foreground.ReadCaptureDiagnostic());log.WriteObservation(foreground.ReadObservationDiagnostic());
 live_mirror::mailbox.Finish(presentation.Read());if(lastScope)report(11,0);if(reportedForeground)report(9,0);
 return FAILED(result)?result:FAILED(disabled)?disabled:S_OK;
}
