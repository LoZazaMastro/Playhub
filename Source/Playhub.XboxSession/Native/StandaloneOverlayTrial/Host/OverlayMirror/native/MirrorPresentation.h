// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <array>
#include <cstdint>
#include <memory>
#include <mutex>
namespace playhub::mirror {
using Id=std::uint64_t;
struct Rect {std::int32_t left{},top{},right{},bottom{};};
inline bool Same(Rect a,Rect b){return a.left==b.left&&a.top==b.top&&a.right==b.right&&a.bottom==b.bottom;}
struct SourceLease {
 std::array<wchar_t,513> expectedFrameAumid{};
 std::array<char,65> sessionId{};Id generation{},gameId{},expiresAt{};
 std::uint32_t ownerPid{},gamePid{},framePid{},frameThread{},sourceThread{},session{};
 Id ownerBirth{},gameBirth{},frameBirth{},gameWindow{},sourceWindow{},sourceCookie{};
 Rect gameRect{},sourceRect{};
};
inline bool CanonicalSession(const std::array<char,65>& s){for(unsigned i=0;i<64;i++)if(!((s[i]>='0'&&s[i]<='9')||(s[i]>='a'&&s[i]<='f')))return false;return s[64]==0;}
inline bool ValidRect(Rect r){return r.right>r.left&&r.bottom>r.top&&std::int64_t(r.right)-r.left<=32768&&std::int64_t(r.bottom)-r.top<=32768;}
inline bool Valid(const SourceLease& l,Id now){return CanonicalSession(l.sessionId)&&l.generation&&l.gameId&&(l.gameId&0xffffffffu)==0x02000000u&&(l.gameId>>32)>=0x80000000u&&l.expiresAt>now&&l.ownerPid&&l.ownerBirth&&l.gamePid&&l.gameBirth&&l.framePid&&l.frameBirth&&l.frameThread&&l.sourceThread&&l.session&&l.gameWindow&&l.sourceWindow&&l.sourceWindow!=l.gameWindow&&l.sourceCookie&&ValidRect(l.gameRect)&&ValidRect(l.sourceRect);}
inline bool SameAuthority(const SourceLease& a,const SourceLease& b){return a.expectedFrameAumid==b.expectedFrameAumid&&a.sessionId==b.sessionId&&a.generation==b.generation&&a.gameId==b.gameId&&a.ownerPid==b.ownerPid&&a.ownerBirth==b.ownerBirth&&a.gamePid==b.gamePid&&a.gameBirth==b.gameBirth&&a.framePid==b.framePid&&a.frameBirth==b.frameBirth&&a.frameThread==b.frameThread&&a.sourceThread==b.sourceThread&&a.session==b.session&&a.gameWindow==b.gameWindow&&a.sourceWindow==b.sourceWindow&&a.sourceCookie==b.sourceCookie&&Same(a.gameRect,b.gameRect)&&Same(a.sourceRect,b.sourceRect);}
enum class State:std::uint32_t {Closed,AwaitingBand,Prepared,Shown,CleanupPending};
enum class CloseReason:std::uint32_t {None,Requested,Expired,AuthorityLost,SourceLost,NativeFailure,Disposed};
enum class BandState {Pending,Ready,Refused};
struct Snapshot {
 std::uint32_t version=1,size=sizeof(Snapshot);State state=State::Closed;CloseReason reason=CloseReason::None;
 std::array<char,65> sessionId{};Id generation{},gameId{},expiresAt{},observedAt{},sourceWindow{},destinationWindow{},sourceCookie{},destinationCookie{},ownerBirth{},gameBirth{};
 std::uint32_t ownerPid{},gamePid{},actualScope{};Id readAt{};
 std::uint32_t preparedCurrent{},shownCurrent{},leaseCurrent{},destinationShown{},dwmAttached{},cleanupComplete=1,pixelsVerified{},inputAuthority{};
};
struct IClock {virtual Id Now()const noexcept=0;virtual ~IClock()=default;};
// Trusted companion issuer, not a public boolean/IPC authorization endpoint.
struct IAuthority {virtual bool Current(const SourceLease&)=0;virtual bool RendererReady(const SourceLease&)=0;virtual ~IAuthority()=default;};
struct IBackend {
 virtual bool CreateHidden(const SourceLease&)=0;
 virtual BandState PollBand()=0;
 virtual bool AttachGpu(const SourceLease&)=0;
 virtual bool ShowNoActivate(const SourceLease&)=0;
 virtual bool Current(const SourceLease&)=0;
 virtual bool Hide()=0;
 virtual bool DetachGpu()=0;
 // Revoke/retire callback; false means the exact hidden HWND must be retained.
 virtual bool RetireBand()=0;
 virtual bool DestroyOwned()=0;
 virtual Id Window()const=0;virtual Id Cookie()const=0;virtual bool IsShown()const=0;virtual bool HasThumbnail()const=0;
 virtual ~IBackend()=default;
};
class Presentation final {
 IClock& clock;IAuthority& authority;std::shared_ptr<IBackend> backend;
 SourceLease lease{};Id lastGeneration{},revision{};bool requested{},driving{},disposed{};State state=State::Closed;CloseReason reason=CloseReason::None;
 mutable std::mutex snapshotMutex;Snapshot snapshot{};
 void Publish(bool shown=false){Snapshot next;next.state=state;next.reason=reason;next.observedAt=clock.Now();next.cleanupComplete=state==State::Closed;next.sessionId=lease.sessionId;next.generation=lease.generation;next.gameId=lease.gameId;next.expiresAt=lease.expiresAt;next.sourceWindow=lease.sourceWindow;next.sourceCookie=lease.sourceCookie;next.destinationWindow=backend->Window();next.destinationCookie=backend->Cookie();next.ownerPid=lease.ownerPid;next.ownerBirth=lease.ownerBirth;next.gamePid=lease.gamePid;next.gameBirth=lease.gameBirth;next.preparedCurrent=requested&&shown&&(state==State::Prepared||state==State::Shown)&&clock.Now()<lease.expiresAt;next.shownCurrent=next.preparedCurrent&&state==State::Shown;next.leaseCurrent=next.preparedCurrent;next.destinationShown=backend->IsShown();next.dwmAttached=backend->HasThumbnail();next.actualScope=next.leaseCurrent?1:0;std::lock_guard<std::mutex> lock(snapshotMutex);snapshot=next;}
 bool Still(Id expected){return requested&&revision==expected&&Valid(lease,clock.Now())&&authority.Current(lease)&&backend->Current(lease)&&requested&&revision==expected&&clock.Now()<lease.expiresAt;}
 void Cleanup(){bool hidden=backend->Hide(),detached=backend->DetachGpu(),retired=backend->RetireBand();bool destroyed=hidden&&detached&&retired&&backend->DestroyOwned();state=destroyed?State::Closed:State::CleanupPending;Publish();}
 void Fail(CloseReason r){requested=false;++revision;reason=r;Publish();}
public:
 Presentation(IClock& c,IAuthority& a,std::shared_ptr<IBackend> b):clock(c),authority(a),backend(std::move(b)){}
 Presentation(const Presentation&)=delete;Presentation& operator=(const Presentation&)=delete;
 ~Presentation(){Close(CloseReason::Disposed);}
 bool Begin(const SourceLease& request){
  if(disposed||driving||state!=State::Closed||!Valid(request,clock.Now())||request.generation<=lastGeneration)return false;
  // The issuer getter may pump/reenter Close. Reserve mutation first, then
  // reject every changed/disposed generation before creating any native target.
  driving=true;Id reservation=revision;bool authorized=false;try{authorized=authority.Current(request);}catch(...){authorized=false;}
  if(!authorized||disposed||revision!=reservation||state!=State::Closed||clock.Now()>=request.expiresAt){driving=false;if(revision!=reservation&&!requested)Cleanup();return false;}
  lease=request;lastGeneration=request.generation;requested=true;reason=CloseReason::None;state=State::AwaitingBand;Id own=++revision;Publish();
  bool created=backend->CreateHidden(lease);if(!created||!Still(own))Fail(created?CloseReason::AuthorityLost:CloseReason::NativeFailure);
  driving=false;if(!requested)Cleanup();else Publish();return requested;
 }
 bool Renew(const SourceLease& request){
  if(disposed||driving||!requested||!SameAuthority(lease,request)||request.expiresAt<lease.expiresAt||!Valid(lease,clock.Now())||!Valid(request,clock.Now()))return false;
  Id own=revision;driving=true;bool current=authority.Current(request)&&backend->Current(request)&&requested&&revision==own&&clock.Now()<lease.expiresAt&&clock.Now()<request.expiresAt;
  if(current){lease.expiresAt=request.expiresAt;Publish(state==State::Prepared||state==State::Shown);}driving=false;if(!requested)Cleanup();return current;
 }
 void Tick(){
  if(driving)return;if(!requested){if(state==State::CleanupPending)Cleanup();return;}
  driving=true;Id own=revision;
  if(!Still(own))Fail(clock.Now()>=lease.expiresAt?CloseReason::Expired:CloseReason::SourceLost);
  else if(state==State::AwaitingBand){auto band=backend->PollBand();
   if(!Still(own))Fail(CloseReason::AuthorityLost);
   else if(band==BandState::Refused)Fail(CloseReason::NativeFailure);
   else if(band==BandState::Ready){bool attached=backend->AttachGpu(lease);if(!attached||!Still(own))Fail(CloseReason::NativeFailure);else{state=State::Prepared;Publish(true);}}
  }else if(!backend->HasThumbnail()||(state==State::Shown&&!backend->IsShown()))Fail(CloseReason::NativeFailure);else Publish(true);
  driving=false;if(!requested)Cleanup();
 }
 // Prepared may authorize composition Set2; it never authorizes input. A
 // separately attested renderer-ready receipt is required before showing.
 bool ShowPrepared(const SourceLease& request){
  if(disposed||driving||!requested||state!=State::Prepared||!SameAuthority(lease,request))return false;
  driving=true;Id own=revision;bool ready=Still(own)&&authority.RendererReady(lease)&&Still(own);
  if(ready){bool shown=backend->ShowNoActivate(lease);if(!shown||!Still(own))Fail(CloseReason::NativeFailure);else{state=State::Shown;Publish(true);}}
  driving=false;if(!requested)Cleanup();return requested&&state==State::Shown;
 }
 void Close(CloseReason r=CloseReason::Requested){if(r==CloseReason::Disposed)disposed=true;Fail(r);if(!driving)Cleanup();}
 Snapshot Read()const {std::lock_guard<std::mutex> lock(snapshotMutex);auto copy=snapshot;copy.readAt=clock.Now();if(copy.readAt>=copy.expiresAt){copy.preparedCurrent=0;copy.shownCurrent=0;copy.leaseCurrent=false;copy.actualScope=0;}return copy;}
};
}
