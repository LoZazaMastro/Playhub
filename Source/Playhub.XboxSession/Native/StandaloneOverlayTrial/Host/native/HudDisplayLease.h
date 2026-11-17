// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <array>
#include <cstdint>
#include <string>
#include <algorithm>
namespace playhub::hud {
using Id=std::uint64_t;
struct Rect{std::int32_t left{},top{},right{},bottom{};friend bool operator==(const Rect&,const Rect&)=default;};
inline bool RectValid(const Rect& r){return r.right>r.left&&r.bottom>r.top&&std::int64_t(r.right)-r.left<=32768&&std::int64_t(r.bottom)-r.top<=32768;}
inline bool Hex(const std::string& text,std::size_t size){return text.size()==size&&std::all_of(text.begin(),text.end(),[](unsigned char c){return(c>='0'&&c<='9')||(c>='a'&&c<='f')||(c>='A'&&c<='F');});}
enum class Purpose:std::uint32_t{None=0,PerformanceHud=1};
// A display-only authority. There is no Steam GameID, renderer/browser state,
// controller/layout permission or frontend-supplied destination window here.
struct Authority {
 Purpose purpose{Purpose::None};Id generation{},collectorEpoch{};std::string collectorLease,ownerBuild;
 std::uint32_t appId{},agentPid{},gamePid{},framePid{},frameThread{},sourceThread{},session{};
 Id agentBirth{},gameBirth{},frameBirth{},gameWindow{},wpfWindow{},wpfInstance{};
 Rect gameRect{},wpfRect{};std::wstring wpfClass,expectedFrameAumid;
 friend bool operator==(const Authority&,const Authority&)=default;
};
struct Pulse {
 Authority authority;Id sequence{},sampleTick{},observedAt{},leaseExpiresAt{};
 bool enabled{},suspended{},ready{};
};
inline bool Valid(const Authority& a){
 return a.purpose==Purpose::PerformanceHud&&a.generation&&a.collectorEpoch&&Hex(a.collectorLease,32)&&Hex(a.ownerBuild,64)
  &&a.appId&&a.agentPid&&a.agentBirth&&a.gamePid&&a.gameBirth&&a.framePid&&a.frameBirth&&a.frameThread&&a.sourceThread&&a.session
  &&a.agentPid!=a.gamePid&&a.agentPid!=a.framePid
  &&a.gameWindow&&a.wpfWindow&&a.wpfInstance&&a.wpfWindow!=a.gameWindow&&RectValid(a.gameRect)&&RectValid(a.wpfRect)
  &&std::int64_t(a.wpfRect.right)-a.wpfRect.left<=4096&&std::int64_t(a.wpfRect.bottom)-a.wpfRect.top<=2160
  &&a.wpfRect.left>=a.gameRect.left&&a.wpfRect.top>=a.gameRect.top&&a.wpfRect.right<=a.gameRect.right&&a.wpfRect.bottom<=a.gameRect.bottom
  &&a.wpfClass.size()>0&&a.wpfClass.size()<256&&a.wpfClass.find(L"HwndWrapper[")==0
  &&a.wpfClass.find(L"Playhub performance HUD;")!=std::wstring::npos&&a.expectedFrameAumid.size()<=512;
}
inline bool Fresh(const Pulse& p,Id now){
 return Valid(p.authority)&&p.enabled&&!p.suspended&&p.ready&&p.sequence&&p.sampleTick&&p.observedAt
  &&p.sampleTick<=p.observedAt&&p.observedAt<=now&&now-p.sampleTick<500&&now-p.observedAt<500&&p.leaseExpiresAt>now;
}
}
