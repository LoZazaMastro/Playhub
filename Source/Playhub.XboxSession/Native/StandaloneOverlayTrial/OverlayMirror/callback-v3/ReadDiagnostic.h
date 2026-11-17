// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <cstdint>
namespace playhub_band_read {
// Diagnostic copies only. No native queries, clock reads or authorization.
enum Reason : std::uint32_t {None,GameHandle,ExactTarget,CurrentSource,Exports,BandBeforeRead,BandBeforeValue,NoInput,Deadline,CommitPermit};
enum Bits : std::uint32_t {GameGood=1,TargetGood=2,SourceGood=4,ExportsGood=8,
 BandReadKnown=16,BandReadGood=32,BandValueKnown=64,BandOne=128,
 InputKnown=256,InputGood=512,TimeKnown=1024,TimeGood=2048,CommitKnown=4096,CommitGood=8192};
struct Copy {
 std::uint32_t first{},bits{};
 bool Observe(Reason reason,std::uint32_t known,std::uint32_t good,bool value)noexcept{
  bits|=known;if(value)bits|=good;else if(!first)first=reason;return value;
 }
 bool Game(bool v)noexcept{return Observe(GameHandle,0,GameGood,v);}
 bool Target(bool v)noexcept{return Observe(ExactTarget,0,TargetGood,v);}
 bool Source(bool v)noexcept{return Observe(CurrentSource,0,SourceGood,v);}
 bool Export(bool v)noexcept{return Observe(Exports,0,ExportsGood,v);}
 void Band(bool read,std::uint32_t band)noexcept{Observe(BandBeforeRead,BandReadKnown,BandReadGood,read);if(read)Observe(BandBeforeValue,BandValueKnown,BandOne,band==1);}
 bool Input(bool v)noexcept{return Observe(NoInput,InputKnown,InputGood,v);}
 bool Time(bool v)noexcept{return Observe(Deadline,TimeKnown,TimeGood,v);}
 bool Commit(bool v)noexcept{return Observe(CommitPermit,CommitKnown,CommitGood,v);}
};
inline const char* Name(std::uint32_t reason)noexcept{
 switch(reason){case None:return "none";case GameHandle:return "game-handle";case ExactTarget:return "exact-target";
 case CurrentSource:return "current-source";case Exports:return "user32-exports";case BandBeforeRead:return "band-before-read";
 case BandBeforeValue:return "band-before-value";case NoInput:return "no-input";case Deadline:return "callback-deadline";
 case CommitPermit:return "commit-permit";default:return "unknown";}
}
}
