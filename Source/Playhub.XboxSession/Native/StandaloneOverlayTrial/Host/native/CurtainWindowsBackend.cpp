// SPDX-License-Identifier: GPL-3.0-only
#include "CurtainWindowsBackend.h"
#include "CurtainBandBroker.h"
#include "band/Target.h"
#include <dwmapi.h>
#include "PresentationBackend.h"
#include <bcrypt.h>
#include <atomic>
#include <vector>
namespace playhub::curtain { namespace {
namespace p=playhub_band16;
struct Handle {HANDLE h{};~Handle(){if(h&&h!=INVALID_HANDLE_VALUE)CloseHandle(h);}Handle()=default;Handle(const Handle&)=delete;Handle& operator=(const Handle&)=delete;};
bool Bounds(HWND window,Rect& value){RECT r{};if(!GetWindowRect(window,&r))return false;value={r.left,r.top,r.right,r.bottom};return Rectangle(value);}
bool Band(HWND window,DWORD expected){using Get=BOOL(WINAPI*)(HWND,DWORD*);auto get=reinterpret_cast<Get>(GetProcAddress(GetModuleHandleW(L"user32.dll"),"GetWindowBand"));DWORD found{};return get&&get(window,&found)&&found==expected;}
bool SameUser(HANDLE process,DWORD session){HANDLE target{},own{};if(!OpenProcessToken(process,TOKEN_QUERY,&target))return false;if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&own)){CloseHandle(target);return false;}BYTE a[2048]{},b[2048]{};DWORD size{},found{};bool okay=GetTokenInformation(target,TokenUser,a,sizeof a,&size)&&GetTokenInformation(own,TokenUser,b,sizeof b,&size)&&IsValidSid(reinterpret_cast<TOKEN_USER*>(a)->User.Sid)&&IsValidSid(reinterpret_cast<TOKEN_USER*>(b)->User.Sid)&&EqualSid(reinterpret_cast<TOKEN_USER*>(a)->User.Sid,reinterpret_cast<TOKEN_USER*>(b)->User.Sid)&&GetTokenInformation(target,TokenSessionId,&found,sizeof found,&size)&&found==session;CloseHandle(own);CloseHandle(target);return okay;}
std::wstring AppId(HWND window){Microsoft::WRL::ComPtr<IPropertyStore> store;if(FAILED(SHGetPropertyStoreForWindow(window,IID_PPV_ARGS(&store))))return {};constexpr PROPERTYKEY key{{0x9F4C2855,0x9F79,0x4B39,{0xA8,0xD0,0xE1,0xD4,0x2D,0xE1,0xD5,0xF3}},5};PROPVARIANT value{};std::wstring result;if(SUCCEEDED(store->GetValue(key,&value))&&value.vt==VT_LPWSTR&&value.pwszVal&&wcsnlen_s(value.pwszVal,513)<513)result=value.pwszVal;PropVariantClear(&value);return result;}
// The outer native source owns the request/Steam-curtain receipt. This layer
// independently retains process handles and checks all physical source HWNDs.
struct Source final:ISourceAuthority {
 Authority a;std::shared_ptr<ISourceAuthority> parent;Handle game,frame,steam;p::Shared physical{};
 // Capture diagnostics only; no new authority, I/O, retries or clock reads.
 mutable std::atomic<DWORD> captureFailureStage{2};
 bool CheckCapture(DWORD stage,bool okay)const{if(!okay)captureFailureStage.store(stage,std::memory_order_relaxed);return okay;}
 Id Now()const noexcept override{return GetTickCount64();}
 bool Steam()const{
  HWND w=reinterpret_cast<HWND>(a.steamWindow);DWORD pid{},cloaked=1;wchar_t cls[256]{};Rect rect{};
  return p::Alive(steam.h,a.steamPid,a.steamBirth)&&GetWindowThreadProcessId(w,&pid)==a.steamThread&&pid==a.steamPid
   &&GetAncestor(w,GA_ROOT)==w&&!GetWindow(w,GW_OWNER)&&GetClassNameW(w,cls,256)&&a.steamClass==cls
   &&IsWindowVisible(w)&&!IsIconic(w)&&Bounds(w,rect)&&rect==a.steamRect
   &&SUCCEEDED(DwmGetWindowAttribute(w,DWMWA_CLOAKED,&cloaked,sizeof cloaked))&&!cloaked;
 }
 bool Current(const Authority& value)const override{
  return CheckCapture(220,value==a)&&CheckCapture(221,parent!=nullptr)&&CheckCapture(222,parent->Current(a))
   &&CheckCapture(223,Steam())&&CheckCapture(224,p::CurrentSource(physical))
   &&CheckCapture(225,p::FastCurrentSource(physical,frame.h,game.h))&&CheckCapture(226,Steam())&&CheckCapture(227,parent->Current(a))
   &&CheckCapture(228,Steam())&&CheckCapture(229,p::FastCurrentSource(physical,frame.h,game.h))
   &&CheckCapture(230,GetForegroundWindow()==reinterpret_cast<HWND>(a.gameWindow));
 }
 bool Capture(const BandLease& lease){
  if(!CheckCapture(201,Valid(lease,Now())&&lease.authority.gameClass.size()<128&&lease.authority.ownerPid==GetCurrentProcessId()&&p::Birth(GetCurrentProcess())==lease.authority.ownerBirth))return false;
  a=lease.authority;parent=lease.source;
  game.h=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,a.gamePid);
  frame.h=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,a.framePid);
  steam.h=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,a.steamPid);
  if(!CheckCapture(202,p::Alive(game.h,a.gamePid,a.gameBirth))||!CheckCapture(203,p::Alive(frame.h,a.framePid,a.frameBirth))||!CheckCapture(204,p::Alive(steam.h,a.steamPid,a.steamBirth))
    ||!CheckCapture(205,SameUser(game.h,a.session))||!CheckCapture(206,SameUser(frame.h,a.session))||!CheckCapture(207,SameUser(steam.h,a.session))||!CheckCapture(208,SameUser(GetCurrentProcess(),a.session))
    ||!CheckCapture(209,p::Image(game.h,a.gameImage.c_str()))||!CheckCapture(210,p::Image(steam.h,a.steamImage.c_str())))return false;
  auto& s=physical;s.sourceForeground=a.gameWindow;s.sourcePid=a.framePid;s.sourceBirth=a.frameBirth;s.sourceThread=a.frameThread;
  s.gamePid=a.gamePid;s.gameBirth=a.gameBirth;s.session=a.session;s.sourceRect={a.gameRect.left,a.gameRect.top,a.gameRect.right,a.gameRect.bottom};s.shellThread=GetCurrentThreadId();
  if(!CheckCapture(211,p::ObjectName(GetProcessWindowStation(),s.station,64))||!CheckCapture(212,wcscmp(s.station,L"WinSta0")==0)||!CheckCapture(213,p::ObjectName(GetThreadDesktop(GetCurrentThreadId()),s.desktop,64)))return false;
  if(a.framePid!=a.gamePid){s.sourceKind=1;auto actual=AppId(reinterpret_cast<HWND>(a.gameWindow));if(!CheckCapture(214,actual==a.expectedFrameAumid))return false;wcscpy_s(s.aumid,actual.c_str());GUITHREADINFO gui{sizeof gui};DWORD pid{};if(!CheckCapture(215,GetGUIThreadInfo(a.frameThread,&gui)!=FALSE)||!CheckCapture(216,gui.hwndFocus!=nullptr))return false;s.sourceCore=reinterpret_cast<Id>(gui.hwndFocus);s.gameThread=GetWindowThreadProcessId(gui.hwndFocus,&pid);if(!CheckCapture(217,pid==a.gamePid))return false;
  }else{s.sourceKind=2;s.sourceCore=a.gameWindow;s.gameThread=a.frameThread;wcscpy_s(s.expectedGameImage,a.gameImage.c_str());wcscpy_s(s.expectedSourceClass,a.gameClass.c_str());}
  return Current(a)&&CheckCapture(218,Valid(lease,Now()));
 }
};
class Backend final:public IWindowsBackend {
#ifdef PLAYHUB_CURTAIN_TEST
 friend struct BackendTestAccess;
#endif
 DWORD thread=GetCurrentThreadId();std::shared_ptr<IBand16CurtainBroker> broker;std::shared_ptr<Source> source;Authority owner{};
 mirror::BandTarget target{};std::wstring cls;HTHUMBNAIL thumbnail{};HMODULE ownModule{};bool begun{},retired{true},canceled{},registered{};DisplayFacts facts{};
 bool Own()const{DWORD pid{};wchar_t name[128]{};return GetCurrentThreadId()==thread&&target.window
   &&GetWindowThreadProcessId(target.window,&pid)==thread&&pid==GetCurrentProcessId()&&p::Birth(GetCurrentProcess())==target.processBirth
   &&GetAncestor(target.window,GA_ROOT)==target.window&&!GetWindow(target.window,GW_OWNER)
   &&GetWindowLongPtrW(target.window,GWLP_USERDATA)==reinterpret_cast<LONG_PTR>(this)&&GetClassNameW(target.window,name,128)&&cls==name;}
 bool Current(const BandLease& lease){facts.sourceCurrent=source&&!canceled&&lease.authority==owner&&lease.source==source->parent
   &&Valid(lease,GetTickCount64())&&source->Current(owner)&&GetTickCount64()<lease.expiresAt;return facts.sourceCurrent;}
 bool Fail(DWORD stage,DWORD error=0,HRESULT result=S_OK){facts.stage=stage;facts.error=error;facts.result=result;return false;}
 bool Black(HDC dc){RECT r{};return Own()&&dc&&GetClientRect(target.window,&r)&&FillRect(dc,&r,static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH)));}
 static LRESULT CALLBACK Proc(HWND h,UINT message,WPARAM w,LPARAM l){auto self=reinterpret_cast<Backend*>(GetWindowLongPtrW(h,GWLP_USERDATA));
  if(message==WM_NCCREATE){self=static_cast<Backend*>(reinterpret_cast<CREATESTRUCTW*>(l)->lpCreateParams);SetWindowLongPtrW(h,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(self));}
  if(self){if(message==WM_NCHITTEST)return HTTRANSPARENT;if(message==WM_MOUSEACTIVATE)return MA_NOACTIVATE;
   if(message==WM_CLOSE||message==WM_DWMCOMPOSITIONCHANGED){self->canceled=true;self->Hide();return 0;}
   if(message==WM_ERASEBKGND)return self->Black(reinterpret_cast<HDC>(w))?1:0;
   if(message==WM_PAINT){PAINTSTRUCT paint{};HDC dc=BeginPaint(h,&paint);bool okay=self->Black(dc);EndPaint(h,&paint);if(!okay)self->canceled=true;return 0;}}
  return DefWindowProcW(h,message,w,l);
 }
 bool Hide(){if(!target.window)return true;if(!Own())return false;
  if(IsWindowVisible(target.window)&&!SetWindowPos(target.window,nullptr,0,0,0,0,SWP_HIDEWINDOW|SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE|SWP_NOZORDER|SWP_NOOWNERZORDER))return false;
  facts.presented=false;facts.hidden=!IsWindowVisible(target.window);return facts.hidden;
 }
 bool ExpectedPresentation()const {return Band(target.window,presentation::ExpectedBand)
  &&(!presentation::ShellFree||(GetWindowLongPtrW(target.window,GWL_EXSTYLE)&WS_EX_TOPMOST)!=0);}
