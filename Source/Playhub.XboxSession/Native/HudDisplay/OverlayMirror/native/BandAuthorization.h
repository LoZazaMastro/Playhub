// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "HudBandBroker.h"
#include "CurtainBandBroker.h"
#include <variant>
namespace playhub::mirror {
// Shared physical game proof. Purpose-specific authority remains a real typed
// variant; a HUD never constructs a fake renderer/GameID SourceLease.
struct BandGameProof {
 std::array<wchar_t,513> expectedFrameAumid{};std::array<char,65> sessionId{};
 Id expiresAt{},gameBirth{},frameBirth{},gameWindow{};
 std::uint32_t gamePid{},framePid{},frameThread{},session{};Rect gameRect{};
};
class BandAuthorization {
 std::variant<SourceLease,hud::BandLease,curtain::BandLease> value;
public:
 explicit BandAuthorization(const SourceLease& l):value(l){}
 explicit BandAuthorization(const hud::BandLease& l):value(l){}
 explicit BandAuthorization(const curtain::BandLease& l):value(l){}
 // Legacy admission remains unchanged. The curtain source additionally gates
 // dispatch and post-callback readiness; it is not encoded in callback ABI B582.
 bool CurtainCurrent(Id now)const{return !std::holds_alternative<curtain::BandLease>(value)||Current(now);}
 bool Current(Id now)const{
  // The legacy validator remains exactly the published SourceLease predicate.
  if(const auto* l=std::get_if<SourceLease>(&value))return mirror::Valid(*l,now);
  if(const auto* l=std::get_if<hud::BandLease>(&value))return hud::Valid(*l,now);
  return curtain::Valid(std::get<curtain::BandLease>(value),now);
 }
 BandGameProof Game()const{
  BandGameProof p;
  if(const auto* l=std::get_if<SourceLease>(&value)){
   p.expectedFrameAumid=l->expectedFrameAumid;p.sessionId=l->sessionId;p.expiresAt=l->expiresAt;
   p.gameBirth=l->gameBirth;p.frameBirth=l->frameBirth;p.gameWindow=l->gameWindow;
   p.gamePid=l->gamePid;p.framePid=l->framePid;p.frameThread=l->frameThread;p.session=l->session;p.gameRect=l->gameRect;
  }else if(const auto* held=std::get_if<hud::BandLease>(&value)){
   const auto& h=*held;const auto& a=h.authority;
   p.expectedFrameAumid=h.actualFrameAumid;
   p.sessionId=h.authorizationNonce;p.expiresAt=h.expiresAt;p.gameBirth=a.gameBirth;p.frameBirth=a.frameBirth;
   p.gameWindow=a.gameWindow;p.gamePid=a.gamePid;p.framePid=a.framePid;p.frameThread=a.frameThread;p.session=a.session;
   p.gameRect={a.gameRect.left,a.gameRect.top,a.gameRect.right,a.gameRect.bottom};
  }else{
   const auto& cover=std::get<curtain::BandLease>(value);const auto& a=cover.authority;
   p.expectedFrameAumid=cover.actualFrameAumid;p.sessionId=cover.authorizationNonce;p.expiresAt=cover.expiresAt;
   p.gameBirth=a.gameBirth;p.frameBirth=a.frameBirth;p.gameWindow=a.gameWindow;
   p.gamePid=a.gamePid;p.framePid=a.framePid;p.frameThread=a.frameThread;p.session=a.session;
   p.gameRect={a.gameRect.left,a.gameRect.top,a.gameRect.right,a.gameRect.bottom};
  }
  return p;
 }
};
}
