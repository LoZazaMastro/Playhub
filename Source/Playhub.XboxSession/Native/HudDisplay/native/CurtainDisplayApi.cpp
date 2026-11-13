// SPDX-License-Identifier: GPL-3.0-only
#include "CurtainDisplayApi.h"
#include "CurtainPublisherMailbox.h"
#include <condition_variable>
#include <mutex>
#include <thread>
#include <objbase.h>
namespace playhub::curtain {namespace {
std::optional<PublisherPulse> Decode(const WirePulse* w,std::uint32_t bytes){
 if(!w||bytes!=sizeof(*w)||w->size!=sizeof(*w)||w->version!=1||w->purpose!=2||w->reserved
    ||w->requestId[32]||w->sourceReceipt[32]||w->ownerBuild[64]||w->steamClass[255]||w->gameClass[255]
    ||w->steamImage[4095]||w->gameImage[4095]||w->expectedFrameAumid[512])return {};
 PublisherPulse p;auto& a=p.authority;a.purpose=Purpose::FirstContentCover;a.appId=w->appId;a.ownerPid=w->ownerPid;a.gamePid=w->gamePid;
 a.framePid=w->framePid;a.frameThread=w->frameThread;a.steamPid=w->steamPid;a.steamThread=w->steamThread;a.session=w->session;
 a.generation=w->generation;a.requestEpoch=w->requestEpoch;a.ownerBirth=w->ownerBirth;a.gameBirth=w->gameBirth;a.frameBirth=w->frameBirth;
 a.gameWindow=w->gameWindow;a.steamBirth=w->steamBirth;a.steamWindow=w->steamWindow;
 a.gameRect={w->gameRect[0],w->gameRect[1],w->gameRect[2],w->gameRect[3]};a.steamRect={w->steamRect[0],w->steamRect[1],w->steamRect[2],w->steamRect[3]};
 a.requestId=w->requestId;a.sourceReceipt=w->sourceReceipt;a.ownerBuild=w->ownerBuild;a.steamClass=w->steamClass;a.gameClass=w->gameClass;
 a.steamImage=w->steamImage;a.gameImage=w->gameImage;a.expectedFrameAumid=w->expectedFrameAumid;
 p.sequence=w->sequence;p.observedAt=w->observedAt;p.expiresAt=w->expiresAt;return Valid(a)?std::optional<PublisherPulse>(std::move(p)):std::nullopt;
}
class Manager {
 std::mutex gate;std::condition_variable wake;PublisherMailbox mailbox;bool configured{},failed{};
 std::wstring callback,root;WireSnapshot published{sizeof(WireSnapshot),1,0,1};
 std::shared_ptr<IWindowsBackend> workerBackend;DisplayEngine* workerEngine{};
 struct Publisher final:ISourceAuthority {
  Manager& owner;explicit Publisher(Manager& m):owner(m){}
  bool Current(const Authority& a)const override{std::lock_guard lock(owner.gate);return !owner.failed&&!owner.mailbox.close
     &&owner.mailbox.latest&&owner.mailbox.latest->authority==a&&Fresh(*owner.mailbox.latest,GetTickCount64());}
  Id Now()const noexcept override{return GetTickCount64();}
 };
 std::shared_ptr<Publisher> publisher=std::make_shared<Publisher>(*this);
 BandLease Latest(){std::lock_guard lock(gate);BandLease lease;if(!mailbox.latest||mailbox.close||failed)return lease;
  const auto& p=*mailbox.latest;lease.authority=p.authority;lease.issuedAt=p.observedAt;lease.expiresAt=p.observedAt+5000;lease.source=publisher;
  // Static callback admission may include slow trust work. Its5s ticket is
  // never a heartbeat grant: Publisher::Current separately requires the live
  // latest250ms pulse and its bounded authenticated intent deadline.
  // The existing callback needs a fresh physical authorization nonce, not a
  // menu/HUD claim. Two private fixed identifiers form64hex for this owner.
  auto nonce=p.authority.requestId+p.authority.sourceReceipt;std::copy(nonce.begin(),nonce.end(),lease.authorizationNonce.begin());
  wcscpy_s(lease.actualFrameAumid.data(),lease.actualFrameAumid.size(),p.authority.expectedFrameAumid.c_str());return lease;
 }
 void Failure(DWORD stage,DWORD error,HRESULT result){std::lock_guard lock(gate);failed=true;mailbox.latest.reset();mailbox.begin.reset();published.stage=stage;published.error=error;published.result=result;published.observedAt=GetTickCount64();}
 void Publish(){auto s=workerEngine->Read();auto d=workerBackend->Facts();WireSnapshot copy{sizeof(WireSnapshot),1,std::uint32_t(s.phase),s.cleanupComplete?1u:0u,d.stage,d.error,d.result,
   (d.band16?1u:0u)|(d.hidden?2u:0u)|(d.thumbnail?4u:0u)|(d.presented?8u:0u)|(d.sourceCurrent?16u:0u),s.generation,d.destination,GetTickCount64()};
  std::lock_guard lock(gate);published=copy;
 }
 void Run(HMODULE module){
  if(!SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)||!AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(),DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)){
   Failure(100,GetLastError(),E_FAIL);FreeLibraryAndExitThread(module,1);return;}
  HRESULT com=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);if(FAILED(com)){Failure(101,0,com);FreeLibraryAndExitThread(module,1);return;}
  workerBackend=CreateCurtainWindowsBackend(callback.c_str(),root.c_str());if(!workerBackend){Failure(102,0,E_FAIL);CoUninitialize();FreeLibraryAndExitThread(module,1);return;}
  workerEngine=new DisplayEngine(*workerBackend);
  for(;;){bool begin{},stop{};{std::lock_guard lock(gate);begin=mailbox.begin.has_value();mailbox.begin.reset();stop=mailbox.close;mailbox.close=false;}
   if(stop)workerEngine->Close();bool rejected=false;if(begin&&!stop)rejected=!workerEngine->Begin([&]{return Latest();},[]{return GetTickCount64();});
   MSG message{};for(unsigned n=0;n<64&&PeekMessageW(&message,nullptr,0,0,PM_REMOVE);++n){if(message.message==WM_QUIT)workerEngine->Close();else{TranslateMessage(&message);DispatchMessageW(&message);}}
   workerEngine->Tick([&]{return Latest();},[]{return GetTickCount64();});Publish();
   std::unique_lock lock(gate);if(published.phase==std::uint32_t(DisplayPhase::Closed)&&published.cleanupComplete){
    if(mailbox.admitted)mailbox.Closed((stop||rejected)?mailbox.admitted->generation:published.generation);
    wake.wait(lock,[&]{return mailbox.close||mailbox.begin.has_value();});
   }else wake.wait_for(lock,std::chrono::milliseconds(8),[&]{return mailbox.close||mailbox.begin.has_value();});
  }
 }
 void Recover(HMODULE module)noexcept{
  Failure(103,0,E_FAIL);for(;;){bool done=workerEngine==nullptr;try{
   if(workerEngine){workerEngine->Close();done=workerEngine->Read().cleanupComplete;Publish();}
   MSG message{};for(unsigned n=0;n<64&&PeekMessageW(&message,nullptr,0,0,PM_REMOVE);++n){TranslateMessage(&message);DispatchMessageW(&message);}
  }catch(...){done=false;}if(done)break;Sleep(8);}CoUninitialize();FreeLibraryAndExitThread(module,1);
 }
