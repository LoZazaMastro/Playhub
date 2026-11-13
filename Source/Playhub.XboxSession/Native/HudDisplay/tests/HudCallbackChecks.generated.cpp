
// Generated from the complete, unchanged callback Hook.cpp. Native APIs below
// are inert fakes. This proves admission/commit flow and typed lowering, not OS
// bands, signatures, physical WPF visibility or actual Explorer execution.
#include "BandAuthorization.h"
#include "band/Protocol.h"
#include "../OverlayMirror/callback-v3/Protocol.h"
#include <iostream>
#include <stdexcept>
#include <string>
#include <cstring>
#include <functional>
namespace test {
namespace wire=::proof;
static wire::Shared s{};static DWORD error{};static bool allow[12];static int writes{},downstream{},checks{};
static HWND const gameFrame=reinterpret_cast<HWND>(600),wpf=reinterpret_cast<HWND>(700),target=reinterpret_cast<HWND>(800),shellWindow=reinterpret_cast<HWND>(900);
static HMODULE const module=reinterpret_cast<HMODULE>(1000);
static std::function<void()> bandRead;
void Check(bool value,const char* text){++checks;if(!value)throw std::runtime_error(text);}
DWORD Fake_GetLastError(){return error;}void Fake_SetLastError(DWORD n){error=n;}
ULONGLONG Fake_GetTickCount64(){return 1010;}
DWORD Fake_GetModuleFileNameW(HMODULE,wchar_t* out,DWORD cap){if(!allow[1])return 0;wcscpy_s(out,cap,s.modulePath);return DWORD(wcslen(out));}
HMODULE Fake_GetModuleHandleW(const wchar_t*){return module;}
BOOL WINAPI FakeGetBandFn(HWND w,DWORD* b){if(bandRead)bandRead();if(w!=target)return FALSE;*b=writes?16:1;return TRUE;}
BOOL WINAPI FakeSetBandFn(HWND w,HWND,DWORD b){if(w!=target||b!=16)return FALSE;++writes;return TRUE;}
FARPROC Fake_GetProcAddress(HMODULE,const char* name){if(!allow[10])return nullptr;return strcmp(name,"GetWindowBand")==0?reinterpret_cast<FARPROC>(&FakeGetBandFn):strcmp(name,"SetWindowBand")==0?reinterpret_cast<FARPROC>(&FakeSetBandFn):nullptr;}
SIZE_T Fake_VirtualQuery(const void*,MEMORY_BASIC_INFORMATION* p,SIZE_T){p->AllocationBase=module;p->State=MEM_COMMIT;p->Protect=PAGE_EXECUTE_READ;return sizeof(*p);}
HANDLE Fake_OpenProcess(DWORD,BOOL,DWORD pid){return reinterpret_cast<HANDLE>(std::uintptr_t(pid));}
DWORD Fake_GetWindowThreadProcessId(HWND w,DWORD* pid){*pid=w==target?s.controllerPid:s.sourcePid;return w==target?s.targetThread:s.sourceThread;}
HANDLE Fake_GetPropW(HWND,const wchar_t* name){return reinterpret_cast<HANDLE>(wcsstr(name,L"Low")?s.nonceLow:s.nonceHigh);}
BOOL Fake_IsWindowVisible(HWND){return FALSE;}
HWND Fake_GetForegroundWindow(){return allow[7]?gameFrame:wpf;}
DWORD Fake_GetCurrentThreadId(){return s.shellThread;}
DWORD Fake_GetCurrentProcessId(){return s.shellPid;}
HANDLE Fake_GetCurrentProcess(){return reinterpret_cast<HANDLE>(std::uintptr_t(s.shellPid));}
HANDLE Fake_OpenFileMappingW(DWORD,BOOL,const wchar_t*){return reinterpret_cast<HANDLE>(1);}
void* Fake_MapViewOfFile(HANDLE,DWORD,DWORD,DWORD,SIZE_T){return &s;}
BOOL Fake_UnmapViewOfFile(const void*){return TRUE;}
BOOL Fake_CloseHandle(HANDLE){return TRUE;}
LRESULT Fake_CallNextHookEx(HHOOK,int,WPARAM,LPARAM){++downstream;return 7;}
HANDLE Fake_OpenEventW(DWORD,BOOL,const wchar_t*){return reinterpret_cast<HANDLE>(1);}
BOOL Fake_SetEvent(HANDLE){return TRUE;}
BOOL Fake_IsValidSid(PSID){return allow[5];}
BOOL Fake_EqualSid(PSID,PSID){return allow[5];}
namespace proof {
using namespace ::proof;
struct Security{BYTE sid[SECURITY_MAX_SID_SIZE]{};DWORD sidLength{},session{},integrity{};};
bool Alive(HANDLE,DWORD,std::uint64_t){return allow[2];}
std::uint64_t Birth(HANDLE){return s.shellBirth;}
bool Image(HANDLE,const wchar_t*){return allow[3];}
bool ExactShell(HWND w,DWORD pid,DWORD thread){return allow[4]&&w==shellWindow&&pid==s.shellPid&&thread==s.shellThread;}
bool ReadSecurity(HANDLE,Security& p){p.session=s.session;p.integrity=s.integrity;p.sidLength=s.sidLength;return allow[5];}
bool SameSecurity(const Security&,const Security&){return allow[5];}
bool DesktopCurrent(DWORD,const wchar_t*,const wchar_t*){return allow[6];}
bool ExactForeground(HWND w,DWORD pid,DWORD thread,HANDLE,std::uint64_t birth){return allow[7]&&w==gameFrame&&pid==s.sourcePid&&thread==s.sourceThread&&birth==s.sourceBirth&&Fake_GetForegroundWindow()==gameFrame;}
bool CurrentSource(const Shared& v){return allow[8]&&v.sourceForeground==reinterpret_cast<std::uintptr_t>(gameFrame)&&v.sourceForeground!=reinterpret_cast<std::uintptr_t>(wpf)&&v.gamePid==20&&v.sourcePid==30&&v.sourceBirth==31&&v.gameBirth==21&&v.sourceCore==610&&v.gameThread==41&&v.sourceKind==1;}
bool FastCurrentSource(const Shared& v,HANDLE,HANDLE){return CurrentSource(v)&&allow[11];}
bool ExactTarget(const Shared& v,bool){return allow[9]&&v.targetWindow==reinterpret_cast<std::uintptr_t>(target)&&v.controllerPid==10&&v.targetThread==50&&v.targetWindow!=v.sourceForeground&&v.targetWindow!=reinterpret_cast<std::uintptr_t>(wpf);}
bool NoInput(const Shared&){return allow[0];}
}
static HMODULE ownModule{};
static int Hex(wchar_t c){if(c>=L'0'&&c<=L'9')return c-L'0';if(c>=L'a'&&c<=L'f')return c-L'a'+10;return -1;}
static bool ModuleNonce(wchar_t* path,wchar_t* nonce,std::uint64_t& low,std::uint64_t& high) {
 DWORD n=Fake_GetModuleFileNameW(ownModule,path,proof::PathCapacity);if(!n||n>=proof::PathCapacity)return false;
 auto name=wcsrchr(path,L'\\');if(!name)return false;++name;
 constexpr auto prefix=sizeof(proof::FilePrefix)/sizeof(wchar_t)-1;
 if(wcslen(name)!=prefix+32+4||wcsncmp(name,proof::FilePrefix,prefix)||wcscmp(name+prefix+32,L".dll"))return false;
 low=high=0;
 for(unsigned i=0;i<32;i++){int h=Hex(name[prefix+i]);if(h<0)return false;nonce[i]=name[prefix+i];if(i<16)low=(low<<4)|unsigned(h);else high=(high<<4)|unsigned(h);}
 nonce[32]=0;return low||high;
}
using GetBand=BOOL(WINAPI*)(HWND,DWORD*);
using SetBand=BOOL(WINAPI*)(HWND,HWND,DWORD);
static bool ExecutableIn(void* address,HMODULE module){MEMORY_BASIC_INFORMATION p{};return address&&Fake_VirtualQuery(address,&p,sizeof(p))&&p.AllocationBase==module&&p.State==MEM_COMMIT&&!(p.Protect&(PAGE_GUARD|PAGE_NOACCESS))&&(p.Protect&(PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY));}
static void AttemptBand(proof::Shared& s,HANDLE controller,HANDLE frame){
 auto module=Fake_GetModuleHandleW(L"user32.dll");auto get=reinterpret_cast<GetBand>(Fake_GetProcAddress(module,"GetWindowBand"));auto set=reinterpret_cast<SetBand>(Fake_GetProcAddress(module,"SetWindowBand"));
 HWND target=reinterpret_cast<HWND>(s.targetWindow);
 HANDLE game=Fake_OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,s.gamePid);
 // Perform potentially slow property/COM/identity queries before committing to
 // the HWND lifetime. The popup remains hidden throughout this discriminator.
 bool exactTarget=proof::ExactTarget(s,false)&&!Fake_IsWindowVisible(target),exactSource=proof::CurrentSource(s);
 bool exports=ExecutableIn(reinterpret_cast<void*>(get),module)&&ExecutableIn(reinterpret_cast<void*>(set),module);
 proof::DispatchBand([&](){
  if(exactTarget&&exports){Fake_SetLastError(0);s.bandBeforeRead=get(target,&s.bandBefore);s.bandBeforeError=s.bandBeforeRead?0:Fake_GetLastError();}
  return game&&exports&&s.bandBeforeRead&&s.bandBefore==1&&proof::MayWrite(exactTarget,exactSource,proof::NoInput(s),proof::Live(s.issued,s.deadline,Fake_GetTickCount64()),proof::Permit::Committed);
 },[&](){return InterlockedCompareExchange(&s.operationPermit,LONG(proof::Permit::Committed),LONG(proof::Permit::Ready))==LONG(proof::Permit::Ready);},[&](){
  // Revalidate fast native identities after band-before and all slow COM getters.
  if(!proof::FastCurrentSource(s,frame,game)||!proof::ExactTarget(s,false))return false;
  DWORD owner=0;return proof::Alive(controller,s.controllerPid,s.controllerBirth)&&Fake_GetWindowThreadProcessId(target,&owner)==s.targetThread&&owner==s.controllerPid&&
   Fake_GetPropW(target,L"PlayhubBandNonceLow")==reinterpret_cast<HANDLE>(s.nonceLow)&&Fake_GetPropW(target,L"PlayhubBandNonceHigh")==reinterpret_cast<HANDLE>(s.nonceHigh)&&
   !Fake_IsWindowVisible(target)&&Fake_GetForegroundWindow()==reinterpret_cast<HWND>(s.sourceForeground)&&!InterlockedCompareExchange(&s.revoked,0,0)&&InterlockedCompareExchange(&s.armed,0,0)==1&&proof::NoInput(s)&&proof::Live(s.issued,s.deadline,Fake_GetTickCount64());
 },[&](){
  s.bandAt=Fake_GetTickCount64();s.bandInvoked=1;MemoryBarrier();Fake_SetLastError(0);s.bandReturned=set(target,nullptr,16);s.bandError=s.bandReturned?0:Fake_GetLastError();
  Fake_SetLastError(0);s.bandAfterRead=get(target,&s.bandAfter);s.bandAfterError=s.bandAfterRead?0:Fake_GetLastError();
 },[&](){
  if(!s.bandInvoked)InterlockedOr(&s.refusalMask,65536);
  MemoryBarrier();InterlockedExchange(&s.operationPermit,LONG(proof::Permit::Finished));
  if(wcsnlen_s(s.operationEvent,96)>0&&wcsnlen_s(s.operationEvent,96)<96){HANDLE done=Fake_OpenEventW(EVENT_MODIFY_STATE,FALSE,s.operationEvent);if(done){Fake_SetEvent(done);Fake_CloseHandle(done);}}
 });
 if(game)Fake_CloseHandle(game);
}
extern "C" __declspec(dllexport) LRESULT CALLBACK ShellCallback(int code,WPARAM wParam,LPARAM lParam) {
 DWORD originalError=Fake_GetLastError();
 HANDLE mapping{};proof::Shared* shared{};bool entered=false;
 if(code>=0&&lParam){
  const auto* message=reinterpret_cast<const CWPSTRUCT*>(lParam);
  if(message->message==WM_NULL){
   wchar_t module[proof::PathCapacity]{},nonce[33]{},mapName[96]{};std::uint64_t low{},high{};
   if(ModuleNonce(module,nonce,low,high)&&std::uint64_t(message->wParam)==low&&std::uint64_t(message->lParam)==high){
    swprintf_s(mapName,L"%s%s",proof::MapPrefix,nonce);
    mapping=Fake_OpenFileMappingW(FILE_MAP_READ|FILE_MAP_WRITE,FALSE,mapName);
    if(mapping)shared=static_cast<proof::Shared*>(Fake_MapViewOfFile(mapping,FILE_MAP_READ|FILE_MAP_WRITE,0,0,sizeof(proof::Shared)));
    if(shared){
     InterlockedIncrement(&shared->inflight);entered=true;
     proof::Facts f{};f.schema=shared->magic==proof::Magic&&shared->version==proof::Version&&shared->size==sizeof(proof::Shared);
     f.nonce=shared->nonceLow==low&&shared->nonceHigh==high;
     f.module=wcsnlen_s(shared->modulePath,proof::PathCapacity)<proof::PathCapacity&&_wcsicmp(module,shared->modulePath)==0;
     f.issued=shared->issued;f.deadline=shared->deadline;f.armed=InterlockedCompareExchange(&shared->armed,0,0)==1;
     f.revoked=InterlockedCompareExchange(&shared->revoked,0,0)!=0;f.unclaimed=InterlockedCompareExchange(&shared->claimed,0,0)==0;
     HANDLE controller{},source{};
     if(f.schema&&f.nonce&&f.module&&f.armed&&!f.revoked&&f.unclaimed&&proof::Live(f.issued,f.deadline,Fake_GetTickCount64())){
      controller=Fake_OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,shared->controllerPid);
      f.controller=proof::Alive(controller,shared->controllerPid,shared->controllerBirth)&&wcsnlen_s(shared->controllerImage,proof::PathCapacity)<proof::PathCapacity&&proof::Image(controller,shared->controllerImage);
      f.thread=Fake_GetCurrentThreadId()==shared->shellThread;
      f.shell=Fake_GetCurrentProcessId()==shared->shellPid&&proof::Birth(Fake_GetCurrentProcess())==shared->shellBirth&&
       wcsnlen_s(shared->shellImage,proof::PathCapacity)<proof::PathCapacity&&proof::Image(Fake_GetCurrentProcess(),shared->shellImage)&&
       proof::ExactShell(reinterpret_cast<HWND>(shared->shellWindow),shared->shellPid,shared->shellThread)&&message->hwnd==reinterpret_cast<HWND>(shared->shellWindow);
      proof::Security a{},b{};f.security=shared->sidLength>0&&shared->sidLength<=SECURITY_MAX_SID_SIZE&&Fake_IsValidSid(shared->sid)&&
       proof::ReadSecurity(Fake_GetCurrentProcess(),a)&&proof::ReadSecurity(controller,b)&&proof::SameSecurity(a,b)&&a.session==shared->session&&a.integrity==shared->integrity&&a.sidLength==shared->sidLength&&Fake_EqualSid(a.sid,shared->sid);
      f.desktop=wcsnlen_s(shared->station,64)<64&&wcsnlen_s(shared->desktop,64)<64&&proof::DesktopCurrent(shared->shellThread,shared->station,shared->desktop);
      source=Fake_OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,shared->sourcePid);
      f.foreground=proof::ExactForeground(reinterpret_cast<HWND>(shared->sourceForeground),shared->sourcePid,shared->sourceThread,source,shared->sourceBirth)&&((shared->sourceKind==1&&wcsnlen_s(shared->aumid,513)>2&&wcsnlen_s(shared->aumid,513)<513)||shared->sourceKind==2)&&proof::CurrentSource(*shared);
     }
     f.now=Fake_GetTickCount64();f.armed=InterlockedCompareExchange(&shared->armed,0,0)==1;f.revoked=InterlockedCompareExchange(&shared->revoked,0,0)!=0;
     if(proof::Admit(f)&&InterlockedCompareExchange(&shared->claimed,1,0)==0){
      // Recheck the cheap revocation/deadline/foreground boundary immediately before publishing the acknowledgement.
      if(InterlockedCompareExchange(&shared->armed,0,0)==1&&!InterlockedCompareExchange(&shared->revoked,0,0)&&
        proof::ExactForeground(reinterpret_cast<HWND>(shared->sourceForeground),shared->sourcePid,shared->sourceThread,source,shared->sourceBirth)&&proof::ExactShell(reinterpret_cast<HWND>(shared->shellWindow),shared->shellPid,shared->shellThread)&&proof::Live(shared->issued,shared->deadline,Fake_GetTickCount64())){
       shared->callbackPid=Fake_GetCurrentProcessId();shared->callbackThread=Fake_GetCurrentThreadId();shared->callbackAt=Fake_GetTickCount64();
       shared->callbackNonceLow=low;shared->callbackNonceHigh=high;shared->callbackError=0;MemoryBarrier();InterlockedExchange(&shared->received,1);
       AttemptBand(*shared,controller,source);
      }else{InterlockedOr(&shared->refusalMask,16384);InterlockedIncrement(&shared->rejected);}
     }else{InterlockedOr(&shared->refusalMask,LONG(proof::RefusalMask(f)|8192));InterlockedIncrement(&shared->rejected);}
     if(source)Fake_CloseHandle(source);
     if(controller)Fake_CloseHandle(controller);
    }
   }
  }
 }
 // Inflight covers downstream hooks too; Unhook returning alone is never treated as quiescence.
 Fake_SetLastError(originalError);LRESULT result=Fake_CallNextHookEx(nullptr,code,wParam,lParam);DWORD downstreamError=Fake_GetLastError();
 if(entered)InterlockedDecrement(&shared->inflight);
 if(shared)Fake_UnmapViewOfFile(shared);if(mapping)Fake_CloseHandle(mapping);Fake_SetLastError(downstreamError);return result;
}

