// SPDX-License-Identifier: GPL-3.0-only
#include "HudDisplayApi.h"
#include "HudWindowsBackend.h"
#include "HudMailbox.h"
#include <atomic>
#include <thread>
#include <mutex>
#include <optional>
#include <condition_variable>
#include <string>
#include <objbase.h>
namespace playhub::hud {namespace {
std::optional<Pulse> Decode(const WirePulse* w,std::uint32_t bytes){if(!w||bytes!=sizeof(*w)||w->size!=sizeof(*w)||w->version!=1||w->reserved||w->purpose!=1||w->enabled>1||w->suspended>1||w->ready>1||w->collectorLease[32]||w->ownerBuild[64]||w->wpfClass[255]||w->expectedFrameAumid[512])return {};Pulse p;auto& a=p.authority;a.purpose=Purpose::PerformanceHud;a.appId=w->appId;a.agentPid=w->agentPid;a.gamePid=w->gamePid;a.framePid=w->framePid;a.frameThread=w->frameThread;a.sourceThread=w->sourceThread;a.session=w->session;a.generation=w->generation;a.collectorEpoch=w->collectorEpoch;a.agentBirth=w->agentBirth;a.gameBirth=w->gameBirth;a.frameBirth=w->frameBirth;a.gameWindow=w->gameWindow;a.wpfWindow=w->wpfWindow;a.wpfInstance=w->wpfInstance;a.collectorLease=w->collectorLease;a.ownerBuild=w->ownerBuild;a.wpfClass=w->wpfClass;a.expectedFrameAumid=w->expectedFrameAumid;a.gameRect={w->gameRect[0],w->gameRect[1],w->gameRect[2],w->gameRect[3]};a.wpfRect={w->wpfRect[0],w->wpfRect[1],w->wpfRect[2],w->wpfRect[3]};p.sequence=w->sequence;p.sampleTick=w->sampleTick;p.observedAt=w->observedAt;p.leaseExpiresAt=w->leaseExpiresAt;p.enabled=w->enabled!=0;p.suspended=w->suspended!=0;p.ready=w->ready!=0;return Valid(a)?std::optional<Pulse>(std::move(p)):std::nullopt;}
class Manager {
 std::mutex gate;std::condition_variable wake;Mailbox mailbox;bool configured{},failed{};std::wstring callback,root;WireSnapshot published{sizeof(WireSnapshot),1,0,1};IWindowsBackend* workerBackend{};Engine* workerEngine{};
 void StartupFailed(std::uint32_t stage,DWORD error,HRESULT hr){std::lock_guard lock(gate);failed=true;published.stage=stage;published.error=error;published.hresult=hr;published.observedAt=GetTickCount64();mailbox.latest.reset();mailbox.begin.reset();}
 void Run(HMODULE module){if(!SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)||!AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(),DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)){StartupFailed(100,GetLastError(),E_FAIL);FreeLibraryAndExitThread(module,1);return;}HRESULT com=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);if(FAILED(com)){StartupFailed(101,0,com);FreeLibraryAndExitThread(module,1);return;}auto backend=CreateHudWindowsBackend(callback.c_str(),root.c_str());if(!backend){StartupFailed(102,0,E_FAIL);if(SUCCEEDED(com))CoUninitialize();FreeLibraryAndExitThread(module,1);return;}workerBackend=backend.release();workerEngine=new Engine(*workerBackend);auto& engine=*workerEngine;auto* backendView=workerBackend;backendView->SetPublisherGuard([this](const Authority& a){std::lock_guard lock(gate);return mailbox.latest&&mailbox.latest->authority==a&&Fresh(*mailbox.latest,GetTickCount64())&&!mailbox.close?mailbox.latest->leaseExpiresAt:Id(0);});
  // Deliberately retained configured worker. It pumps cleanup after Close while
  // foreign work is uncertain. No detached HWND/module is reported cleaned.
  for(;;){std::optional<Pulse> begin;bool stop;{std::lock_guard lock(gate);begin=std::move(mailbox.begin);mailbox.begin.reset();stop=mailbox.close;mailbox.close=false;}
   if(stop)engine.Close();bool rejected=false;if(begin&&!stop)rejected=!engine.Begin(*begin,GetTickCount64());
   MSG message{};for(unsigned i=0;i<64&&PeekMessageW(&message,nullptr,0,0,PM_REMOVE);i++){if(message.message==WM_QUIT)engine.Close();else{TranslateMessage(&message);DispatchMessageW(&message);}}
   engine.Tick([&]{std::lock_guard lock(gate);return mailbox.latest.value_or(Pulse{});},[]{return GetTickCount64();});auto s=engine.Read();auto d=backendView->Diagnostics();WireSnapshot copy{sizeof(WireSnapshot),1,std::uint32_t(s.phase),s.cleanupComplete?1u:0u,d.stage,d.error,d.hresult,(d.band16?1u:0u)|(d.disabled?2u:0u)|(d.thumbnail?4u:0u)|(d.visible?8u:0u)|(d.sourceCurrent?16u:0u),s.generation,s.source,s.destination,s.shownSequence,GetTickCount64(),d.dwmUpdates};
   std::unique_lock lock(gate);published=copy;if(copy.phase==std::uint32_t(Phase::Closed)&&copy.cleanupComplete){if((stop||rejected)&&mailbox.admitted)mailbox.Closed(mailbox.admitted->generation);else mailbox.Closed(copy.generation);wake.wait(lock,[&]{return mailbox.close||mailbox.begin.has_value();});}else wake.wait_for(lock,std::chrono::milliseconds(8),[&]{return mailbox.close||mailbox.begin.has_value();});
  }
 }
 void Recover(HMODULE module)noexcept{
  StartupFailed(103,0,E_FAIL);
  // Keep the same GUI thread, HWND userdata and module alive while a callback
  // may be committed. Only a confirmed cleanup permits this worker to exit.
  for(;;){bool done=workerEngine==nullptr;try{if(workerEngine){workerEngine->Close();auto s=workerEngine->Read();done=s.cleanupComplete;std::lock_guard lock(gate);published.phase=std::uint32_t(s.phase);published.cleanupComplete=done?1:0;published.generation=s.generation;published.source=s.source;published.destination=s.destination;published.flags=0;published.observedAt=GetTickCount64();}MSG m{};for(unsigned i=0;i<64&&PeekMessageW(&m,nullptr,0,0,PM_REMOVE);i++){TranslateMessage(&m);DispatchMessageW(&m);}}catch(...){done=false;}if(done)break;Sleep(8);}
  CoUninitialize();FreeLibraryAndExitThread(module,1);
 }
