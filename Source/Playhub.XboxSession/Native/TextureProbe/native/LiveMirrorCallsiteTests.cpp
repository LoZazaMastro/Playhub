// SPDX-License-Identifier: GPL-3.0-only
#include "LiveMirrorApi.h"
#include "ForegroundViewPolicy.h"
#include "NonceLifetime.h"
#include <cstdlib>
#include <iostream>
using namespace playhub::mirror;using namespace playhub::mirror::callsite;
static unsigned checks;static void Check(bool value,const char* why){++checks;if(!value){std::cerr<<why<<'\n';std::exit(1);}}
static view::NonceIdentity property;static unsigned releases;
static void ReleaseNonce(const view::NonceIdentity& held)noexcept{if(view::SameNonceIdentity(held,property,true)){++releases;property={};}}
static Snapshot Current(){Snapshot s;s.state=State::Prepared;s.sessionId.fill('a');s.sessionId[64]=0;s.generation=1;s.gameId=0x9234567802000000ull;s.expiresAt=2500;
 s.sourceWindow=1;s.sourceCookie=2;s.destinationWindow=3;s.destinationCookie=4;s.ownerPid=5;s.ownerBirth=134389000000000001ull;
 s.gamePid=6;s.gameBirth=134389000000000003ull;s.leaseCurrent=s.preparedCurrent=1;return s;}
