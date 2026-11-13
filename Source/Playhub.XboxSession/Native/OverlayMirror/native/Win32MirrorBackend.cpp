// SPDX-License-Identifier: GPL-3.0-only
#include "Win32MirrorBackend.h"
#include <dwmapi.h>
#include <atomic>
#include <string>
#include <vector>
#include <cstdio>
namespace playhub::mirror {namespace {
// Per-call, GUI-owned facts. All security values come from the original
// getter invocation. No logging/query occurs until Current has already failed.
struct CurrentProbe {
 const char* field=nullptr;Id observed{},expected{};Rect actualRect{},expectedRect{};bool rectangle{};
 bool Equal(const char* name,Id actual,Id wanted){if(actual==wanted)return true;if(!field){field=name;observed=actual;expected=wanted;}return false;}
 bool RectEqual(const char* name,Rect actual,Rect wanted){if(Same(actual,wanted))return true;if(!field){field=name;actualRect=actual;expectedRect=wanted;rectangle=true;}return false;}
};
inline bool Equal(CurrentProbe* p,const char* name,Id actual,Id wanted){return p?p->Equal(name,actual,wanted):actual==wanted;}
inline bool RectEqual(CurrentProbe* p,const char* name,Rect actual,Rect wanted){return p?p->RectEqual(name,actual,wanted):Same(actual,wanted);}
inline Id ClassHash(const wchar_t* value){Id h=1469598103934665603ull;for(unsigned i=0;i<128&&value[i];++i){h^=static_cast<std::uint16_t>(value[i]);h*=1099511628211ull;}return h;}
inline bool ClassEqual(CurrentProbe* p,const char* field,const wchar_t* actual,const wchar_t* expected){if(wcscmp(actual,expected)==0)return true;if(p&&!p->field){p->field=field;p->observed=ClassHash(actual);p->expected=ClassHash(expected);}return false;}
inline bool ClassEqual(CurrentProbe* p,const char* field,const wchar_t* actual,const std::wstring& expected){if(expected==actual)return true;if(p&&!p->field){p->field=field;p->observed=ClassHash(actual);p->expected=ClassHash(expected.c_str());}return false;}
Id Birth(HANDLE p){FILETIME c{},e{},k{},u{};return GetProcessTimes(p,&c,&e,&k,&u)?(Id(c.dwHighDateTime)<<32)|c.dwLowDateTime:0;}
bool Alive(HANDLE h,DWORD pid,Id birth,CurrentProbe* p=nullptr,const char* group="process"){
 const bool game=group[0]=='g';
 return Equal(p,game?"game-handle":"frame-handle",h!=nullptr,1)&&Equal(p,game?"game-process-id":"frame-process-id",GetProcessId(h),pid)
  &&Equal(p,game?"game-birth":"frame-birth",Birth(h),birth)&&Equal(p,game?"game-wait":"frame-wait",WaitForSingleObject(h,0),WAIT_TIMEOUT);
}
bool SameUser(HANDLE a,HANDLE b){HANDLE x{},y{};if(!OpenProcessToken(a,TOKEN_QUERY,&x))return false;if(!OpenProcessToken(b,TOKEN_QUERY,&y)){CloseHandle(x);return false;}BYTE ax[1024]{},by[1024]{};DWORD n{};bool ok=GetTokenInformation(x,TokenUser,ax,sizeof(ax),&n)&&GetTokenInformation(y,TokenUser,by,sizeof(by),&n)&&IsValidSid(reinterpret_cast<TOKEN_USER*>(ax)->User.Sid)&&IsValidSid(reinterpret_cast<TOKEN_USER*>(by)->User.Sid)&&EqualSid(reinterpret_cast<TOKEN_USER*>(ax)->User.Sid,reinterpret_cast<TOKEN_USER*>(by)->User.Sid);CloseHandle(x);CloseHandle(y);return ok;}
bool GetRect(HWND h,Rect& result,CurrentProbe* p=nullptr,const char* field="rectangle-read"){RECT r{};if(!Equal(p,field,GetWindowRect(h,&r)!=FALSE,1))return false;result={r.left,r.top,r.right,r.bottom};return Equal(p,field,ValidRect(result),1);}
bool Band(HWND h,DWORD expected,CurrentProbe* p=nullptr,const char* field="band"){using Get=BOOL(WINAPI*)(HWND,DWORD*);auto get=reinterpret_cast<Get>(GetProcAddress(GetModuleHandleW(L"user32.dll"),"GetWindowBand"));DWORD band{};return Equal(p,field,get!=nullptr,1)&&Equal(p,field,get(h,&band)!=FALSE,1)&&Equal(p,field,band,expected);}
bool Desktop(DWORD frameThread,CurrentProbe* p=nullptr){wchar_t own[128]{},frame[128]{},active[128]{};DWORD n{};auto name=[&](HANDLE h,wchar_t* target){return h&&GetUserObjectInformationW(h,UOI_NAME,target,128*sizeof(wchar_t),&n)&&n>sizeof(wchar_t)&&n<=128*sizeof(wchar_t);};HDESK input=OpenInputDesktop(0,FALSE,DESKTOP_READOBJECTS);bool ok=name(GetThreadDesktop(GetCurrentThreadId()),own)&&name(GetThreadDesktop(frameThread),frame)&&name(input,active)&&wcscmp(own,frame)==0&&wcscmp(own,active)==0;if(input)CloseDesktop(input);return Equal(p,"desktop",ok,1);}
struct Native final {
 std::shared_ptr<IBand16Broker> broker;SourceLease source{};BandTarget target{};DWORD ownerThread=GetCurrentThreadId();std::wstring className;HANDLE game{},frame{};HTHUMBNAIL thumbnail{};bool bandStarted{},retired=true,glassReady{},cancelled{};Id cookie{},reportedFailureGeneration{};unsigned diagnosticRows{};
 explicit Native(std::shared_ptr<IBand16Broker> b):broker(std::move(b)){}
 bool Thread(CurrentProbe* p=nullptr)const{return Equal(p,"owner-thread",GetCurrentThreadId(),ownerThread);}
 bool Own(CurrentProbe* p=nullptr)const{DWORD pid{};wchar_t cls[128]{};
 return Thread(p)&&Equal(p,"target-present",target.window!=nullptr,1)&&Equal(p,"target-thread",GetWindowThreadProcessId(target.window,&pid),ownerThread)
  &&Equal(p,"target-pid",pid,GetCurrentProcessId())&&Equal(p,"target-root",reinterpret_cast<Id>(GetAncestor(target.window,GA_ROOT)),reinterpret_cast<Id>(target.window))
  &&Equal(p,"target-owner",reinterpret_cast<Id>(GetWindow(target.window,GW_OWNER)),0)&&Equal(p,"target-cookie",static_cast<Id>(GetWindowLongPtrW(target.window,GWLP_USERDATA)),reinterpret_cast<Id>(this))
  &&Equal(p,"target-class-read",GetClassNameW(target.window,cls,128)!=0,1)&&ClassEqual(p,"target-class",cls,className)
  &&Equal(p,"target-birth",Birth(GetCurrentProcess()),target.processBirth);
 }
 bool Context(const SourceLease& l,CurrentProbe* p=nullptr){DWORD pid{},session{};Rect sourceBounds{},gameBounds{};wchar_t cls[128]{};HWND sw=reinterpret_cast<HWND>(l.sourceWindow),gw=reinterpret_cast<HWND>(l.gameWindow);
  return Thread(p)&&Equal(p,"cancelled",cancelled,0)&&Equal(p,"source-owner-pid",GetCurrentProcessId(),l.ownerPid)&&Equal(p,"source-owner-birth",Birth(GetCurrentProcess()),l.ownerBirth)
   &&Alive(game,l.gamePid,l.gameBirth,p,"game-alive")&&Alive(frame,l.framePid,l.frameBirth,p,"frame-alive")
   &&Equal(p,"game-session-read",ProcessIdToSessionId(l.gamePid,&session)!=FALSE,1)&&Equal(p,"game-session",session,l.session)
   &&Equal(p,"frame-session-read",ProcessIdToSessionId(l.framePid,&session)!=FALSE,1)&&Equal(p,"frame-session",session,l.session)
   &&Equal(p,"owner-session-read",ProcessIdToSessionId(l.ownerPid,&session)!=FALSE,1)&&Equal(p,"owner-session",session,l.session)
   &&Equal(p,"source-thread",GetWindowThreadProcessId(sw,&pid),l.sourceThread)&&Equal(p,"source-pid",pid,l.ownerPid)
   &&Equal(p,"source-root",reinterpret_cast<Id>(GetAncestor(sw,GA_ROOT)),reinterpret_cast<Id>(sw))&&Equal(p,"source-owner",reinterpret_cast<Id>(GetWindow(sw,GW_OWNER)),0)
   &&Equal(p,"source-class-read",GetClassNameW(sw,cls,128)!=0,1)&&ClassEqual(p,"source-class",cls,L"PlayhubSteamCompanion")
   &&Equal(p,"source-cookie",static_cast<Id>(GetWindowLongPtrW(sw,GWLP_USERDATA)),l.sourceCookie)&&Equal(p,"source-visible",IsWindowVisible(sw)!=FALSE,1)&&Equal(p,"source-enabled",IsWindowEnabled(sw)!=FALSE,1)
   &&GetRect(sw,sourceBounds,p,"source-rect-read")&&RectEqual(p,"source-rect",sourceBounds,l.sourceRect)&&Band(sw,1,p,"source-band")
   &&Equal(p,"game-thread",GetWindowThreadProcessId(gw,&pid),l.frameThread)&&Equal(p,"game-pid",pid,l.framePid)&&Equal(p,"game-root",reinterpret_cast<Id>(GetAncestor(gw,GA_ROOT)),reinterpret_cast<Id>(gw))
   &&Equal(p,"game-visible",IsWindowVisible(gw)!=FALSE,1)&&Equal(p,"game-enabled",IsWindowEnabled(gw)!=FALSE,1)&&Equal(p,"game-iconic",IsIconic(gw)!=FALSE,0)
   &&GetRect(gw,gameBounds,p,"game-rect-read")&&RectEqual(p,"game-rect",gameBounds,l.gameRect)&&Desktop(l.frameThread,p)&&Equal(p,"game-foreground",reinterpret_cast<Id>(GetForegroundWindow()),reinterpret_cast<Id>(gw));
 }
 void ReportCurrentFailure(const SourceLease& l,const CurrentProbe& p)noexcept{
  struct PreserveError {DWORD value;~PreserveError(){SetLastError(value);}} preserve{GetLastError()};
  try{
  if(!p.field||!l.generation||!l.ownerPid||!l.ownerBirth||reportedFailureGeneration==l.generation||diagnosticRows>=32)return;
  reportedFailureGeneration=l.generation;++diagnosticRows;
  // Separate file avoids corrupting the existing EventLog writer's file cursor.
  // These diagnostic file/timestamp calls are not authority/security re-queries.
  wchar_t temp[32768]{};DWORD n=GetTempPathW(32768,temp);if(!n||n>=32768)return;std::wstring folder=std::wstring(temp)+L"Playhub-Live-Mirror-20261112";
  if(!CreateDirectoryW(folder.c_str(),nullptr)&&GetLastError()!=ERROR_ALREADY_EXISTS)return;
  DWORD attributes=GetFileAttributesW(folder.c_str());if(attributes==INVALID_FILE_ATTRIBUTES||!(attributes&FILE_ATTRIBUTE_DIRECTORY)||(attributes&FILE_ATTRIBUTE_REPARSE_POINT))return;
  std::wstring path=folder+L"\\backend-"+std::to_wstring(l.ownerPid)+L"-"+std::to_wstring(l.ownerBirth)+L"-g"+std::to_wstring(l.generation)+L".jsonl";
  HANDLE file=CreateFileW(path.c_str(),FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL|FILE_FLAG_OPEN_REPARSE_POINT,nullptr);if(file==INVALID_HANDLE_VALUE)return;
  BY_HANDLE_FILE_INFORMATION info{};if(!GetFileInformationByHandle(file,&info)||(info.dwFileAttributes&(FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT))){CloseHandle(file);return;}
  char text[1536]{};int bytes=snprintf(text,sizeof(text),"{\"stage\":\"backend-current-refused\",\"bootMs\":%llu,\"generation\":%llu,\"field\":\"%s\",\"observed\":\"%llu\",\"expected\":\"%llu\",\"rectangle\":%s,\"actualRect\":[%d,%d,%d,%d],\"expectedRect\":[%d,%d,%d,%d],\"gameGuiSampled\":false,\"securityGuardRelaxed\":false}\n",
   static_cast<unsigned long long>(GetTickCount64()),static_cast<unsigned long long>(l.generation),p.field,static_cast<unsigned long long>(p.observed),static_cast<unsigned long long>(p.expected),p.rectangle?"true":"false",p.actualRect.left,p.actualRect.top,p.actualRect.right,p.actualRect.bottom,p.expectedRect.left,p.expectedRect.top,p.expectedRect.right,p.expectedRect.bottom);
  if(bytes>0&&bytes<int(sizeof(text))){DWORD written{};WriteFile(file,text,DWORD(bytes),&written,nullptr);}CloseHandle(file);
  }catch(...){/* Diagnostic failure cannot alter the false Current result. */}
 }

