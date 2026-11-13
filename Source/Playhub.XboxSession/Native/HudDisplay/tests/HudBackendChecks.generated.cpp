
// Actual production Attach/Show methods; all OS calls and slow identities are
// inert fakes. The publisher guard itself remains a real explicit predicate.
#include "HudDisplayLease.h"
#include "HudWindowsBackend.h"
#include <windows.h>
#include <dwmapi.h>
#include <functional>
#include <iostream>
#include <stdexcept>
using namespace playhub::hud;
static bool enabled=true,published=true,actualFG=true,shown=false;static int writes{},registers{},updates{},getterCalls{},checks{};static std::function<void()> onGetter,onDisable,onQuery;static HRESULT updateResult=S_OK;static int width=288;static DWM_THUMBNAIL_PROPERTIES props{};
BOOL Fake_EnableWindow(HWND,BOOL){if(onDisable)onDisable();enabled=false;return FALSE;}
BOOL Fake_IsWindowEnabled(HWND){return enabled;}
BOOL Fake_IsWindowVisible(HWND){return shown;}
BOOL Fake_SetWindowPos(HWND,HWND,int,int,int,int,UINT flags){if((flags&SWP_SHOWWINDOW)==0)throw std::runtime_error("wrong Show flags");++writes;shown=true;return TRUE;}
DWORD Fake_GetLastError(){return 0;}
HWND Fake_GetForegroundWindow(){return reinterpret_cast<HWND>(std::uintptr_t(actualFG?600:999));}
HRESULT Fake_DwmIsCompositionEnabled(BOOL* p){*p=TRUE;return S_OK;}
HRESULT Fake_DwmExtendFrameIntoClientArea(HWND,const MARGINS* m){if(m->cxLeftWidth!=-1||m->cyTopHeight!=-1)throw std::runtime_error("glass margins changed");return S_OK;}
HDC Fake_GetDC(HWND){return reinterpret_cast<HDC>(1);}int Fake_ReleaseDC(HWND,HDC){return 1;}
HRESULT Fake_DwmRegisterThumbnail(HWND,HWND,HTHUMBNAIL* p){++registers;*p=reinterpret_cast<HTHUMBNAIL>(1);return S_OK;}
HRESULT Fake_DwmQueryThumbnailSourceSize(HTHUMBNAIL,SIZE* p){p->cx=width;p->cy=184;if(onQuery)onQuery();return S_OK;}
HRESULT Fake_DwmUpdateThumbnailProperties(HTHUMBNAIL,const DWM_THUMBNAIL_PROPERTIES* p){++updates;props=*p;return updateResult;}
bool Band(HWND,DWORD n){return n==16;}
struct Fake {
 struct Target{HWND window=reinterpret_cast<HWND>(800);}target;
 bool retired=true,glass=true;HTHUMBNAIL thumbnail{};NativeDiagnostics diag{};std::function<bool(const Authority&)>publisher=[](const Authority&){return published;};
 bool Own(){return true;}bool CurrentSource(const Authority&){if(onGetter)onGetter();return true;}
 bool Current(const Authority&){++getterCalls;if(onGetter)onGetter();return true;}
 bool Fail(DWORD,DWORD=0,HRESULT=S_OK){return false;}bool Black(HDC){return true;}
 bool AttachLiveWpf(const Authority& a){if(!Own()||!retired||thumbnail||Fake_IsWindowVisible(target.window)||!CurrentSource(a)||!Band(target.window,16))return Fail(10);BOOL composed{};MARGINS all{-1,-1,-1,-1};HRESULT hr=Fake_DwmIsCompositionEnabled(&composed);if(FAILED(hr)||!composed)return Fail(11,0,hr);hr=Fake_DwmExtendFrameIntoClientArea(target.window,&all);if(FAILED(hr))return Fail(12,0,hr);HDC dc=Fake_GetDC(target.window);glass=Black(dc);if(dc)Fake_ReleaseDC(target.window,dc);if(!glass||!CurrentSource(a)||!publisher||!publisher(a)||Fake_GetForegroundWindow()!=reinterpret_cast<HWND>(a.gameWindow))return Fail(13);hr=Fake_DwmRegisterThumbnail(target.window,reinterpret_cast<HWND>(a.wpfWindow),&thumbnail);if(FAILED(hr))return Fail(14,0,hr);diag.thumbnail=true;SIZE size{};hr=Fake_DwmQueryThumbnailSourceSize(thumbnail,&size);if(FAILED(hr)||size.cx!=a.wpfRect.right-a.wpfRect.left||size.cy!=a.wpfRect.bottom-a.wpfRect.top)return Fail(15,0,hr);DWM_THUMBNAIL_PROPERTIES props{};props.dwFlags=DWM_TNP_RECTDESTINATION|DWM_TNP_VISIBLE|DWM_TNP_OPACITY|DWM_TNP_SOURCECLIENTAREAONLY;props.rcDestination={0,0,size.cx,size.cy};props.opacity=255;props.fVisible=TRUE;props.fSourceClientAreaOnly=TRUE;if(!publisher||!publisher(a)||Fake_GetForegroundWindow()!=reinterpret_cast<HWND>(a.gameWindow))return Fail(16);hr=Fake_DwmUpdateThumbnailProperties(thumbnail,&props);if(FAILED(hr))return Fail(16,0,hr);diag.dwmUpdates++;diag.thumbnail=true;return Current(a);}
 bool ShowNoActivate(const Authority& a){if(!Own()||!retired||!thumbnail||!Current(a))return Fail(17);Fake_EnableWindow(target.window,FALSE);diag.disabled=!Fake_IsWindowEnabled(target.window);if(!diag.disabled||!Current(a)||!publisher||!publisher(a)||Fake_GetForegroundWindow()!=reinterpret_cast<HWND>(a.gameWindow))return Fail(18);if(!Fake_SetWindowPos(target.window,nullptr,0,0,0,0,SWP_SHOWWINDOW|SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE|SWP_NOZORDER|SWP_NOOWNERZORDER))return Fail(19,Fake_GetLastError());diag.visible=Fake_IsWindowVisible(target.window)!=FALSE;diag.stage=20;return diag.visible&&Own()&&CurrentSource(a)&&Band(target.window,16)&&!Fake_IsWindowEnabled(target.window);}
};
void Reset(){enabled=published=actualFG=true;shown=false;writes=registers=updates=getterCalls=0;onGetter=onDisable=onQuery={};updateResult=S_OK;width=288;props={};}
void Check(bool b,const char* text){++checks;if(!b)throw std::runtime_error(text);}
int main(){try{Authority a;a.gameWindow=600;a.wpfWindow=700;a.wpfRect={7,7,295,191};
 Reset();{Fake f;f.thumbnail=reinterpret_cast<HTHUMBNAIL>(1);Check(f.ShowNoActivate(a)&&writes==1&&!enabled,"real Show disables only owned HUD target and commits once");}
 Reset();{Fake f;f.thumbnail=reinterpret_cast<HTHUMBNAIL>(1);onGetter=[](){published=false;};Check(!f.ShowNoActivate(a)&&writes==0,"Close or expiry in slow Current vetoes actual SHOW");}
 Reset();{Fake f;f.thumbnail=reinterpret_cast<HTHUMBNAIL>(1);onDisable=[](){published=false;};Check(!f.ShowNoActivate(a)&&writes==0,"reentrant disable cannot bypass final publisher gate");}
 Reset();{Fake f;f.thumbnail=reinterpret_cast<HTHUMBNAIL>(1);onGetter=[](){actualFG=false;};Check(!f.ShowNoActivate(a)&&writes==0,"foreign foreground after slow getter vetoes SHOW");}
 Reset();{Fake f;Check(f.AttachLiveWpf(a)&&registers==1&&updates==1,"real Attach uses one live DWM relationship");Check(props.opacity==255&&props.fVisible&&props.fSourceClientAreaOnly&&props.rcDestination.right==288&&props.rcDestination.bottom==184,"exact client-size and full source alpha retained");}
 Reset();{Fake f;onGetter=[](){published=false;};Check(!f.AttachLiveWpf(a)&&registers==0,"Close during pre-register identity never creates relationship");}
 Reset();{Fake f;onQuery=[](){published=false;};Check(!f.AttachLiveWpf(a)&&updates==0,"Close during DWM source-size query vetoes update");}
 Reset();{Fake f;width=287;Check(!f.AttachLiveWpf(a)&&updates==0,"mismatched WPF client geometry refuses update");}
 Reset();{Fake f;updateResult=E_FAIL;Check(!f.AttachLiveWpf(a),"actual HRESULT failure not treated as valid DWM attachment");}
 std::cout<<"{\"checks\":"<<checks<<",\"actualBackendMethods\":true,\"nativeCalls\":false,\"passed\":true}\n";
 }catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}return 0;}