struct HudSource:playhub::hud::ISourceAuthority{bool current=true;bool Current(const playhub::hud::Authority&)const override{return current;}};
void Reset(){for(auto& b:allow)b=true;s={};writes=downstream=0;bandRead={};s.magic=wire::Magic;s.version=wire::Version;s.size=sizeof(s);s.nonceLow=0x1111111111111111;s.nonceHigh=0x2222222222222222;s.controllerPid=10;s.controllerBirth=11;s.targetThread=50;s.targetWindow=reinterpret_cast<std::uintptr_t>(target);s.shellPid=100;s.shellBirth=101;s.shellThread=102;s.shellWindow=reinterpret_cast<std::uintptr_t>(shellWindow);s.session=1;s.integrity=0x2000;s.sidLength=12;s.sourceForeground=reinterpret_cast<std::uintptr_t>(gameFrame);s.sourcePid=30;s.sourceBirth=31;s.sourceThread=40;s.gamePid=20;s.gameBirth=21;s.gameThread=41;s.sourceCore=610;s.sourceKind=1;s.issued=1000;s.deadline=1400;s.armed=1;wcscpy_s(s.aumid,L"Package!Game");wcscpy_s(s.modulePath,L"C:\\private\\PlayhubExactBand-11111111111111112222222222222222.dll");wcscpy_s(s.controllerImage,L"C:\\GamingMode.exe");wcscpy_s(s.shellImage,L"C:\\Windows\\explorer.exe");wcscpy_s(s.station,L"WinSta0");wcscpy_s(s.desktop,L"Default");wcscpy_s(s.operationEvent,L"Local\\event");}
void Invoke(){CWPSTRUCT message{};message.message=WM_NULL;message.hwnd=shellWindow;message.wParam=WPARAM(s.nonceLow);message.lParam=LPARAM(s.nonceHigh);ShellCallback(0,0,reinterpret_cast<LPARAM>(&message));}
int Run(){
 Reset();Invoke();Check(s.received==1&&writes==1&&s.bandAfter==16&&s.operationPermit==LONG(wire::Permit::Finished),"whole actual callback admits exact hidden HUD target with real game foreground");Check(s.inflight==0&&downstream==1,"whole callback cleans inflight and forwards");
 for(unsigned i=0;i<12;i++){Reset();allow[i]=false;Invoke();Check(writes==0,"every full callback native validation/commit refusal suppresses write");Check(s.inflight==0&&downstream==1,"rejection forwards and cleans inflight");}
 Reset();s.revoked=1;Invoke();Check(writes==0,"revoked lease never writes");Reset();s.deadline=999;Invoke();Check(writes==0,"expired wire never writes");Reset();s.version++;Invoke();Check(writes==0,"wrong callback protocol rejected");Reset();s.claimed=1;Invoke();Check(writes==0,"old claimed authority cannot replay");
 Reset();bandRead=[](){allow[7]=false;};Invoke();Check(writes==0&&s.operationPermit==LONG(wire::Permit::Finished),"foreground drift during band getter cannot write after commit");Reset();bandRead=[](){s.revoked=1;};Invoke();Check(writes==0,"revoked during getter cannot write");
 playhub::hud::Authority a;a.purpose=playhub::hud::Purpose::PerformanceHud;a.generation=1;a.collectorEpoch=2;a.collectorLease=std::string(32,'a');a.ownerBuild=std::string(64,'b');a.appId=123;a.agentPid=10;a.agentBirth=11;a.gamePid=20;a.gameBirth=21;a.framePid=30;a.frameBirth=31;a.frameThread=40;a.sourceThread=55;a.session=1;a.gameWindow=reinterpret_cast<std::uintptr_t>(gameFrame);a.wpfWindow=reinterpret_cast<std::uintptr_t>(wpf);a.wpfInstance=1;a.gameRect={0,0,3840,2160};a.wpfRect={7,7,295,191};a.wpfClass=L"HwndWrapper[GamingMode;Playhub performance HUD;GUID]";
 auto source=std::make_shared<HudSource>();playhub::hud::BandLease hud;hud.authority=a;hud.issuedAt=1000;hud.expiresAt=5000;hud.source=source;std::fill_n(hud.authorizationNonce.data(),64,'a');wcscpy_s(hud.actualFrameAumid.data(),513,L"Package!Game");playhub::mirror::BandAuthorization lowered(hud);auto physical=lowered.Game();Check(lowered.Current(1010),"HUD authorization uses its own typed lease validator");Check(physical.gameWindow==a.gameWindow&&physical.gameWindow!=a.wpfWindow&&physical.framePid==30&&physical.gamePid==20&&physical.expectedFrameAumid[0]==L'P',"HUD lowering carries real game/frame not WPF/Steam identity");
 Reset();s.controllerPid=a.agentPid;s.controllerBirth=a.agentBirth;s.sourceForeground=physical.gameWindow;s.sourcePid=physical.framePid;s.sourceBirth=physical.frameBirth;s.sourceThread=physical.frameThread;s.gamePid=physical.gamePid;s.gameBirth=physical.gameBirth;s.session=physical.session;s.sourceRect={physical.gameRect.left,physical.gameRect.top,physical.gameRect.right,physical.gameRect.bottom};wcscpy_s(s.aumid,physical.expectedFrameAumid.data());Invoke();Check(s.received==1&&writes==1&&s.bandAfter==16,"actual typed HUD lowering feeds whole callback with separate WPF and native target threads");Check(a.sourceThread!=s.targetThread&&s.sourceThread==a.frameThread,"WPF UI thread is never confused with target worker or game frame thread");
 source->current=false;Check(!lowered.Current(1010),"revoked publisher/source rejects HUD lowering");source->current=true;hud.authority.purpose=playhub::hud::Purpose::None;Check(!playhub::mirror::BandAuthorization(hud).Current(1010),"untyped purpose cannot enter callback lowering");
 static_assert(sizeof(wire::Shared)==sizeof(playhub_band16::Shared));
 std::cout<<"{\"checks\":"<<checks<<",\"wholeActualCallback\":true,\"nativeApis\":\"inert-fakes\",\"passed\":true}\n";return 0;
}
}
int main(){try{return test::Run();}catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}}
