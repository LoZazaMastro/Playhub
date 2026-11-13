// SPDX-License-Identifier: GPL-3.0-only
#include "ScopedForegroundView.h"
#include "ForegroundViewPolicy.h"
#include "LastErrorReturn.h"
#include "NonceLifetime.h"
#include "Target.h"
#include "MinHook.h"
#if defined(PLAYHUB_VALVE_WINDOW_OBSERVATION)
#include "ObservationTraceHooks.h"
#endif
#include <intrin.h>
#include <atomic>
#include <algorithm>
#include <memory>
#include <mutex>
#include <bcrypt.h>
namespace view {
namespace {
using Getter=HWND(WINAPI*)();
constexpr wchar_t Property[]=L"Playhub.ForegroundView.OwnNonce";
static void ReleaseNativeNonce(const NonceIdentity& held)noexcept{HWND own=reinterpret_cast<HWND>(held.window);DWORD pid{};
 NonceIdentity now{held.window,static_cast<ULONG_PTR>(GetWindowLongPtrW(own,GWLP_USERDATA)),reinterpret_cast<ULONG_PTR>(GetPropW(own,Property)),GetWindowThreadProcessId(own,&pid)};
 if(SameNonceIdentity(held,now,pid==GetCurrentProcessId()))RemovePropW(own,Property);}
struct Lease {
 proof::Shared source{};HANDLE game{},host{};HWND own{};DWORD ownThread{};wchar_t ownClass[128]{};ULONG_PTR nonce{},ownCookie{};
 bool hosted{};unsigned expectedBand=16;std::uint64_t generation{},expiry{};std::shared_ptr<NonceOwner> nonceOwner;
 ~Lease(){if(game)CloseHandle(game);if(host)CloseHandle(host);}
 bool Current(HWND actual,ArmDiagnostic* diagnostic=nullptr)const noexcept{
  auto refused=[&](ArmStage stage){NoteArmRefusal(diagnostic,stage);return false;};
  if(actual!=reinterpret_cast<HWND>(source.sourceForeground)||!proof::Alive(game,source.gamePid,source.gameBirth)||!proof::Alive(host,source.sourcePid,source.sourceBirth))return refused(ArmStage::OwnSource);
  DWORD pid{},ownPid{};RECT rect{};wchar_t cls[128]{};
  if(GetWindowThreadProcessId(own,&ownPid)!=ownThread||ownPid!=GetCurrentProcessId()||GetAncestor(own,GA_ROOT)!=own||GetWindow(own,GW_OWNER)||!GetClassNameW(own,cls,128)||wcscmp(cls,ownClass)||GetPropW(own,Property)!=reinterpret_cast<HANDLE>(nonce)||static_cast<ULONG_PTR>(GetWindowLongPtrW(own,GWLP_USERDATA))!=ownCookie)return refused(ArmStage::OwnIdentity);
  if(!IsWindowVisible(own)||!IsWindowEnabled(own))return refused(ArmStage::OwnVisibleEnabled);
  if(!proof::DesktopCurrent(ownThread,source.station,source.desktop))return refused(ArmStage::OwnDesktop);
  using GetBand=BOOL(WINAPI*)(HWND,DWORD*);auto band=reinterpret_cast<GetBand>(GetProcAddress(GetModuleHandleW(L"user32.dll"),"GetWindowBand"));DWORD number{};
  if(!band)return refused(ArmStage::OwnBandApi);if(!band(own,&number))return refused(ArmStage::OwnBandRead);
  if(diagnostic){diagnostic->expectedBand=expectedBand;diagnostic->observedBand=number;diagnostic->bandObserved=true;}
  if(number!=expectedBand)return refused(ArmStage::OwnBandMismatch);
  if(hosted){bool current=proof::FastCurrentSource(source,host,game);return current?true:refused(ArmStage::HostedCurrent);}
  HWND window=reinterpret_cast<HWND>(source.sourceForeground);
  if(!(GetWindowThreadProcessId(window,&pid)==source.sourceThread&&pid==source.gamePid&&GetAncestor(window,GA_ROOT)==window&&IsWindowVisible(window)&&IsWindowEnabled(window)&&!IsIconic(window)&&GetClassNameW(window,cls,128)&&wcscmp(cls,source.targetClass)==0&&GetWindowRect(window,&rect)&&proof::SameRect(rect,source.sourceRect)))return refused(ArmStage::NativeWindow);
  if(!proof::CleanGui(source.sourceThread))return refused(ArmStage::NativeCurrentCleanGui);
  return proof::DesktopCurrent(source.sourceThread,source.station,source.desktop)?true:refused(ArmStage::NativeDesktop);
 }
};
static std::mutex control;
static std::shared_ptr<const Lease> active;
static std::shared_ptr<const PinnedRenderer> renderer;
static std::atomic<std::uint64_t> currentGeneration{},lastGeneration{},calls{},routed{},foreignCaller{},inactiveScope{};
static std::atomic<unsigned> inFlight{};
static Getter realGetter{},hookTarget{};
static ScopedForegroundView* installedOwner{};
static TargetIdentity sessionIdentity{};
static std::wstring sessionAumid;
static HMODULE retainedSelf{};
static bool installationUsed=false;
static std::atomic<bool> patchDisabled{true};
static thread_local bool recursive=false;

using CaptureSetter=HWND(WINAPI*)(HWND);using CaptureReleaser=BOOL(WINAPI*)();
static CaptureSetter realCaptureSet{};static Getter realCaptureGet{};static CaptureReleaser realCaptureRelease{};
static std::array<void*,3> captureTargets{};static std::atomic<bool> captureReady{};
static thread_local CaptureLedger captureLedger;static thread_local bool captureRecursion{};
static std::mutex captureDiagnosticMutex;static CaptureDiagnostic captureDiagnostic;
static CaptureKey CaptureGrant(const std::shared_ptr<const Lease>& scope,bool pinned,HWND requested){
 if(!captureReady.load(std::memory_order_acquire)||!pinned||!scope||scope->expectedBand!=1||requested!=scope->own||GetCurrentThreadId()!=scope->ownThread)return {};
 HWND actual=realGetter();if(!scope->Current(actual)||realGetter()!=actual||currentGeneration.load(std::memory_order_acquire)!=scope->generation||GetTickCount64()>=scope->expiry)return {};
 return {scope->generation,scope->nonce,reinterpret_cast<CaptureId>(scope->own),scope->ownCookie,scope->ownThread};
}
struct CaptureOps {
 const std::shared_ptr<const Lease>& scope;bool pinned;DWORD error;bool virtualized=true;
 CaptureKey Grant(CaptureId w){return CaptureGrant(scope,pinned,reinterpret_cast<HWND>(w));}
 CaptureId SnapshotCapture(){SetLastError(error);return reinterpret_cast<CaptureId>(realCaptureGet());}
 CaptureId Set(CaptureId w){virtualized=false;SetLastError(error);auto result=realCaptureSet(reinterpret_cast<HWND>(w));error=GetLastError();return reinterpret_cast<CaptureId>(result);}
 CaptureId Get(){virtualized=false;SetLastError(error);auto result=realCaptureGet();error=GetLastError();return reinterpret_cast<CaptureId>(result);}
 bool Release(){virtualized=false;SetLastError(error);bool result=realCaptureRelease()!=FALSE;error=GetLastError();return result;}
};
struct CaptureFlight {CaptureFlight(){inFlight.fetch_add(1,std::memory_order_acq_rel);captureRecursion=true;}~CaptureFlight(){captureRecursion=false;inFlight.fetch_sub(1,std::memory_order_acq_rel);}};
__declspec(noinline) static HWND WINAPI CaptureSetHook(HWND requested){
 if(captureRecursion)return realCaptureSet(requested);CaptureFlight flight;const void* caller=_ReturnAddress();DWORD incoming=GetLastError();
 auto pin=std::atomic_load(&renderer);auto scope=std::atomic_load(&active);bool pinned=pin&&pin->Caller(caller);HWND before=pinned?realGetter():nullptr;
 CaptureOps ops{scope,pinned,incoming};auto result=CaptureSet(captureLedger,pinned,reinterpret_cast<CaptureId>(requested),ops);HWND after=pinned?realGetter():nullptr;
 {std::lock_guard<std::mutex> lock(captureDiagnosticMutex);++captureDiagnostic.setCalls;if(ops.virtualized)++captureDiagnostic.virtualSets;else ++captureDiagnostic.realSets;
  if(pinned&&captureDiagnostic.rows<captureDiagnostic.records.size()){auto& r=captureDiagnostic.records[captureDiagnostic.rows++];r={captureDiagnostic.rows,GetTickCount64(),scope?scope->generation:0,reinterpret_cast<CaptureId>(requested),result,reinterpret_cast<CaptureId>(before),reinterpret_cast<CaptureId>(after),GetCurrentThreadId(),true,ops.virtualized};}}
 return FinishReturn(reinterpret_cast<HWND>(result),ops.error,scope,pin);
}
__declspec(noinline) static HWND WINAPI CaptureGetHook(){
 if(captureRecursion)return realCaptureGet();CaptureFlight flight;const void* caller=_ReturnAddress();DWORD incoming=GetLastError();
 auto pin=std::atomic_load(&renderer);auto scope=std::atomic_load(&active);bool pinned=pin&&pin->Caller(caller);CaptureOps ops{scope,pinned,incoming};auto result=CaptureGet(captureLedger,pinned,ops);
 {std::lock_guard<std::mutex> lock(captureDiagnosticMutex);++captureDiagnostic.getCalls;if(ops.virtualized)++captureDiagnostic.virtualGets;}
 return FinishReturn(reinterpret_cast<HWND>(result),ops.error,scope,pin);
}
__declspec(noinline) static BOOL WINAPI CaptureReleaseHook(){
 if(captureRecursion)return realCaptureRelease();CaptureFlight flight;const void* caller=_ReturnAddress();DWORD incoming=GetLastError();
 auto pin=std::atomic_load(&renderer);auto scope=std::atomic_load(&active);bool pinned=pin&&pin->Caller(caller);CaptureOps ops{scope,pinned,incoming};bool result=CaptureRelease(captureLedger,pinned,ops);
 {std::lock_guard<std::mutex> lock(captureDiagnosticMutex);++captureDiagnostic.releaseCalls;if(ops.virtualized)++captureDiagnostic.virtualReleases;}
 return FinishReturn(result?TRUE:FALSE,ops.error,scope,pin);
}
static bool CreateCaptureHooks(HMODULE user){
 const char* names[]{"SetCapture","GetCapture","ReleaseCapture"};void* detours[]{reinterpret_cast<void*>(&CaptureSetHook),reinterpret_cast<void*>(&CaptureGetHook),reinterpret_cast<void*>(&CaptureReleaseHook)};
 void** originals[]{reinterpret_cast<void**>(&realCaptureSet),reinterpret_cast<void**>(&realCaptureGet),reinterpret_cast<void**>(&realCaptureRelease)};
 for(unsigned i=0;i<3;++i){auto target=reinterpret_cast<void*>(GetProcAddress(user,names[i]));MEMORY_BASIC_INFORMATION m{};
  if(!target||!VirtualQuery(target,&m,sizeof(m))||m.AllocationBase!=user||m.Type!=MEM_IMAGE||MH_CreateHook(target,detours[i],originals[i])!=MH_OK){for(unsigned j=0;j<i;++j){MH_RemoveHook(captureTargets[j]);captureTargets[j]=nullptr;}return false;}captureTargets[i]=target;}
 return true;
}
static bool DisableCaptureHooks(){captureReady.store(false,std::memory_order_release);bool disabled=true;for(auto target:captureTargets)if(target){MH_QueueDisableHook(target);auto s=MH_DisableHook(target);disabled=(s==MH_OK||s==MH_ERROR_DISABLED)&&disabled;}return disabled;}
#if defined(PLAYHUB_VALVE_WINDOW_OBSERVATION)
// Logging only. A null/unrelated requested HWND may be observed, never granted.
// Full existing Current still requires the REAL game foreground; FG0 is not
// admitted as ownership. The post-original sample can therefore become false.
static bool ReadObservationScope(const void* caller,HWND,observation::Scope& out)noexcept{
 try{
  auto pin=std::atomic_load(&renderer);if(!pin||!pin->Caller(caller))return false;out.callerPinned=true;
  auto held=std::atomic_load(&active);if(!held||held->expectedBand!=1||!realGetter)return false;
  out.generation=held->generation;out.ownWindow=reinterpret_cast<std::uintptr_t>(held->own);out.ownCookie=held->ownCookie;
  out.ownerPid=GetCurrentProcessId();out.ownerBirth=proof::Birth(GetCurrentProcess());out.ownThread=held->ownThread;
  out.gamePid=held->source.gamePid;out.gameBirth=held->source.gameBirth;out.gameWindow=held->source.sourceForeground;
  out.framePid=held->source.sourcePid;out.frameBirth=held->source.sourceBirth;out.frameThread=held->source.sourceThread;out.expiresAt=held->expiry;
  HWND actual=realGetter();if(!held->Current(actual))return false;
  return std::atomic_load(&active).get()==held.get()&&currentGeneration.load(std::memory_order_acquire)==held->generation
   &&GetTickCount64()<held->expiry&&realGetter()==actual;
 }catch(...){return false;}
}
#endif


__declspec(noinline) static HWND WINAPI Foreground(){
 const void* caller=_ReturnAddress();inFlight.fetch_add(1,std::memory_order_acq_rel);
 struct Exit{~Exit(){inFlight.fetch_sub(1,std::memory_order_acq_rel);}} exit;
 Getter original=realGetter;if(!original)return nullptr;if(recursive)return original();
 recursive=true;struct Recursion{~Recursion(){recursive=false;}} recursion;
 HWND actual=original();DWORD error=GetLastError();calls.fetch_add(1,std::memory_order_relaxed);
 auto pin=std::atomic_load(&renderer);auto scope=std::atomic_load(&active);HWND answer=actual;
 if(!pin||!pin->Caller(caller))foreignCaller.fetch_add(1,std::memory_order_relaxed);
 else if(scope){
  bool source=scope->Current(actual);auto now=GetTickCount64();
  Facts facts{true,true,true,source,source,source,currentGeneration.load(std::memory_order_acquire)==scope->generation,now<scope->expiry};
  if(Route(facts)&&original()==actual&&currentGeneration.load(std::memory_order_acquire)==scope->generation&&GetTickCount64()<scope->expiry){answer=scope->own;routed.fetch_add(1,std::memory_order_relaxed);}else inactiveScope.fetch_add(1,std::memory_order_relaxed);
 }else inactiveScope.fetch_add(1,std::memory_order_relaxed);
 return FinishReturn(answer,error,scope,pin);
}
static bool Duplicate(HANDLE from,HANDLE& to){return from&&DuplicateHandle(GetCurrentProcess(),from,GetCurrentProcess(),&to,0,FALSE,DUPLICATE_SAME_ACCESS)!=FALSE;}
static bool SameUser(HANDLE left,HANDLE right){
 auto read=[](HANDLE process,BYTE (&sid)[SECURITY_MAX_SID_SIZE],DWORD& length){HANDLE token{};if(!OpenProcessToken(process,TOKEN_QUERY,&token))return false;BYTE data[1024]{};DWORD count{};bool ok=GetTokenInformation(token,TokenUser,data,sizeof(data),&count)!=FALSE;if(ok){auto value=reinterpret_cast<TOKEN_USER*>(data);ok=IsValidSid(value->User.Sid)!=FALSE;if(ok){length=GetLengthSid(value->User.Sid);ok=length<=sizeof(sid)&&CopySid(sizeof(sid),sid,value->User.Sid)!=FALSE;}}CloseHandle(token);return ok;};
 BYTE a[SECURITY_MAX_SID_SIZE]{},b[SECURITY_MAX_SID_SIZE]{};DWORD as{},bs{};return read(left,a,as)&&read(right,b,bs)&&as==bs&&EqualSid(a,b);
}
static std::shared_ptr<const Lease> Capture(const Request& request,ULONG_PTR replaceOwnNonce,std::shared_ptr<NonceOwner> reuseNonce={},ArmDiagnostic* diagnostic=nullptr){
 auto refused=[&](ArmStage stage)->std::shared_ptr<const Lease>{NoteArmRefusal(diagnostic,stage);return {};};
 auto result=std::make_shared<Lease>();auto& s=result->source;s.gamePid=request.gamePid;s.gameBirth=request.gameBirth;s.sourceForeground=reinterpret_cast<std::uint64_t>(request.exactSource);result->own=request.ownSurface;result->ownCookie=request.ownLifetimeCookie;result->generation=request.sequence;result->expiry=request.expiresAt;
 if(!request.exactSource||!request.ownSurface||request.exactSource==request.ownSurface||!Duplicate(request.retainedGame,result->game)||!proof::Alive(result->game,request.gamePid,request.gameBirth)||!ProcessIdToSessionId(GetCurrentProcessId(),&s.session))return refused(ArmStage::CaptureHandles);
 if(request.surfacePurpose==SurfacePurpose::VisibleBand16)result->expectedBand=16;else if(request.surfacePurpose==SurfacePurpose::OccludedTextureProbeBand1)result->expectedBand=1;else return refused(ArmStage::CapturePurpose);
 if(!SameUser(GetCurrentProcess(),result->game))return refused(ArmStage::GameUser);
 DWORD session{},ownPid{};s.sourceThread=GetWindowThreadProcessId(request.exactSource,&s.sourcePid);result->ownThread=GetWindowThreadProcessId(request.ownSurface,&ownPid);s.shellThread=result->ownThread;
 if(diagnostic){diagnostic->sourcePid=s.sourcePid;diagnostic->sourceThread=s.sourceThread;diagnostic->ownPid=ownPid;diagnostic->ownThread=result->ownThread;}
 if(!s.sourceThread||!s.sourcePid||ownPid!=GetCurrentProcessId()||result->ownThread!=GetCurrentThreadId()||!result->ownCookie||static_cast<ULONG_PTR>(GetWindowLongPtrW(request.ownSurface,GWLP_USERDATA))!=result->ownCookie||GetAncestor(request.ownSurface,GA_ROOT)!=request.ownSurface||GetWindow(request.ownSurface,GW_OWNER)||!GetClassNameW(request.ownSurface,result->ownClass,128)||!GetWindowRect(request.exactSource,&s.sourceRect)||!ProcessIdToSessionId(s.gamePid,&session)||session!=s.session||!ProcessIdToSessionId(s.sourcePid,&session)||session!=s.session)return refused(ArmStage::WindowIdentity);
 result->host=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,s.sourcePid);s.sourceBirth=proof::Birth(result->host);
 if(!proof::Alive(result->host,s.sourcePid,s.sourceBirth)||!SameUser(GetCurrentProcess(),result->host)||!proof::ObjectName(GetProcessWindowStation(),s.station,64)||wcscmp(s.station,L"WinSta0")||!proof::ObjectName(GetThreadDesktop(GetCurrentThreadId()),s.desktop,64))return refused(ArmStage::HostIdentity);
 result->hosted=s.sourcePid!=s.gamePid;
 if(result->hosted){
  if(!request.expectedFrameAumid||wcslen(request.expectedFrameAumid)<3||wcslen(request.expectedFrameAumid)>512)return refused(ArmStage::HostedAumid);wcscpy_s(s.aumid,request.expectedFrameAumid);
  GUITHREADINFO gui{sizeof(gui)};if(!GetGUIThreadInfo(s.sourceThread,&gui)||!gui.hwndFocus)return refused(ArmStage::HostedGui);s.sourceCore=reinterpret_cast<std::uint64_t>(gui.hwndFocus);DWORD pid{};s.gameThread=GetWindowThreadProcessId(gui.hwndFocus,&pid);if(pid!=s.gamePid||!proof::CurrentSource(s))return refused(ArmStage::HostedCore);
 }else{if(!GetClassNameW(request.exactSource,s.targetClass,128))return refused(ArmStage::NativeClass);if(!proof::CleanGui(s.sourceThread))return refused(ArmStage::NativeCleanGui);}
 if(reuseNonce){
  const auto& token=reuseNonce->Identity();
  if(token.window!=reinterpret_cast<std::uintptr_t>(result->own)||token.thread!=result->ownThread||token.cookie!=result->ownCookie
   ||!token.nonce||GetPropW(result->own,Property)!=reinterpret_cast<HANDLE>(token.nonce))return refused(ArmStage::NonceReuse);
  result->nonceOwner=std::move(reuseNonce);result->nonce=result->nonceOwner->Identity().nonce;
 }else{
  if(BCryptGenRandom(nullptr,reinterpret_cast<PUCHAR>(&result->nonce),sizeof(result->nonce),BCRYPT_USE_SYSTEM_PREFERRED_RNG)<0||!result->nonce)return refused(ArmStage::NonceRandom);
  auto token=std::make_shared<NonceOwner>(NonceIdentity{reinterpret_cast<std::uintptr_t>(result->own),result->ownCookie,result->nonce,result->ownThread},ReleaseNativeNonce);
  HANDLE existing=GetPropW(request.ownSurface,Property);if((existing&&existing!=reinterpret_cast<HANDLE>(replaceOwnNonce))||!SetPropW(request.ownSurface,Property,reinterpret_cast<HANDLE>(result->nonce)))return refused(ArmStage::NonceProperty);
  result->nonceOwner=std::move(token);
 }
 if(!result->Current(realGetter(),diagnostic))return {};if(GetTickCount64()>=request.expiresAt)return refused(ArmStage::Expired);return result;
}
}
HRESULT ScopedForegroundView::Install(std::shared_ptr<const PinnedRenderer> pin){
 std::lock_guard<std::mutex> lock(control);if(installed||installedOwner||installationUsed||!pin)return HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS);
 HMODULE user=GetModuleHandleW(L"user32.dll");auto target=reinterpret_cast<Getter>(GetProcAddress(user,"GetForegroundWindow"));MEMORY_BASIC_INFORMATION memory{};if(!target||!VirtualQuery(reinterpret_cast<void*>(target),&memory,sizeof(memory))||memory.AllocationBase!=user||memory.Type!=MEM_IMAGE)return E_ACCESSDENIED;
 auto status=MH_Initialize();ownsMinHook=status==MH_OK;if(status!=MH_OK&&status!=MH_ERROR_ALREADY_INITIALIZED)return E_FAIL;
 if(MH_CreateHook(reinterpret_cast<void*>(target),reinterpret_cast<void*>(&Foreground),reinterpret_cast<void**>(&realGetter))!=MH_OK){if(ownsMinHook)MH_Uninitialize();ownsMinHook=false;return E_FAIL;}
 if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,reinterpret_cast<const wchar_t*>(&Foreground),&retainedSelf)){MH_RemoveHook(reinterpret_cast<void*>(target));if(ownsMinHook)MH_Uninitialize();ownsMinHook=false;return E_FAIL;}
 installationUsed=true;
 std::atomic_store(&renderer,std::move(pin));hookTarget=target;installedOwner=this;
 if(!CreateCaptureHooks(user)){MH_RemoveHook(reinterpret_cast<void*>(target));installedOwner=nullptr;hookTarget=realGetter=nullptr;std::atomic_store(&renderer,std::shared_ptr<const PinnedRenderer>{});if(ownsMinHook)MH_Uninitialize();ownsMinHook=false;return E_FAIL;}
 bool queued=MH_QueueEnableHook(reinterpret_cast<void*>(target))==MH_OK;for(auto extra:captureTargets)queued=(MH_QueueEnableHook(extra)==MH_OK)&&queued;
 // No virtual Set is admitted before all three capture hooks are enabled.
 // On partial enable failure retain every trampoline/module, revoke and disable.
 installed=true;installationUsed=true;patchDisabled.store(false);
 if(!queued||MH_ApplyQueued()!=MH_OK){DisableCaptureHooks();MH_QueueDisableHook(reinterpret_cast<void*>(target));MH_DisableHook(reinterpret_cast<void*>(target));return E_FAIL;}
 captureReady.store(true,std::memory_order_release);
