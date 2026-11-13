// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "CurtainWindowsBackend.h"
#include <optional>
namespace playhub::curtain {
enum class DisplayPhase {Closed,Preparing,Presented,Closing};
struct DisplaySnapshot {DisplayPhase phase{DisplayPhase::Closed};Id generation{};bool cleanupComplete{true};};
// GUI worker sequencing only. A validated frame observation may request Close;
// pixel facts never authorize Begin, Present, Renew or an input/menu lease.
class DisplayEngine {
 IWindowsBackend& backend;std::optional<Authority> owner;DisplaySnapshot state{};bool busy{},closing{};
 struct Busy {bool& b;explicit Busy(bool& v):b(v){b=true;}~Busy(){b=false;}};
 template<class F>static bool Try(F&& f){try{return f();}catch(...){return false;}}
 void Cleanup(){if(busy){closing=true;return;}Busy guard(busy);state.phase=DisplayPhase::Closing;state.cleanupComplete=false;
  if(Try([&]{return backend.Close();})){state.phase=DisplayPhase::Closed;state.cleanupComplete=true;owner.reset();closing=false;}}
public:
 explicit DisplayEngine(IWindowsBackend& b):backend(b){}
 DisplaySnapshot Read()const noexcept{return state;}
 void Close(){closing=true;Cleanup();}
 template<class Latest,class Clock>bool Begin(Latest&& latest,Clock&& clock){
  if(busy||owner||!state.cleanupComplete||closing)return false;
  bool good=false;
  {Busy guard(busy);good=Try([&]{
    auto first=latest();
    if(closing||!Valid(first,clock())||closing||first.authority.generation<=state.generation)return false;
    owner=first.authority;state={DisplayPhase::Preparing,owner->generation,false};
    return backend.Begin(first);
  });}
  if(!good||closing){if(owner||closing)Cleanup();return false;}return true;
 }
 template<class Latest,class Clock>void Tick(Latest&& latest,Clock&& clock){
  if(!owner)return;if(closing||state.phase==DisplayPhase::Closing){Cleanup();return;}
  bool good=false;{Busy guard(busy);try{
   auto fresh=[&](BandLease& value){value=latest();return !closing&&owner&&value.authority==*owner&&Valid(value,clock());};
   BandLease value;good=fresh(value);
   if(good&&state.phase==DisplayPhase::Preparing){
    auto result=backend.Poll(value);good=fresh(value)&&result!=Prepare::Refused;
    if(good&&result==Prepare::Ready){good=backend.Present(value)&&fresh(value);if(good)state.phase=DisplayPhase::Presented;}
   }
   if(good&&state.phase==DisplayPhase::Presented)good=backend.Renew(value)&&fresh(value);
  }catch(...){good=false;}}
  if(!good||closing)Cleanup();
 }
};
}
