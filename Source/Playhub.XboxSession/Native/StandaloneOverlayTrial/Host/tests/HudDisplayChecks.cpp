#include "HudDisplayEngine.h"
#include <stdexcept>
#include <functional>
#include <iostream>
#include <vector>
using namespace playhub::hud;
static int checks;
void Check(bool value,const char*name){++checks;if(!value)throw std::runtime_error(name);}
Pulse Make(){Pulse p;p.authority={};auto&a=p.authority;a.purpose=Purpose::PerformanceHud;a.generation=1;a.collectorEpoch=1;a.collectorLease=std::string(32,'a');a.ownerBuild=std::string(64,'b');a.appId=123;a.agentPid=10;a.agentBirth=11;a.gamePid=20;a.gameBirth=21;a.framePid=30;a.frameBirth=31;a.frameThread=40;a.sourceThread=50;a.session=1;a.gameWindow=60;a.wpfWindow=70;a.wpfInstance=80;a.gameRect={0,0,3840,2160};a.wpfRect={7,7,295,191};a.wpfClass=L"HwndWrapper[GamingMode;Playhub performance HUD;owned-guid]";a.expectedFrameAumid=L"ExactPackage!Game";p.enabled=p.ready=true;p.sequence=1;p.sampleTick=990;p.observedAt=1000;p.leaseExpiresAt=6000;return p;}
struct Fake final:IBackend{
 std::vector<std::string>calls;std::function<void(const char*)>event;
 bool current=true,create=true,attach=true,show=true,hidden=true,detach=true,retired=true,destroy=true;Prepare band=Prepare::Ready;Id window=90;
 bool Bool(const char*name,bool value){calls.emplace_back(name);if(event)event(name);return value;}
 bool CreateHidden(const Authority&)override{return Bool("create-hidden",create);}
 Prepare PollBand(const Authority&)override{calls.emplace_back("poll-band");if(event)event("poll-band");return band;}
 bool Current(const Authority&)override{return Bool("current",current);}
 bool AttachLiveWpf(const Authority&)override{return Bool("attach-wpf",attach);}
 bool ShowNoActivate(const Authority&)override{return Bool("show-noactivate",show);}
 bool HideOwned()override{return Bool("hide",hidden);}
 bool DetachOwned()override{return Bool("detach",detach);}
 bool RetireOwnedBand()override{return Bool("retire",retired);}
 bool DestroyOwned()override{return Bool("destroy",destroy);}
 Id Window()const noexcept override{return window;}
 int Count(const char*n)const{return int(std::count(calls.begin(),calls.end(),n));}
};
int main(){try{
 {auto p=Make();Check(Valid(p.authority),"valid real HUD authority");Check(Fresh(p,1000),"fresh enabled live data");auto a=p.authority;a.purpose=Purpose::None;Check(!Valid(a),"explicit HUD purpose required");a=p.authority;a.collectorLease="480";Check(!Valid(a),"no fake appID as collector lease");a=p.authority;a.ownerBuild="";Check(!Valid(a),"owner build required");a=p.authority;a.wpfClass=L"Foreign window";Check(!Valid(a),"only HUD role");a=p.authority;a.wpfWindow=a.gameWindow;Check(!Valid(a),"source game is not WPF source");a=p.authority;a.wpfRect.right=a.gameRect.right+1;Check(!Valid(a),"HUD rectangle must remain in game");a=p.authority;a.gameRect={-8000,-4000,8000,4000};Check(Valid(a),"large/negative monitor geometry not arbitrarily rejected");}
 {auto p=Make();p.suspended=true;Check(!Fresh(p,1000),"QAM suspended never display");p=Make();p.enabled=false;Check(!Fresh(p,1000),"Off never display");p=Make();p.sampleTick=500;Check(!Fresh(p,1000),"500ms stale rejected");p=Make();p.sampleTick=1001;Check(!Fresh(p,1000),"future sample rejected");p=Make();p.leaseExpiresAt=1000;Check(!Fresh(p,1000),"expired collector rejected");}
 {auto p=Make();Fake f;Engine e(f);Check(e.Begin(p,1000),"begin hidden");Check(e.Read().phase==Phase::Preparing&&f.Count("show-noactivate")==0,"preparing gives display-only hidden state");e.Tick([&]{return p;},[]{return Id(1000);});Check(e.Read().phase==Phase::Shown,"ready then live attach/show");Check(f.Count("attach-wpf")==1&&f.Count("show-noactivate")==1,"one live WPF relation/show");e.Close();Check(e.Read().cleanupComplete&&e.Read().destination==0,"closed retains generation and clears destination");Check(f.Count("hide")==1&&f.Count("detach")==1&&f.Count("retire")==1&&f.Count("destroy")==1,"owned cleanup order");Check(!e.Begin(p,1000),"stale same generation cannot reopen");++p.authority.generation;Check(e.Begin(p,1000),"explicit new authority can reopen");}
 {auto p=Make();Fake f;f.band=Prepare::Pending;Engine e(f);e.Begin(p,1000);e.Tick([&]{return p;},[]{return Id(1000);});Check(e.Read().phase==Phase::Preparing&&f.Count("show-noactivate")==0,"pending broker grants no visible UI");p.enabled=false;e.Tick([&]{return p;},[]{return Id(1000);});Check(e.Read().phase==Phase::Closed,"Off during prepare cleanup");}
 for(const char*stage:{"current","poll-band","attach-wpf"}){auto p=Make();Fake f;Engine e(f);e.Begin(p,1000);f.event=[&](const char*n){if(std::string(n)==stage)p.suspended=true;};e.Tick([&]{return p;},[]{return Id(1000);});Check(f.Count("show-noactivate")==0,"suspend during getter/attach no show");Check(e.Read().cleanupComplete,"suspend cleanup completes only own resources");}
 {auto p=Make();Fake f;Engine e(f);e.Begin(p,1000);f.event=[&](const char*n){if(std::string(n)=="current")++p.authority.agentBirth;};e.Tick([&]{return p;},[]{return Id(1000);});Check(f.Count("show-noactivate")==0&&e.Read().cleanupComplete,"source owner PID reuse refuses");}
 {auto p=Make();Fake f;Engine e(f);e.Begin(p,1000);f.event=[&](const char*n){if(std::string(n)=="poll-band")++p.authority.wpfInstance;};e.Tick([&]{return p;},[]{return Id(1000);});Check(f.Count("attach-wpf")==0&&e.Read().cleanupComplete,"same HWND new WPF instance refuses");}
 {auto p=Make();Fake f;Engine e(f);e.Begin(p,1000);f.current=false;e.Tick([&]{return p;},[]{return Id(1000);});Check(f.Count("show-noactivate")==0&&e.Read().cleanupComplete,"AltTab/actual foreground failure refuses");}
 {auto p=Make();Fake f;Engine e(f);e.Begin(p,1000);f.attach=false;e.Tick([&]{return p;},[]{return Id(1000);});Check(f.Count("show-noactivate")==0&&e.Read().cleanupComplete,"alpha/DWM attach failure cannot show");}
 {auto p=Make();Fake f;Engine e(f);e.Begin(p,1000);f.event=[&](const char*n){if(std::string(n)=="attach-wpf")e.Close();};e.Tick([&]{return p;},[]{return Id(1000);});Check(f.Count("show-noactivate")==0&&e.Read().cleanupComplete,"close reentrant during attach deferred to owned cleanup");}
 {auto p=Make();Fake f;Engine e(f);e.Begin(p,1000);f.event=[](const char*n){if(std::string(n)=="attach-wpf")throw std::runtime_error("fake attach exception");};e.Tick([&]{return p;},[]{return Id(1000);});Check(f.Count("show-noactivate")==0&&e.Read().cleanupComplete,"exception resets driving and cleans up");}
 {auto p=Make();Fake f;Engine e(f);e.Begin(p,1000);f.retired=false;e.Close();Check(e.Read().phase==Phase::Closing&&!e.Read().cleanupComplete&&f.Count("destroy")==0,"unknown callback retirement never destroys target");f.retired=true;e.Close();Check(e.Read().cleanupComplete&&f.Count("destroy")==1,"later own retirement permits destruction");}
 {auto p=Make();Fake f;Engine e(f);e.Begin(p,1000);e.Tick([&]{return p;},[]{return Id(1000);});p.sequence=0;e.Tick([&]{return p;},[]{return Id(1000);});Check(e.Read().phase==Phase::Closed,"missing sample closes shown");}
 {auto p=Make();Fake f;Engine e(f);e.Begin(p,1000);e.Tick([&]{return p;},[]{return Id(1500);});Check(f.Count("show-noactivate")==0&&e.Read().cleanupComplete,"sample expiry cannot show old WPF frame");}
 {auto p=Make();Fake f;Engine e(f);e.Begin(p,1000);bool once=false;f.event=[&](const char*n){if(std::string(n)=="hide"&&!once){once=true;e.Close();}};e.Close();Check(f.Count("destroy")==1&&e.Read().cleanupComplete,"cleanup reentry never double-destroys owned target");}
 std::cout<<"{\"checks\":"<<checks<<",\"passed\":true,\"nativeCalls\":false,\"inputAuthority\":false}\n";return 0;
 }catch(const std::exception&e){std::cerr<<e.what()<<"\n";return 1;}}