#if defined(PLAYHUB_VALVE_WINDOW_OBSERVATION)
 // A diagnostic install failure changes no functional admission or original API.
 (void)observation::InstallHooks(&ReadObservationScope,realGetter);
#endif
 return S_OK;
}
HRESULT ScopedForegroundView::Arm(const Request& request){
 return ArmCurrent(request,false);
}
HRESULT ScopedForegroundView::ArmPreparation(const Request& request){
 return ArmCurrent(request,true);
}
HRESULT ScopedForegroundView::ArmContinuation(const Request& request,playhub::resume_intent::ContinuationGrant& grant){
 const auto now=GetTickCount64();const auto& b=grant.binding;
 bool valid=b.Valid()&&!request.authenticatedGuide&&grant.resumeDepth==1&&grant.guideEpoch&&grant.retiredGeneration&&grant.minimumGeneration>grant.retiredGeneration&&request.sequence>=grant.minimumGeneration
  &&now>=grant.admissionStartedAt&&now<grant.maximumPreparationExpiry&&request.expiresAt>now&&request.expiresAt<=grant.maximumPreparationExpiry&&request.expiresAt-now<=PreparationLifetimeMs
  &&request.gamePid==b.gamePid&&request.gameBirth==b.gameBirth&&reinterpret_cast<std::uintptr_t>(request.exactSource)==b.gameWindow
  &&reinterpret_cast<std::uintptr_t>(request.ownSurface)==b.sourceWindow&&request.ownLifetimeCookie==b.sourceCookie&&GetCurrentProcessId()==b.ownerPid&&proof::Birth(GetCurrentProcess())==b.ownerBirth;
 grant.minimumGeneration=UINT64_MAX;grant.guideEpoch=0; // consume before any slow or reentrant native getter.
 if(!valid)return E_ACCESSDENIED;
 return ArmCurrent(request,true,true);
}
HRESULT ScopedForegroundView::ArmCurrent(const Request& request,bool preparation,bool continuation){
 std::lock_guard<std::mutex> lock(control);auto now=GetTickCount64();
 auto previousGeneration=lastGeneration.load();bool fresh=preparation?FreshPreparationRequest(previousGeneration,request.sequence,now,request.expiresAt):FreshRequest(previousGeneration,request.sequence,now,request.expiresAt);
 auto serial=armDiagnostic.serial+1;armDiagnostic={};armDiagnostic.serial=serial;armDiagnostic.generation=request.sequence;armDiagnostic.previousGeneration=previousGeneration;armDiagnostic.observedAt=now;armDiagnostic.expiresAt=request.expiresAt;armDiagnostic.preparation=preparation;
 auto refused=[&](ArmStage stage){NoteArmRefusal(&armDiagnostic,stage);return E_ACCESSDENIED;};
 if(!installed)return refused(ArmStage::NotInstalled);if(installedOwner!=this)return refused(ArmStage::WrongController);
 if(!request.authenticatedGuide&&!continuation)return refused(ArmStage::Unauthenticated);if(!fresh)return refused(ArmStage::NotFresh);
 if(preparation&&request.surfacePurpose!=SurfacePurpose::OccludedTextureProbeBand1)return refused(ArmStage::WrongPreparationPurpose);
 lastGeneration.store(request.sequence);auto previous=std::atomic_load(&active);currentGeneration.store(0,std::memory_order_release);std::atomic_store(&active,std::shared_ptr<const Lease>{});
 TargetIdentity incoming{request.gamePid,request.gameBirth,reinterpret_cast<std::uintptr_t>(request.ownSurface),request.ownLifetimeCookie};std::wstring aumid=request.expectedFrameAumid?request.expectedFrameAumid:L"";
 if(sessionIdentity.pid&&(!SameTarget(sessionIdentity,incoming)||sessionAumid!=aumid))return refused(ArmStage::SessionIdentity);
 ULONG_PTR replace=previous&&previous->own==request.ownSurface?previous->nonce:0;
 auto lease=Capture(request,replace,{},&armDiagnostic);if(!lease)return E_ACCESSDENIED;
 if(!sessionIdentity.pid){sessionIdentity=incoming;sessionAumid=std::move(aumid);}
 currentGeneration.store(request.sequence,std::memory_order_release);std::atomic_store(&active,std::move(lease));armDiagnostic.stage=ArmStage::Accepted;return S_OK;
}
void ScopedForegroundView::Disarm()noexcept{std::lock_guard<std::mutex> lock(control);if(installedOwner!=this)return;currentGeneration.store(0,std::memory_order_release);std::atomic_store(&active,std::shared_ptr<const Lease>{});}
HRESULT ScopedForegroundView::Renew(const Request& request){
 std::lock_guard<std::mutex> lock(control);auto previous=std::atomic_load(&active);
 if(!installed||installedOwner!=this||!request.authenticatedGuide||!previous)return E_ACCESSDENIED;
 TargetIdentity incoming{request.gamePid,request.gameBirth,reinterpret_cast<std::uintptr_t>(request.ownSurface),request.ownLifetimeCookie};
 std::wstring aumid=request.expectedFrameAumid?request.expectedFrameAumid:L"";
 bool same=SameTarget(sessionIdentity,incoming)&&sessionAumid==aumid&&previous->own==request.ownSurface
  &&previous->source.sourceForeground==reinterpret_cast<std::uintptr_t>(request.exactSource)
  &&previous->expectedBand==(request.surfacePurpose==SurfacePurpose::OccludedTextureProbeBand1?1u:16u);
 if(!RenewableRequest(currentGeneration.load(),request.sequence,GetTickCount64(),previous->expiry,request.expiresAt,same))return E_ACCESSDENIED;
 // Keep the old, independently fail-closed view valid while slow identity
 // getters run. The same nonce is shared; old Lease destruction cannot remove
 // the new lease's property. No call observes a synthetic revoke/reattach gap.
 std::shared_ptr<const Lease> lease;try{lease=Capture(request,previous->nonce,previous->nonceOwner);}catch(...){
  currentGeneration.store(0,std::memory_order_release);std::atomic_store(&active,std::shared_ptr<const Lease>{});return E_OUTOFMEMORY;
 }
 const auto& old=previous->source;
 bool identity=lease&&SameRenewalSource({lease->source.sourcePid,lease->source.sourceThread,lease->source.gameThread,lease->source.sourceBirth,lease->source.sourceCore,lease->hosted},
  {old.sourcePid,old.sourceThread,old.gameThread,old.sourceBirth,old.sourceCore,previous->hosted})
  &&proof::SameRect(lease->source.sourceRect,old.sourceRect)&&wcscmp(lease->source.station,old.station)==0
  &&wcscmp(lease->source.desktop,old.desktop)==0&&wcscmp(lease->source.targetClass,old.targetClass)==0;
 if(!identity||!previous->Current(realGetter())||GetTickCount64()>=previous->expiry||GetTickCount64()>=request.expiresAt){
  currentGeneration.store(0,std::memory_order_release);std::atomic_store(&active,std::shared_ptr<const Lease>{});return E_ACCESSDENIED;
 }
 std::atomic_store(&active,std::move(lease));return S_OK;
}
HWND ScopedForegroundView::ActualForeground()const noexcept{return realGetter?realGetter():GetForegroundWindow();}
ArmDiagnostic ScopedForegroundView::ReadArmDiagnostic()const{std::lock_guard<std::mutex> lock(control);return armDiagnostic;}
CaptureDiagnostic ScopedForegroundView::ReadCaptureDiagnostic()const{std::lock_guard<std::mutex> lock(captureDiagnosticMutex);return captureDiagnostic;}
observation::Diagnostic ScopedForegroundView::ReadObservationDiagnostic()const noexcept{
#if defined(PLAYHUB_VALVE_WINDOW_OBSERVATION)
 return observation::Read();
#else
 return {};
#endif
}