public:
 int Configure(const wchar_t* c,const wchar_t* r){if(!c||!r||wcsnlen_s(c,32768)<4||wcsnlen_s(c,32768)>=32768||wcsnlen_s(r,32768)<4||wcsnlen_s(r,32768)>=32768)return 0;std::lock_guard lock(gate);if(configured)return callback==c&&root==r?1:0;HMODULE pin{};if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,reinterpret_cast<LPCWSTR>(&ConfigureHudDisplayV1),&pin))return 0;callback=c;root=r;configured=true;try{std::thread([this,pin]{try{Run(pin);}catch(...){Recover(pin);}}).detach();return 1;}catch(...){configured=false;FreeLibrary(pin);return 0;}}
 int Queue(std::uint32_t action,const WirePulse* w,std::uint32_t bytes){auto p=Decode(w,bytes);if(!p)return 0;std::lock_guard lock(gate);if(!configured||failed||!mailbox.Queue(action,*p,GetTickCount64()))return 0;wake.notify_one();return 1;}
 int Read(WireSnapshot* out,std::uint32_t bytes){if(!out||bytes!=sizeof(*out))return 0;std::lock_guard lock(gate);if(!configured)return 0;*out=published;return 1;}
};
Manager& State(){static auto* state=new Manager;return *state;}
}}
extern "C" int WINAPI ConfigureHudDisplayV1(const wchar_t* callback,const wchar_t* root){try{return playhub::hud::State().Configure(callback,root);}catch(...){return 0;}}
extern "C" int WINAPI QueueHudDisplayV1(std::uint32_t action,const playhub::hud::WirePulse* pulse,std::uint32_t bytes){try{return playhub::hud::State().Queue(action,pulse,bytes);}catch(...){return 0;}}
extern "C" int WINAPI ReadHudDisplayV1(playhub::hud::WireSnapshot* out,std::uint32_t bytes){try{return playhub::hud::State().Read(out,bytes);}catch(...){return 0;}}
