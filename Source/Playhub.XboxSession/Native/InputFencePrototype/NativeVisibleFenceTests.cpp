// SPDX-License-Identifier: GPL-3.0-only
#include "FencePolicy.h"
#include <cstddef>
#include <memory>
#include <cstdio>
#include <cstdlib>
using namespace fence;
unsigned checks{};
void Need(bool value){++checks;if(!value){std::printf("FAIL %u\n",checks);std::exit(1);}}
struct Fixture {
 Identity id{{1,2,3,4},10,20,0x8000000002000000,100,200,1};
 Frame frame;std::uint64_t now=1000;bool alive=true,readable=true;unsigned currentCalls{},denyAt{};
 std::unique_ptr<Policy> policy;
 Fixture(){frame.kind=3;frame.session=id.session;frame.gameBirth=id.gameBirth;frame.producerBirth=id.producerBirth;
  frame.gamePid=id.gamePid;frame.producerPid=id.producerPid;frame.windowsSession=id.windowsSession;frame.sequence=2;frame.generation=1;frame.gameId=id.gameId;
  frame.sourceCookie=8;frame.destinationCookie=0;At(now,1);
  policy=std::make_unique<Policy>(id,[&](Frame& out){out=frame;return readable;},[&]{++currentCalls;return alive&&currentCalls!=denyAt;},[&]{return now;});}
 void At(std::uint64_t at,unsigned phase){now=at;frame.phase=phase;frame.readAt=at;frame.expiresAt=at+250;}
 State Input(std::uint16_t bits=0x2000)const{State s;s.packet=7;s.pad.buttons=bits;s.pad.left=31;s.pad.right=44;s.pad.lx=100;s.pad.ry=-200;return s;}
};
int main(){
 static_assert(sizeof(Frame)==128&&alignof(Frame)==8);
 static_assert(offsetof(Frame,session)==16&&offsetof(Frame,generation)==48&&offsetof(Frame,sourceCookie)==112&&offsetof(Frame,destinationCookie)==120);
 Fixture admitted;auto state=admitted.Input();
 // Parent rejects this exact original-surface frame because kind3 is unknown.
 Need(admitted.policy->StateResult(0,0,&state));Need(Neutral(state.pad)&&state.packet==8);
 unsigned char key[8]{1,2,3};Need(admitted.policy->KeystrokeResult(0,key,sizeof key,false,false,0,0x2000,false)&&key[0]==0);
 for(unsigned field=0;field<25;++field){Fixture x;switch(field){
  case 0:x.frame.kind=4;break;case 1:x.frame.sourceCookie=0;break;case 2:x.frame.destinationCookie=9;break;
  case 3:x.frame.phase=0;break;case 4:x.frame.phase=4;break;case 5:x.frame.phase=3;break;case 6:x.frame.phase=6;break;
  case 7:++x.frame.gamePid;break;case 8:++x.frame.gameBirth;break;case 9:++x.frame.producerPid;break;case 10:++x.frame.producerBirth;break;
  case 11:++x.frame.windowsSession;break;case 12:++x.frame.session[0];break;case 13:x.frame.sequence=3;break;case 14:x.frame.sequence=0;break;
  case 15:x.frame.magic=0;break;case 16:x.frame.version=2;break;case 17:x.frame.size=129;break;case 18:x.frame.readAt=x.now-251;break;
  case 19:x.frame.readAt=x.now+1;break;case 20:x.frame.expiresAt=x.now;break;case 21:x.frame.expiresAt=x.frame.readAt+1501;break;
  case 22:x.alive=false;break;case 23:x.denyAt=2;break;case 24:x.frame.expiresAt=x.frame.readAt+251;break;}
  state=x.Input();Need(!x.policy->StateResult(0,0,&state)&&state.pad.buttons==0x2000&&state.pad.left==31);
 }
 Fixture exactAge;exactAge.now=999;exactAge.frame.readAt=750;exactAge.frame.expiresAt=1000;state=exactAge.Input();Need(exactAge.policy->StateResult(0,0,&state));
 for(std::uint16_t kind:std::array<std::uint16_t,2>{1,2}){Fixture legacy;legacy.frame.kind=kind;legacy.frame.destinationCookie=9;state=legacy.Input();Need(legacy.policy->StateResult(0,0,&state)&&Neutral(state.pad));
  Fixture absentDestination;absentDestination.frame.kind=kind;state=absentDestination.Input();Need(!absentDestination.policy->StateResult(0,0,&state)&&state.pad.buttons==0x2000);}
 // Native original cleanup removes only held digital bits; no full neutral tail.
 Fixture release;state=release.Input();Need(release.policy->StateResult(0,0,&state));release.At(1010,2);
 state=release.Input(0x3000);Need(release.policy->StateResult(0,0,&state)&&state.pad.buttons==0x1000&&state.pad.left==31&&state.pad.lx==100);
 release.At(1400,5);state=release.Input(0x3000);Need(release.policy->StateResult(0,0,&state)&&state.pad.buttons==0x1000&&state.pad.right==44);
 state=release.Input(0x1000);release.policy->StateResult(0,0,&state);state=release.Input(0x3000);release.policy->StateResult(0,0,&state);Need(state.pad.buttons==0x3000&&state.pad.left==31);
 Fixture bounded;state=bounded.Input();Need(bounded.policy->StateResult(0,0,&state));bounded.At(1010,2);state=bounded.Input();Need(bounded.policy->StateResult(0,0,&state));bounded.At(6010,5);state=bounded.Input();Need(!bounded.policy->StateResult(0,0,&state)&&state.pad.buttons==0x2000);
 // Substitution of the source identity cannot revive a generation's owner.
 Fixture changed;state=changed.Input();Need(changed.policy->StateResult(0,0,&state));++changed.frame.sourceCookie;state=changed.Input();Need(!changed.policy->StateResult(0,0,&state));--changed.frame.sourceCookie;state=changed.Input();Need(!changed.policy->StateResult(0,0,&state));
 changed.frame.generation=2;state=changed.Input();Need(changed.policy->StateResult(0,0,&state));
 // kind3 must not silently change to an own-mirror cookie pair.
 changed.frame.kind=1;changed.frame.destinationCookie=9;state=changed.Input();Need(!changed.policy->StateResult(0,0,&state));
 Fixture unknown;state=unknown.Input();Need(unknown.policy->StateResult(0,0,&state));unknown.readable=false;state=unknown.Input();Need(!unknown.policy->StateResult(0,0,&state)&&state.pad.buttons==0x2000);
 std::printf("PASS %u actual kind3 policy/ABI/identity/neutral-release checks; no OS/input calls\n",checks);
}
