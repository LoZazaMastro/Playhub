// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "Protocol.h"
#include "Identity.h"
#include <appmodel.h>
#include <propsys.h>
#include <shellapi.h>
#include <wrl/client.h>
namespace proof {
inline bool CleanGui(DWORD thread){GUITHREADINFO g{sizeof(g)};return GetGUIThreadInfo(thread,&g)&&!(g.flags&(GUI_INMOVESIZE|GUI_INMENUMODE|GUI_SYSTEMMENUMODE|GUI_POPUPMENUMODE))&&!g.hwndCapture&&!g.hwndMenuOwner&&!g.hwndMoveSize;}
inline bool HeldInput(){for(int key=1;key<256;key++)if(GetAsyncKeyState(key)&0x8000)return true;return false;}
inline bool SameRect(const RECT& a,const RECT& b){return a.left==b.left&&a.top==b.top&&a.right==b.right&&a.bottom==b.bottom;}
inline bool ShellAppId(HWND window,const wchar_t* expected){
 Microsoft::WRL::ComPtr<IPropertyStore> store;if(FAILED(SHGetPropertyStoreForWindow(window,IID_PPV_ARGS(&store))))return false;
 constexpr PROPERTYKEY key{{0x9F4C2855,0x9F79,0x4B39,{0xA8,0xD0,0xE1,0xD4,0x2D,0xE1,0xD5,0xF3}},5};PROPVARIANT value{};
 bool match=SUCCEEDED(store->GetValue(key,&value))&&value.vt==VT_LPWSTR&&value.pwszVal&&_wcsicmp(value.pwszVal,expected)==0;PropVariantClear(&value);return match;
}
inline bool Package(HANDLE game,const wchar_t* aumid){UINT32 length=0;if(GetPackageFamilyName(game,&length,nullptr)!=ERROR_INSUFFICIENT_BUFFER||length<2||length>513)return false;wchar_t family[513]{};if(GetPackageFamilyName(game,&length,family))return false;size_t size=wcslen(family);return _wcsnicmp(family,aumid,size)==0&&aumid[size]==L'!';}
inline bool CurrentSource(const Shared& s){
 HANDLE frame=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,s.sourcePid),game=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,s.gamePid);
 HWND source=reinterpret_cast<HWND>(s.sourceForeground),core=reinterpret_cast<HWND>(s.sourceCore);DWORD owner=0,corePid=0;RECT rect{};wchar_t frameClass[96]{},coreClass[96]{},system[1024]{};UINT n=GetSystemDirectoryW(system,1024);
 bool ok=Alive(frame,s.sourcePid,s.sourceBirth)&&Alive(game,s.gamePid,s.gameBirth)&&ExactForeground(source,s.sourcePid,s.sourceThread,frame,s.sourceBirth)&&
  GetClassNameW(source,frameClass,96)&&wcscmp(frameClass,L"ApplicationFrameWindow")==0&&n&&n<1000;
 if(ok){wcscat_s(system,L"\\ApplicationFrameHost.exe");ok=Image(frame,system)&&GetAncestor(source,GA_ROOT)==source&&IsWindowVisible(source)&&IsWindowEnabled(source)&&!IsIconic(source)&&
  GetWindowThreadProcessId(core,&corePid)==s.gameThread&&corePid==s.gamePid&&GetClassNameW(core,coreClass,96)&&wcscmp(coreClass,L"Windows.UI.Core.CoreWindow")==0&&GetAncestor(core,GA_ROOT)==source&&
  IsWindowVisible(core)&&IsWindowEnabled(core)&&GetWindowRect(source,&rect)&&SameRect(rect,s.sourceRect)&&Package(game,s.aumid)&&ShellAppId(source,s.aumid)&&CleanGui(s.sourceThread)&&CleanGui(s.gameThread);}
 GUITHREADINFO gui{sizeof(gui)};DWORD session=0;
 wchar_t desktop[64]{};
 if(ok)ok=GetGUIThreadInfo(s.sourceThread,&gui)&&gui.hwndFocus==core&&ProcessIdToSessionId(s.gamePid,&session)&&session==s.session&&ObjectName(GetThreadDesktop(s.sourceThread),desktop,64)&&wcscmp(desktop,s.desktop)==0&&
  GetWindowThreadProcessId(source,&owner)==s.sourceThread&&owner==s.sourcePid&&GetForegroundWindow()==source;
 if(game)CloseHandle(game);if(frame)CloseHandle(frame);return ok;
}
inline bool ExactTarget(const Shared& s,bool visible){
 HWND target=reinterpret_cast<HWND>(s.targetWindow);DWORD owner=0;wchar_t cls[128]{},desktop[64]{};RECT rect{};
 return target&&s.targetThread&&wcsnlen_s(s.targetClass,128)>0&&wcsnlen_s(s.targetClass,128)<128&&GetWindowThreadProcessId(target,&owner)==s.targetThread&&owner==s.controllerPid&&GetAncestor(target,GA_ROOT)==target&&GetWindow(target,GW_OWNER)==nullptr&&
  GetClassNameW(target,cls,128)&&wcscmp(cls,s.targetClass)==0&&GetPropW(target,L"PlayhubBandNonceLow")==reinterpret_cast<HANDLE>(s.nonceLow)&&GetPropW(target,L"PlayhubBandNonceHigh")==reinterpret_cast<HANDLE>(s.nonceHigh)&&
  GetWindowRect(target,&rect)&&SameRect(rect,s.targetRect)&&IsWindowEnabled(target)&&(!visible||IsWindowVisible(target))&&
  ObjectName(GetThreadDesktop(s.targetThread),desktop,64)&&wcscmp(desktop,s.desktop)==0;
}
inline bool ControllerOwnsTarget(const Shared& s){
 HWND target=reinterpret_cast<HWND>(s.targetWindow);DWORD pid=0;wchar_t cls[128]{};
 return target&&GetWindowThreadProcessId(target,&pid)==s.targetThread&&pid==s.controllerPid&&s.controllerPid==GetCurrentProcessId()&&Birth(GetCurrentProcess())==s.controllerBirth&&GetAncestor(target,GA_ROOT)==target&&GetWindow(target,GW_OWNER)==nullptr&&GetClassNameW(target,cls,128)&&wcscmp(cls,s.targetClass)==0;
}
inline bool NoInput(const Shared& s){POINT p{};return !HeldInput()&&GetPhysicalCursorPos(&p)&&p.x==s.initialCursor.x&&p.y==s.initialCursor.y;}
inline bool FastCurrentSource(const Shared& s,HANDLE frame,HANDLE game){
 HWND window=reinterpret_cast<HWND>(s.sourceForeground),core=reinterpret_cast<HWND>(s.sourceCore);DWORD owner=0,pid=0;RECT rect{};wchar_t cls[96]{},coreClass[96]{},desktop[64]{};GUITHREADINFO gui{sizeof(gui)};
 return Alive(frame,s.sourcePid,s.sourceBirth)&&Alive(game,s.gamePid,s.gameBirth)&&
  GetWindowThreadProcessId(window,&owner)==s.sourceThread&&owner==s.sourcePid&&GetWindowThreadProcessId(core,&pid)==s.gameThread&&pid==s.gamePid&&
  GetAncestor(window,GA_ROOT)==window&&GetAncestor(core,GA_ROOT)==window&&GetClassNameW(window,cls,96)&&wcscmp(cls,L"ApplicationFrameWindow")==0&&GetClassNameW(core,coreClass,96)&&wcscmp(coreClass,L"Windows.UI.Core.CoreWindow")==0&&
  IsWindowVisible(window)&&IsWindowEnabled(window)&&!IsIconic(window)&&IsWindowVisible(core)&&IsWindowEnabled(core)&&GetWindowRect(window,&rect)&&SameRect(rect,s.sourceRect)&&
  GetGUIThreadInfo(s.sourceThread,&gui)&&gui.hwndFocus==core&&!(gui.flags&(GUI_INMOVESIZE|GUI_INMENUMODE|GUI_SYSTEMMENUMODE|GUI_POPUPMENUMODE))&&!gui.hwndCapture&&!gui.hwndMenuOwner&&!gui.hwndMoveSize&&CleanGui(s.gameThread)&&
  ObjectName(GetThreadDesktop(s.sourceThread),desktop,64)&&wcscmp(desktop,s.desktop)==0&&DesktopCurrent(s.shellThread,s.station,s.desktop)&&GetForegroundWindow()==window;
}
}