Counts ScopedForegroundView::ReadCounts()const noexcept{auto lease=std::atomic_load(&active);return {calls.load(),routed.load(),foreignCaller.load(),inactiveScope.load(),inFlight.load(),patchDisabled.load(),retainedSelf!=nullptr,lease?lease->expectedBand:0,lease?lease->generation:0};}
HRESULT ScopedForegroundView::Close(unsigned budget)noexcept{
 std::lock_guard<std::mutex> lock(control);if(!installed)return S_FALSE;if(installedOwner!=this)return E_ACCESSDENIED;currentGeneration.store(0,std::memory_order_release);std::atomic_store(&active,std::shared_ptr<const Lease>{});
 bool observedDisabled=true;
#if defined(PLAYHUB_VALVE_WINDOW_OBSERVATION)
 observedDisabled=observation::DisableHooks();
#endif
 auto observationFlights=[](){
#if defined(PLAYHUB_VALVE_WINDOW_OBSERVATION)
 return observation::InFlight();
#else
 return 0u;
#endif
 };
 bool captureDisabled=DisableCaptureHooks()&&observedDisabled;MH_QueueDisableHook(reinterpret_cast<void*>(hookTarget));auto status=MH_DisableHook(reinterpret_cast<void*>(hookTarget));bool disabled=captureDisabled&&(status==MH_OK||status==MH_ERROR_DISABLED);patchDisabled.store(disabled);ULONGLONG until=GetTickCount64()+std::min(budget,1000u);while(disabled&&(inFlight.load(std::memory_order_acquire)||observationFlights())&&GetTickCount64()<until)Sleep(1);
 if(!disabled||inFlight.load(std::memory_order_acquire)||observationFlights())return HRESULT_FROM_WIN32(ERROR_BUSY);
 // A thread may be in the detour prologue before inFlight increments. Therefore
 // never free the trampoline or this module's acquired reference here. No CPU
 // loop/worker is retained, and the patch is disabled; process exit frees storage.
 installed=false;installedOwner=nullptr;return S_OK;
}
ScopedForegroundView::~ScopedForegroundView(){Close();}
}
