// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "Win32MirrorBackend.h"
#include "HudDisplayLease.h"
namespace playhub::hud {
struct ISourceAuthority {virtual bool Current(const Authority&)const=0;virtual ~ISourceAuthority()=default;};
struct BandLease {
 Authority authority;std::array<char,65> authorizationNonce{};std::array<wchar_t,513> actualFrameAumid{};Id issuedAt{},expiresAt{};
 std::shared_ptr<ISourceAuthority> source;
};
inline bool Valid(const BandLease& l,Id now){
 return hud::Valid(l.authority)&&mirror::CanonicalSession(l.authorizationNonce)&&l.issuedAt&&l.issuedAt<=now
  &&l.expiresAt>now&&l.expiresAt-l.issuedAt<=5000&&l.source&&l.source->Current(l.authority);
}
struct IBand16HudBroker {
 virtual mirror::BandState Begin(const mirror::BandTarget&,const BandLease&)=0;
 virtual mirror::BandState Poll(const mirror::BandTarget&)=0;
 virtual bool Retire(const mirror::BandTarget&)=0;
 virtual mirror::BrokerDiagnostic Diagnostics()const{return {};}
 virtual ~IBand16HudBroker()=default;
};
std::shared_ptr<IBand16HudBroker> CreateHudDisplayBand16Broker(const wchar_t* callbackDll,const wchar_t* privateRuntimeRoot);
}