public:
 int Configure(const wchar_t* dll,const wchar_t* path){if(!dll||!path||wcsnlen_s(dll,32768)<4||wcsnlen_s(dll,32768)>=32768||wcsnlen_s(path,32768)<4||wcsnlen_s(path,32768)>=32768)return 0;
  std::lock_guard lock(gate);if(configured)return callback==dll&&root==path?1:0;HMODULE module{};
  if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,reinterpret_cast<LPCWSTR>(&ConfigureCurtainDisplayV1),&module))return 0;
  callback=dll;root=path;configured=true;try{std::thread([this,module]{try{Run(module);}catch(...){Recover(module);}}).detach();return 1;}catch(...){configured=false;FreeLibrary(module);return 0;}}
 int Queue(std::uint32_t action,const WirePulse* value,std::uint32_t bytes){auto pulse=Decode(value,bytes);if(!pulse)return 0;std::lock_guard lock(gate);
  if(!configured||failed||!mailbox.Queue(action,*pulse,GetTickCount64()))return 0;wake.notify_one();return 1;}
 int Read(WireSnapshot* out,std::uint32_t bytes){if(!out||bytes!=sizeof(*out))return 0;std::lock_guard lock(gate);if(!configured)return 0;*out=published;return 1;}
};
Manager& State(){static auto* manager=new Manager;return *manager;}
}}
extern "C" int WINAPI ConfigureCurtainDisplayV1(const wchar_t* callback,const wchar_t* root){try{return playhub::curtain::State().Configure(callback,root);}catch(...){return 0;}}
extern "C" int WINAPI QueueCurtainDisplayV1(std::uint32_t action,const playhub::curtain::WirePulse* value,std::uint32_t bytes){try{return playhub::curtain::State().Queue(action,value,bytes);}catch(...){return 0;}}
extern "C" int WINAPI ReadCurtainDisplayV1(playhub::curtain::WireSnapshot* out,std::uint32_t bytes){try{return playhub::curtain::State().Read(out,bytes);}catch(...){return 0;}}
