// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "ExactSampleLayout.h"
#include <array>
#include <cstdint>
#include <cstddef>
#include <algorithm>
#include <cmath>
#include <cstring>
namespace playhub::mirror::output {
enum class PixelEncoding : std::uint32_t {Bgra8,ScRgbFloat16};
struct ImageView {std::uint32_t width{},height{},stride{};const std::uint8_t* bgra{};std::size_t bytes{};PixelEncoding encoding{};std::uint32_t sdrWhiteLevel{1000};ImageLayout layout{};SampleGrid grid{};};
struct Binding {std::uint64_t generation{},sourceWindow{},sourceCookie{},destinationWindow{},destinationCookie{},monitor{},adapterLuid{};};
inline bool SameBinding(const Binding&a,const Binding&b){return a.generation==b.generation&&a.sourceWindow==b.sourceWindow&&a.sourceCookie==b.sourceCookie&&a.destinationWindow==b.destinationWindow&&a.destinationCookie==b.destinationCookie&&a.monitor==b.monitor&&a.adapterLuid==b.adapterLuid;}
struct Facts {Binding binding{};std::uint64_t sourceQpc{},baselinePresentQpc{},baselineAcquiredQpc{},showQpc{},framePresentQpc{},frameAcquiredQpc{},nowQpc{},qpcFrequency{};bool protectedContent{},rotationIdentity{};};
struct Metrics {unsigned samples{},matched{},changed{},colorCount{},yBands{},changedBands{};};
inline bool ValidImage(const ImageView&i){const auto bpp=i.encoding==PixelEncoding::Bgra8?4u:i.encoding==PixelEncoding::ScRgbFloat16?8u:0u;
 const auto rows=StoredRows(i.layout,i.height);const bool layout=i.layout==ImageLayout::Full?i.grid.sourceWidth==0&&i.grid.sourceHeight==0:i.layout==ImageLayout::SourceRows?i.encoding==PixelEncoding::Bgra8&&i.grid.sourceWidth==i.width&&i.grid.sourceHeight==i.height:i.layout==ImageLayout::OutputRows&&ValidGrid(i.grid);
 return bpp&&layout&&rows&&i.sdrWhiteLevel>0&&i.sdrWhiteLevel<=1000000&&i.bgra&&i.width>=32&&i.height>=32&&i.width<=16384&&i.height<=16384&&i.stride>=i.width*std::uint64_t(bpp)&&i.stride<=16384ull*bpp&&i.bytes>=std::size_t(i.stride)*rows;
}
inline bool Near(const std::uint8_t*a,const std::uint8_t*b,unsigned tolerance=8){for(unsigned c=0;c<3;++c)if(unsigned(a[c]>b[c]?a[c]-b[c]:b[c]-a[c])>tolerance)return false;return true;}
inline float Half(std::uint16_t bits){const unsigned exponent=(bits>>10)&31u,mantissa=bits&1023u;float v=exponent==31?INFINITY:exponent==0?std::ldexp(float(mantissa),-24):std::ldexp(float(1024u+mantissa),int(exponent)-25);if(exponent==31&&mantissa)v=NAN;return bits&0x8000u?-v:v;}
inline float Linear(unsigned value){const float v=float(value)/255.0f;return v<=0.04045f?v/12.92f:std::pow((v+0.055f)/1.055f,2.4f);}
// Windows scRGB SDR UI is linear(sRGB) * (SDRWhiteLevel / 1000).
// Compare only distributed samples in this native domain; HDR highlights are
// never clipped/tone-mapped into SDR white and cannot create a false match.
inline bool NearOutput(const std::uint8_t* source,const ImageView& image,unsigned x,unsigned y,unsigned tolerance=8,unsigned gridRow=GridRows){
 if(image.layout==ImageLayout::SourceRows)return false;
 if(image.layout==ImageLayout::OutputRows&&(gridRow>=GridRows||OutputGridY(image.grid.sourceHeight,image.height,gridRow)!=y))return false;
 const auto storedY=image.layout==ImageLayout::OutputRows?gridRow:y;
 const auto* p=image.bgra+std::size_t(storedY)*image.stride+std::size_t(x)*(image.encoding==PixelEncoding::Bgra8?4u:8u);
 if(image.encoding==PixelEncoding::Bgra8)return Near(source,p,tolerance);
 std::uint16_t rgba[4]{};std::memcpy(rgba,p,sizeof(rgba));const float alpha=Half(rgba[3]);if(!std::isfinite(alpha)||alpha<0.98f||alpha>1.02f)return false;
 const float scale=float(image.sdrWhiteLevel)/1000.0f;
 for(unsigned c=0;c<3;++c){const float observed=Half(rgba[2-c]);if(!std::isfinite(observed))return false;const unsigned low=source[c]>tolerance?source[c]-tolerance:0u,high=std::min(255u,unsigned(source[c])+tolerance);const float lo=Linear(low)*scale,hi=Linear(high)*scale;const float roundoff=std::max(0.0000001f,std::max(std::abs(lo),std::abs(hi))/1024.0f);if(observed<lo-roundoff||observed>hi+roundoff)return false;}
 return true;
}
inline const std::uint8_t* Pixel(const ImageView&i,unsigned x,unsigned y){return i.bgra+std::size_t(y)*i.stride+std::size_t(x)*4;}
inline const std::uint8_t* SourceSample(const ImageView&i,unsigned x,unsigned y,unsigned gridRow,int dx=0,int dy=0){const auto row=i.layout==ImageLayout::SourceRows?unsigned(int(gridRow*3+1)+dy):unsigned(int(y)+dy);return i.bgra+std::size_t(row)*i.stride+std::size_t(unsigned(int(x)+dx))*4;}
// No authority is derived from HWND visibility, band or a successful GPU copy.
// The SAME distributed, opaque source samples must differ from pre-show output
// and match a genuinely newer physical desktop frame. Raw frames stay in memory.
inline bool Validate(const ImageView& source,const ImageView& baseline,const ImageView& actual,const Binding& expected,const Facts& f,Metrics* metrics=nullptr){
 if(metrics)*metrics={};
 if(!ValidImage(source)||!ValidImage(baseline)||!ValidImage(actual)||baseline.width!=actual.width||baseline.height!=actual.height||!SameBinding(expected,f.binding)||source.encoding!=PixelEncoding::Bgra8||baseline.encoding!=actual.encoding||baseline.sdrWhiteLevel!=actual.sdrWhiteLevel)return false;
 if((source.layout!=ImageLayout::Full&&source.layout!=ImageLayout::SourceRows)||(baseline.layout!=ImageLayout::Full&&baseline.layout!=ImageLayout::OutputRows)||baseline.layout!=actual.layout)return false;
 if(actual.layout==ImageLayout::OutputRows&&(baseline.grid.sourceWidth!=source.width||baseline.grid.sourceHeight!=source.height||actual.grid.sourceWidth!=source.width||actual.grid.sourceHeight!=source.height))return false;
 if(!expected.generation||!expected.sourceWindow||!expected.sourceCookie||!expected.destinationWindow||!expected.destinationCookie||!expected.monitor||!expected.adapterLuid||!f.rotationIdentity||f.protectedContent||!f.qpcFrequency)return false;
 if(!f.sourceQpc||!f.baselinePresentQpc||!f.baselineAcquiredQpc||!f.showQpc||!f.framePresentQpc||!f.frameAcquiredQpc||!f.nowQpc)return false;
 if(f.baselinePresentQpc>f.baselineAcquiredQpc||f.baselineAcquiredQpc>=f.showQpc||f.showQpc-f.baselineAcquiredQpc>f.qpcFrequency/4||f.sourceQpc>f.framePresentQpc||f.framePresentQpc<=f.showQpc||f.framePresentQpc>f.frameAcquiredQpc||f.frameAcquiredQpc>f.nowQpc||f.sourceQpc>f.nowQpc)return false;
 const auto fresh=[&](std::uint64_t time){return time<=f.nowQpc&&(f.nowQpc-time)<=f.qpcFrequency/4;};
 if(!fresh(f.sourceQpc)||!fresh(f.framePresentQpc)||!fresh(f.frameAcquiredQpc))return false;
 unsigned samples=0,matched=0,changed=0,yBands=0,changedBands=0,minX=source.width,maxX=0;std::array<bool,4096> colors{};unsigned colorCount=0;
 for(unsigned gy=0;gy<GridRows;++gy)for(unsigned gx=0;gx<GridColumns;++gx){
  const unsigned x=GridX(source.width,gx),y=GridY(source.height,gy);const auto*s=SourceSample(source,x,y,gy);
  if(s[3]<250)continue;bool smooth=true;for(int dy=-1;dy<=1&&smooth;++dy)for(int dx=-1;dx<=1;++dx){auto*p=SourceSample(source,x,y,gy,dx,dy);if(p[3]<250||!Near(s,p,4)){smooth=false;break;}}if(!smooth)continue;
  const unsigned ox=std::min(actual.width-1,unsigned((std::uint64_t(x)*2+1)*actual.width/(std::uint64_t(source.width)*2))),oy=std::min(actual.height-1,unsigned((std::uint64_t(y)*2+1)*actual.height/(std::uint64_t(source.height)*2)));
  ++samples;const unsigned band=std::min(3u,unsigned(std::uint64_t(y)*4/source.height));yBands|=1u<<band;minX=std::min(minX,x);maxX=std::max(maxX,x);
  const unsigned color=(s[0]>>4)|((s[1]>>4)<<4)|((s[2]>>4)<<8);if(!colors[color]){colors[color]=true;++colorCount;}
  if(NearOutput(s,actual,ox,oy,8,gy))++matched;
  if(!NearOutput(s,baseline,ox,oy,16,gy)){++changed;changedBands|=1u<<band;}
 }
 if(metrics)*metrics={samples,matched,changed,colorCount,yBands,changedBands};
 return samples>=16&&colorCount>=4&&yBands==15&&changedBands==15&&(maxX-minX)*100ull>=source.width*8ull&&changed*2ull>=samples&&matched*100ull>=samples*95ull;
}
// Scheduling eligibility only: the unchanged physical-output Validate remains
// mandatory. A translucent/fading source stays Prepared while normal Draw runs.
inline bool SourceCanSatisfyOutputProof(const ImageView& source){
 if(!ValidImage(source)||source.encoding!=PixelEncoding::Bgra8||(source.layout!=ImageLayout::Full&&source.layout!=ImageLayout::SourceRows))return false;
 unsigned samples=0,colorCount=0,yBands=0,minX=source.width,maxX=0;std::array<bool,4096> colors{};
 for(unsigned gy=0;gy<GridRows;++gy)for(unsigned gx=0;gx<GridColumns;++gx){
  const unsigned x=GridX(source.width,gx),y=GridY(source.height,gy);const auto* s=SourceSample(source,x,y,gy);
  if(s[3]<250)continue;bool smooth=true;for(int dy=-1;dy<=1&&smooth;++dy)for(int dx=-1;dx<=1;++dx){auto* p=SourceSample(source,x,y,gy,dx,dy);if(p[3]<250||!Near(s,p,4)){smooth=false;break;}}if(!smooth)continue;
  ++samples;yBands|=1u<<std::min(3u,unsigned(std::uint64_t(y)*4/source.height));minX=std::min(minX,x);maxX=std::max(maxX,x);
  const unsigned color=(s[0]>>4)|((s[1]>>4)<<4)|((s[2]>>4)<<8);if(!colors[color]){colors[color]=true;++colorCount;}
 }
 return samples>=16&&colorCount>=4&&yBands==15&&(maxX-minX)*100ull>=source.width*8ull;
}
// Diagnostic only. Validate remains the sole authority and is unchanged.
// This classifier runs only after refusal and persists no image data.
inline const char* ExplainValidationFailure(const ImageView& source,const ImageView& baseline,const ImageView& actual,const Binding& expected,const Facts& f,const Metrics& metrics){
 if(!ValidImage(source))return "source-image-invalid";
 if(!ValidImage(baseline))return "baseline-image-invalid";
 if(!ValidImage(actual))return "output-image-invalid";
 if(baseline.width!=actual.width||baseline.height!=actual.height)return "baseline-output-dimensions";
 if(!SameBinding(expected,f.binding))return "binding-mismatch";
 if(source.encoding!=PixelEncoding::Bgra8)return "source-encoding";
 if(baseline.encoding!=actual.encoding)return "output-encoding-changed";
 if(baseline.sdrWhiteLevel!=actual.sdrWhiteLevel)return "output-white-level-changed";
 if(source.layout!=ImageLayout::Full&&source.layout!=ImageLayout::SourceRows)return "source-layout";
 if((baseline.layout!=ImageLayout::Full&&baseline.layout!=ImageLayout::OutputRows)||baseline.layout!=actual.layout)return "output-layout-changed";
 if(actual.layout==ImageLayout::OutputRows&&(baseline.grid.sourceWidth!=source.width||baseline.grid.sourceHeight!=source.height||actual.grid.sourceWidth!=source.width||actual.grid.sourceHeight!=source.height))return "sample-grid-mismatch";
 if(!expected.generation||!expected.sourceWindow||!expected.sourceCookie||!expected.destinationWindow||!expected.destinationCookie||!expected.monitor||!expected.adapterLuid)return "binding-incomplete";
 if(!f.rotationIdentity)return "rotation-not-identity";
 if(f.protectedContent)return "protected-content";
 if(!f.qpcFrequency)return "qpc-frequency-zero";
 if(!f.sourceQpc)return "source-qpc-zero";
 if(!f.baselinePresentQpc)return "baseline-present-qpc-zero";
 if(!f.baselineAcquiredQpc)return "baseline-acquired-qpc-zero";
 if(!f.showQpc)return "show-qpc-zero";
 if(!f.framePresentQpc)return "output-present-qpc-zero";
 if(!f.frameAcquiredQpc)return "output-acquired-qpc-zero";
 if(!f.nowQpc)return "now-qpc-zero";
 if(f.baselinePresentQpc>f.baselineAcquiredQpc)return "baseline-present-after-acquire";
 if(f.baselineAcquiredQpc>=f.showQpc)return "baseline-not-before-show";
 if(f.showQpc-f.baselineAcquiredQpc>f.qpcFrequency/4)return "baseline-acquisition-stale";
 if(f.sourceQpc>f.framePresentQpc)return "source-newer-than-output";
 if(f.framePresentQpc<=f.showQpc)return "output-not-after-show";
 if(f.framePresentQpc>f.frameAcquiredQpc)return "output-present-after-acquire";
 if(f.frameAcquiredQpc>f.nowQpc)return "output-acquired-future";
 if(f.sourceQpc>f.nowQpc)return "source-future";
 if(f.nowQpc-f.sourceQpc>f.qpcFrequency/4)return "source-frame-stale";
 if(f.nowQpc-f.framePresentQpc>f.qpcFrequency/4)return "output-frame-stale";
 if(f.nowQpc-f.frameAcquiredQpc>f.qpcFrequency/4)return "output-acquisition-stale";
 if(metrics.samples<16)return "insufficient-opaque-smooth-source-samples";
 if(metrics.colorCount<4)return "insufficient-source-color-diversity";
 if(metrics.yBands!=15)return "insufficient-source-vertical-spread";
 if(metrics.changedBands!=15)return "insufficient-baseline-changed-bands";
 // Horizontal span is intentionally not recomputed: it is absent from Metrics.
 if(metrics.changed*2ull<metrics.samples)return "insufficient-baseline-change";
 if(metrics.matched*100ull<metrics.samples*95ull)return "output-pixel-match";
 return "source-horizontal-span-or-other-sample-refusal";
}

struct SourceSampleDiagnostic {unsigned centers{},alphaZero{},alphaOpaque{},alpha255{},alphaMin{255},alphaMax{},rgbNonzero{},rgbSmooth{},opaqueSmooth{},colors{},smoothColors{},smoothBands{};};
// Aggregate-only inspection of the same owned source grid. No authority or
// opacity interpretation is derived from these counters.
inline SourceSampleDiagnostic InspectSourceSamples(const ImageView& source){
 SourceSampleDiagnostic d{};if(!ValidImage(source)||source.encoding!=PixelEncoding::Bgra8||(source.layout!=ImageLayout::Full&&source.layout!=ImageLayout::SourceRows))return d;
 std::array<bool,4096> colors{},smoothColors{};
 for(unsigned gy=0;gy<GridRows;++gy)for(unsigned gx=0;gx<GridColumns;++gx){
  const auto x=GridX(source.width,gx),y=GridY(source.height,gy);const auto* s=SourceSample(source,x,y,gy);++d.centers;
  d.alphaZero+=s[3]==0;d.alphaOpaque+=s[3]>=250;d.alpha255+=s[3]==255;d.alphaMin=std::min(d.alphaMin,unsigned(s[3]));d.alphaMax=std::max(d.alphaMax,unsigned(s[3]));d.rgbNonzero+=(s[0]|s[1]|s[2])!=0;
  const unsigned c=(s[0]>>4)|((s[1]>>4)<<4)|((s[2]>>4)<<8);if(!colors[c]){colors[c]=true;++d.colors;}
  bool smooth=true,opaque=s[3]>=250;
  for(int dy=-1;dy<=1;++dy)for(int dx=-1;dx<=1;++dx){const auto* n=SourceSample(source,x,y,gy,dx,dy);if(!Near(s,n,4))smooth=false;if(n[3]<250)opaque=false;}
  if(smooth){++d.rgbSmooth;d.opaqueSmooth+=opaque;d.smoothBands|=1u<<std::min(3u,unsigned(std::uint64_t(y)*4/source.height));if(!smoothColors[c]){smoothColors[c]=true;++d.smoothColors;}}
 }
 return d;
}

}