namespace lifetime_regression {
struct Clock:IClock{Id now=1000;Id Now()const noexcept override{return now;}};
struct Authority:IAuthority{bool current=true;bool Current(const SourceLease&)override{return current;}bool RendererReady(const SourceLease&)override{return true;}};
struct Backend:IBackend{
 bool created{},shown{},attached{};BandState band=BandState::Pending;
 bool CreateHidden(const SourceLease&)override{created=true;return true;}BandState PollBand()override{return band;}
 bool AttachGpu(const SourceLease&)override{attached=true;return true;}bool ShowNoActivate(const SourceLease&)override{shown=true;return true;}
 bool Current(const SourceLease&)override{return true;}bool Hide()override{shown=false;return true;}bool DetachGpu()override{attached=false;return true;}
 bool RetireBand()override{return true;}bool DestroyOwned()override{created=false;return true;}
 Id Window()const override{return created?10:0;}Id Cookie()const override{return created?11:0;}bool IsShown()const override{return shown;}bool HasThumbnail()const override{return attached;}
};
SourceLease Lease(Id now,Id lifetime){SourceLease l;l.sessionId.fill('a');l.sessionId[64]=0;l.generation=1;l.gameId=0x9234567802000000ull;l.expiresAt=now+lifetime;l.ownerPid=1;l.ownerBirth=2;l.gamePid=3;l.gameBirth=4;l.framePid=5;l.frameBirth=6;l.frameThread=7;l.sourceThread=8;l.session=1;l.gameWindow=20;l.sourceWindow=21;l.sourceCookie=22;l.gameRect=l.sourceRect={0,0,3840,2160};return l;}
struct Fixture{Clock clock;Authority authority;std::shared_ptr<Backend> backend=std::make_shared<Backend>();Presentation p{clock,authority,backend};SourceLease lease;explicit Fixture(Id duration):lease(Lease(clock.now,duration)){Check(p.Begin(lease),"lifetime fixture exact Guide accepted");}};
bool Show(Fixture& f,bool fresh){ControlV1 c;c.action=Action::Show;c.expected=f.p.Read();Id next=f.clock.now+1500;c.requestedExpiresAt=fresh&&next>c.expected.expiresAt?next:0;if(!CommandCurrent(c,f.p.Read(),f.clock.now,1500))return false;if(c.requestedExpiresAt){auto renewed=f.lease;renewed.expiresAt=c.requestedExpiresAt;if(!f.p.Renew(renewed))return false;f.lease=renewed;}f.clock.now+=31;return f.p.ShowPrepared(f.lease);}
void Run(){
 {Fixture f(1500);f.backend->band=BandState::Ready;f.p.Tick();f.clock.now=2391;Check(Show(f,false),"old Show reaches Shown before expiry");Check(f.p.Read().expiresAt-f.clock.now==78,"live78ms remainder reproduced");f.clock.now+=500;f.p.Tick();Check(f.p.Read().state==State::Closed&&!f.backend->shown,"old Show expires before first500ms worker sample");}
 {Fixture f(1500);f.backend->band=BandState::Ready;f.p.Tick();f.clock.now=2391;Check(Show(f,true),"fresh Show admitted only while old lease live");Check(f.p.Read().expiresAt-f.clock.now==1469,"fresh Show preserves full short interval after frame");f.clock.now+=500;f.p.Tick();Check(f.p.Read().state==State::Shown,"fresh Show survives first500ms worker sample");ControlV1 renew;renew.action=Action::Renew;renew.expected=f.p.Read();renew.requestedExpiresAt=f.clock.now+1500;Check(CommandCurrent(renew,f.p.Read(),f.clock.now,1500),"ordinary renewal remains admitted after fresh Show");auto next=f.lease;next.expiresAt=renew.requestedExpiresAt;Check(f.p.Renew(next),"actual Presentation extends same authority");}
 {Fixture f(view::PreparationLifetimeMs);Check(view::FreshPreparationRequest(0,1,f.clock.now,f.lease.expiresAt),"explicit preparation allows fixed5s");Check(!view::FreshRequest(0,1,f.clock.now,f.lease.expiresAt),"regular Arm still rejects5s");f.clock.now+=2500;f.backend->band=BandState::Ready;f.p.Tick();Check(f.p.Read().state==State::Prepared&&!f.backend->shown,"cold admission2500ms prepares without showing");Check(Show(f,true)&&f.p.Read().expiresAt==6000,"early Show cannot shrink or renew initial deadline");}
 {Fixture f(view::PreparationLifetimeMs);f.p.Close();f.clock.now+=2500;f.backend->band=BandState::Ready;f.p.Tick();Check(f.p.Read().state==State::Closed&&!f.backend->attached&&!f.backend->shown,"close before admission prevents late attachment or show");}
 {Fixture f(view::PreparationLifetimeMs);f.authority.current=false;f.clock.now+=2500;f.backend->band=BandState::Ready;f.p.Tick();Check(f.p.Read().state==State::Closed&&!f.backend->shown,"foreign foreground or replaced authority revokes during preparation");}
 {Fixture f(view::PreparationLifetimeMs);f.clock.now=f.lease.expiresAt;f.backend->band=BandState::Ready;f.p.Tick();Check(f.p.Read().state==State::Closed&&!f.backend->shown,"initial hard5s deadline cannot auto-renew");auto later=f.lease;later.expiresAt+=1500;Check(!f.p.Renew(later),"expired preparation cannot revive");}
}
}
int main(){
 lifetime_regression::Run();
 ConfigurationV1 c;c.gameId=Current().gameId;c.sessionId=Current().sessionId;c.leaseDurationMs=2000;
 Check(sizeof(c)==96&&sizeof(ControlV1)==248&&sizeof(Snapshot)==224,"exact V1 sizes");Check(ValidConfiguration(c),"canonical config");
 for(unsigned which=0;which<7;++which){auto bad=c;if(which==0)bad.version=2;if(which==1)--bad.size;if(which==2)bad.gameId=480;if(which==3)bad.sessionId[0]='z';if(which==4)bad.reserved[0]=1;if(which==5)bad.leaseDurationMs=499;if(which==6)bad.leaseDurationMs=2001;Check(!ValidConfiguration(bad),"configuration invalid value");}
 auto s=Current();ControlV1 command;command.expected=s;command.action=Action::Show;
 Check(CommandCurrent(command,s,1000,2000),"prepared show is a receipt, never input authority");
 for(unsigned which=0;which<10;++which){auto changed=s;if(which==0)changed.generation++;if(which==1)changed.gameId++;if(which==2)changed.sourceWindow++;if(which==3)changed.sourceCookie++;if(which==4)changed.destinationWindow++;if(which==5)changed.destinationCookie++;if(which==6)changed.ownerBirth++;if(which==7)changed.gameBirth++;if(which==8)changed.expiresAt++;if(which==9)changed.sessionId[0]='b';Check(!CommandCurrent(command,changed,1000,2000),"stale exact tuple rejected");}
 auto spoof=command;spoof.expected.actualScope=2;spoof.expected.inputAuthority=1;spoof.expected.pixelsVerified=1;Check(CommandCurrent(spoof,s,1000,2000),"untrusted proof bits are ignored, never copied");
 Check(!CommandCurrent(command,s,2500,2000),"expired command refuses");
 command.action=Action::Renew;command.requestedExpiresAt=3000;auto shown=s;shown.state=State::Shown;
 Check(!CommandCurrent(command,s,1000,2000),"prepared never auto-renews before renderer readiness");
 Check(CommandCurrent(command,shown,1000,2000),"bounded shown renewal");
 command.requestedExpiresAt=3001;Check(!CommandCurrent(command,shown,1000,2000),"renew beyond original duration refuses");
 command.requestedExpiresAt=2499;Check(!CommandCurrent(command,shown,1000,2000),"renew cannot shorten or revive");
 command.action=Action::Close;command.requestedExpiresAt=0;auto awaiting=s;awaiting.state=State::AwaitingBand;awaiting.leaseCurrent=0;
 Check(CommandCurrent(command,awaiting,1000,2000),"exact close may revoke preparation without promoting readiness");
 auto pending=s;pending.state=State::CleanupPending;Check(!CommandCurrent(command,pending,1000,2000),"cleanup cannot be reactivated");
 Mailbox box;Check(!box.Queue(command,1000),"unconfigured mailbox cannot queue");Check(box.Configure(c,L"C:/owned/callback.dll",L"C:/owned/runtime"),"reserve config");
 Check(!box.Configure(c,L"C:/other.dll",L"C:/other"),"single reservation per DLL");ConfigurationV1 copied;std::wstring dll,root;
 Check(box.Start(copied,dll,root)&&copied.gameId==c.gameId,"GUI owns configuration copy");Check(!box.Start(copied,dll,root),"second GUI owner refuses");box.Publish(s);
 command.action=Action::Show;Check(box.Queue(command,1000),"queue show");Check(!box.Queue(command,1000),"bounded queue no duplicate");
 command.action=Action::Close;Check(box.Queue(command,1000),"close supersedes only own pending show");
 auto revoked=box.Read(1001);Check(!revoked.leaseCurrent&&!revoked.preparedCurrent&&!revoked.shownCurrent&&!revoked.actualScope&&!revoked.cleanupComplete,"queued close revokes cached capability before GUI cleanup");ControlV1 taken;
 auto advanced=s;advanced.expiresAt=3000;advanced.state=State::Shown;advanced.shownCurrent=1;box.Publish(advanced);
 Check(!box.Read(1001).leaseCurrent,"GUI publish after in-flight renewal cannot undo queued close");
 Check(box.Take(taken)&&taken.action==Action::Close&&!box.Take(taken),"one GUI consumption");
 Check(CommandCurrent(taken,advanced,1001,2000),"same-generation Close survives an in-flight expiry advance");
 auto replaced=advanced;replaced.generation++;Check(!CommandCurrent(taken,replaced,1001,2000),"late Close never closes a new generation");
 auto read=box.Read(3100);Check(!read.leaseCurrent&&!read.preparedCurrent&&!read.shownCurrent&&!read.actualScope,"read expiry revokes all capability bits");
 box.Finish({});Check(!box.Queue(command,1000),"finished mailbox cannot accept work");Check(!box.Start(copied,dll,root),"finished owner cannot be reused as a new runtime session");
 Check(view::RenewableRequest(1,1,1000,2000,2500,true),"same original Guide may renew fresh source view");
 Check(!view::RenewableRequest(1,2,1000,2000,2500,true),"renew cannot mint a new Guide");
 Check(!view::RenewableRequest(1,1,2000,2000,2500,true),"renew cannot revive expired view");
 Check(!view::RenewableRequest(1,1,1000,2000,3001,true),"renew retains core2000ms bound");
 Check(!view::RenewableRequest(1,1,1000,2000,2500,false),"different authority cannot renew");
 Check(!view::RenewableRequest(0,0,1000,2000,2500,true),"revoked view cannot renew");
 Check(RendererReceiptReady(true,2,1,0,true,true,true),"show after receipt, successful new present, Valve open and same routed lease");
 Check(!RendererReceiptReady(true,1,1,0,true,true,true),"old present is not renderer receipt");
 Check(!RendererReceiptReady(true,2,1,0x087a0001,true,true,true),"occluded present does not qualify");
 Check(!RendererReceiptReady(true,2,1,-1,true,true,true),"failed present does not qualify");
 Check(!RendererReceiptReady(false,2,1,0,true,true,true)&&!RendererReceiptReady(true,2,1,0,true,true,false),"missing receipt or changed source refuses show");
 Check(!RendererReceiptReady(true,2,1,0,false,true,true),"blank new draw with Valve closed is never ready");
 Check(!RendererReceiptReady(true,2,1,0,true,false,true),"unrouted or different active renderer lease refuses show");
 property={10,20,30,40};auto oldLease=std::make_shared<view::NonceOwner>(property,ReleaseNonce);auto newLease=oldLease;auto oldDetour=oldLease;
 oldLease.reset();oldDetour.reset();Check(releases==0&&property.nonce==30,"last old detour ref cannot delete nonce shared by renewed active lease");
 newLease.reset();Check(releases==1&&property.nonce==0,"final revocation releases shared nonce once");
 property={10,20,30,40};oldLease=std::make_shared<view::NonceOwner>(property,ReleaseNonce);property.nonce=31;oldLease.reset();
 Check(releases==1&&property.nonce==31,"old nonce cleanup never removes a replacement property");
 const view::NonceIdentity identity{10,20,30,40};
 Check(!view::SameNonceIdentity(identity,{10,21,30,40},true)&&!view::SameNonceIdentity(identity,{10,20,30,41},true)&&!view::SameNonceIdentity(identity,identity,false),"recycled cookie/thread or foreign process refuses property cleanup");
 const view::RenewalSource oldSource{1,2,3,134389000000000001ull,4,true};Check(view::SameRenewalSource(oldSource,oldSource),"same retained core/frame renewal");
 auto replacement=oldSource;replacement.core++;Check(!view::SameRenewalSource(oldSource,replacement),"CoreWindow replacement refuses renewal");
 replacement=oldSource;replacement.birth++;Check(!view::SameRenewalSource(oldSource,replacement),"frame PID reuse refuses renewal");
 replacement=oldSource;replacement.coreThread++;Check(!view::SameRenewalSource(oldSource,replacement),"core thread replacement refuses renewal");
 replacement=oldSource;replacement.hosted=false;Check(!view::SameRenewalSource(oldSource,replacement),"renew never reclassifies AppContainer/Win32");
 std::cout<<"PASS "<<checks<<" pure callsite/API/renewal checks; no Windows/Steam/API/UI actions.\n";
}
