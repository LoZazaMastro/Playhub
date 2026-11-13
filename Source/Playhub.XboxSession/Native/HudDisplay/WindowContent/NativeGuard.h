#pragma once
#include <windows.h>
#include "TargetCore.h"
struct Handle {HANDLE value{};~Handle(){if(value&&value!=INVALID_HANDLE_VALUE)CloseHandle(value);}};
class NativeGuard {
 const progress::Target& target;Handle process;HWND window;DWORD thread{},session{};
 const char* initialReason="none";DWORD initialError{};
 const char* reason="none";DWORD error{};
 bool Refuse(const char* why,DWORD code=0){reason=why;error=code;return false;}
public:
 explicit NativeGuard(const progress::Target& t):target(t),window(reinterpret_cast<HWND>(static_cast<ULONG_PTR>(t.hwnd))){
  process.value=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,t.pid);
  if(!process.value){initialReason="process-open";initialError=GetLastError();}
  DWORD pid{};thread=GetWindowThreadProcessId(window,&pid);
  if(!thread&&initialReason==std::string_view("none")){initialReason="window-thread";initialError=GetLastError();}
  if(!ProcessIdToSessionId(GetCurrentProcessId(),&session)){
   if(initialReason==std::string_view("none")){initialReason="reader-session";initialError=GetLastError();}thread=0;
  }else if(pid!=t.pid){if(initialReason==std::string_view("none"))initialReason="window-pid";thread=0;}
 }
 bool Current(){
  reason="none";error=0;
  if(!thread||!process.value||process.value==INVALID_HANDLE_VALUE||GetProcessId(process.value)!=target.pid
   ||WaitForSingleObject(process.value,0)!=WAIT_TIMEOUT)return Refuse(initialReason==std::string_view("none")?"retained-process":initialReason,initialError);
  FILETIME born{},exit{},kernel{},user{};
  if(!GetProcessTimes(process.value,&born,&exit,&kernel,&user))return Refuse("process-times",GetLastError());
  if(((static_cast<std::uint64_t>(born.dwHighDateTime)<<32)|born.dwLowDateTime)!=target.birth)return Refuse("process-birth");
  wchar_t image[32768]{};DWORD length=32768;
  if(!QueryFullProcessImageNameW(process.value,0,image,&length))return Refuse("process-image-query",GetLastError());
  if(length==0||length>=32768)return Refuse("process-image-length");
  if(CompareStringOrdinal(image,static_cast<int>(length),target.image.c_str(),static_cast<int>(target.image.size()),TRUE)!=CSTR_EQUAL)return Refuse("process-image-mismatch");
  wchar_t name[257]{};int classLength=GetClassNameW(window,name,257);
  if(classLength<=0)return Refuse("window-class-query",GetLastError());
  if(target.windowClass!=std::wstring_view(name,static_cast<std::size_t>(classLength)))return Refuse("window-class-mismatch");
  DWORD pid{},actualSession{};DWORD actualThread=GetWindowThreadProcessId(window,&pid);
  if(!ProcessIdToSessionId(pid,&actualSession))return Refuse("process-session",GetLastError());
  progress::Identity identity{target.hwnd,target.birth,pid,actualThread,actualSession,
   WaitForSingleObject(process.value,0)==WAIT_TIMEOUT,IsWindow(window)&&GetAncestor(window,GA_ROOT)==window,
   target.image,std::wstring(name,static_cast<std::size_t>(classLength))};
  if(!progress::Match(target,identity,thread,session))return Refuse("window-identity");
  if(GetProcessId(process.value)!=target.pid)return Refuse("final-process-pid");
  return true;
 }
 const char* Reason()const{return reason;}
 DWORD Error()const{return error;}
 bool Foreground()const{return GetForegroundWindow()==window;}
 DWORD Thread()const{return thread;}
 bool FinalCurrent()const{
  DWORD pid{};
  return thread&&process.value&&process.value!=INVALID_HANDLE_VALUE
   &&GetProcessId(process.value)==target.pid&&WaitForSingleObject(process.value,0)==WAIT_TIMEOUT
   &&GetWindowThreadProcessId(window,&pid)==thread&&pid==target.pid
   &&IsWindow(window)&&GetAncestor(window,GA_ROOT)==window;
 }
};