public:
 explicit Backend(std::shared_ptr<IBand16CurtainBroker> value):broker(std::move(value)){}
 DisplayFacts Facts()const noexcept override{return facts;}
 bool Begin(const BandLease& lease)override{
  if(begun||target.window||thumbnail||!cls.empty()||(!presentation::ShellFree&&!broker)||GetCurrentThreadId()!=thread)return Fail(1);
  source=std::make_shared<Source>();if(!source->Capture(lease))return Fail(source->captureFailureStage.load(std::memory_order_relaxed));owner=lease.authority;
  // The display module is process-lifetime pinned once used. Never unload the
  // code containing a WndProc or a cleanup-debt object from inside Close().
  if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,reinterpret_cast<LPCWSTR>(&CreateCurtainWindowsBackend),&ownModule))return Fail(3,GetLastError());
  static std::atomic<Id> sequence{};cls=L"Playhub.FirstContentCover."+std::to_wstring(GetCurrentProcessId())+L"."+std::to_wstring(++sequence);
  WNDCLASSEXW c{sizeof c};c.lpfnWndProc=Proc;c.hInstance=GetModuleHandleW(nullptr);c.lpszClassName=cls.c_str();c.hbrBackground=static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH));
  if(!RegisterClassExW(&c))return Fail(4,GetLastError());registered=true;
  target.processId=GetCurrentProcessId();target.processBirth=p::Birth(GetCurrentProcess());target.threadId=thread;target.cookie=reinterpret_cast<Id>(this);
  target.rectangle={owner.gameRect.left,owner.gameRect.top,owner.gameRect.right,owner.gameRect.bottom};
  target.window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TRANSPARENT|(presentation::ShellFree?WS_EX_TOPMOST:0),cls.c_str(),L"",WS_POPUP,
    owner.gameRect.left,owner.gameRect.top,owner.gameRect.right-owner.gameRect.left,owner.gameRect.bottom-owner.gameRect.top,nullptr,nullptr,c.hInstance,this);
  facts.destination=reinterpret_cast<Id>(target.window);facts.cleanupComplete=false;
  if(!Own()||IsWindowVisible(target.window)||!Current(lease))return Fail(5);
  if constexpr(presentation::ShellFree){begun=true;retired=true;facts.stage=7;facts.capability=1;return ExpectedPresentation()&&Current(lease);}
  else {BandLease admission=lease;admission.source=source;begun=true;retired=false;
  if(broker->Begin(target,admission)==mirror::BandState::Refused)return Fail(6);
  facts.stage=7;return true;}
 }
 Prepare Poll(const BandLease& lease)override{
  if(!begun||!Own()||!Current(lease))return Prepare::Refused;
  if constexpr(presentation::ShellFree){facts.band16=false;facts.capability=1;if(!ExpectedPresentation()||IsWindowVisible(target.window)){Fail(9);return Prepare::Refused;}facts.stage=10;return Prepare::Ready;}
  else {auto state=broker->Poll(target);if(state==mirror::BandState::Refused){Fail(8);return Prepare::Refused;}
  if(state!=mirror::BandState::Ready)return Prepare::Pending;
  retired=broker->Retire(target);if(!retired)return Prepare::Pending;
  facts.band16=!presentation::ShellFree&&ExpectedPresentation();if(!Own()||!facts.band16||IsWindowVisible(target.window)||!Current(lease)){Fail(9);return Prepare::Refused;}
  facts.stage=10;return Prepare::Ready;}
 }
 bool Present(const BandLease& lease)override{
  if(!begun||!retired||thumbnail||!Own()||IsWindowVisible(target.window)||!ExpectedPresentation()||!Current(lease))return Fail(11);
  BOOL composition{};HRESULT hr=DwmIsCompositionEnabled(&composition);if(FAILED(hr)||!composition)return Fail(12,0,hr);
  MARGINS all{-1,-1,-1,-1};hr=DwmExtendFrameIntoClientArea(target.window,&all);if(FAILED(hr))return Fail(13,0,hr);
  HDC dc=GetDC(target.window);bool black=Black(dc);if(dc)ReleaseDC(target.window,dc);if(!black||!Current(lease))return Fail(14);
  hr=DwmRegisterThumbnail(target.window,reinterpret_cast<HWND>(owner.steamWindow),&thumbnail);if(FAILED(hr))return Fail(15,0,hr);facts.thumbnail=true;
  SIZE size{};hr=DwmQueryThumbnailSourceSize(thumbnail,&size);
  if(FAILED(hr)||size.cx!=owner.steamRect.right-owner.steamRect.left||size.cy!=owner.steamRect.bottom-owner.steamRect.top)return Fail(16,0,hr);
  DWM_THUMBNAIL_PROPERTIES props{};props.dwFlags=DWM_TNP_RECTDESTINATION|DWM_TNP_VISIBLE|DWM_TNP_OPACITY|DWM_TNP_SOURCECLIENTAREAONLY;
  props.rcDestination={0,0,owner.gameRect.right-owner.gameRect.left,owner.gameRect.bottom-owner.gameRect.top};props.opacity=255;props.fVisible=TRUE;props.fSourceClientAreaOnly=TRUE;
  if(!Current(lease))return Fail(17);hr=DwmUpdateThumbnailProperties(thumbnail,&props);if(FAILED(hr))return Fail(17,0,hr);
  EnableWindow(target.window,FALSE);if(IsWindowEnabled(target.window)||!Own()||!Current(lease)||!ExpectedPresentation())return Fail(18);
  if(!SetWindowPos(target.window,presentation::ShellFree?HWND_TOPMOST:nullptr,0,0,0,0,SWP_SHOWWINDOW|SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE|(presentation::ShellFree?0:SWP_NOZORDER)|SWP_NOOWNERZORDER))return Fail(19,GetLastError());
  facts.hidden=!IsWindowVisible(target.window);facts.presented=!facts.hidden&&Own()&&Current(lease)&&ExpectedPresentation()&&!IsWindowEnabled(target.window);
  if(!facts.presented){Hide();return Fail(20);}facts.stage=21;return true;
 }
 bool Renew(const BandLease& lease)override{
  Rect bounds{};bool okay=begun&&retired&&thumbnail&&Own()&&Bounds(target.window,bounds)&&bounds==owner.gameRect
   &&!IsWindowEnabled(target.window)&&ExpectedPresentation()&&Current(lease);
  if(!okay)Hide();return okay;
 }
 bool Fade(const BandLease& lease,std::uint8_t opacity)override{
  if(!begun||!retired||!thumbnail||!Own()||!IsWindowVisible(target.window)||IsWindowEnabled(target.window)
     ||!ExpectedPresentation()||!Current(lease))return false;
  auto style=GetWindowLongPtrW(target.window,GWL_EXSTYLE);
  if(!(style&WS_EX_LAYERED)){
   SetLastError(0);if(!SetWindowLongPtrW(target.window,GWL_EXSTYLE,style|WS_EX_LAYERED)&&GetLastError()!=0)return false;
  }
  if(!Own()||!Current(lease)||!(GetWindowLongPtrW(target.window,GWL_EXSTYLE)&WS_EX_LAYERED)
     ||!SetLayeredWindowAttributes(target.window,0,opacity,LWA_ALPHA)||!Own()||!Current(lease))return false;
  return true;
 }
 bool Close()override{
  if(GetCurrentThreadId()!=thread)return false;
  // Source revocation never blocks withdrawal of this owned destination.
  const DWORD priorSourceFailure=source?source->captureFailureStage.load(std::memory_order_relaxed):0;
  bool sourceAtRetire=false;try{sourceAtRetire=source&&source->Current(owner);}catch(...){}facts.sourceCurrent=sourceAtRetire;
  if(!sourceAtRetire&&source){const DWORD failed=((priorSourceFailure>=201&&priorSourceFailure<=218)||(priorSourceFailure>=220&&priorSourceFailure<=230))
      ?priorSourceFailure:source->captureFailureStage.load(std::memory_order_relaxed);
   if((failed>=201&&failed<=218)||(failed>=220&&failed<=230))facts.stage=failed;} // Diagnostic only, before the owned source is released.
  if(!Hide())return false;
  if(begun&&!retired){retired=broker->Retire(target);if(!retired)return false;}
  if(thumbnail){HRESULT hr=DwmUnregisterThumbnail(thumbnail);if(FAILED(hr))return Fail(22,0,hr);thumbnail=nullptr;facts.thumbnail=false;}
  if(target.window){if(!Own()||IsWindowVisible(target.window)||!DestroyWindow(target.window))return Fail(23,GetLastError());target.window=nullptr;}
  if(registered){if(!UnregisterClassW(cls.c_str(),GetModuleHandleW(nullptr)))return Fail(24,GetLastError());registered=false;}cls.clear();
  source.reset();begun=false;facts.destination=0;facts.hidden=true;facts.presented=false;facts.cleanupComplete=true;
  ownModule=nullptr;return true;
 }
};
// Deliberate cleanup debt, matching the existing broker's quiescence contract.
// Do not destroy a still-live WndProc object or unload its retained module.
std::vector<Backend*>& Pending(){static auto* pending=new std::vector<Backend*>;return *pending;}
}
std::shared_ptr<IWindowsBackend> CreateCurtainWindowsBackend(const wchar_t* callback,const wchar_t* root){
 if(!callback||!*callback||!root||!*root)return {};
 std::shared_ptr<IBand16CurtainBroker> broker;
 if constexpr(!presentation::ShellFree){broker=CreateCurtainBand16Broker(callback,root);if(!broker)return {};}
 return std::shared_ptr<IWindowsBackend>(new Backend(std::move(broker)),[](Backend* value){bool closed=false;try{closed=value->Close();}catch(...){}if(closed)delete value;else Pending().push_back(value);});
}
}
