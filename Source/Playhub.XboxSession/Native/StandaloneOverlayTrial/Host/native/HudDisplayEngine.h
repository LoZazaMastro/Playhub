// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "HudDisplayLease.h"
#include <optional>
namespace playhub::hud {
enum class Phase{Closed,Preparing,Shown,Closing};
enum class Prepare{Pending,Ready,Refused};
struct Snapshot{Phase phase{Phase::Closed};Id generation{},source{},destination{};bool cleanupComplete{true};Id shownSequence{};};
struct IBackend {
 virtual ~IBackend()=default;
 // Implementations own destination creation. Caller never supplies its HWND.
 virtual bool CreateHidden(const Authority&)=0;
 virtual Prepare PollBand(const Authority&)=0;
 virtual bool Current(const Authority&)=0;
 virtual bool AttachLiveWpf(const Authority&)=0;
 virtual bool ShowNoActivate(const Authority&)=0;
 virtual bool HideOwned()=0;
 virtual bool DetachOwned()=0;
 virtual bool RetireOwnedBand()=0;
 virtual bool DestroyOwned()=0;
 virtual Id Window()const noexcept=0;
};
// GUI-owner engine. Supply the latest explicit publisher pulse, not a getter
// which renews authority. Slow backend getters may revoke/replace the publisher;
// latest() is checked again immediately before any attach/show commit.
class Engine {
 IBackend& backend;std::optional<Authority> owner;Snapshot snapshot{};bool driving{},closeRequested{};
 struct Driving {bool& value;explicit Driving(bool& v):value(v){value=true;}~Driving(){value=false;}};
 template<class Call>static bool Try(Call&& call){try{return call();}catch(...){return false;}}
 bool SameFresh(const Pulse& pulse,Id now)const{return owner&&pulse.authority==*owner&&Fresh(pulse,now);}
 void Cleanup(){
  if(driving){closeRequested=true;return;}
  Driving guard(driving);
  snapshot.phase=Phase::Closing;snapshot.cleanupComplete=false;
  const bool hidden=Try([&]{return backend.HideOwned();});
  const bool detached=Try([&]{return backend.DetachOwned();});
  const bool retired=Try([&]{return backend.RetireOwnedBand();});
  if(hidden&&detached&&retired&&Try([&]{return backend.DestroyOwned();})){
   snapshot.phase=Phase::Closed;snapshot.destination=0;snapshot.cleanupComplete=true;owner.reset();closeRequested=false;
  }
 }
public:
 explicit Engine(IBackend& value):backend(value){}
 Snapshot Read()const{return snapshot;}
 void Close(){closeRequested=true;Cleanup();}
 bool Begin(const Pulse& pulse,Id now){
  if(owner||snapshot.phase!=Phase::Closed||!snapshot.cleanupComplete||!Fresh(pulse,now)
    ||pulse.authority.generation<=snapshot.generation)return false;
  owner=pulse.authority;snapshot={Phase::Preparing,owner->generation,owner->wpfWindow,0,false,0};closeRequested=false;
  bool created;
  {Driving guard(driving);created=Try([&]{return backend.CreateHidden(*owner);});}
  snapshot.destination=backend.Window();
  if(!created||closeRequested){Cleanup();return false;}return true;
 }
 template<class Latest,class Clock>
 void Tick(Latest&& latest,Clock&& clock){
  if(!owner)return;
  if(snapshot.phase==Phase::Closing||closeRequested){Cleanup();return;}
  if(!Try([&]{return SameFresh(latest(),clock());})){Cleanup();return;}
  bool good=false;
  {Driving guard(driving);try {
  auto current=[&]{auto p=latest();return !closeRequested&&SameFresh(p,clock())
    &&(!snapshot.shownSequence||p.sequence>=snapshot.shownSequence);};
  good=backend.Current(*owner)&&current();
  if(good&&snapshot.phase==Phase::Preparing){
   const auto prepared=backend.PollBand(*owner);
   good=current();
   if(good&&prepared==Prepare::Ready){
    good=backend.Current(*owner)&&current();
    if(good)good=backend.AttachLiveWpf(*owner)&&current();
    if(good)good=backend.Current(*owner)&&current();
    if(good)good=backend.ShowNoActivate(*owner)&&current();
    if(good){snapshot.phase=Phase::Shown;snapshot.shownSequence=latest().sequence;}
   }else if(prepared==Prepare::Refused)good=false;
  }
  if(good&&snapshot.phase==Phase::Shown){good=backend.Current(*owner)&&current();if(good)snapshot.shownSequence=latest().sequence;}
  }catch(...){good=false;}}
  if(!good||closeRequested)Cleanup();
 }
};
}
