// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <cstdint>
#include <utility>
namespace playhub::mirror::native_observation {
using Tick=std::uint64_t;
enum class AcquireResult {Ready,Pending,Refused};
// GPU/source snapshots retain their real timestamp; no retry samples a clock
// into their metadata. Same-owner and a quarter-second age are mandatory.
template<class Payload,class Identity> class FrozenSource {
 Payload payload{};Identity identity{};Tick barrier{};bool held{};
public:
 void Clear(){payload={};identity={};barrier=0;held=false;}
 template<class Same> const Payload* Get(const Identity& expected,Tick minimum,Tick now,Tick frequency,Same same){
  if(!held||barrier!=minimum||!same(identity,expected)||!frequency||!payload.presentQpc||payload.presentQpc>now||now-payload.presentQpc>frequency/4){Clear();return nullptr;}
  return &payload;
 }
 void Store(Payload value,const Identity& owner,Tick minimum){payload=std::move(value);identity=owner;barrier=minimum;held=true;}
};
// Caller captures source once and supplies ONE original wall-clock deadline.
// Each successful stale frame is released by Acquire before this next call.
template<class Frame,class Acquire,class Retryable,class Current,class Now>
AcquireResult AcquireAligned(Tick sourceQpc,Tick minimumQpc,Tick deadline,Frame& frame,
 Acquire acquire,Retryable retryable,Current current,Now now,unsigned& attempts,unsigned& skipped){
 attempts=skipped=0;Tick previous=now();bool deferred=false;
 if(!sourceQpc||!current())return AcquireResult::Refused;
 if(previous>=deadline)return AcquireResult::Pending;
 for(unsigned i=0;i<32;++i){
  const Tick tick=now();if(tick<previous)return AcquireResult::Refused;previous=tick;
  if(!current())return AcquireResult::Refused;
  if(tick>=deadline)return AcquireResult::Pending;
  const Tick remaining=deadline-tick;if(!remaining||remaining>40)return AcquireResult::Refused;
  frame={};++attempts;const bool acquired=acquire(static_cast<unsigned>(remaining),frame);
  const Tick after=now();if(after<previous||!current())return AcquireResult::Refused;previous=after;
  if(!acquired){if(retryable()){deferred=true;continue;}return AcquireResult::Refused;}
  if(!frame.presentQpc)return AcquireResult::Refused;
  if(frame.presentQpc<sourceQpc||(minimumQpc&&frame.presentQpc<=minimumQpc)){++skipped;deferred=true;continue;}
  return after<deadline?AcquireResult::Ready:AcquireResult::Pending;
 }
 return deferred?AcquireResult::Pending:AcquireResult::Refused;
}
}

