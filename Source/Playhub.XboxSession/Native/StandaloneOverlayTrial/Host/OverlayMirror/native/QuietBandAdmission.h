// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <cstdint>
#include <algorithm>
namespace playhub::mirror {
struct NoInputFacts {
 bool passed{},held{},cursorAttempted{},cursorRead{};std::uint32_t firstHeldVk{};
 std::int32_t initialX{},initialY{},currentX{},currentY{};
};
// Exact short-circuit order of band::NoInput: stop at first high-bit key;
// only with no held keys read one physical cursor and compare exact positions.
template<class Key,class Cursor>
NoInputFacts ReadNoInputFacts(std::int32_t x,std::int32_t y,Key&& key,Cursor&& cursor){
 NoInputFacts f;f.initialX=x;f.initialY=y;
 for(int vk=1;vk<256;vk++)if(key(vk)&0x8000){f.held=true;f.firstHeldVk=std::uint32_t(vk);return f;}
 f.cursorAttempted=true;f.cursorRead=cursor(f.currentX,f.currentY);
 f.passed=f.cursorRead&&f.currentX==x&&f.currentY==y;return f;
}
enum class QuietStep {Pending,Retry,Refused};
class QuietBandAdmission {
 std::uint64_t started{},deadline{},lastSample{};unsigned retries{},stable{};
 std::int32_t x{},y{};bool active{},cleaned{},sampled{};
public:
 static constexpr std::uint64_t BudgetMs=750,SampleGapMs=16;
 static constexpr unsigned MaxRetries=2;
 void Reset(){started=deadline=lastSample=0;retries=stable=0;x=y=0;active=cleaned=sampled=false;}
 bool Defer(std::uint64_t now,std::uint64_t leaseEnd){
  if(!started){if(!now||now>=leaseEnd)return false;started=now;deadline=(std::min)(leaseEnd,now>UINT64_MAX-BudgetMs?UINT64_MAX:now+BudgetMs);}
  if(now<started||now>=deadline||retries>=MaxRetries)return false;
  active=true;cleaned=false;stable=0;sampled=false;return true;
 }
 bool Pending()const{return active;}
 unsigned Retries()const{return retries;}
 void CleanupComplete(){cleaned=true;}
 template<class Read,class Clock>
 QuietStep Poll(bool current,Read&& read,Clock&& clock){
  const auto now=clock();
  if(!active||!current||now<started||now>=deadline){active=false;return QuietStep::Refused;}
  if(!cleaned||(sampled&&(now<lastSample||now-lastSample<SampleGapMs)))return QuietStep::Pending;
  const auto f=read();const auto after=clock();
  if(!active||after<now||after>=deadline){active=false;return QuietStep::Refused;}
  lastSample=after;sampled=true;
  if(f.held||!f.cursorRead){stable=0;return QuietStep::Pending;}
  if(stable&&f.currentX==x&&f.currentY==y)++stable;else{stable=1;x=f.currentX;y=f.currentY;}
  if(stable<2)return QuietStep::Pending;
  active=false;++retries;return retries<=MaxRetries?QuietStep::Retry:QuietStep::Refused;
 }
};
}
