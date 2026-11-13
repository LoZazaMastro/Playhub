// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <windows.h>
#include <cwchar>
#include <cstdint>
namespace proof {
inline std::uint64_t Birth(HANDLE p) {FILETIME c{},e{},k{},u{};return GetProcessTimes(p,&c,&e,&k,&u)?(std::uint64_t(c.dwHighDateTime)<<32)|c.dwLowDateTime:0;}
inline bool Alive(HANDLE p,DWORD pid,std::uint64_t birth) {return p&&GetProcessId(p)==pid&&birth&&Birth(p)==birth&&WaitForSingleObject(p,0)==WAIT_TIMEOUT;}
inline bool Image(HANDLE p,const wchar_t* expected) {wchar_t path[1024]{};DWORD n=1024;return QueryFullProcessImageNameW(p,0,path,&n)&&n<1024&&_wcsicmp(path,expected)==0;}
struct Security {BYTE sid[SECURITY_MAX_SID_SIZE]{};DWORD sidLength{},session{},integrity{};};
inline bool ReadSecurity(HANDLE p,Security& value) {
 HANDLE token{};if(!OpenProcessToken(p,TOKEN_QUERY,&token))return false;
 BYTE data[1024]{};DWORD n=0,ui=1,container=1;
 bool ok=GetTokenInformation(token,TokenUser,data,sizeof(data),&n)!=FALSE;
 if(ok){auto u=reinterpret_cast<TOKEN_USER*>(data);ok=IsValidSid(u->User.Sid)!=FALSE;
  if(ok){value.sidLength=GetLengthSid(u->User.Sid);ok=value.sidLength<=sizeof(value.sid)&&CopySid(sizeof(value.sid),value.sid,u->User.Sid)!=FALSE;}}
 if(ok)ok=GetTokenInformation(token,TokenSessionId,&value.session,sizeof(value.session),&n)!=FALSE;
 if(ok)ok=GetTokenInformation(token,TokenIntegrityLevel,data,sizeof(data),&n)!=FALSE;
 if(ok){auto sid=reinterpret_cast<TOKEN_MANDATORY_LABEL*>(data)->Label.Sid;ok=IsValidSid(sid)!=FALSE;
  if(ok){auto count=*GetSidSubAuthorityCount(sid);ok=count>0;if(ok)value.integrity=*GetSidSubAuthority(sid,count-1);}}
 if(ok)ok=GetTokenInformation(token,TokenUIAccess,&ui,sizeof(ui),&n)!=FALSE&&ui==0;
 if(ok)ok=GetTokenInformation(token,TokenIsAppContainer,&container,sizeof(container),&n)!=FALSE&&container==0;
 CloseHandle(token);return ok&&value.session!=0;
}
inline bool SameSecurity(const Security& a,const Security& b){return a.session==b.session&&a.integrity==b.integrity&&a.sidLength==b.sidLength&&a.sidLength&&EqualSid(const_cast<BYTE*>(a.sid),const_cast<BYTE*>(b.sid));}
inline bool ObjectName(HANDLE handle,wchar_t* out,DWORD count) {DWORD n=0;return handle&&GetUserObjectInformationW(handle,UOI_NAME,out,count*sizeof(wchar_t),&n)&&n>sizeof(wchar_t)&&n<=count*sizeof(wchar_t);}
inline bool DesktopCurrent(DWORD shellThread,const wchar_t* station,const wchar_t* desktop) {
 wchar_t a[64]{},b[64]{},c[64]{},d[64]{};
 HDESK input=OpenInputDesktop(0,FALSE,DESKTOP_READOBJECTS);
 bool ok=input&&ObjectName(GetProcessWindowStation(),a,64)&&ObjectName(GetThreadDesktop(GetCurrentThreadId()),b,64)&&ObjectName(GetThreadDesktop(shellThread),c,64)&&ObjectName(input,d,64)&&
  wcscmp(a,station)==0&&wcscmp(b,desktop)==0&&wcscmp(c,desktop)==0&&wcscmp(d,desktop)==0;
 if(input)CloseDesktop(input);return ok;
}
inline bool ExactShell(HWND window,DWORD pid,DWORD thread) {
 DWORD owner=0;wchar_t cls[64]{};
 return window&&GetShellWindow()==window&&IsWindow(window)&&GetWindowThreadProcessId(window,&owner)==thread&&owner==pid&&GetAncestor(window,GA_ROOT)==window&&GetClassNameW(window,cls,64)&&wcscmp(cls,L"Progman")==0;
}
inline bool NativeX64(HANDLE process){USHORT machine=0,native=0;return IsWow64Process2(process,&machine,&native)&&machine==IMAGE_FILE_MACHINE_UNKNOWN&&native==IMAGE_FILE_MACHINE_AMD64;}
inline bool ExactForeground(HWND window,DWORD pid,DWORD thread,HANDLE retained,std::uint64_t birth){DWORD owner=0;return window&&GetForegroundWindow()==window&&GetWindowThreadProcessId(window,&owner)==thread&&owner==pid&&Alive(retained,pid,birth)&&GetForegroundWindow()==window;}
}
