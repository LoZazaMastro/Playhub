// SPDX-License-Identifier: GPL-3.0-only
// Private phase2: one fixed band16 call on the nonce-bound controller-owned popup only.
#include "Protocol.h"
#include "Identity.h"
#include "Target.h"
#include <cwchar>
static HMODULE ownModule{};
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID) {
 if(reason==DLL_PROCESS_ATTACH)ownModule=instance;return TRUE;
}
static int Hex(wchar_t c){if(c>=L'0'&&c<=L'9')return c-L'0';if(c>=L'a'&&c<=L'f')return c-L'a'+10;return -1;}
static bool ModuleNonce(wchar_t* path,wchar_t* nonce,std::uint64_t& low,std::uint64_t& high) {
 DWORD n=GetModuleFileNameW(ownModule,path,proof::PathCapacity);if(!n||n>=proof::PathCapacity)return false;
 auto name=wcsrchr(path,L'\\');if(!name)return false;++name;
 constexpr auto prefix=sizeof(proof::FilePrefix)/sizeof(wchar_t)-1;
 if(wcslen(name)!=prefix+32+4||wcsncmp(name,proof::FilePrefix,prefix)||wcscmp(name+prefix+32,L".dll"))return false;
 low=high=0;
 for(unsigned i=0;i<32;i++){int h=Hex(name[prefix+i]);if(h<0)return false;nonce[i]=name[prefix+i];if(i<16)low=(low<<4)|unsigned(h);else high=(high<<4)|unsigned(h);}
 nonce[32]=0;return low||high;
}
using GetBand=BOOL(WINAPI*)(HWND,DWORD*);
using SetBand=BOOL(WINAPI*)(HWND,HWND,DWORD);
static bool ExecutableIn(void* address,HMODULE module){MEMORY_BASIC_INFORMATION p{};return address&&VirtualQuery(address,&p,sizeof(p))&&p.AllocationBase==module&&p.State==MEM_COMMIT&&!(p.Protect&(PAGE_GUARD|PAGE_NOACCESS))&&(p.Protect&(PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY));}
static void AttemptBand(proof::Shared& s,HANDLE controller,HANDLE frame){
 auto module=GetModuleHandleW(L"user32.dll");auto get=reinterpret_cast<GetBand>(GetProcAddress(module,"GetWindowBand"));auto set=reinterpret_cast<SetBand>(GetProcAddress(module,"SetWindowBand"));
 HWND target=reinterpret_cast<HWND>(s.targetWindow);
 HANDLE game=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,s.gamePid);
 // Perform potentially slow property/COM/identity queries before committing to
 // the HWND lifetime. The popup remains hidden throughout this discriminator.
 bool exactTarget=proof::ExactTarget(s,false)&&!IsWindowVisible(target),exactSource=proof::CurrentSource(s);
 bool exports=ExecutableIn(reinterpret_cast<void*>(get),module)&&ExecutableIn(reinterpret_cast<void*>(set),module);
 proof::DispatchBand([&](){
  if(exactTarget&&exports){SetLastError(0);s.bandBeforeRead=get(target,&s.bandBefore);s.bandBeforeError=s.bandBeforeRead?0:GetLastError();}
  return game&&exports&&s.bandBeforeRead&&s.bandBefore==1&&proof::MayWrite(exactTarget,exactSource,proof::NoInput(s),proof::Live(s.issued,s.deadline,GetTickCount64()),proof::Permit::Committed);
 },[&](){return InterlockedCompareExchange(&s.operationPermit,LONG(proof::Permit::Committed),LONG(proof::Permit::Ready))==LONG(proof::Permit::Ready);},[&](){
  // Revalidate fast native identities after band-before and all slow COM getters.
  if(!proof::FastCurrentSource(s,frame,game)||!proof::ExactTarget(s,false))return false;
  DWORD owner=0;return proof::Alive(controller,s.controllerPid,s.controllerBirth)&&GetWindowThreadProcessId(target,&owner)==s.targetThread&&owner==s.controllerPid&&
   GetPropW(target,L"PlayhubBandNonceLow")==reinterpret_cast<HANDLE>(s.nonceLow)&&GetPropW(target,L"PlayhubBandNonceHigh")==reinterpret_cast<HANDLE>(s.nonceHigh)&&
   !IsWindowVisible(target)&&GetForegroundWindow()==reinterpret_cast<HWND>(s.sourceForeground)&&!InterlockedCompareExchange(&s.revoked,0,0)&&InterlockedCompareExchange(&s.armed,0,0)==1&&proof::NoInput(s)&&proof::Live(s.issued,s.deadline,GetTickCount64());
 },[&](){
  s.bandAt=GetTickCount64();s.bandInvoked=1;MemoryBarrier();SetLastError(0);s.bandReturned=set(target,nullptr,16);s.bandError=s.bandReturned?0:GetLastError();
  SetLastError(0);s.bandAfterRead=get(target,&s.bandAfter);s.bandAfterError=s.bandAfterRead?0:GetLastError();
 },[&](){
  if(!s.bandInvoked)InterlockedOr(&s.refusalMask,65536);
  MemoryBarrier();InterlockedExchange(&s.operationPermit,LONG(proof::Permit::Finished));
  if(wcsnlen_s(s.operationEvent,96)>0&&wcsnlen_s(s.operationEvent,96)<96){HANDLE done=OpenEventW(EVENT_MODIFY_STATE,FALSE,s.operationEvent);if(done){SetEvent(done);CloseHandle(done);}}
 });
 if(game)CloseHandle(game);
}
extern "C" __declspec(dllexport) LRESULT CALLBACK ShellCallback(int code,WPARAM wParam,LPARAM lParam) {
 DWORD originalError=GetLastError();
 HANDLE mapping{};proof::Shared* shared{};bool entered=false;
 if(code>=0&&lParam){
  const auto* message=reinterpret_cast<const CWPSTRUCT*>(lParam);
  if(message->message==WM_NULL){
   wchar_t module[proof::PathCapacity]{},nonce[33]{},mapName[96]{};std::uint64_t low{},high{};
   if(ModuleNonce(module,nonce,low,high)&&std::uint64_t(message->wParam)==low&&std::uint64_t(message->lParam)==high){
    swprintf_s(mapName,L"%s%s",proof::MapPrefix,nonce);
    mapping=OpenFileMappingW(FILE_MAP_READ|FILE_MAP_WRITE,FALSE,mapName);
    if(mapping)shared=static_cast<proof::Shared*>(MapViewOfFile(mapping,FILE_MAP_READ|FILE_MAP_WRITE,0,0,sizeof(proof::Shared)));
    if(shared){
     InterlockedIncrement(&shared->inflight);entered=true;
     proof::Facts f{};f.schema=shared->magic==proof::Magic&&shared->version==proof::Version&&shared->size==sizeof(proof::Shared);
     f.nonce=shared->nonceLow==low&&shared->nonceHigh==high;
     f.module=wcsnlen_s(shared->modulePath,proof::PathCapacity)<proof::PathCapacity&&_wcsicmp(module,shared->modulePath)==0;
     f.issued=shared->issued;f.deadline=shared->deadline;f.armed=InterlockedCompareExchange(&shared->armed,0,0)==1;
     f.revoked=InterlockedCompareExchange(&shared->revoked,0,0)!=0;f.unclaimed=InterlockedCompareExchange(&shared->claimed,0,0)==0;
     HANDLE controller{},source{};
     if(f.schema&&f.nonce&&f.module&&f.armed&&!f.revoked&&f.unclaimed&&proof::Live(f.issued,f.deadline,GetTickCount64())){
      controller=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,shared->controllerPid);
      f.controller=proof::Alive(controller,shared->controllerPid,shared->controllerBirth)&&wcsnlen_s(shared->controllerImage,proof::PathCapacity)<proof::PathCapacity&&proof::Image(controller,shared->controllerImage);
      f.thread=GetCurrentThreadId()==shared->shellThread;
      f.shell=GetCurrentProcessId()==shared->shellPid&&proof::Birth(GetCurrentProcess())==shared->shellBirth&&
       wcsnlen_s(shared->shellImage,proof::PathCapacity)<proof::PathCapacity&&proof::Image(GetCurrentProcess(),shared->shellImage)&&
       proof::ExactShell(reinterpret_cast<HWND>(shared->shellWindow),shared->shellPid,shared->shellThread)&&message->hwnd==reinterpret_cast<HWND>(shared->shellWindow);
      proof::Security a{},b{};f.security=shared->sidLength>0&&shared->sidLength<=SECURITY_MAX_SID_SIZE&&IsValidSid(shared->sid)&&
       proof::ReadSecurity(GetCurrentProcess(),a)&&proof::ReadSecurity(controller,b)&&proof::SameSecurity(a,b)&&a.session==shared->session&&a.integrity==shared->integrity&&a.sidLength==shared->sidLength&&EqualSid(a.sid,shared->sid);
      f.desktop=wcsnlen_s(shared->station,64)<64&&wcsnlen_s(shared->desktop,64)<64&&proof::DesktopCurrent(shared->shellThread,shared->station,shared->desktop);
      source=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,shared->sourcePid);
      f.foreground=proof::ExactForeground(reinterpret_cast<HWND>(shared->sourceForeground),shared->sourcePid,shared->sourceThread,source,shared->sourceBirth)&&wcsnlen_s(shared->aumid,513)>2&&wcsnlen_s(shared->aumid,513)<513&&proof::CurrentSource(*shared);
     }
     f.now=GetTickCount64();f.armed=InterlockedCompareExchange(&shared->armed,0,0)==1;f.revoked=InterlockedCompareExchange(&shared->revoked,0,0)!=0;
     if(proof::Admit(f)&&InterlockedCompareExchange(&shared->claimed,1,0)==0){
      // Recheck the cheap revocation/deadline/foreground boundary immediately before publishing the acknowledgement.
      if(InterlockedCompareExchange(&shared->armed,0,0)==1&&!InterlockedCompareExchange(&shared->revoked,0,0)&&
        proof::ExactForeground(reinterpret_cast<HWND>(shared->sourceForeground),shared->sourcePid,shared->sourceThread,source,shared->sourceBirth)&&proof::ExactShell(reinterpret_cast<HWND>(shared->shellWindow),shared->shellPid,shared->shellThread)&&proof::Live(shared->issued,shared->deadline,GetTickCount64())){
       shared->callbackPid=GetCurrentProcessId();shared->callbackThread=GetCurrentThreadId();shared->callbackAt=GetTickCount64();
       shared->callbackNonceLow=low;shared->callbackNonceHigh=high;shared->callbackError=0;MemoryBarrier();InterlockedExchange(&shared->received,1);
       AttemptBand(*shared,controller,source);
      }else{InterlockedOr(&shared->refusalMask,16384);InterlockedIncrement(&shared->rejected);}
     }else{InterlockedOr(&shared->refusalMask,LONG(proof::RefusalMask(f)|8192));InterlockedIncrement(&shared->rejected);}
     if(source)CloseHandle(source);
     if(controller)CloseHandle(controller);
    }
   }
  }
 }
 // Inflight covers downstream hooks too; Unhook returning alone is never treated as quiescence.
 SetLastError(originalError);LRESULT result=CallNextHookEx(nullptr,code,wParam,lParam);DWORD downstreamError=GetLastError();
 if(entered)InterlockedDecrement(&shared->inflight);
 if(shared)UnmapViewOfFile(shared);if(mapping)CloseHandle(mapping);SetLastError(downstreamError);return result;
}
