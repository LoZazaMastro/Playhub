// SPDX-License-Identifier: GPL-3.0-only
#include "BandBrokerPolicy.h"
#include <windows.h>
#include <array>
#include <vector>
#include "QuietBandAdmission.h"
#include "BrokerDiagnostics.h"
static std::array<SHORT,256> inputKeys{};static POINT inputCursor{10,20};static bool inputCursorRead=true;static std::vector<int> inputCalls;
static SHORT FakeAsyncKey(int vk){inputCalls.push_back(vk);return inputKeys[unsigned(vk)];}
static BOOL FakePhysicalCursor(POINT* out){inputCalls.push_back(1000);if(inputCursorRead)*out=inputCursor;return inputCursorRead?TRUE:FALSE;}
#define GetAsyncKeyState FakeAsyncKey
#define GetPhysicalCursorPos FakePhysicalCursor
#include "band/Target.h"
#include "../callback-v3/Target.h"
#include "../callback-v3/Tests.h"
#undef GetAsyncKeyState
#undef GetPhysicalCursorPos
#include <cstddef>
#include <iostream>
static_assert(sizeof(playhub_band16::Shared)==sizeof(proof::Shared),"C449 wire size cannot change");
static_assert(alignof(playhub_band16::Shared)==alignof(proof::Shared),"C449 wire alignment cannot change");
#define SAME_OFFSET(field) static_assert(offsetof(playhub_band16::Shared,field)==offsetof(proof::Shared,field),"C449 wire offset changed: " #field)
SAME_OFFSET(magic);SAME_OFFSET(size);SAME_OFFSET(nonceLow);SAME_OFFSET(controllerBirth);SAME_OFFSET(sourceCore);SAME_OFFSET(gamePid);SAME_OFFSET(targetWindow);SAME_OFFSET(targetClass);SAME_OFFSET(operationEvent);SAME_OFFSET(operationPermit);SAME_OFFSET(inflight);SAME_OFFSET(bandAt);SAME_OFFSET(bandAfterRead);
static int QuietAdmissionChecks(){
 using namespace playhub::mirror;unsigned count=0;auto check=[&](bool v){++count;if(!v)std::cerr<<"FAIL quiet "<<count<<"\n";return v;};
 playhub_band16::Shared source{};source.initialCursor={10,20};
 auto capture=[](){return ReadNoInputFacts(10,20,FakeAsyncKey,[](std::int32_t& x,std::int32_t& y){POINT p{};bool ok=FakePhysicalCursor(&p)!=FALSE;if(ok){x=p.x;y=p.y;}return ok;});};
 for(unsigned held=1;held<256;held++){
  inputKeys.fill(0);inputKeys[held]=SHORT(0x8000);inputCalls.clear();bool original=playhub_band16::NoInput(source);auto before=inputCalls;inputCalls.clear();auto facts=capture();
  if(!check(original==facts.passed&&before==inputCalls&&!facts.cursorAttempted&&!facts.cursorRead&&facts.held&&facts.firstHeldVk==held))return 1;
 }
 inputKeys.fill(0);inputKeys[195]=1;
 for(unsigned mode=0;mode<3;mode++){
  inputCursorRead=mode!=2;inputCursor={LONG(mode==1?11:10),20};inputCalls.clear();bool original=playhub_band16::NoInput(source);auto before=inputCalls;inputCalls.clear();auto facts=capture();
  if(!check(original==facts.passed&&before==inputCalls&&!facts.held&&facts.cursorAttempted&&facts.cursorRead==(mode!=2)&&facts.passed==(mode==0)))return 1;
 }
 inputKeys.fill(0);inputCursorRead=true;inputCursor={10,20};
 for(unsigned stage=0;stage<=unsigned(BrokerStage::PollRefused);stage++){
  const auto s=BrokerStage(stage);bool expected=s==BrokerStage::InitialNoInput||s==BrokerStage::PreHookNoInput;
  if(!check(MayDeferNoInput(s,false,false)==expected&&!MayDeferNoInput(s,true,false)&&!MayDeferNoInput(s,false,true)))return 1;
 }
 QuietBandAdmission q;std::uint64_t now=1000;unsigned reads=0;NoInputFacts sample{};sample.cursorRead=true;sample.currentX=10;sample.currentY=20;
 auto poll=[&](bool current=true){return q.Poll(current,[&]{++reads;return sample;},[&]{return now;});};
 if(!check(q.Defer(now,6000)&&poll()==QuietStep::Pending&&reads==0))return 1;
 q.CleanupComplete();sample.held=true;sample.firstHeldVk=195;sample.cursorRead=false;
 if(!check(poll()==QuietStep::Pending&&reads==1))return 1;
 now+=8;if(!check(poll()==QuietStep::Pending&&reads==1))return 1;
 for(now=1016;now<1370;now+=16)if(!check(poll()==QuietStep::Pending))return 1;
 now=1384;sample.held=false;sample.cursorRead=true;if(!check(poll()==QuietStep::Pending))return 1;
 now=1400;if(!check(poll()==QuietStep::Retry&&q.Retries()==1))return 1;
 now=1416;if(!check(q.Defer(now,6000)))return 1;q.CleanupComplete();if(!check(poll()==QuietStep::Pending))return 1;now=1432;if(!check(poll()==QuietStep::Retry&&q.Retries()==2&&!q.Defer(1440,6000)))return 1;
 q.Reset();now=1000;reads=0;if(!check(q.Defer(now,1100)))return 1;q.CleanupComplete();now=1100;if(!check(poll()==QuietStep::Refused&&reads==0))return 1;
 q.Reset();now=1000;q.Defer(now,6000);now=1750;if(!check(poll()==QuietStep::Refused&&reads==0))return 1;
 q.Reset();now=1000;q.Defer(now,6000);q.CleanupComplete();if(!check(poll(false)==QuietStep::Refused&&reads==0))return 1;
 q.Reset();now=1000;q.Defer(now,6000);q.CleanupComplete();sample.cursorRead=false;if(!check(poll()==QuietStep::Pending))return 1;now=1016;sample.cursorRead=true;if(!check(poll()==QuietStep::Pending))return 1;now=1032;sample.currentX=11;if(!check(poll()==QuietStep::Pending))return 1;now=1048;if(!check(poll()==QuietStep::Retry))return 1;
 q.Reset();now=1000;q.Defer(now,6000);q.CleanupComplete();auto slow=q.Poll(true,[&]{now=1800;return sample;},[&]{return now;});if(!check(slow==QuietStep::Refused))return 1;
 q.Reset();now=1000;q.Defer(now,6000);q.CleanupComplete();auto cancel=q.Poll(true,[&]{q.Reset();return sample;},[&]{return now;});if(!check(cancel==QuietStep::Refused&&!q.Pending()))return 1;
 FirstBrokerFailure d;d.Begin();NoInputFacts blocked{};blocked.held=true;blocked.firstHeldVk=195;d.InputBlocked(blocked);d.DeferInput();d.Reject(BrokerStage::InitialNoInput);d.RestartAttempt();
 if(!check(d.Read().serial==1&&d.Read().stage==BrokerStage::None&&d.Read().noInputFailures==1&&d.Read().quietDeferrals==1&&d.Read().blockedInput.firstHeldVk==195))return 1;
 d.Reject(BrokerStage::ShellSecurity);if(!check(d.Read().stage==BrokerStage::ShellSecurity))return 1;
 std::cout<<"PASS "<<count<<" quiet-admission/actual NoInput equivalence checks; no OS/input/hooks\n";return 0;
}
int main(){if(QuietAdmissionChecks())return 1;int inherited=PureTests();if(inherited)return inherited;using namespace playhub::mirror;unsigned count{};auto check=[&](bool v){++count;if(!v)std::cerr<<"FAIL retirement case "<<count<<"\n";return v;};RetirementFacts good;good.unhooked=good.operationFinishedOrRevoked=good.barrierReturned=good.absenceRead=good.moduleAbsent=true;good.absentForMs=100;if(!check(MayReleaseCallback(good)))return 1;
for(unsigned i=0;i<8;i++){auto f=good;if(i==0)f.unhooked=false;if(i==1)f.operationFinishedOrRevoked=false;if(i==2)f.barrierReturned=false;if(i==3)f.absenceRead=false;if(i==4)f.moduleAbsent=false;if(i==5)f.inFlight=1;if(i==6)f.inFlight=-1;if(i==7)f.absentForMs=99;if(!check(!MayReleaseCallback(f)))return 1;}
RetirementFacts exited;exited.shellExitObserved=true;exited.inFlight=1;if(!check(MayReleaseCallback(exited)))return 1;exited.shellExitObserved=false;if(!check(!MayReleaseCallback(exited)))return 1;
if(!check(sizeof(playhub_band16::Shared)==sizeof(proof::Shared)&&playhub_band16::Magic==proof::Magic&&playhub_band16::Version==proof::Version))return 1;
playhub_band16::Win32SourceFacts win{true,true,true,true,true,true,true,true,true};if(!check(playhub_band16::AdmitWin32Source(win)))return 1;for(unsigned i=0;i<9;i++){auto f=win;if(i==0)f.aliasesRetainedGame=false;if(i==1)f.retainedBirth=false;if(i==2)f.exactImage=false;if(i==3)f.nativeDesktopToken=false;if(i==4)f.exactWindowThread=false;if(i==5)f.exactRootClass=false;if(i==6)f.visibleRect=false;if(i==7)f.cleanDesktop=false;if(i==8)f.actualForeground=false;if(!check(!playhub_band16::AdmitWin32Source(f)))return 1;}
DeferredBandAdmission deferred;unsigned setup{};if(!check(deferred.Reserve()&&!deferred.Reserve()&&setup==0))return 1;deferred.Reset();if(deferred.TakeStart())++setup;if(!check(setup==0))return 1;if(!check(deferred.Reserve()&&deferred.TakeStart()&&!deferred.TakeStart()))return 1;++setup;if(!check(setup==1))return 1;deferred.Reset();if(!check(deferred.Reserve()))return 1;
std::cout<<"{\"bandBrokerChecks\":"<<count<<",\"wireBytes\":"<<sizeof(proof::Shared)<<",\"passed\":true,\"hooksInstalled\":false,\"dllLoaded\":false}\n";return 0;}
