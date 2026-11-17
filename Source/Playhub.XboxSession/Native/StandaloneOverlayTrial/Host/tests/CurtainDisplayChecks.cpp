// SPDX-License-Identifier: GPL-3.0-only
#include "CurtainDisplayEngine.h"
#include <iostream>
#include <stdexcept>
#include <vector>
#include <functional>
using namespace playhub::curtain;
struct Source final:ISourceAuthority {mutable Id now=1000;bool alive=true;std::function<void()> read;
 bool Current(const Authority&)const override{if(read)read();return alive;}Id Now()const noexcept override{return now;}};
BandLease Lease(const std::shared_ptr<Source>& source){BandLease l;auto& a=l.authority;
 a.purpose=Purpose::FirstContentCover;a.generation=1;a.requestEpoch=1;a.requestId=std::string(32,'a');a.sourceReceipt=std::string(32,'b');a.ownerBuild=std::string(64,'c');
 a.appId=100;a.ownerPid=1;a.ownerBirth=101;a.gamePid=a.framePid=3;a.gameBirth=a.frameBirth=103;a.frameThread=33;a.gameWindow=300;
 a.steamPid=2;a.steamBirth=102;a.steamThread=22;a.steamWindow=200;a.session=1;a.gameRect={0,0,1920,1080};a.steamRect=a.gameRect;
 a.steamClass=L"SDL_app";a.gameClass=L"GameWindow";a.steamImage=L"C:\\Steam\\steamwebhelper.exe";a.gameImage=L"C:\\Games\\game.exe";
 l.source=source;l.issuedAt=900;l.expiresAt=1500;l.authorizationNonce.fill('d');l.authorizationNonce[64]=0;return l;}
struct Backend final:IWindowsBackend {std::vector<std::string> calls;Prepare ready=Prepare::Ready;bool canClose=true;std::function<void(const char*)> effect;
 bool Call(const char* name){calls.push_back(name);if(effect)effect(name);return true;}
 bool Begin(const BandLease&)override{return Call("begin-hidden");}Prepare Poll(const BandLease&)override{Call("poll");return ready;}
 bool Present(const BandLease&)override{return Call("present");}bool Renew(const BandLease&)override{return Call("renew");}
 bool Close()override{Call("close-owned");return canClose;}DisplayFacts Facts()const noexcept override{return {};}
};
struct FadeBackend final:IWindowsBackend {
 std::shared_ptr<Source> source;bool closeOkay=true,fadeOkay=true,dropOnClose=false;DisplayFacts facts{};unsigned fades{};std::function<void()> onClose;
 explicit FadeBackend(std::shared_ptr<Source> s):source(s){}
 bool Begin(const BandLease&)override{facts={};facts.hidden=true;facts.cleanupComplete=false;return true;}
 Prepare Poll(const BandLease&)override{return Prepare::Ready;}
 bool Present(const BandLease&)override{facts.stage=21;facts.sourceCurrent=true;facts.hidden=false;facts.presented=facts.thumbnail=true;facts.destination=40;return true;}
 bool Renew(const BandLease&)override{return source->alive;}
 bool Fade(const BandLease&,std::uint8_t)override{++fades;return fadeOkay&&source->alive;}
 bool Close()override{auto callback=std::move(onClose);if(callback)callback();if(dropOnClose)source->alive=false;facts.sourceCurrent=source->alive;if(!source->alive)facts.stage=224;
  if(!closeOkay)return false;facts.hidden=facts.cleanupComplete=true;facts.presented=facts.thumbnail=false;facts.destination=0;return true;}
 DisplayFacts Facts()const noexcept override{return facts;}
};
struct FinishDriver {
 void Step(DisplayEngine& e,FadeBackend& b,const BandLease& lease,Id now){(void)b;e.FinishFade(lease,now);}
 bool Complete(const DisplayEngine& e){return e.Read().normalFadeComplete;}
};
static void NormalFadeReceiptChecks(){try{unsigned checks{};auto check=[&](bool b,const char* label){++checks;if(!b)throw std::runtime_error(label);};
 for(const std::string mode:{"normal","cancel","source-gone","close-debt","fade-fails","clock-reverse","source-during-close","generation-replaced","reentrant-close"}){
  auto source=std::make_shared<Source>();auto lease=Lease(source);FadeBackend b(source);DisplayEngine e(b);FinishDriver driver;
  auto latest=[&]{return lease;};auto clock=[&]{return source->now;};check(e.Begin(latest,clock),"initial exact lease");e.Tick(latest,clock);
  check(e.Read().phase==DisplayPhase::Presented,"real Present before finish");driver.Step(e,b,lease,source->now);
  check(!driver.Complete(e),"no early completion from accepted finish");
  source->now=1319;e.Tick(latest,clock);driver.Step(e,b,lease,source->now);check(!driver.Complete(e),"319ms never complete");
  if(mode=="cancel")e.Close();if(mode=="source-gone")source->alive=false;if(mode=="close-debt")b.closeOkay=false;
  if(mode=="fade-fails"){b.fadeOkay=false;source->now=1319;driver.Step(e,b,lease,source->now);}
  if(mode=="source-during-close")b.dropOnClose=true;if(mode=="generation-replaced")++lease.authority.generation;
  if(mode=="reentrant-close")b.onClose=[&]{e.Close();};
  source->now=mode=="clock-reverse"?900:1320;e.Tick(latest,clock);driver.Step(e,b,lease,source->now);
  if(mode=="normal"){
   check(e.Read().phase==DisplayPhase::Closed&&e.Read().cleanupComplete,"normal owned cleanup complete");
   check(driver.Complete(e),"normal native fade must expose explicit completion receipt for late heartbeat race");
   e.Close();check(!driver.Complete(e),"explicit close cannot retain a finish receipt");
  }else check(!driver.Complete(e),"failure/cancel/foreign generation cannot claim normal fade");
 }
 std::cout<<checks<<" actual normal-fade receipt checks PASS; inert source/backend/clock, no OS calls\n";
 }catch(...){throw;}}

