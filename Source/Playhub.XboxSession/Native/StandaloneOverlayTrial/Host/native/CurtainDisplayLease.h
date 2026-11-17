// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <array>
#include <cstdint>
#include <string>
#include <memory>
#include <algorithm>
namespace playhub::curtain {
using Id=std::uint64_t;
struct Rect {std::int32_t left{},top{},right{},bottom{};friend bool operator==(const Rect&,const Rect&)=default;};
inline bool Rectangle(const Rect& r){return r.right>r.left&&r.bottom>r.top
 &&std::int64_t(r.right)-r.left<=32768&&std::int64_t(r.bottom)-r.top<=32768;}
inline bool Hex(const std::string& s,std::size_t n){return s.size()==n&&std::all_of(s.begin(),s.end(),[](unsigned char c){return(c>='0'&&c<='9')||(c>='a'&&c<='f')||(c>='A'&&c<='F');});}
inline bool ImagePath(const std::wstring& p){return p.size()>3&&p.size()<4096&&p[1]==L':'&&p[2]==L'\\'
 &&((p[0]>=L'A'&&p[0]<=L'Z')||(p[0]>=L'a'&&p[0]<=L'z'));}
enum class Purpose:std::uint32_t {None=0,FirstContentCover=2};
// A display-only lease for an existing Steam-curtain receipt and retained game.
// It has no HUD metrics, controller binding, layout grant or menu/Valve state.
struct Authority {
 Purpose purpose{Purpose::None};Id generation{},requestEpoch{};std::string requestId,sourceReceipt,ownerBuild;
 std::uint32_t appId{},ownerPid{},gamePid{},framePid{},frameThread{},steamPid{},steamThread{},session{};
 Id ownerBirth{},gameBirth{},frameBirth{},gameWindow{},steamBirth{},steamWindow{};
 Rect gameRect{},steamRect{};std::wstring steamClass,steamImage,gameClass,gameImage,expectedFrameAumid;
 friend bool operator==(const Authority&,const Authority&)=default;
};
inline bool Valid(const Authority& a){
 return a.purpose==Purpose::FirstContentCover&&a.generation&&a.requestEpoch
  &&Hex(a.requestId,32)&&Hex(a.sourceReceipt,32)&&Hex(a.ownerBuild,64)
  &&a.appId&&a.ownerPid&&a.ownerBirth&&a.gamePid&&a.gameBirth&&a.framePid&&a.frameBirth&&a.frameThread
  &&a.steamPid&&a.steamBirth&&a.steamThread&&a.session&&a.gameWindow&&a.steamWindow
  &&a.ownerPid!=a.gamePid&&a.ownerPid!=a.framePid&&a.ownerPid!=a.steamPid
  &&a.steamPid!=a.gamePid&&a.steamPid!=a.framePid&&a.gameWindow!=a.steamWindow
  &&Rectangle(a.gameRect)&&Rectangle(a.steamRect)
  &&!a.steamClass.empty()&&a.steamClass.size()<256&&!a.gameClass.empty()&&a.gameClass.size()<256
  &&ImagePath(a.steamImage)&&ImagePath(a.gameImage)
  &&a.expectedFrameAumid.size()<=512
  &&(a.framePid==a.gamePid?(a.frameBirth==a.gameBirth&&a.expectedFrameAumid.empty()):a.expectedFrameAumid.find(L'!')!=std::wstring::npos);
}
struct ISourceAuthority {
 virtual bool Current(const Authority&)const=0;
 virtual Id Now()const noexcept=0;
 virtual ~ISourceAuthority()=default;
};
struct BandLease {
 Authority authority;std::array<char,65> authorizationNonce{};std::array<wchar_t,513> actualFrameAumid{};
 Id issuedAt{},expiresAt{};std::shared_ptr<ISourceAuthority> source;
};
inline bool Valid(const BandLease& l,Id now){
 if(!Valid(l.authority)||!l.source||!l.issuedAt||l.issuedAt>now||l.expiresAt<=now
  ||l.expiresAt-l.issuedAt>5000||l.authorizationNonce[64]!=0
  ||!Hex(std::string(l.authorizationNonce.data(),64),64)||l.actualFrameAumid[512]!=0
  ||std::wstring(l.actualFrameAumid.data())!=l.authority.expectedFrameAumid
  ||!l.source->Current(l.authority))return false;
 const Id after=l.source->Now();return after>=now&&after<l.expiresAt;
}
}
