// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "ShellFreeOutputReceipt.h"
namespace playhub::mirror::output {
// This observes an already visible authenticated Valve source. It never grants
// own-mirror presentation or input; the original changed-baseline Validate stays exact.
struct NativeVisibleBinding {
 std::uint64_t generation{},sourceWindow{},sourceCookie{},gameWindow{},gameBirth{},monitor{},adapterLuid{};
};
inline bool SameNativeBinding(const NativeVisibleBinding&a,const NativeVisibleBinding&b){
 return a.generation==b.generation&&a.sourceWindow==b.sourceWindow&&a.sourceCookie==b.sourceCookie&&a.gameWindow==b.gameWindow
  &&a.gameBirth==b.gameBirth&&a.monitor==b.monitor&&a.adapterLuid==b.adapterLuid;
}
// minimumFrameQpc==0 is only initial existing-source observation. The caller
// must keep its owned target hidden. Cleanup/renewal pass the actual completed
// cleanup or previous physical-frame QPC and still require a newer frame.
struct NativeVisibleFacts {
 NativeVisibleBinding binding{};std::uint64_t sourceQpc{},minimumFrameQpc{},framePresentQpc{},frameAcquiredQpc{},nowQpc{},qpcFrequency{};
 bool protectedContent{},rotationIdentity{};
};
// Color diversity is not visibility: an authenticated monochrome menu can
// have dark or localized text. Match every opaque color group independently,
// then prove disjoint matched opaque references or a distributed relative
// contrast mask. A dominant background
// cannot hide missing glyphs in the aggregate 95% pixel-match result.
inline bool ValidFootprintOutputSample(const ImageView& image,unsigned x,unsigned y,unsigned gridRow){
 if(!ValidImage(image)||x>=image.width||y>=image.height||image.layout==ImageLayout::SourceRows)return false;
 if(image.layout==ImageLayout::OutputRows&&(gridRow>=GridRows||OutputGridY(image.grid.sourceHeight,image.height,gridRow)!=y))return false;
 const auto storedY=image.layout==ImageLayout::OutputRows?gridRow:y;
 const auto* p=image.bgra+std::size_t(storedY)*image.stride+std::size_t(x)*(image.encoding==PixelEncoding::Bgra8?4u:8u);
 if(image.encoding==PixelEncoding::Bgra8)return p[3]>=250;
 std::uint16_t rgba[4]{};std::memcpy(rgba,p,sizeof(rgba));const float alpha=Half(rgba[3]);
 return std::isfinite(alpha)&&alpha>=0.98f&&alpha<=1.02f
  &&std::isfinite(Half(rgba[0]))&&std::isfinite(Half(rgba[1]))&&std::isfinite(Half(rgba[2]));
}
// Cached scalar diagnostics only: no RGB values, pixels, authority or clocks.
// Stages:0 not run,1 opaque scan,2 group overflow,3 group mismatch,
// 4 anchor/control scan,5 accepted controls,6 no qualifying footprint,
// 7 matched opaque contrast certificate. failedGroup is1-based.
// Reject bits:1 count,2 horizontal span,4 vertical span,8 output contrast;
// control bit16 means not evaluated because that anchor was ineligible.
struct NativeSpatialGroupDiagnostic{unsigned count{},matched{},spanX{},spanY{},anchorReject{},controls{},contrasting{},controlSpanX{},controlSpanY{},controlReject{16};};
struct NativeSpatialDiagnostic{unsigned stage{},groupCount{},failedGroup{},testedGroups{};std::array<NativeSpatialGroupDiagnostic,3> groups{};};
inline bool MatchesMonochromeFootprint(const ImageView& source,const ImageView& actual,NativeSpatialDiagnostic* diagnostic=nullptr){
 if(diagnostic)*diagnostic={};
 struct Group{unsigned color{},count{},matched{},minX{},maxX{},minY{},maxY{};std::array<std::uint8_t,4> reference{};bool referenceMatched{};};
 std::array<Group,3> groups{};unsigned groupCount{};
 const auto record=[&](unsigned stage,unsigned failedGroup=0){if(!diagnostic)return;
  diagnostic->stage=stage;diagnostic->groupCount=groupCount;diagnostic->failedGroup=failedGroup;
  for(unsigned i=0;i<groupCount;++i){const auto& g=groups[i];auto& d=diagnostic->groups[i];
   d.count=g.count;d.matched=g.matched;d.spanX=g.count?g.maxX-g.minX:0;d.spanY=g.count?g.maxY-g.minY:0;
   d.anchorReject=(g.count<16?1u:0u)|(d.spanX*100ull<source.width*8ull?2u:0u)|(d.spanY*100ull<source.height*8ull?4u:0u);
  }
 };
 record(1);
 const auto smooth=[&](unsigned x,unsigned y,unsigned gy,const std::uint8_t* s,bool opaque){
  for(int dy=-1;dy<=1;++dy)for(int dx=-1;dx<=1;++dx){const auto* p=SourceSample(source,x,y,gy,dx,dy);
   if(!Near(s,p,4)||(opaque?p[3]<250:p[3]>200||p[3]==0))return false;}return true;};
 const auto spread=[&](unsigned count,unsigned minX,unsigned maxX,unsigned minY,unsigned maxY){
  return count>=16&&(maxX-minX)*100ull>=source.width*8ull&&(maxY-minY)*100ull>=source.height*8ull;};
 for(unsigned gy=0;gy<GridRows;++gy)for(unsigned gx=0;gx<GridColumns;++gx){
  const auto x=GridX(source.width,gx),y=GridY(source.height,gy);const auto* s=SourceSample(source,x,y,gy);
  if(s[3]<250||!smooth(x,y,gy,s,true))continue;
  const unsigned color=(s[0]>>4)|((s[1]>>4)<<4)|((s[2]>>4)<<8);unsigned index{};
  while(index<groupCount&&groups[index].color!=color)++index;
  if(index==groupCount){if(groupCount==groups.size()){record(2);return false;}auto& g=groups[groupCount++];g.color=color;g.minX=x;g.minY=y;std::copy_n(s,4,g.reference.begin());}
  auto& g=groups[index];++g.count;g.minX=std::min(g.minX,x);g.maxX=std::max(g.maxX,x);g.minY=std::min(g.minY,y);g.maxY=std::max(g.maxY,y);
  const unsigned ox=std::min(actual.width-1,unsigned((std::uint64_t(x)*2+1)*actual.width/(std::uint64_t(source.width)*2))),oy=std::min(actual.height-1,unsigned((std::uint64_t(y)*2+1)*actual.height/(std::uint64_t(source.height)*2)));
  const bool matched=ValidFootprintOutputSample(actual,ox,oy,gy)&&NearOutput(s,actual,ox,oy,8,gy);
  g.matched+=matched;if(g.count==1)g.referenceMatched=matched;
 }
 // Every observed group must match, including a sparse contrasting group that
 // would fit inside the aggregate mismatch allowance. No group is output-picked.
 for(unsigned i=0;i<groupCount;++i)if(groups[i].matched*100ull<groups[i].count*95ull){record(3,i+1);return false;}
 // Two SOURCE-first opaque reference samples with disjoint RGB tolerance
 // intervals independently matched the actual output. A rare glyph may have
 // only one sample; its whole group still satisfies the unchanged95% rule.
 // Require a distributed large anchor. The caller separately retains the
 // global16opaque/all4bands/8%span/95% rule and real renewal barrier.
 for(unsigned i=0;i<groupCount;++i){const auto& g=groups[i];
  if(!g.referenceMatched||!spread(g.count,g.minX,g.maxX,g.minY,g.maxY))continue;
  for(unsigned j=0;j<groupCount;++j)if(j!=i&&groups[j].referenceMatched
    &&!Near(g.reference.data(),groups[j].reference.data(),16)){record(7);return true;}
 }
 record(4);
 for(unsigned i=0;i<groupCount;++i){const auto& g=groups[i];
  if(!spread(g.count,g.minX,g.maxX,g.minY,g.maxY))continue;
  if(diagnostic)++diagnostic->testedGroups;
  unsigned controls{},contrasting{},minX=source.width,maxX{},minY=source.height,maxY{};
  for(unsigned gy=0;gy<GridRows;++gy)for(unsigned gx=0;gx<GridColumns;++gx){
   const auto x=GridX(source.width,gx),y=GridY(source.height,gy);const auto* s=SourceSample(source,x,y,gy);
   const bool opaque=s[3]>=250,translucent=s[3]>0&&s[3]<=200;
   if((!opaque&&!translucent)||Near(s,g.reference.data(),32)||!smooth(x,y,gy,s,opaque))continue;
   ++controls;minX=std::min(minX,x);maxX=std::max(maxX,x);minY=std::min(minY,y);maxY=std::max(maxY,y);
   const unsigned ox=std::min(actual.width-1,unsigned((std::uint64_t(x)*2+1)*actual.width/(std::uint64_t(source.width)*2))),oy=std::min(actual.height-1,unsigned((std::uint64_t(y)*2+1)*actual.height/(std::uint64_t(source.height)*2)));
   // Opaque controls match their actual source color. Translucent controls can
   // contain the game beneath them, so only their contrasting mask is asserted.
   contrasting+=ValidFootprintOutputSample(actual,ox,oy,gy)&&(!opaque||NearOutput(s,actual,ox,oy,8,gy))&&!NearOutput(g.reference.data(),actual,ox,oy,32,gy);
  }
  if(diagnostic){auto& d=diagnostic->groups[i];d.controls=controls;d.contrasting=contrasting;d.controlSpanX=controls?maxX-minX:0;d.controlSpanY=controls?maxY-minY:0;
   d.controlReject=(controls<16?1u:0u)|(d.controlSpanX*100ull<source.width*8ull?2u:0u)|(d.controlSpanY*100ull<source.height*8ull?4u:0u)|(contrasting*100ull<controls*95ull?8u:0u);
  }
  if(spread(controls,minX,maxX,minY,maxY)&&contrasting*100ull>=controls*95ull){record(5);return true;}
 }
 record(6);return false;
}
// Diagnostic only: never participates in acceptance, renewal or input.
inline const char* ExplainNativeVisibleFailure(const ImageView& source,const ImageView& actual,const NativeVisibleBinding& expected,const NativeVisibleFacts& f,const Metrics& metrics){
 if(!ValidImage(source))return "source-image-invalid";
 if(!ValidImage(actual))return "output-image-invalid";
 if(!SameNativeBinding(expected,f.binding))return "binding-mismatch";
 if(source.encoding!=PixelEncoding::Bgra8)return "source-encoding";
 if(source.layout!=ImageLayout::Full&&source.layout!=ImageLayout::SourceRows)return "source-layout";
 if(actual.layout!=ImageLayout::Full&&actual.layout!=ImageLayout::OutputRows)return "output-layout";
 if(actual.layout==ImageLayout::OutputRows&&(actual.grid.sourceWidth!=source.width||actual.grid.sourceHeight!=source.height))return "output-grid-source-dimensions";
 if(!expected.generation||!expected.sourceWindow||!expected.sourceCookie||!expected.gameWindow||!expected.gameBirth||!expected.monitor||!expected.adapterLuid)return "identity-missing";
 if(!f.rotationIdentity)return "output-rotation";
 if(f.protectedContent)return "protected-content";
 if(!f.qpcFrequency)return "qpc-frequency-zero";
 if(!f.sourceQpc||!f.framePresentQpc||!f.frameAcquiredQpc||!f.nowQpc)return "qpc-missing";
 if(f.minimumFrameQpc&&f.minimumFrameQpc>=f.framePresentQpc)return "frame-not-after-barrier";
 if(f.sourceQpc>f.framePresentQpc)return "source-newer-than-frame";
 if(f.framePresentQpc>f.frameAcquiredQpc)return "frame-newer-than-acquisition";
 if(f.frameAcquiredQpc>f.nowQpc)return "acquisition-in-future";
 const auto fresh=[&](std::uint64_t time){return time<=f.nowQpc&&f.nowQpc-time<=f.qpcFrequency/4;};
 if(!fresh(f.sourceQpc))return "source-stale-or-future";
 if(!fresh(f.framePresentQpc))return "frame-stale-or-future";
 if(!fresh(f.frameAcquiredQpc))return "acquisition-stale-or-future";
 if(f.minimumFrameQpc&&!fresh(f.minimumFrameQpc))return "barrier-stale-or-future";
 if(metrics.samples<16)return "insufficient-opaque-smooth-source-samples";
 if(metrics.colorCount<4)return "insufficient-source-colors";
 if(metrics.yBands!=15)return "insufficient-source-bands";
 if(metrics.matched*100ull<metrics.samples*95ull)return "output-pixel-match";
 return "insufficient-source-horizontal-spread";
}
inline bool ValidateNativeVisible(const ImageView& source,const ImageView& actual,const NativeVisibleBinding& expected,const NativeVisibleFacts& f,Metrics* metrics=nullptr,NativeSpatialDiagnostic* diagnostic=nullptr){
 if(metrics)*metrics={};
 if(diagnostic)*diagnostic={};
 if(!ValidImage(source)||!ValidImage(actual)||!SameNativeBinding(expected,f.binding)||source.encoding!=PixelEncoding::Bgra8)return false;
 if((source.layout!=ImageLayout::Full&&source.layout!=ImageLayout::SourceRows)||(actual.layout!=ImageLayout::Full&&actual.layout!=ImageLayout::OutputRows))return false;
 if(actual.layout==ImageLayout::OutputRows&&(actual.grid.sourceWidth!=source.width||actual.grid.sourceHeight!=source.height))return false;
 if(!expected.generation||!expected.sourceWindow||!expected.sourceCookie||!expected.gameWindow||!expected.gameBirth||!expected.monitor||!expected.adapterLuid||!f.rotationIdentity||f.protectedContent||!f.qpcFrequency)return false;
 if(!f.sourceQpc||!f.framePresentQpc||!f.frameAcquiredQpc||!f.nowQpc||(f.minimumFrameQpc&&f.minimumFrameQpc>=f.framePresentQpc)||f.sourceQpc>f.framePresentQpc||f.framePresentQpc>f.frameAcquiredQpc||f.frameAcquiredQpc>f.nowQpc)return false;
 const auto fresh=[&](std::uint64_t time){return time<=f.nowQpc&&f.nowQpc-time<=f.qpcFrequency/4;};
 if(!fresh(f.sourceQpc)||!fresh(f.framePresentQpc)||!fresh(f.frameAcquiredQpc)||(f.minimumFrameQpc&&!fresh(f.minimumFrameQpc)))return false;
 unsigned samples=0,matched=0,yBands=0,minX=source.width,maxX=0;std::array<bool,4096> colors{};unsigned colorCount=0;
 for(unsigned gy=0;gy<GridRows;++gy)for(unsigned gx=0;gx<GridColumns;++gx){
  const unsigned x=GridX(source.width,gx),y=GridY(source.height,gy);const auto* s=SourceSample(source,x,y,gy);
  if(s[3]<250)continue;bool smooth=true;for(int dy=-1;dy<=1&&smooth;++dy)for(int dx=-1;dx<=1;++dx){const auto* p=SourceSample(source,x,y,gy,dx,dy);if(p[3]<250||!Near(s,p,4)){smooth=false;break;}}if(!smooth)continue;
  const unsigned ox=std::min(actual.width-1,unsigned((std::uint64_t(x)*2+1)*actual.width/(std::uint64_t(source.width)*2))),oy=std::min(actual.height-1,unsigned((std::uint64_t(y)*2+1)*actual.height/(std::uint64_t(source.height)*2)));
  ++samples;yBands|=1u<<std::min(3u,unsigned(std::uint64_t(y)*4/source.height));minX=std::min(minX,x);maxX=std::max(maxX,x);
  const unsigned color=(s[0]>>4)|((s[1]>>4)<<4)|((s[2]>>4)<<8);if(!colors[color]){colors[color]=true;++colorCount;}
  if(NearOutput(s,actual,ox,oy,8,gy))++matched;
 }
 if(metrics)*metrics={samples,matched,0,colorCount,yBands,0};
 const bool informative=colorCount>=4||(f.minimumFrameQpc&&MatchesMonochromeFootprint(source,actual,diagnostic));
 return samples>=16&&informative&&yBands==15&&(maxX-minX)*100ull>=source.width*8ull&&matched*100ull>=samples*95ull;
}
}