 bool Paint(HDC dc){RECT client{};return Own()&&dc&&GetClientRect(target.window,&client)&&FillRect(dc,&client,static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH)))!=0;}
 bool Glass(){glassReady=false;if(!Own())return false;BOOL enabled{};MARGINS all{-1,-1,-1,-1};if(FAILED(DwmIsCompositionEnabled(&enabled))||!enabled||FAILED(DwmExtendFrameIntoClientArea(target.window,&all)))return false;HDC dc=GetDC(target.window);glassReady=Paint(dc);if(dc)ReleaseDC(target.window,dc);return glassReady;}
 static LRESULT CALLBACK Proc(HWND h,UINT m,WPARAM w,LPARAM l){Native* self=reinterpret_cast<Native*>(GetWindowLongPtrW(h,GWLP_USERDATA));if(m==WM_NCCREATE){auto create=reinterpret_cast<CREATESTRUCTW*>(l);self=static_cast<Native*>(create->lpCreateParams);SetWindowLongPtrW(h,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(self));}if(self){if(m==WM_MOUSEACTIVATE)return MA_NOACTIVATE;if(m==WM_CLOSE){self->cancelled=true;SetWindowPos(h,nullptr,0,0,0,0,SWP_HIDEWINDOW|SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE|SWP_NOZORDER);return 0;}if(m==WM_DWMCOMPOSITIONCHANGED){self->Glass();return 0;}if(m==WM_ERASEBKGND){bool painted=self->Paint(reinterpret_cast<HDC>(w));if(!painted)self->glassReady=false;return painted?1:0;}if(m==WM_PAINT){PAINTSTRUCT ps{};HDC dc=BeginPaint(h,&ps);bool painted=self->Paint(dc);EndPaint(h,&ps);if(!painted)self->glassReady=false;return 0;}}
  return DefWindowProcW(h,m,w,l);
 }
 bool Create(const SourceLease& l){if(!Thread()||target.window||!broker)return false;source=l;cancelled=false;game=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,l.gamePid);frame=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,l.framePid);if(!Alive(game,l.gamePid,l.gameBirth)||!Alive(frame,l.framePid,l.frameBirth)||!SameUser(game,GetCurrentProcess())||!SameUser(frame,GetCurrentProcess())||!Context(l))return false;
  static std::atomic<Id> next{0};cookie=++next;className=L"Playhub.OverlayMirror."+std::to_wstring(GetCurrentProcessId())+L"."+std::to_wstring(cookie);WNDCLASSEXW cls{sizeof(cls)};cls.lpfnWndProc=Proc;cls.hInstance=GetModuleHandleW(nullptr);cls.lpszClassName=className.c_str();cls.hbrBackground=static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH));if(!RegisterClassExW(&cls))return false;
  target.processId=GetCurrentProcessId();target.threadId=ownerThread;target.processBirth=Birth(GetCurrentProcess());target.cookie=reinterpret_cast<Id>(this);target.rectangle={l.gameRect.left,l.gameRect.top,l.gameRect.left+128,l.gameRect.top+64};target.window=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,className.c_str(),L"",WS_POPUP,target.rectangle.left,target.rectangle.top,128,64,nullptr,nullptr,cls.hInstance,this);
  if(!Own()||IsWindowVisible(target.window)||!Context(l))return false;bandStarted=true;retired=false;return broker->Begin(target,l)!=BandState::Refused;
 }
 BandState Poll(){if(!Thread()||!Own())return BandState::Refused;auto state=broker->Poll(target);if(state==BandState::Ready){retired=broker->Retire(target);if(!retired||!Band(target.window,16)||IsWindowVisible(target.window))return BandState::Refused;}return state;}
 bool Attach(const SourceLease& l){if(!Own()||!retired||thumbnail||IsWindowVisible(target.window)||!Context(l)||!Band(target.window,16))return false;MONITORINFO monitor{sizeof(monitor)};auto display=MonitorFromWindow(reinterpret_cast<HWND>(l.gameWindow),MONITOR_DEFAULTTONULL);if(!display||!GetMonitorInfoW(display,&monitor))return false;Rect r{monitor.rcMonitor.left,monitor.rcMonitor.top,monitor.rcMonitor.right,monitor.rcMonitor.bottom};if(!ValidRect(r))return false;if(!SetWindowPos(target.window,nullptr,r.left,r.top,r.right-r.left,r.bottom-r.top,SWP_NOACTIVATE|SWP_NOZORDER|SWP_NOOWNERZORDER))return false;Rect actual{};if(!Own()||!GetRect(target.window,actual)||!Same(actual,r)||IsWindowVisible(target.window)||!Context(l))return false;target.rectangle=actual;if(!Glass()||!Context(l)||GetTickCount64()>=l.expiresAt)return false;
  if(FAILED(DwmRegisterThumbnail(target.window,reinterpret_cast<HWND>(l.sourceWindow),&thumbnail)))return false;DWM_THUMBNAIL_PROPERTIES p{};p.dwFlags=DWM_TNP_RECTDESTINATION|DWM_TNP_VISIBLE|DWM_TNP_OPACITY|DWM_TNP_SOURCECLIENTAREAONLY;p.rcDestination={0,0,r.right-r.left,r.bottom-r.top};p.opacity=255;p.fVisible=TRUE;p.fSourceClientAreaOnly=TRUE;return SUCCEEDED(DwmUpdateThumbnailProperties(thumbnail,&p))&&Context(l)&&Own()&&Band(target.window,16)&&GetTickCount64()<l.expiresAt;
 }
 bool Show(const SourceLease& l){if(!Own()||!retired||!thumbnail||!glassReady||!Context(l)||GetTickCount64()>=l.expiresAt)return false;bool shown=SetWindowPos(target.window,nullptr,0,0,0,0,SWP_SHOWWINDOW|SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE|SWP_NOZORDER|SWP_NOOWNERZORDER)!=FALSE;return shown&&Own()&&IsWindowVisible(target.window)&&Context(l)&&Band(target.window,16)&&GetTickCount64()<l.expiresAt;}
 bool Hide(){if(!target.window)return true;if(!Own())return false;if(IsWindowVisible(target.window)&&!SetWindowPos(target.window,nullptr,0,0,0,0,SWP_HIDEWINDOW|SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE|SWP_NOZORDER|SWP_NOOWNERZORDER))return false;return !IsWindowVisible(target.window);}
 bool Detach(){if(!Thread())return false;if(!thumbnail)return true;if(FAILED(DwmUnregisterThumbnail(thumbnail)))return false;thumbnail=nullptr;return true;}
 bool Retire(){if(!Thread())return false;if(!bandStarted||retired)return true;retired=broker->Retire(target);return retired;}
 bool Destroy(){if(!Thread()||!retired||thumbnail)return false;if(target.window){if(!Own()||IsWindowVisible(target.window)||!DestroyWindow(target.window))return false;target.window=nullptr;}if(!className.empty()){if(!UnregisterClassW(className.c_str(),GetModuleHandleW(nullptr)))return false;className.clear();}if(game){CloseHandle(game);game=nullptr;}if(frame){CloseHandle(frame);frame=nullptr;}bandStarted=false;return true;}
 bool Cleanup(){if(!Thread())return false;bool hidden=Hide(),detached=Detach(),safe=Retire();return hidden&&detached&&safe&&Destroy();}
};
std::mutex retiredMutex;std::vector<std::unique_ptr<Native>>& Retained(){static auto* list=new std::vector<std::unique_ptr<Native>>;return *list;}
class Backend final:public IBackend {
 std::unique_ptr<Native> native;
public:
 explicit Backend(std::shared_ptr<IBand16Broker> b):native(std::make_unique<Native>(std::move(b))){}
 ~Backend(){if(native&&!native->Cleanup()){std::lock_guard<std::mutex> lock(retiredMutex);Retained().push_back(std::move(native));}}
 bool CreateHidden(const SourceLease& l)override{return native->Create(l);}BandState PollBand()override{return native->Poll();}bool AttachGpu(const SourceLease& l)override{return native->Attach(l);}bool ShowNoActivate(const SourceLease& l)override{return native->Show(l);}bool Current(const SourceLease& l)override{Rect current{};CurrentProbe p;
  bool valid=native->Own(&p)&&GetRect(native->target.window,current,&p,"target-rect-read")&&RectEqual(&p,"target-rect",current,native->target.rectangle)
   &&(!native->thumbnail||(Equal(&p,"glass-ready",native->glassReady,1)&&Band(native->target.window,16,&p,"target-band")))&&native->Context(l,&p);
  if(!valid)native->ReportCurrentFailure(l,p);return valid;
 }
 bool Hide()override{return native->Hide();}bool DetachGpu()override{return native->Detach();}bool RetireBand()override{return native->Retire();}bool DestroyOwned()override{return native->Destroy();}Id Window()const override{return reinterpret_cast<Id>(native->target.window);}Id Cookie()const override{return native->target.cookie;}bool IsShown()const override{return native->Own()&&IsWindowVisible(native->target.window);}bool HasThumbnail()const override{return native->thumbnail!=nullptr;}
};
}
std::shared_ptr<IBackend> CreateWin32MirrorBackend(std::shared_ptr<IBand16Broker> broker){return broker?std::make_shared<Backend>(std::move(broker)):nullptr;}
void DrainRetainedMirrorCleanup(){std::vector<std::unique_ptr<Native>> pending;{std::lock_guard<std::mutex> lock(retiredMutex);pending.swap(Retained());}for(auto& owner:pending)if(owner&&owner->Thread()&&owner->Cleanup())owner.reset();{std::lock_guard<std::mutex> lock(retiredMutex);for(auto& owner:pending)if(owner)Retained().push_back(std::move(owner));}}
}
