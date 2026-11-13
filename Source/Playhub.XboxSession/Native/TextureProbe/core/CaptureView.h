// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <array>
#include <cstdint>
namespace view {
using CaptureId=std::uintptr_t;
struct CaptureKey {std::uint64_t generation{},nonce{};CaptureId window{},cookie{};std::uint32_t thread{};};
inline bool SameCapture(const CaptureKey&a,const CaptureKey&b){return a.generation&&a.nonce&&a.window&&a.cookie&&a.thread&&a.generation==b.generation&&a.nonce==b.nonce&&a.window==b.window&&a.cookie==b.cookie&&a.thread==b.thread;}
// Thread-local logical mouse ownership for the exact display-only Valve view.
// A retired pending entry is only a paired cleanup tombstone: it grants no view,
// and its Release never releases an unrelated real OS capture after revocation.
struct CaptureLedger {
 CaptureKey key{};bool held{},pending{};
 void Clear(){key={};held=pending=false;}
 void Retire(){held=false;}
 bool Matches(const CaptureKey& k)const{return held&&SameCapture(key,k);}
 CaptureId Set(const CaptureKey& k,CaptureId actualPrevious){CaptureId previous=Matches(k)?key.window:actualPrevious;key=k;held=pending=true;return previous;}
};
// Ops performs the original APIs. Grant must reattest the pinned caller/exact
// source/thread/gen/cookie/nonce/real game foreground AFTER SnapshotCapture.
// Foreign/unrelated setters retire local bookkeeping but invoke original once.
template<class Ops> CaptureId CaptureSet(CaptureLedger& state,bool pinned,CaptureId requested,Ops& ops){
 if(pinned){auto previous=ops.SnapshotCapture();auto key=ops.Grant(requested);if(key.window&&SameCapture(key,key))return state.Set(key,previous);}
 state.Clear();return ops.Set(requested);
}
template<class Ops> CaptureId CaptureGet(CaptureLedger& state,bool pinned,Ops& ops){
 if(pinned&&state.pending){auto key=ops.Grant(state.key.window);if(state.Matches(key))return state.key.window;state.Retire();}
 return ops.Get();
}
template<class Ops> bool CaptureRelease(CaptureLedger& state,bool pinned,Ops& ops){
 if(pinned&&state.pending){state.Clear();return true;}
 state.Clear();return ops.Release();
}
struct CaptureRecord {std::uint64_t serial{},bootMs{},generation{};CaptureId requested{},result{},actualBefore{},actualAfter{};std::uint32_t thread{};bool callerPinned{},virtualized{};};
struct CaptureDiagnostic {std::uint64_t setCalls{},virtualSets{},realSets{},getCalls{},virtualGets{},releaseCalls{},virtualReleases{};std::uint32_t rows{};std::array<CaptureRecord,32> records{};};
}
