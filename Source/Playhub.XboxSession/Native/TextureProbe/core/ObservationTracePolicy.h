// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <cstdint>
#include <array>
#include <atomic>
#include <type_traits>
namespace view::observation {
struct Scope {
 std::uint64_t generation{},ownWindow{},ownCookie{},ownerBirth{},gameBirth{},gameWindow{},frameBirth{},expiresAt{};
 std::uint32_t ownerPid{},ownThread{},gamePid{},framePid{},frameThread{};bool callerPinned{};
};
inline bool Same(const Scope&a,const Scope&b)noexcept{return a.generation==b.generation&&a.ownWindow==b.ownWindow&&a.ownCookie==b.ownCookie&&a.ownerBirth==b.ownerBirth&&a.ownerPid==b.ownerPid&&a.ownThread==b.ownThread&&a.gamePid==b.gamePid&&a.gameBirth==b.gameBirth&&a.gameWindow==b.gameWindow&&a.framePid==b.framePid&&a.frameBirth==b.frameBirth&&a.frameThread==b.frameThread;}
enum class Kind:std::uint32_t {SetFocus=1,SetActiveWindow=2,SetForegroundWindow=3,GetActiveWindow=4,GetFocus=5};
struct Record {
 Kind kind{};Scope before{},after{};std::uint64_t caller{},requested{},begin{},end{},foregroundBefore{},foregroundAfter{},returned{},repeats=1;
 std::uint64_t logicalReturned{};std::uint32_t thread{},lastErrorAfterOriginal{};bool currentAfter{},activeWindowView{};
};
inline bool SameRecord(const Record&a,const Record&b)noexcept{return a.kind==b.kind&&a.caller==b.caller&&a.requested==b.requested&&a.thread==b.thread&&a.returned==b.returned&&a.foregroundBefore==b.foregroundBefore&&a.foregroundAfter==b.foregroundAfter&&a.currentAfter==b.currentAfter&&a.logicalReturned==b.logicalReturned&&a.activeWindowView==b.activeWindowView&&Same(a.before,b.before)&&Same(a.after,b.after);}
struct Diagnostic {
 bool compiled{},attempted{},ready{},copyKnown{},activeWindowViewCompiled{};std::int32_t installResult{};std::uint32_t configuredMask{},enabledMask{},inFlight{},rows{};
 std::uint64_t dropped{},actualGetActiveNull{},logicalActiveReturns{};std::array<std::uint64_t,5> seen{},pinned{},admitted{},delegated{};std::array<Record,32> records{};
};
// The original is never gated. Diagnostic failures cannot suppress it, alter
// its input LastError, or replace its return/output LastError. No I/O here.
template<class Ops,class Original,class Before,class After,class Select,class Publish>
auto ObservedCall(Ops&ops,Original original,Before before,After after,Select select,Publish publish){
 const auto entry=ops.Error();bool admitted=false;Record record{};
 try{admitted=before(record);}catch(...){admitted=false;}
 ops.Error(entry);
 auto answer=original();const auto returnedError=ops.Error();auto logical=answer;
 if(admitted){try{after(record,answer,returnedError);logical=select(record,answer);publish(record);}catch(...){}}
 ops.Error(returnedError);return logical;
}
template<class Ops,class Original,class Before,class After,class Publish>
auto PassThrough(Ops&ops,Original original,Before before,After after,Publish publish){
 return ObservedCall(ops,original,before,after,[](Record&,auto answer){return answer;},publish);
}
// Pure decision only. This never calls an OS setter or grants native FG/input.
inline bool ActiveWindowEligible(const Record&r,bool enabled,std::uint32_t callerThread,std::uint64_t now)noexcept{
 return enabled&&r.kind==Kind::GetActiveWindow&&r.returned==0&&r.before.callerPinned&&r.after.callerPinned
  &&r.currentAfter&&Same(r.before,r.after)&&r.before.generation&&r.before.ownWindow&&r.before.ownCookie
  &&r.before.ownerPid&&r.before.ownerBirth&&r.before.gamePid&&r.before.gameBirth&&r.before.framePid&&r.before.frameBirth&&r.before.frameThread
  &&r.before.ownThread&&r.thread==r.before.ownThread&&callerThread==r.before.ownThread
  &&now<r.before.expiresAt&&now<r.after.expiresAt&&r.foregroundBefore==r.before.gameWindow
  &&r.foregroundAfter==r.before.gameWindow&&r.before.gameWindow!=0&&r.before.gameWindow!=r.before.ownWindow;
}
template<std::size_t Capacity=32,std::size_t GetterLimit=24> class Ring {
 std::array<Record,Capacity> rows{};std::size_t count{},getterRows{};std::atomic_flag busy=ATOMIC_FLAG_INIT;
 std::atomic<std::uint64_t> dropped{};
public:
 bool Push(const Record&r)noexcept{
  if(busy.test_and_set(std::memory_order_acquire)){dropped.fetch_add(1);return false;}
  for(std::size_t i=0;i<count;i++)if(SameRecord(rows[i],r)){
   rows[i].end=r.end;rows[i].after=r.after;++rows[i].repeats;busy.clear(std::memory_order_release);return true;
  }
  const bool getter=std::uint32_t(r.kind)>=4;
  bool accepted=count<Capacity&&(!getter||getterRows<GetterLimit);
  if(accepted){rows[count++]=r;if(getter)++getterRows;}else dropped.fetch_add(1);
  busy.clear(std::memory_order_release);return accepted;
 }
 bool Copy(std::array<Record,Capacity>&out,std::size_t&size,std::uint64_t&lost)noexcept{
  if(busy.test_and_set(std::memory_order_acquire))return false;
  size=count;out=rows;lost=dropped.load();busy.clear(std::memory_order_release);return true;
 }
};
}
