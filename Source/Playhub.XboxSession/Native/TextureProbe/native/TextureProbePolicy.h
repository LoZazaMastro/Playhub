// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <cstdint>
namespace texture {
// Source-linked to SteamCompanionStatus.TakeOpenRequest: <=400ms requests can
// still be returned after a stop. No old request may cross this admission edge.
constexpr std::uint64_t GuideRequestFreshnessMs=400;
class AdmissionBarrier {
 bool waiting=false;std::uint64_t notBefore=0;
public:
 void Retire(std::uint64_t now){waiting=true;notBefore=now>UINT64_MAX-(GuideRequestFreshnessMs+1)?UINT64_MAX:now+GuideRequestFreshnessMs+1;}
 bool Consume(bool requested,std::uint64_t now){
  if(!waiting)return requested;
  if(!requested&&now>=notBefore)waiting=false;
  // Even the zero that finishes retirement cannot become an activation.
  // Only a positive callback in a subsequent iteration is new authority.
  return false;
 }
 bool Waiting()const{return waiting;}
 std::uint64_t NotBefore()const{return notBefore;}
};
constexpr unsigned MaxPumpMessages=64;
template<class Next,class Dispatch>unsigned PumpBounded(Next&& next,Dispatch&& dispatch){unsigned count=0;while(count<MaxPumpMessages&&next()){dispatch();++count;}return count;}
enum class Stop {None,Expired,SourceLost,GameEnded};
inline Stop ShouldStop(bool active,bool gameAlive,bool sourceExact,std::uint64_t now,std::uint64_t expiry){if(!active)return Stop::None;if(!gameAlive)return Stop::GameEnded;if(!sourceExact)return Stop::SourceLost;if(now>=expiry)return Stop::Expired;return Stop::None;}
inline bool MayBegin(bool authenticated,bool gameAlive,bool sourceExact,bool ownsSurface){return authenticated&&gameAlive&&sourceExact&&ownsSurface;}
inline bool MayAdmitProbe(unsigned admitted,bool active,bool pendingKeys,bool pendingView){return admitted<2&&!active&&!pendingKeys&&!pendingView;}
inline bool MayPost(bool requested,bool active,bool sourceExact,bool ownsSurface,bool notExpired){return requested&&active&&sourceExact&&ownsSurface&&notExpired;}
inline int ActualScope(bool gameExact,bool ownExact,bool eligible){return eligible?(gameExact?1:ownExact?2:0):0;}
template<class Guard,class Shift,class Tab>bool PostDown(Guard&& current,Shift&& shift,Tab&& tab){return current()&&shift()&&current()&&tab();}
}
