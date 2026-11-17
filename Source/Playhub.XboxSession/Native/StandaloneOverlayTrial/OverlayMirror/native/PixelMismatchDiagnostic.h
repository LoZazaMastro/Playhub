// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "ShellFreeOutputReceipt.h"
#include <cstdio>
#include <limits>

namespace playhub::mirror::output::diagnostic {
// Diagnostic content only. Nothing here grants presentation/input authority.
// At most twelve existing grid samples are retained, with no image acquisition.
struct Sample {
 unsigned tile{},gx{},gy{},x{},y{},ox{},oy{};
 std::array<unsigned,4> source{},baselineBits{},actualBits{};
 std::array<float,4> baselineNative{},actualNative{};
 std::array<float,3> expectedNative{},expectedLinear{},delta{};
 bool matched{},baselineChanged{},baselineFinite{},actualFinite{};
 float score{-1};
};
struct Report {
 std::uint64_t generation{},sourceQpc{},baselinePresentQpc{},actualPresentQpc{},showQpc{};
 unsigned sourceWidth{},sourceHeight{},outputWidth{},outputHeight{},encoding{},white{},count{};
 std::array<Sample,12> samples{};
};
inline bool ReadOutput(const ImageView& i,unsigned x,unsigned y,unsigned gy,
 std::array<unsigned,4>& bits,std::array<float,4>& native)noexcept {
 const auto row=i.layout==ImageLayout::OutputRows?gy:y;
 const auto* p=i.bgra+std::size_t(row)*i.stride+std::size_t(x)*(i.encoding==PixelEncoding::Bgra8?4u:8u);
 if(i.encoding==PixelEncoding::Bgra8){for(unsigned c=0;c<4;++c){bits[c]=p[c];native[c]=float(p[c]);}return true;}
 std::uint16_t rgba[4]{};std::memcpy(rgba,p,sizeof(rgba));bool finite=true;
 for(unsigned c=0;c<4;++c){const auto at=c<3?2-c:c;bits[c]=rgba[at];const float v=Half(rgba[at]);finite=finite&&std::isfinite(v);native[c]=std::isfinite(v)?v:0.0f;}return finite;
}
inline Report Capture(const ImageView& source,const ImageView& baseline,const ImageView& actual,
 const Binding& expected,const Facts& facts)noexcept {
 Report r{};
 if(!ValidImage(source)||!ValidImage(baseline)||!ValidImage(actual)||!SameBinding(expected,facts.binding)||facts.protectedContent
  ||source.encoding!=PixelEncoding::Bgra8||baseline.width!=actual.width||baseline.height!=actual.height
  ||baseline.encoding!=actual.encoding||baseline.sdrWhiteLevel!=actual.sdrWhiteLevel
  ||(source.layout!=ImageLayout::Full&&source.layout!=ImageLayout::SourceRows)
  ||(baseline.layout!=ImageLayout::Full&&baseline.layout!=ImageLayout::OutputRows)||baseline.layout!=actual.layout
  ||(actual.layout==ImageLayout::OutputRows&&(baseline.grid.sourceWidth!=source.width||baseline.grid.sourceHeight!=source.height
    ||actual.grid.sourceWidth!=source.width||actual.grid.sourceHeight!=source.height)))return r;
 r.generation=expected.generation;r.sourceQpc=facts.sourceQpc;r.baselinePresentQpc=facts.baselinePresentQpc;
 r.actualPresentQpc=facts.framePresentQpc;r.showQpc=facts.showQpc;r.sourceWidth=source.width;r.sourceHeight=source.height;
 r.outputWidth=actual.width;r.outputHeight=actual.height;r.encoding=unsigned(actual.encoding);r.white=actual.sdrWhiteLevel;
 for(unsigned gy=0;gy<GridRows;++gy)for(unsigned gx=0;gx<GridColumns;++gx){
  const unsigned x=GridX(source.width,gx),y=GridY(source.height,gy);const auto* p=SourceSample(source,x,y,gy);
  if(p[3]<250)continue;bool smooth=true;
  for(int dy=-1;dy<=1&&smooth;++dy)for(int dx=-1;dx<=1;++dx){const auto* n=SourceSample(source,x,y,gy,dx,dy);if(n[3]<250||!Near(p,n,4)){smooth=false;break;}}
  if(!smooth)continue;
  const unsigned ox=std::min(actual.width-1,unsigned((std::uint64_t(x)*2+1)*actual.width/(std::uint64_t(source.width)*2)));
  const unsigned oy=std::min(actual.height-1,unsigned((std::uint64_t(y)*2+1)*actual.height/(std::uint64_t(source.height)*2)));
  // Same center-pixel mapping as Validate. Each spatial tile keeps its largest
  // RGB disagreement, so a repeated matching black background cannot hide it.
  const unsigned tile=std::min(3u,unsigned(std::uint64_t(y)*4/source.height))*3+std::min(2u,unsigned(std::uint64_t(x)*3/source.width));
  Sample s{};s.tile=tile;s.gx=gx;s.gy=gy;s.x=x;s.y=y;s.ox=ox;s.oy=oy;
  for(unsigned c=0;c<4;++c)s.source[c]=p[c];
  s.baselineFinite=ReadOutput(baseline,ox,oy,gy,s.baselineBits,s.baselineNative);
  s.actualFinite=ReadOutput(actual,ox,oy,gy,s.actualBits,s.actualNative);
  s.matched=NearOutput(p,actual,ox,oy,8,gy);s.baselineChanged=!NearOutput(p,baseline,ox,oy,16,gy);
  s.score=0;
  for(unsigned c=0;c<3;++c){s.expectedLinear[c]=Linear(p[c])*(float(actual.sdrWhiteLevel)/1000.0f);
   s.expectedNative[c]=actual.encoding==PixelEncoding::Bgra8?float(p[c]):s.expectedLinear[c];
   s.delta[c]=s.actualNative[c]-s.expectedNative[c];s.score=std::max(s.score,std::abs(s.delta[c]));}
  if(!s.actualFinite)s.score=std::numeric_limits<float>::max();
  // Prefer an actual predicate mismatch before comparing the numeric distance.
  auto& prior=r.samples[tile];if(prior.score<0||(!s.matched&&prior.matched)||(s.matched==prior.matched&&s.score>prior.score))prior=s;
 }
 for(const auto& s:r.samples)r.count+=s.score>=0;return r;
}
inline std::size_t Format(const Report& r,char* buffer,std::size_t capacity)noexcept {
 if(!buffer||!capacity||!r.count)return 0;std::size_t used=0;
 const auto append=[&](const char* format,auto... values){if(used>=capacity)return false;const int n=std::snprintf(buffer+used,capacity-used,format,values...);if(n<0||std::size_t(n)>=capacity-used){used=capacity;return false;}used+=std::size_t(n);return true;};
 if(!append("{\"stage\":\"bounded-output-pixel-diagnostic\",\"selection\":\"largest-native-rgb-delta-per-4x3-tile\",\"generation\":%llu,\"sourceQpc\":%llu,\"baselinePresentQpc\":%llu,\"actualPresentQpc\":%llu,\"showQpc\":%llu,\"sourceSize\":[%u,%u],\"outputSize\":[%u,%u],\"outputEncoding\":%u,\"sdrWhiteLevel\":%u,\"channelOrder\":\"BGRA\",\"count\":%u,\"samples\":[",
  static_cast<unsigned long long>(r.generation),static_cast<unsigned long long>(r.sourceQpc),static_cast<unsigned long long>(r.baselinePresentQpc),static_cast<unsigned long long>(r.actualPresentQpc),static_cast<unsigned long long>(r.showQpc),r.sourceWidth,r.sourceHeight,r.outputWidth,r.outputHeight,r.encoding,r.white,r.count))return 0;
 bool comma=false;for(const auto& s:r.samples){if(s.score<0)continue;
  if(!append("%s{\"tile\":%u,\"grid\":[%u,%u],\"sourceXY\":[%u,%u],\"outputXY\":[%u,%u],\"sourceBGRA\":[%u,%u,%u,%u],\"baselineBitsBGRA\":[%u,%u,%u,%u],\"actualBitsBGRA\":[%u,%u,%u,%u],\"baselineNativeBGRA\":[%.7g,%.7g,%.7g,%.7g],\"actualNativeBGRA\":[%.7g,%.7g,%.7g,%.7g],\"expectedNativeBGR\":[%.7g,%.7g,%.7g],\"sourceLinearScaledBGR\":[%.7g,%.7g,%.7g],\"deltaActualMinusExpectedBGR\":[%.7g,%.7g,%.7g],\"matched\":%s,\"baselineChanged\":%s,\"baselineFinite\":%s,\"actualFinite\":%s}",comma?",":"",s.tile,s.gx,s.gy,s.x,s.y,s.ox,s.oy,s.source[0],s.source[1],s.source[2],s.source[3],s.baselineBits[0],s.baselineBits[1],s.baselineBits[2],s.baselineBits[3],s.actualBits[0],s.actualBits[1],s.actualBits[2],s.actualBits[3],s.baselineNative[0],s.baselineNative[1],s.baselineNative[2],s.baselineNative[3],s.actualNative[0],s.actualNative[1],s.actualNative[2],s.actualNative[3],s.expectedNative[0],s.expectedNative[1],s.expectedNative[2],s.expectedLinear[0],s.expectedLinear[1],s.expectedLinear[2],s.delta[0],s.delta[1],s.delta[2],s.matched?"true":"false",s.baselineChanged?"true":"false",s.baselineFinite?"true":"false",s.actualFinite?"true":"false"))return 0;comma=true;
 }
 if(!append("],\"fullFrameSaved\":false,\"grantsAuthority\":false}\n"))return 0;return used;
}
}
