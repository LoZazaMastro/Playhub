// SPDX-License-Identifier: GPL-3.0-only
#include "CurtainPublisherMailbox.h"
#include "CurtainDisplayApi.h"
#include <iostream>
#include <stdexcept>
using namespace playhub::curtain;
PublisherPulse Pulse(){PublisherPulse p;auto& a=p.authority;a.purpose=Purpose::FirstContentCover;a.generation=1;a.requestEpoch=1;
 a.requestId=std::string(32,'a');a.sourceReceipt=std::string(32,'b');a.ownerBuild=std::string(64,'c');a.appId=42;
 a.ownerPid=1;a.ownerBirth=101;a.gamePid=a.framePid=3;a.gameBirth=a.frameBirth=103;a.frameThread=33;a.gameWindow=300;
 a.steamPid=2;a.steamBirth=102;a.steamThread=22;a.steamWindow=200;a.session=1;a.gameRect={0,0,1920,1080};a.steamRect=a.gameRect;
 a.steamClass=L"SDL_app";a.gameClass=L"GameWindow";a.steamImage=L"C:\\Steam\\steamwebhelper.exe";a.gameImage=L"C:\\Games\\game.exe";
 p.sequence=1;p.observedAt=1000;p.expiresAt=2000;return p;}
int main(){try{unsigned checks=0;auto check=[&](bool ok){++checks;if(!ok)throw std::runtime_error("mailbox "+std::to_string(checks));};
 check(sizeof(WirePulse)==18736&&sizeof(WireSnapshot)==56);auto p=Pulse();PublisherMailbox m;
 check(Fresh(p,1000));check(Fresh(p,1250));check(!Fresh(p,1251));check(!Fresh(p,999));check(!Fresh(p,2000));
 check(m.Queue(1,p,1000));check(!m.Queue(1,p,1000));check(!m.Queue(2,p,1000));
 m.begin.reset();auto next=p;next.sequence=2;next.observedAt=1100;next.expiresAt=2100;
 check(m.Queue(2,next,1100));check(m.admitted==p.authority);check(!m.Queue(1,next,1100));
 auto foreign=next;foreign.authority.sourceReceipt=std::string(32,'d');foreign.sequence=3;
 check(!m.Queue(2,foreign,1100));check(!m.Queue(3,foreign,1100));
 check(m.Queue(3,p,5000));check(!m.latest&&!m.begin&&m.close);check(!m.Queue(2,next,1100));
 m.close=false;m.Closed(1);check(!m.admitted);check(!m.Queue(1,p,1000));
 auto fresh=p;fresh.authority.generation=2;check(m.Queue(1,fresh,1000));
 for(unsigned action:{0u,4u,5u}){PublisherMailbox f;check(!f.Queue(action,p,1000));}
 for(unsigned field:{0u,1u,2u}){PublisherMailbox f;auto bad=p;if(field==0)bad.authority.purpose=Purpose::None;
  if(field==1)bad.sequence=0;if(field==2)bad.expiresAt=2501;check(!f.Queue(1,bad,1000));}
 std::cout<<checks<<" actual curtain mailbox/ABI checks PASS; no OS calls\n";
 }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}}
