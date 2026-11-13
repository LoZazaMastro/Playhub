// SPDX-License-Identifier: GPL-3.0-only
#include "ObservationTracePolicy.h"
#include <cstdio>
#include <stdexcept>
#include <cstdlib>
using namespace view::observation;
struct Ops{unsigned error=5;unsigned Error(){return error;}void Error(unsigned e){error=e;}};
static int checks{};static void Check(bool value){++checks;if(!value){std::fprintf(stderr,"check %d failed\n",checks);std::exit(1);}}
int main(){
 for(int mode=0;mode<7;mode++){
  Ops ops;int originalCalls=0,beforeCalls=0,afterCalls=0,published=0;
  auto result=PassThrough(ops,[&]{++originalCalls;Check(ops.error==5);ops.error=77;return std::uint64_t(0x100000001);},[&](Record&r){++beforeCalls;ops.error=9;if(mode==1)throw 1;r.before.generation=3;return mode!=2;},[&](Record&r,auto answer,auto error){++afterCalls;Check(answer==0x100000001&&error==77);ops.error=11;if(mode==3)throw 1;r.returned=answer;if(mode==6)r.currentAfter=false;},[&](const Record&r){++published;ops.error=13;if(mode==4)throw 1;Check(r.returned==0x100000001);});
  Check(result==0x100000001);Check(originalCalls==1&&beforeCalls==1);Check(ops.error==77);
  Check(afterCalls==((mode==1||mode==2)?0:1));Check(published==((mode==1||mode==2||mode==3)?0:1));
 }
 {Ops ops;int calls=0;bool thrown=false;try{PassThrough(ops,[&]()->int{++calls;throw std::runtime_error("original");},[](Record&){return true;},[](Record&,int,unsigned){throw 4;},[](const Record&){});}catch(const std::runtime_error&e){thrown=std::string(e.what())=="original";}Check(thrown&&calls==1);}
 {Ops ops;int calls=0;auto result=PassThrough(ops,[&]{++calls;ops.error=100;return 0;},[](Record&){return true;},[](Record&r,int value,unsigned){r.returned=value;},[](const Record&r){Check(r.returned==0);});Check(result==0&&calls==1&&ops.error==100);}
 {Scope a{};a.generation=1;a.ownWindow=2;a.ownCookie=3;a.ownerBirth=134389349126092720ULL;a.ownerPid=5;a.ownThread=6;a.gamePid=7;a.gameBirth=134389349126092721ULL;a.gameWindow=9;a.framePid=10;a.frameBirth=134389349126092722ULL;a.frameThread=12;
 Check(Same(a,a));for(int i=0;i<12;i++){auto b=a;switch(i){case 0:++b.generation;break;case 1:++b.ownWindow;break;case 2:++b.ownCookie;break;case 3:++b.ownerBirth;break;case 4:++b.ownerPid;break;case 5:++b.ownThread;break;case 6:++b.gamePid;break;case 7:++b.gameBirth;break;case 8:++b.gameWindow;break;case 9:++b.framePid;break;case 10:++b.frameBirth;break;case 11:++b.frameThread;break;}Check(!Same(a,b));}auto renewed=a;++renewed.expiresAt;Check(Same(a,renewed));}

 {Ring<2> ring;Record a{};a.before.generation=1;Check(ring.Push(a));a.before.generation=2;Check(ring.Push(a));a.before.generation=3;Check(!ring.Push(a));std::array<Record,2> rows{};std::uint64_t lost{};std::size_t size{};Check(ring.Copy(rows,size,lost)&&size==2&&lost==1&&rows[0].before.generation==1&&rows[1].before.generation==2);}
 {Ring<32,24> ring;Record r{};r.kind=Kind::GetActiveWindow;r.caller=1;r.before.generation=1;r.end=1;Check(ring.Push(r));bool repeated=true;for(int i=0;i<10000;i++){r.end++;repeated=ring.Push(r)&&repeated;}Check(repeated);std::array<Record,32> rows{};std::size_t size{};std::uint64_t lost{};Check(ring.Copy(rows,size,lost)&&size==1&&rows[0].repeats==10001&&rows[0].end==10001);
 for(int i=2;i<=24;i++){r.caller=i;Check(ring.Push(r));}r.caller=25;Check(!ring.Push(r));r.kind=Kind::SetForegroundWindow;for(int i=0;i<8;i++){r.caller=100+i;Check(ring.Push(r));}Check(ring.Copy(rows,size,lost)&&size==32&&lost==1);}
 std::printf("%d pure pass-through checks PASS; no Win32 methods called\n",checks);return 0;
}
