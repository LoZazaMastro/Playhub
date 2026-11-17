// SPDX-License-Identifier: GPL-3.0-only
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <dwmapi.h>
#include "band/Target.h"
#include "CurtainWindowsBackend.h"
#include "CurtainBandBroker.h"
#include <functional>
#include <iostream>
#include <stdexcept>
#include <vector>
using playhub::curtain::Id;
static unsigned brokerCalls{};
static bool visible{},enabled=true,band=true,foreground=true,physical=true,steamClassCurrent=true,steamAlive=true;
static void* cookie{};static std::vector<std::string> events;static std::function<void(const char*)> effect;
static void Event(const char* value){events.push_back(value);if(effect)effect(value);}
static LONG_PTR WINAPI FakeSetStyle(HWND,int,LONG_PTR value){Event("layered-style");return value;}
static BYTE lastAlpha=255;static BOOL WINAPI FakeAlpha(HWND,COLORREF,BYTE value,DWORD flags){if(flags!=LWA_ALPHA)throw std::runtime_error("alpha flags");lastAlpha=value;Event("alpha");return TRUE;}
static DWORD WINAPI FakePid(){return 1;}static DWORD WINAPI FakeThread(){return 11;}
static HANDLE WINAPI FakeProcess(){return nullptr;}static HWND WINAPI FakeForeground(){return reinterpret_cast<HWND>(Id(foreground?300:400));}
static DWORD WINAPI FakeWindowThread(HWND w,LPDWORD p){auto n=reinterpret_cast<Id>(w);*p=n==900?1:n==200?2:3;return n==900?11:n==200?22:33;}
static HWND WINAPI FakeAncestor(HWND w,UINT){return w;}static HWND WINAPI FakeOwner(HWND,UINT){return nullptr;}
static bool topmost=true,coverAboveGame=false;static unsigned showCalls{};
static LONG_PTR WINAPI FakeData(HWND,int index){return index==GWL_EXSTYLE?((topmost?WS_EX_TOPMOST:0)|WS_EX_LAYERED):reinterpret_cast<LONG_PTR>(cookie);}
static int WINAPI FakeClass(HWND w,LPWSTR out,int cap){const wchar_t* v=reinterpret_cast<Id>(w)==200?(steamClassCurrent?L"SDL_app":L"ForeignRoot"):L"Playhub.FirstContentCover.Test";wcscpy_s(out,static_cast<std::size_t>(cap),v);return static_cast<int>(wcslen(v));}
static BOOL WINAPI FakeVisible(HWND w){return reinterpret_cast<Id>(w)==200||visible;}static BOOL WINAPI FakeIconic(HWND){return FALSE;}
static BOOL WINAPI FakeBounds(HWND,LPRECT r){*r={0,0,1920,1080};return TRUE;}
static HRESULT WINAPI FakeAttribute(HWND,DWORD,void* p,DWORD){*static_cast<DWORD*>(p)=0;return S_OK;}
static HMODULE WINAPI FakeModule(LPCWSTR){return reinterpret_cast<HMODULE>(1);}
static BOOL WINAPI FakeBand(HWND,DWORD* value){*value=band?1:16;return TRUE;}
static FARPROC WINAPI FakeExport(HMODULE,LPCSTR){return reinterpret_cast<FARPROC>(&FakeBand);}
static HRESULT WINAPI FakeComposition(BOOL* p){*p=TRUE;return S_OK;}
static HRESULT WINAPI FakeExtend(HWND,const MARGINS*){return S_OK;}
static HDC WINAPI FakeDc(HWND){return reinterpret_cast<HDC>(1);}static int WINAPI FakeReleaseDc(HWND,HDC){return 1;}
static BOOL WINAPI FakeClient(HWND,LPRECT r){*r={0,0,1920,1080};return TRUE;}
static int WINAPI FakeFill(HDC,const RECT*,HBRUSH){return 1;}static HGDIOBJ WINAPI FakeStock(int){return nullptr;}
static HRESULT WINAPI FakeRegister(HWND,HWND,HTHUMBNAIL* p){Event("register");*p=reinterpret_cast<HTHUMBNAIL>(123);return S_OK;}
static HRESULT WINAPI FakeSize(HTHUMBNAIL,PSIZE s){Event("size");*s={1920,1080};return S_OK;}
static HRESULT WINAPI FakeUpdate(HTHUMBNAIL,const DWM_THUMBNAIL_PROPERTIES*){Event("update");return S_OK;}
static BOOL WINAPI FakeEnable(HWND,BOOL value){enabled=value!=FALSE;Event("disable");return TRUE;}
static BOOL WINAPI FakeEnabled(HWND){return enabled;}
static BOOL WINAPI FakePosition(HWND,HWND after,int,int,int,int,UINT flags){
 if((flags&SWP_SHOWWINDOW)!=0){if(!(flags&SWP_NOACTIVATE))throw std::runtime_error("activation flags");
  ++showCalls;if(!(flags&SWP_NOZORDER)&&after==HWND_TOPMOST)coverAboveGame=true;
  visible=true;Event("show");}
 if((flags&SWP_HIDEWINDOW)!=0){visible=false;Event("hide");}return TRUE;
}
static HRESULT WINAPI FakeDetach(HTHUMBNAIL){Event("detach");return S_OK;}
static BOOL WINAPI FakeDestroy(HWND){Event("destroy");visible=false;return TRUE;}
namespace playhub_band16 {
 static Id FakeBirth(HANDLE){return 101;}static bool FakeAlive(HANDLE,DWORD pid,Id){return pid!=2||steamAlive;}
 static bool FakePhysical(const Shared&){return physical;}static bool FakeFast(const Shared&,HANDLE,HANDLE){return physical;}
}
#define PLAYHUB_CURTAIN_TEST
#define GetCurrentProcessId FakePid
#define GetCurrentThreadId FakeThread
#define GetCurrentProcess FakeProcess
#define GetForegroundWindow FakeForeground
#define GetWindowThreadProcessId FakeWindowThread
#define GetAncestor FakeAncestor
#define GetWindow FakeOwner
#define GetWindowLongPtrW FakeData
#define SetWindowLongPtrW FakeSetStyle
#define SetLayeredWindowAttributes FakeAlpha
#define GetClassNameW FakeClass
#define IsWindowVisible FakeVisible
#define IsIconic FakeIconic
#define GetWindowRect FakeBounds
#define DwmGetWindowAttribute FakeAttribute
#define GetModuleHandleW FakeModule
#define GetProcAddress FakeExport
#define DwmIsCompositionEnabled FakeComposition
#define DwmExtendFrameIntoClientArea FakeExtend
#define GetDC FakeDc
#define ReleaseDC FakeReleaseDc
#define GetClientRect FakeClient
#define FillRect FakeFill
#define GetStockObject FakeStock
#define DwmRegisterThumbnail FakeRegister
#define DwmQueryThumbnailSourceSize FakeSize
#define DwmUpdateThumbnailProperties FakeUpdate
#define EnableWindow FakeEnable
#define IsWindowEnabled FakeEnabled
#define SetWindowPos FakePosition
#define DwmUnregisterThumbnail FakeDetach
#define DestroyWindow FakeDestroy
#define Birth FakeBirth
#define Alive FakeAlive
#define CurrentSource FakePhysical
#define FastCurrentSource FakeFast
#include "CurtainWindowsBackend.cpp"
namespace playhub::curtain {namespace {
struct Parent final:ISourceAuthority {Id tick=GetTickCount64();bool live=true;std::function<void()> read;bool Current(const Authority&)const override{if(read)read();return live;}Id Now()const noexcept override{return tick;}};
struct Broker final:IBand16CurtainBroker {bool clean=true;mirror::BandState Begin(const mirror::BandTarget&,const BandLease&)override{++brokerCalls;return mirror::BandState::Pending;}mirror::BandState Poll(const mirror::BandTarget&)override{++brokerCalls;return mirror::BandState::Ready;}bool Retire(const mirror::BandTarget&)override{Event("retire");return clean;}};
struct BackendTestAccess {
 static void Set(Backend& b,const BandLease& l){b.owner=l.authority;b.source=std::make_shared<Source>();b.source->a=l.authority;b.source->parent=l.source;
  b.target.window=reinterpret_cast<HWND>(900);b.target.processId=1;b.target.threadId=11;b.target.processBirth=101;
  b.cls=L"Playhub.FirstContentCover.Test";b.target.cookie=reinterpret_cast<Id>(&b);cookie=&b;b.begun=true;b.retired=true;}
 // Exact state after Backend::Begin rejects Source::Capture: Source has
 // copied its request, but Backend::owner is assigned only after success.
 static void RejectedCapture(Backend& b,const BandLease& l,DWORD stage){b.source=std::make_shared<Source>();if(stage!=201){b.source->a=l.authority;b.source->parent=l.source;}
  b.source->captureFailureStage.store(stage,std::memory_order_relaxed);b.facts.stage=stage;}
 static void UnknownRetire(Backend& b){b.retired=false;}
 static bool SourceCurrent(Backend& b){return b.source->Current(b.owner);}
};
}
std::shared_ptr<IBand16CurtainBroker> CreateCurtainBand16Broker(const wchar_t*,const wchar_t*){return {};}
}
int main(){using namespace playhub::curtain;try{unsigned count=0;auto check=[&](bool b){++count;if(!b)throw std::runtime_error("native backend "+std::to_string(count));};
 auto parent=std::make_shared<Parent>();BandLease l;auto& a=l.authority;a.purpose=Purpose::FirstContentCover;a.generation=a.requestEpoch=1;
 a.requestId=std::string(32,'a');a.sourceReceipt=std::string(32,'b');a.ownerBuild=std::string(64,'c');a.appId=100;a.ownerPid=1;a.ownerBirth=101;
 a.gamePid=a.framePid=3;a.gameBirth=a.frameBirth=103;a.frameThread=33;a.gameWindow=300;a.steamPid=2;a.steamBirth=102;a.steamThread=22;a.steamWindow=200;a.session=1;
 a.gameRect=a.steamRect={0,0,1920,1080};a.steamClass=L"SDL_app";a.gameClass=L"GameRoot";a.steamImage=L"C:\\Steam\\steamwebhelper.exe";a.gameImage=L"C:\\Game\\game.exe";
 l.source=parent;l.issuedAt=parent->tick;l.expiresAt=l.issuedAt+5000;l.authorizationNonce.fill('d');l.authorizationNonce[64]=0;
 auto reset=[&]{coverAboveGame=false;showCalls=0;topmost=true;brokerCalls=0;visible=false;enabled=true;foreground=physical=band=steamClassCurrent=steamAlive=true;events.clear();effect={};parent->live=true;parent->read={};};
 reset();{auto broker=std::make_shared<Broker>();Backend b(broker);BackendTestAccess::Set(b,l);check(b.Poll(l)==Prepare::Ready);check(brokerCalls==0&&!b.Facts().band16&&b.Facts().capability==1);check(b.Close());}
 reset();{auto broker=std::make_shared<Broker>();Backend b(broker);BackendTestAccess::Set(b,l);topmost=false;check(b.Poll(l)==Prepare::Refused);check(!b.Present(l));check(!visible);check(b.Close());}
 reset();{auto broker=std::make_shared<Broker>();Backend b(broker);BackendTestAccess::Set(b,l);band=false;check(b.Poll(l)==Prepare::Refused);check(!b.Present(l));check(!visible);check(b.Close());}
 reset();{auto broker=std::make_shared<Broker>();Backend b(broker);BackendTestAccess::Set(b,l);parent->live=false;check(b.Poll(l)==Prepare::Refused);check(!b.Present(l));check(!visible);check(b.Close());}
 reset();{auto broker=std::make_shared<Broker>();Backend b(broker);BackendTestAccess::Set(b,l);check(b.Present(l));check(visible&&!enabled&&foreground);check(coverAboveGame);check(events==std::vector<std::string>{"register","size","update","disable","show"});check(b.Renew(l));check(b.Fade(l,255));check(lastAlpha==255&&visible&&!enabled);check(b.Fade(l,128));check(lastAlpha==128&&foreground);check(b.Fade(l,0));check(lastAlpha==0&&visible);check(showCalls==1);check(b.Close());check(!visible);check(events[events.size()-3]=="hide"&&events[events.size()-2]=="detach"&&events.back()=="destroy");}
 for(const char* stage:{"register","size","update","disable","show"}){reset();auto broker=std::make_shared<Broker>();Backend b(broker);BackendTestAccess::Set(b,l);
  effect=[&](const char* at){if(std::string(at)==stage)parent->live=false;};check(!b.Present(l));check(!visible);check(showCalls==(std::string(stage)=="show"?1u:0u));effect={};check(b.Close());check(!visible);}
 reset();{auto broker=std::make_shared<Broker>();Backend b(broker);BackendTestAccess::Set(b,l);check(b.Present(l));parent->live=false;check(!b.Renew(l));check(!visible);check(b.Close());}
 reset();{auto broker=std::make_shared<Broker>();Backend b(broker);BackendTestAccess::Set(b,l);check(b.Present(l));BackendTestAccess::UnknownRetire(b);broker->clean=false;parent->live=false;
  check(!b.Close());check(!visible);check(std::find(events.begin(),events.end(),"destroy")==events.end());broker->clean=true;check(b.Close());check(events.back()=="destroy");}
 reset();{auto broker=std::make_shared<Broker>();Backend b(broker);BackendTestAccess::Set(b,l);check(BackendTestAccess::SourceCurrent(b));}
 for(bool death:{false,true}){reset();auto broker=std::make_shared<Broker>();Backend b(broker);BackendTestAccess::Set(b,l);unsigned reads=0;
  parent->read=[&]{if(++reads==2){if(death)steamAlive=false;else steamClassCurrent=false;}};
  check(!BackendTestAccess::SourceCurrent(b));parent->read={};}
 // Cleanup must not replace the original Capture rejection (notably the
 // explicitly retryable217 focus mismatch) with220 from the unassigned owner.
 for(DWORD stage:{217u,201u,218u,222u,225u}){reset();auto broker=std::make_shared<Broker>();Backend b(broker);BackendTestAccess::RejectedCapture(b,l,stage);
  check(b.Close());if(b.Facts().stage!=stage)std::cerr<<"capture_stage="<<stage<<" cleanup_stage="<<b.Facts().stage<<"\n";
  check(b.Facts().stage==stage);check(b.Facts().destination==0&&b.Facts().cleanupComplete&&!b.Facts().presented);check(events.empty());
  check(b.Close());check(b.Facts().stage==stage);}
 std::cout<<count<<" actual Windows backend method checks PASS; native functions replaced, no OS windows/capture/input\n";
 }catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}}
