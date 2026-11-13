// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <array>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <functional>
#include <mutex>
namespace fence {
enum class Phase:std::uint32_t { Pass=0,Shown=1,ClosingDrain=2,OpeningIntent=3,ArmedAuthority=4,ClosedRelease=5 };
struct alignas(8) Frame {
 std::uint32_t magic=0x46494850;std::uint16_t version=1,kind=1;std::uint32_t size=128,sequence{};
 std::array<std::uint64_t,4> session{};
 std::uint64_t generation{},gameBirth{},producerBirth{},readAt{},expiresAt{},gameId{};
 std::uint32_t gamePid{},producerPid{},windowsSession{},phase{};
 std::uint64_t sourceCookie{},destinationCookie{};
};
static_assert(sizeof(Frame)==128);
struct Identity {
 std::array<std::uint64_t,4> session{};
 std::uint64_t gameBirth{},producerBirth{},gameId{};
 std::uint32_t gamePid{},producerPid{},windowsSession{};
};
struct Pad {std::uint16_t buttons{};std::uint8_t left{},right{};std::int16_t lx{},ly{},rx{},ry{};};
static_assert(sizeof(Pad)==12);
struct State {std::uint32_t packet{};Pad pad{};};
static_assert(sizeof(State)==16);
inline bool Neutral(const Pad& p){return p.buttons==0&&!p.left&&!p.right&&!p.lx&&!p.ly&&!p.rx&&!p.ry;}
// Transport and kernel current predicates are fixed by the native adapter,
// never supplied by a remote RPC or a JSON/boolean caller.
class Policy {
 Identity identity;std::function<bool(Frame&)> read;std::function<bool()> current;
 std::function<std::uint64_t()> clock;std::mutex lock;
 std::uint64_t generation{},retired{},drainUntil{};std::array<std::uint64_t,4> openingUntil{};
 std::array<bool,4> guideLatched{};std::uint8_t seen{},released{};bool sawShown{};
 std::uint16_t ownerKind{};std::uint64_t ownerSource{},ownerDestination{},releaseUntil{};
 std::array<std::uint16_t,4> heldDigital{},heldKeys{};
 std::array<State,4> lastOutput{};std::array<bool,4> lastOutputKnown{};
 void ClearIndex(std::uint32_t index){if(index<4){heldDigital[index]=heldKeys[index]=0;lastOutputKnown[index]=false;}else ClearHeld();}
 void ClearHeld(){heldDigital={};heldKeys={};lastOutputKnown={};releaseUntil=0;ownerKind=0;ownerSource=ownerDestination=0;sawShown=false;}
 bool SameSource(const Frame& f)const{return ownerKind==f.kind&&ownerSource==f.sourceCookie&&ownerDestination==f.destinationCookie;}
 enum class Decision{Pass,Full,Release};
 bool Packet(std::uint32_t index,State& value){
  bool changed=lastOutputKnown[index]&&lastOutput[index].packet==value.packet&&std::memcmp(&lastOutput[index].pad,&value.pad,sizeof(Pad))!=0;
  if(changed)++value.packet;lastOutput[index]=value;lastOutputKnown[index]=true;return changed;
 }
 bool Valid(const Frame& f,std::uint64_t now)const {
  return f.magic==0x46494850&&f.version==1&&f.size==128&&f.sequence&&!(f.sequence&1)
   &&f.session==identity.session&&f.gameBirth==identity.gameBirth&&f.producerBirth==identity.producerBirth
   &&f.gamePid==identity.gamePid&&f.producerPid==identity.producerPid&&f.windowsSession==identity.windowsSession
   &&f.kind<=2&&f.generation&&f.gameId==identity.gameId&&static_cast<std::uint32_t>(f.gameId)==0x02000000
   &&f.gameId>>32>=0x80000000&&f.phase<=5&&f.phase!=3
   &&((f.phase==0||f.phase==4)&&f.kind==0||f.phase!=0&&f.phase!=4&&f.kind!=0&&f.sourceCookie&&f.destinationCookie)
   &&f.readAt<=now&&now-f.readAt<=250&&f.expiresAt>now&&f.expiresAt>=f.readAt
   &&f.expiresAt-f.readAt<=1500;
 }
 Decision Capture(Frame& f,std::uint32_t index,bool guideKnown=false,bool guideHeld=false) {
  if(!current()||!read(f)){ClearHeld();return Decision::Pass;}
  auto now=clock();if(!Valid(f,now)||!current()||!Valid(f,clock())){ClearHeld();return Decision::Pass;}
  if(index<4&&guideKnown&&!guideHeld){guideLatched[index]=false;openingUntil[index]=0;}
  if(f.phase==4){
   ClearHeld();
   if(!guideKnown||index>=4)return Decision::Pass;
   if(!guideHeld){guideLatched[index]=false;openingUntil[index]=0;return Decision::Pass;}
   if(!guideLatched[index]){guideLatched[index]=true;openingUntil[index]=now+1500;}
   return now<openingUntil[index]?Decision::Full:Decision::Pass;
  }
  if(f.phase==0){retired=(std::max)(retired,f.generation);ClearHeld();return Decision::Pass;}
  if(f.generation<generation){ClearHeld();return Decision::Pass;}
  if(f.generation!=generation){generation=f.generation;seen=released=0;drainUntil=0;ClearHeld();}
  if(ownerKind&&!SameSource(f)){retired=(std::max)(retired,generation);ClearHeld();return Decision::Pass;}
  if(f.phase==1){
   if(f.generation<=retired||releaseUntil)return Decision::Pass;
   // Remember a held Guide already consumed by Shown; it is not a new
   // opening edge if that same physical hold continues through Closed.
   if(index<4&&guideKnown&&guideHeld){guideLatched[index]=true;openingUntil[index]=0;}
   ownerKind=f.kind;ownerSource=f.sourceCookie;ownerDestination=f.destinationCookie;sawShown=true;return Decision::Full;
  }
  if(!sawShown||!SameSource(f))return Decision::Pass;
  // Both2 and5 are real-Closed cleanup receipts, not Shown authority.
  if(!releaseUntil){releaseUntil=now+5000;retired=(std::max)(retired,generation);}
  if(now>=releaseUntil){ClearHeld();return Decision::Pass;}
  // A real new Guide edge can reopen while the previous owner's release
  // tail is still current. Preserve that tail/mask and its original budget.
  if(index<4&&guideKnown&&guideHeld){
   if(!guideLatched[index]){guideLatched[index]=true;openingUntil[index]=now+1500;}
   if(now<openingUntil[index])return Decision::Full;
  }
  return Decision::Release;
 }

public:
 Policy(Identity i,std::function<bool(Frame&)> r,std::function<bool()> c,std::function<std::uint64_t()> n)
  :identity(i),read(std::move(r)),current(std::move(c)),clock(std::move(n)){}
 bool StateResult(std::uint32_t index,std::uint32_t status,State* result,bool guideKnown=false,bool guideHeld=false) {
  std::lock_guard<std::mutex> lockHeld(lock);Frame f;
  if(index>=4)return false;if(status||!result){ClearIndex(index);return false;}
  const auto decision=Capture(f,index,guideKnown,guideHeld);
  if(decision==Decision::Pass)return false;
  if(decision==Decision::Release){
   heldDigital[index]&=result->pad.buttons;heldKeys[index]&=result->pad.buttons;const auto removed=heldDigital[index];
   result->pad.buttons&=static_cast<std::uint16_t>(~removed);const bool packet=Packet(index,*result);
   return removed!=0||packet;
  }
  if(f.phase==1){seen|=std::uint8_t(1u<<index);heldDigital[index]=result->pad.buttons;}
  result->pad={};++result->packet;Packet(index,*result);return true;
 }
 bool KeystrokeResult(std::uint32_t status,void* result,std::size_t size,bool guideKnown=false,bool guideHeld=false,std::uint32_t index=0xffffffff,std::uint16_t bit=0,bool up=false) {
  std::lock_guard<std::mutex> lockHeld(lock);Frame f;
  if(status){if(status!=4306)ClearIndex(index);return false;}if(!result)return false;
  const auto decision=Capture(f,index,guideKnown,guideHeld);
  if(decision==Decision::Pass)return false;
  if(decision==Decision::Release){
   if(index>=4||!bit||(bit&(bit-1))||!(heldKeys[index]&bit))return false;
   if(up){heldKeys[index]&=static_cast<std::uint16_t>(~bit);heldDigital[index]&=static_cast<std::uint16_t>(~bit);}
  }else if(f.phase==1&&index<4&&bit&&!(bit&(bit-1))){
   if(up){heldKeys[index]&=static_cast<std::uint16_t>(~bit);heldDigital[index]&=static_cast<std::uint16_t>(~bit);}else{heldKeys[index]|=bit;heldDigital[index]|=bit;}
  }
  std::memset(result,0,size);return true;
 }
};
}
