// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "MirrorSnapshotApi.h"
#include "MirrorContinuationWire.h"
#include <algorithm>
#include <array>
#include <cstring>
#include <mutex>
#include <string>
namespace playhub::mirror::callsite {
struct ConfigurationV1 {
 std::uint32_t version=1,size=sizeof(ConfigurationV1);
 Id gameId{};std::array<char,65> sessionId{};std::array<std::uint8_t,7> reserved{};
 Id leaseDurationMs{};
};
enum class Action:std::uint32_t {Show=1,Renew=2,Close=3};
struct ControlV1 {
 std::uint32_t version=1,size=sizeof(ControlV1);Action action=Action::Close;std::uint32_t reserved{};
 Snapshot expected{};Id requestedExpiresAt{};
};
static_assert(sizeof(ConfigurationV1)==96&&sizeof(ControlV1)==248,"Mirror callsite V1 ABI changed");
inline bool ValidConfiguration(const ConfigurationV1& c){return c.version==1&&c.size==sizeof(c)&&CanonicalSession(c.sessionId)
 &&c.gameId&&(c.gameId&0xffffffffu)==0x02000000u&&(c.gameId>>32)>=0x80000000u
 &&c.leaseDurationMs>=500&&c.leaseDurationMs<=2000
 &&std::all_of(c.reserved.begin(),c.reserved.end(),[](auto n){return n==0;});}
inline bool SameTuple(const Snapshot& a,const Snapshot& b,bool compareExpiry=true){return a.version==1&&a.size==sizeof(a)&&b.version==1&&b.size==sizeof(b)
 &&CanonicalSession(a.sessionId)&&a.sessionId==b.sessionId&&a.generation&&a.generation==b.generation&&a.gameId==b.gameId
 &&(!compareExpiry||a.expiresAt==b.expiresAt)&&a.sourceWindow&&a.sourceWindow==b.sourceWindow&&a.sourceCookie&&a.sourceCookie==b.sourceCookie
 &&a.destinationWindow&&a.destinationWindow==b.destinationWindow&&a.destinationCookie&&a.destinationCookie==b.destinationCookie
 &&a.ownerPid&&a.ownerPid==b.ownerPid&&a.ownerBirth&&a.ownerBirth==b.ownerBirth&&a.gamePid&&a.gamePid==b.gamePid
 &&a.gameBirth&&a.gameBirth==b.gameBirth;}
inline bool CommandCurrent(const ControlV1& c,const Snapshot& s,Id now,Id duration){
 if(c.version!=1||c.size!=sizeof(c)||c.reserved)return false;
 // Expiry is not an identity. A same-generation close must survive a renewal
 // which was already in flight when the caller read its exact owner snapshot.
 if(c.action==Action::Close)return SameTuple(c.expected,s,false)&&c.requestedExpiresAt==0&&(s.state==State::AwaitingBand||s.state==State::Prepared||s.state==State::Shown);
 if(!SameTuple(c.expected,s)||now>=s.expiresAt)return false;
 if(!s.leaseCurrent)return false;
 if(s.state!=State::Prepared&&s.state!=State::Shown)return false;
 if(c.action==Action::Show&&s.state!=State::Prepared)return false;
 if(c.action==Action::Renew&&s.state!=State::Shown)return false;
 if(c.action!=Action::Show&&c.action!=Action::Renew)return false;
 if(c.action==Action::Show&&!c.requestedExpiresAt)return true;
 return c.requestedExpiresAt>=s.expiresAt&&c.requestedExpiresAt>now&&c.requestedExpiresAt-now<=duration;
}
inline bool RendererReceiptReady(bool received,Id frames,Id receiptFrame,std::int32_t present,bool rawOpen,bool sameActiveRoutedLease,bool current){return received&&frames>receiptFrame&&present==0&&rawOpen&&sameActiveRoutedLease&&current;}
// Copy-only cross-thread mailbox. Native state mutates on the original GUI thread.
class Mailbox {
 mutable std::mutex mutex;ConfigurationV1 configuration{};std::wstring callback,root;
 Snapshot snapshot{};ContinuationSnapshotV1 continuation{};ControlV1 control{};bool configured{},running{},started{},queued{};
public:
 bool Configure(const ConfigurationV1& c,const wchar_t* dll,const wchar_t* directory){
  if(!ValidConfiguration(c)||!dll||!directory||!*dll||!*directory||wcsnlen_s(dll,32768)>=32768||wcsnlen_s(directory,32768)>=32768)return false;
  std::lock_guard<std::mutex> lock(mutex);if(configured||running)return false;
  configuration=c;callback=dll;root=directory;configured=true;return true;
 }
 bool Start(ConfigurationV1& c,std::wstring& dll,std::wstring& directory){std::lock_guard<std::mutex> lock(mutex);
  if(!configured||running||started)return false;running=started=true;c=configuration;dll=callback;directory=root;return true;}
 void Publish(const Snapshot& value){std::lock_guard<std::mutex> lock(mutex);snapshot=value;
  if(queued&&control.action==Action::Close&&SameTuple(control.expected,value,false)){
   snapshot.preparedCurrent=snapshot.shownCurrent=snapshot.leaseCurrent=snapshot.actualScope=0;snapshot.state=State::CleanupPending;snapshot.cleanupComplete=0;}}
 void Revoke(CloseReason reason,Id now){std::lock_guard<std::mutex> lock(mutex);snapshot.preparedCurrent=snapshot.shownCurrent=snapshot.leaseCurrent=snapshot.actualScope=0;
  if(snapshot.state!=State::Closed){snapshot.state=State::CleanupPending;snapshot.cleanupComplete=0;}snapshot.reason=reason;snapshot.observedAt=now;}
 Snapshot Read(Id now)const{std::lock_guard<std::mutex> lock(mutex);auto copy=snapshot;copy.readAt=now;
  if(now>=copy.expiresAt){copy.preparedCurrent=copy.shownCurrent=copy.leaseCurrent=copy.actualScope=0;}return copy;}
 // A passive lineage marker never changes Snapshot flags or command authority.
 void PublishPair(const Snapshot& value,const ContinuationSnapshotV1& marker){std::lock_guard<std::mutex> lock(mutex);snapshot=value;continuation=marker;
  if(queued&&control.action==Action::Close&&SameTuple(control.expected,value,false)){snapshot.preparedCurrent=snapshot.shownCurrent=snapshot.leaseCurrent=snapshot.actualScope=0;snapshot.state=State::CleanupPending;snapshot.cleanupComplete=0;continuation.phase=3;}}
 void RevokePair(CloseReason reason,Id now,const ContinuationSnapshotV1& marker){std::lock_guard<std::mutex> lock(mutex);continuation=marker;snapshot.preparedCurrent=snapshot.shownCurrent=snapshot.leaseCurrent=snapshot.actualScope=0;
  if(snapshot.state!=State::Closed){snapshot.state=State::CleanupPending;snapshot.cleanupComplete=0;}snapshot.reason=reason;snapshot.observedAt=now;}
 ContinuationSnapshotV1 ReadContinuation(Id now)const{std::lock_guard<std::mutex> lock(mutex);auto value=continuation;value.readAt=now;
  if(value.phase==1&&now>=value.gapDeadline)value.phase=3;
  if((value.phase==2||value.phase==4)&&now>=value.preparationExpiresAt)return {};
  return value;}
 bool Queue(const ControlV1& c,Id now){std::lock_guard<std::mutex> lock(mutex);
  if(!running||!CommandCurrent(c,snapshot,now,configuration.leaseDurationMs))return false;
  // An exact close revokes any not-yet-consumed show/renew. Other commands never replace one another.
  if(queued&&c.action!=Action::Close)return false;control=c;queued=true;
  if(c.action==Action::Close){snapshot.preparedCurrent=snapshot.shownCurrent=snapshot.leaseCurrent=snapshot.actualScope=0;snapshot.state=State::CleanupPending;snapshot.cleanupComplete=0;snapshot.observedAt=now;}
  return true;}
 bool Take(ControlV1& c){std::lock_guard<std::mutex> lock(mutex);if(!queued)return false;c=control;queued=false;return true;}
 void Finish(const Snapshot& value){std::lock_guard<std::mutex> lock(mutex);snapshot=value;continuation={};queued=false;running=false;}
};
}
