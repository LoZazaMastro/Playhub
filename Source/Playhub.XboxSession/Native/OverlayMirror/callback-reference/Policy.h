// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <cstdint>
namespace proof {
constexpr std::uint64_t Magic=0x50485348434C4231ull;
constexpr std::uint32_t Version=2;
constexpr std::uint64_t MaxLifetimeMs=500;
enum class Permit : std::int32_t { Ready=0, Committed=1, Finished=2, Revoked=3 };
constexpr bool MayDestroy(Permit state){return state==Permit::Revoked||state==Permit::Finished;}
constexpr bool MayWrite(bool exactTarget,bool exactSource,bool noInput,bool withinDeadline,Permit state){return exactTarget&&exactSource&&noInput&&withinDeadline&&state==Permit::Committed;}
constexpr bool CallerLease(std::uint64_t now,std::uint64_t deadline){return deadline>now&&deadline-now<=5000;}
constexpr bool ShellExitObserved(std::uint32_t waitResult){return waitResult==0;}
template<class Read,class Commit,class Current,class Write,class Finish>
bool DispatchBand(Read&& read,Commit&& commit,Current&& current,Write&& write,Finish&& finish){
 if(!read()||!commit())return false;
 bool valid=current();if(valid)write();finish();return valid;
}
struct Facts {
 bool schema{},nonce{},module{},controller{},shell{},thread{},security{},desktop{},foreground{},armed{},revoked{},unclaimed{};
 std::uint64_t issued{},deadline{},now{};
};
constexpr bool Live(std::uint64_t issued,std::uint64_t deadline,std::uint64_t now) {
 return issued>0&&deadline>issued&&deadline-issued<=MaxLifetimeMs&&now>=issued&&now<=deadline;
}
constexpr bool Admit(const Facts& f) {
 return f.schema&&f.nonce&&f.module&&f.controller&&f.shell&&f.thread&&f.security&&f.desktop&&f.foreground&&f.armed&&!f.revoked&&f.unclaimed&&Live(f.issued,f.deadline,f.now);
}
constexpr std::uint32_t RefusalMask(const Facts& f) {
 return (!f.schema?1u:0u)|(!f.nonce?2u:0u)|(!f.module?4u:0u)|(!f.controller?8u:0u)|(!f.shell?16u:0u)|(!f.thread?32u:0u)|(!f.security?64u:0u)|(!f.desktop?128u:0u)|(!f.foreground?256u:0u)|(!f.armed?512u:0u)|(f.revoked?1024u:0u)|(!f.unclaimed?2048u:0u)|(!Live(f.issued,f.deadline,f.now)?4096u:0u);
}
constexpr bool Cleanup(bool revoked,bool unhooked,bool barrier,bool noInflight,bool absenceRead,bool moduleAbsent,bool shellAlive,bool foregroundPreserved) {
 return revoked&&unhooked&&barrier&&noInflight&&absenceRead&&moduleAbsent&&shellAlive&&foregroundPreserved;
}
constexpr bool AbsenceSettled(bool read,bool absent,std::int32_t inflight,std::uint64_t observedMs){return read&&absent&&inflight==0&&observedMs>=100;}
}
