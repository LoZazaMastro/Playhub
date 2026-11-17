// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "Policy.h"
#include <iostream>
inline int PureTests(){
 int count=0;auto check=[&](bool ok,const char* name){++count;if(!ok){std::cerr<<"FAIL "<<name<<"\n";return false;}return true;};
 proof::Facts good{true,true,true,true,true,true,true,true,true,true,false,true,100,600,200};
 if(!check(proof::Admit(good),"valid exact callback"))return 1;
 for(int drift=-1;drift<7;drift++){
  bool facts[]{true,true,true,true,true,true,true};int writes=0,commits=0,finishes=0;bool read=false;
  bool dispatched=proof::DispatchBand([&](){read=true;if(drift>=0)facts[drift]=false;return true;},[&](){++commits;return true;},[&](){if(!read)return false;for(bool f:facts)if(!f)return false;return true;},[&](){++writes;},[&](){++finishes;});
  if(!check(dispatched==(drift<0)&&writes==(drift<0?1:0)&&commits==1&&finishes==1,"band-before getter frame/core/birth/root/rect/focus/desktop drift veto"))return 1;
 }
 {int writes=0,finishes=0;bool result=proof::DispatchBand([](){return true;},[](){return false;},[](){return true;},[&](){++writes;},[&](){++finishes;});if(!check(!result&&!writes&&!finishes,"revoked Ready never commits or writes"))return 1;}
 {int commits=0;bool result=proof::DispatchBand([](){return false;},[&](){++commits;return true;},[](){return true;},[](){},[](){});if(!check(!result&&!commits,"failed getter never commits"))return 1;}
 if(!check(proof::ShellExitObserved(0),"retained process signaled")||!check(!proof::ShellExitObserved(258),"live process not exit")||!check(!proof::ShellExitObserved(0xffffffffu),"failed query not exit"))return 1;
 if(!check(proof::CallerLease(100,5100),"bounded caller lease")||!check(!proof::CallerLease(100,5101),"caller cannot extend budget")||!check(!proof::CallerLease(100,100),"expired caller")||!check(!proof::CallerLease(100,99),"past caller")||!check(proof::CallerLease(100,101),"short remaining lease"))return 1;
 if(!check(!proof::MayDestroy(proof::Permit::Ready),"ready permit must be revoked before destroy")||!check(!proof::MayDestroy(proof::Permit::Committed),"committed callback retains target HWND")||!check(proof::MayDestroy(proof::Permit::Finished),"finished callback releases target")||!check(proof::MayDestroy(proof::Permit::Revoked),"revocation won before commit"))return 1;
 for(auto state:{proof::Permit::Ready,proof::Permit::Committed,proof::Permit::Finished,proof::Permit::Revoked}){
  if(!check(proof::MayWrite(true,true,true,true,state)==(state==proof::Permit::Committed),"exact committed one-shot only"))return 1;
 }
 for(unsigned missing=0;missing<4;missing++){bool facts[4]{true,true,true,true};facts[missing]=false;if(!check(!proof::MayWrite(facts[0],facts[1],facts[2],facts[3],proof::Permit::Committed),"target/source/input/deadline veto"))return 1;}
 if(!check(proof::RefusalMask(good)==0,"valid request has no refusal flags"))return 1;
 for(auto member:{&proof::Facts::schema,&proof::Facts::nonce,&proof::Facts::module,&proof::Facts::controller,&proof::Facts::shell,&proof::Facts::thread,&proof::Facts::security,&proof::Facts::desktop,&proof::Facts::foreground,&proof::Facts::armed,&proof::Facts::unclaimed}){
  auto changed=good;changed.*member=false;if(!check(!proof::Admit(changed)&&proof::RefusalMask(changed)!=0,"individual identity/scope/nonce rejection"))return 1;
 }
 auto changed=good;changed.revoked=true;if(!check(!proof::Admit(changed),"revoked request"))return 1;
 if(!check(!proof::Live(0,500,100),"unset time")||!check(!proof::Live(100,100,100),"empty lifetime")||!check(!proof::Live(100,601,200),"overlong lifetime")||!check(!proof::Live(100,600,99),"future request")||!check(!proof::Live(100,600,601),"late callback")||!check(proof::Live(100,600,600),"deadline inclusive"))return 1;
 // Production admission function exercised over revocation, replay and expiry transitions.
 auto lease=good;if(!check(proof::Admit(lease),"first entry permitted"))return 1;lease.unclaimed=false;
 if(!check(!proof::Admit(lease),"second callback rejected"))return 1;lease.revoked=true;lease.armed=false;lease.unclaimed=true;
 if(!check(!proof::Admit(lease),"unhook race never rearms"))return 1;
 lease=good;lease.now=601;if(!check(!proof::Admit(lease),"delayed message resumes expired"))return 1;
 if(!check(proof::Cleanup(true,true,true,true,true,true,true,true),"complete observed cleanup"))return 1;
 if(!check(proof::AbsenceSettled(true,true,0,100),"stable observed absence")||!check(!proof::AbsenceSettled(true,true,0,99),"single late absence insufficient")||!check(!proof::AbsenceSettled(false,true,0,100),"unknown module enumeration")||!check(!proof::AbsenceSettled(true,false,0,100),"mapped module remains")||!check(!proof::AbsenceSettled(true,true,1,100),"callback still in flight"))return 1;
 for(unsigned missing=0;missing<8;missing++){
  bool c[8]{true,true,true,true,true,true,true,true};c[missing]=false;
  if(!check(!proof::Cleanup(c[0],c[1],c[2],c[3],c[4],c[5],c[6],c[7]),"unhook alone/unknown absence not cleanup"))return 1;
 }
 std::cout<<"{\"pureChecks\":"<<count<<",\"passed\":true,\"hooksInstalled\":false,\"dllLoaded\":false,\"uiCreated\":false}\n";return 0;
}