int main(){try{NormalFadeReceiptChecks();unsigned checks=0;auto check=[&](bool good){++checks;if(!good)throw std::runtime_error("display "+std::to_string(checks));};
 auto source=std::make_shared<Source>();auto l=Lease(source);Backend b;DisplayEngine e(b);auto latest=[&]{return l;};auto clock=[&]{return source->now;};
 check(e.Begin(latest,clock));check(e.Read().phase==DisplayPhase::Preparing);e.Tick(latest,clock);
 check(e.Read().phase==DisplayPhase::Presented);check(b.calls==std::vector<std::string>{"begin-hidden","poll","present","renew"});
 source->alive=false;e.Tick(latest,clock);check(e.Read().phase==DisplayPhase::Closed&&e.Read().cleanupComplete);check(b.calls.back()=="close-owned");
 source->alive=true;check(!e.Begin(latest,clock));l.authority.generation=2;check(e.Begin(latest,clock));e.Close();check(e.Read().cleanupComplete);
 for(auto stage:{"poll","present","renew"}){Backend f;DisplayEngine engine(f);l=Lease(source);f.effect=[&](const char* at){if(std::string(at)==stage)source->alive=false;};
  check(engine.Begin(latest,clock));engine.Tick(latest,clock);check(engine.Read().phase==DisplayPhase::Closed);check(f.calls.back()=="close-owned");source->alive=true;
  if(std::string(stage)=="poll")check(std::find(f.calls.begin(),f.calls.end(),"present")==f.calls.end());}
 {Backend f;DisplayEngine engine(f);l=Lease(source);f.effect=[&](const char* at){if(std::string(at)=="poll")l.authority.requestEpoch++;};
  check(engine.Begin(latest,clock));engine.Tick(latest,clock);check(engine.Read().phase==DisplayPhase::Closed);check(f.calls==std::vector<std::string>{"begin-hidden","poll","close-owned"});}
 {Backend f;DisplayEngine engine(f);l=Lease(source);f.effect=[&](const char* at){if(std::string(at)=="present")source->now=1500;};
  check(engine.Begin(latest,clock));engine.Tick(latest,clock);check(engine.Read().phase==DisplayPhase::Closed);source->now=1000;}
 {Backend f;DisplayEngine engine(f);l=Lease(source);f.canClose=false;check(engine.Begin(latest,clock));engine.Close();
  check(engine.Read().phase==DisplayPhase::Closing&&!engine.Read().cleanupComplete);check(!engine.Begin(latest,clock));f.canClose=true;engine.Tick(latest,clock);check(engine.Read().phase==DisplayPhase::Closed);}
 {Backend f;DisplayEngine engine(f);l=Lease(source);f.effect=[&](const char* at){if(std::string(at)=="present")engine.Close();};
  check(engine.Begin(latest,clock));engine.Tick(latest,clock);check(engine.Read().phase==DisplayPhase::Closed);check(f.calls.back()=="close-owned");}
 {Backend f;DisplayEngine engine(f);l=Lease(source);source->read=[&]{source->now=1500;};check(!engine.Begin(latest,clock));check(f.calls.empty());source->read={};source->now=1000;}
 for(bool insideSource:{false,true}){Backend f;DisplayEngine engine(f);l=Lease(source);
  auto closeLatest=[&]{if(!insideSource)engine.Close();return l;};
  if(insideSource)source->read=[&]{engine.Close();};
  check(!engine.Begin(closeLatest,clock));check(std::find(f.calls.begin(),f.calls.end(),"begin-hidden")==f.calls.end());
  check(engine.Read().phase==DisplayPhase::Closed&&engine.Read().cleanupComplete);source->read={};}
 std::cout<<checks<<" curtain sequence checks PASS; fake source/backend/clock, no windows/capture/input\n";
 }catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}}
