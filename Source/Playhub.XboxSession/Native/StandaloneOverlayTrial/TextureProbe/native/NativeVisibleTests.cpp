// SPDX-License-Identifier: GPL-3.0-only
#include "NativeVisibleSession.h"
#include "NativeVisibleOutputReceipt.h"
#include "NativeVisibleAlignedAcquire.h"
#include "DesktopOutputSampler.h"
#include <vector>
#include <cstdio>
#include <cstdlib>
using namespace playhub::mirror;
namespace a=playhub::mirror::callsite;
namespace o=playhub::mirror::output;
unsigned count{};void Need(bool v){++count;if(!v){std::printf("FAIL %u\n",count);std::exit(1);}}
SourceLease Lease(){SourceLease l;for(unsigned i=0;i<64;++i)l.sessionId[i]='a';l.generation=1;l.gameId=(2822752531ull<<32)|0x02000000ull;l.expiresAt=2000;
 l.ownerPid=100;l.ownerBirth=1;l.gamePid=200;l.gameBirth=2;l.framePid=200;l.frameBirth=2;l.frameThread=3;l.sourceThread=4;l.session=1;l.gameWindow=5;l.sourceWindow=6;l.sourceCookie=7;l.gameRect={0,0,128,128};l.sourceRect=l.gameRect;return l;}
// Spatial footprint regression fixtures use only the actual pixel validator.

struct SpatialFixture{

 static constexpr unsigned W=640,H=360,S=W*4;

 std::vector<std::uint8_t> source=std::vector<std::uint8_t>(S*H),actual=source;

 o::NativeVisibleBinding bind{1,2,3,4,5,6,7};o::NativeVisibleFacts facts{bind,1000,1001,1010,1015,1020,1000,false,true};

 SpatialFixture(unsigned gray=96){for(unsigned y=0;y<H;++y)for(unsigned x=0;x<W;++x){const bool glyph=(x/32+y/32)%4==0;auto at=(y*W+x)*4;

  for(unsigned c=0;c<3;++c){source[at+c]=static_cast<std::uint8_t>(glyph?gray:0);actual[at+c]=static_cast<std::uint8_t>(glyph?gray:20);}source[at+3]=glyph?255:111;actual[at+3]=255;}}

 o::ImageView Source()const{return{W,H,S,source.data(),source.size()};}o::ImageView Actual()const{return{W,H,S,actual.data(),actual.size()};}

 bool Valid(){return o::ValidateNativeVisible(Source(),Actual(),bind,facts);}

 void Patch(unsigned gx,unsigned gy,unsigned value,unsigned alpha,bool match=true){const auto x=o::GridX(W,gx),y=o::GridY(H,gy);

  for(int dy=-1;dy<=1;++dy)for(int dx=-1;dx<=1;++dx){const auto at=(std::size_t(int(y)+dy)*W+unsigned(int(x)+dx))*4;

   for(unsigned c=0;c<3;++c){source[at+c]=static_cast<std::uint8_t>(value);if(match)actual[at+c]=static_cast<std::uint8_t>(value);}source[at+3]=static_cast<std::uint8_t>(alpha);}}

 void DominantBlack(bool missing){for(unsigned i=0;i<W*H;++i){source[i*4]=source[i*4+1]=source[i*4+2]=actual[i*4]=actual[i*4+1]=actual[i*4+2]=0;source[i*4+3]=actual[i*4+3]=255;}

  for(unsigned gy:{2u,11u,20u,29u}){for(unsigned gx:{3u,13u,23u,33u})Patch(gx,gy,255,255,!missing);for(unsigned gx:{8u,18u,28u,38u})Patch(gx,gy,0,128,false);}}

};

void NativeVisibleSpatialChecks(const char* only=nullptr){

 const auto run=[&](const char* name){return !only||std::strcmp(only,name)==0;};

 if(run("dim")){SpatialFixture f;o::Metrics m{};Need(o::ValidateNativeVisible(f.Source(),f.Actual(),f.bind,f.facts,&m));Need(m.colorCount==1&&m.samples==m.matched);}

 if(run("localized")){SpatialFixture f;for(unsigned i=0;i<f.W*f.H;++i){f.source[i*4]=f.source[i*4+1]=f.source[i*4+2]=0;f.source[i*4+3]=111;f.actual[i*4]=f.actual[i*4+1]=f.actual[i*4+2]=20;}

  for(unsigned gy:{2u,11u,20u,29u})for(unsigned gx:{3u,13u,23u,33u})f.Patch(gx,gy,0,255);

  for(unsigned gy:{3u,6u,9u,12u})for(unsigned gx:{8u,18u,28u,38u})f.Patch(gx,gy,220,255);

  Need(f.Valid());}

 if(run("missing-glyph")){SpatialFixture f;f.DominantBlack(true);o::Metrics m{};Need(!o::ValidateNativeVisible(f.Source(),f.Actual(),f.bind,f.facts,&m));Need(m.samples==2288&&m.matched==2272&&m.colorCount==2);}

 if(only)return;

 {SpatialFixture f;f.DominantBlack(false);Need(f.Valid());}

 {SpatialFixture f(255);Need(f.Valid());}

 {SpatialFixture f;f.facts.minimumFrameQpc=0;Need(!f.Valid());}

 for(unsigned value:{0u,96u,255u}){SpatialFixture f;for(unsigned i=0;i<f.W*f.H;++i)for(unsigned c=0;c<3;++c)f.actual[i*4+c]=static_cast<std::uint8_t>(value);Need(!f.Valid());}

 {SpatialFixture f;for(unsigned i=0;i<f.W*f.H;++i){f.source[i*4]=f.source[i*4+1]=f.source[i*4+2]=f.actual[i*4]=f.actual[i*4+1]=f.actual[i*4+2]=96;f.source[i*4+3]=255;}Need(!f.Valid());}

 {SpatialFixture f;for(unsigned i=3;i<f.source.size();i+=4)f.source[i]=111;Need(!f.Valid());}

 {SpatialFixture f;for(unsigned i=3;i<f.source.size();i+=4)if(f.source[i]!=255)f.source[i]=0;Need(!f.Valid());}

 {SpatialFixture f;for(unsigned y=0;y<f.H;++y)for(unsigned x=f.W/20;x<f.W;++x)f.source[y*f.S+x*4+3]=111;Need(!f.Valid());}

 {SpatialFixture f;for(unsigned y=f.H/8;y<f.H;++y)for(unsigned x=0;x<f.W;++x)f.source[y*f.S+x*4+3]=111;Need(!f.Valid());}

 {SpatialFixture f;for(unsigned i=0;i<f.W*f.H;++i)if(f.source[i*4+3]<250)f.actual[i*4+3]=0;Need(!f.Valid());}

 {SpatialFixture f;for(unsigned i=0;i<f.W*f.H;++i)if(f.source[i*4+3]>=250)f.actual[i*4+3]=0;Need(!f.Valid());}

 {SpatialFixture f;for(unsigned i=0;i<f.W*f.H;++i)if(f.source[i*4+3]<250)f.actual[i*4]=f.actual[i*4+1]=f.actual[i*4+2]=96;Need(!f.Valid());}

 {SpatialFixture f;for(unsigned y=0;y<f.H;++y)for(unsigned x=0;x<f.W;++x){auto at=(y*f.W+x)*4;const bool shifted=((x+32)/32+y/32)%4==0;f.actual[at]=f.actual[at+1]=f.actual[at+2]=shifted?96:20;}Need(!f.Valid());}

 {SpatialFixture f;auto s=f.Source();std::vector<std::uint8_t> packed(f.S*o::GridRows*3);for(unsigned gy=0;gy<o::GridRows;++gy)for(int dy=-1;dy<=1;++dy){const auto sy=unsigned(int(o::GridY(f.H,gy))+dy),py=unsigned(int(gy*3+1)+dy);std::copy_n(f.source.data()+sy*f.S,f.S,packed.data()+py*f.S);}s.bgra=packed.data();s.bytes=packed.size();s.layout=o::ImageLayout::SourceRows;s.grid={f.W,f.H};Need(o::ValidateNativeVisible(s,f.Actual(),f.bind,f.facts));}

 const auto half=[](float value){std::uint32_t bits{};std::memcpy(&bits,&value,4);const unsigned sign=(bits>>16)&0x8000u;const int exp=int((bits>>23)&255u)-127+15;if(exp<=0)return std::uint16_t(sign);if(exp>=31)return std::uint16_t(sign|0x7c00u);return std::uint16_t(sign|(unsigned(exp)<<10)|((bits>>13)&1023u));};

 for(unsigned white:{1000u,3000u}){SpatialFixture f;std::vector<std::uint16_t> hdr(std::size_t(f.W)*f.H*4);

  for(unsigned i=0;i<f.W*f.H;++i){for(unsigned c=0;c<3;++c)hdr[i*4+c]=half(o::Linear(f.actual[i*4+2-c])*float(white)/1000.0f);hdr[i*4+3]=0x3c00;}

  o::ImageView a{f.W,f.H,f.W*8,reinterpret_cast<const std::uint8_t*>(hdr.data()),hdr.size()*2,o::PixelEncoding::ScRgbFloat16,white};Need(o::ValidateNativeVisible(f.Source(),a,f.bind,f.facts));

  for(auto invalid:{std::uint16_t(0x7e00),std::uint16_t(0x7c00)}){auto broken=hdr;for(unsigned i=0;i<f.W*f.H;++i)if(f.source[i*4+3]<250)broken[i*4]=invalid;a.bgra=reinterpret_cast<const std::uint8_t*>(broken.data());Need(!o::ValidateNativeVisible(f.Source(),a,f.bind,f.facts));}

  for(auto invalid:{std::uint16_t(0),std::uint16_t(0x7e00),std::uint16_t(0x7c00),half(.97f),half(1.03f)}){auto broken=hdr;for(unsigned i=0;i<f.W*f.H;++i)if(f.source[i*4+3]<250)broken[i*4+3]=invalid;a.bgra=reinterpret_cast<const std::uint8_t*>(broken.data());Need(!o::ValidateNativeVisible(f.Source(),a,f.bind,f.facts));}

  a.bgra=reinterpret_cast<const std::uint8_t*>(hdr.data());std::vector<std::uint16_t> packed(std::size_t(f.W)*o::GridRows*4);for(unsigned gy=0;gy<o::GridRows;++gy)std::copy_n(hdr.data()+o::GridY(f.H,gy)*f.W*4,f.W*4,packed.data()+gy*f.W*4);a.bgra=reinterpret_cast<const std::uint8_t*>(packed.data());a.bytes=packed.size()*2;a.layout=o::ImageLayout::OutputRows;a.grid={f.W,f.H};Need(o::ValidateNativeVisible(f.Source(),a,f.bind,f.facts));

 }

 for(unsigned i=0;i<7;++i){SpatialFixture f;switch(i){case 0:++f.facts.binding.generation;break;case 1:++f.facts.binding.sourceWindow;break;case 2:++f.facts.binding.sourceCookie;break;case 3:++f.facts.binding.gameWindow;break;case 4:++f.facts.binding.gameBirth;break;case 5:++f.facts.binding.monitor;break;case 6:++f.facts.binding.adapterLuid;break;}Need(!f.Valid());}

 {SpatialFixture f;f.facts.protectedContent=true;Need(!f.Valid());}

 {SpatialFixture f;f.facts.rotationIdentity=false;Need(!f.Valid());}

 {SpatialFixture f;f.facts.nowQpc=1251;Need(!f.Valid());}

 {SpatialFixture f;f.facts.framePresentQpc=f.facts.minimumFrameQpc;Need(!f.Valid());}

 {SpatialFixture f;o::Binding b{1,2,3,4,5,6,7};o::Facts facts{b,1000,990,995,1001,1010,1015,1020,1000,false,true};Need(!o::Validate(f.Source(),f.Actual(),f.Actual(),b,facts));}

}


// Same-pass diagnostic contract: cached scalars never change the verdict.
void NativeVisibleSpatialDiagnosticChecks(){
 {SpatialFixture f;o::NativeSpatialDiagnostic d{};Need(o::ValidateNativeVisible(f.Source(),f.Actual(),f.bind,f.facts,nullptr,&d));Need(d.stage==5&&d.groupCount==1&&d.testedGroups==1&&d.failedGroup==0);const auto& g=d.groups[0];Need(g.count==g.matched&&g.count>=16&&g.spanX>0&&g.spanY>0&&g.anchorReject==0);Need(g.controls>=16&&g.controls==g.contrasting&&g.controlReject==0);}
 {SpatialFixture f;f.DominantBlack(true);o::NativeSpatialDiagnostic d{};Need(!o::ValidateNativeVisible(f.Source(),f.Actual(),f.bind,f.facts,nullptr,&d));Need(d.stage==3&&d.groupCount==2&&d.failedGroup>0&&d.testedGroups==0);const auto& g=d.groups[d.failedGroup-1];Need(g.count==16&&g.matched==0&&g.controlReject==16);}
 {SpatialFixture f;for(unsigned i=0;i<f.W*f.H;++i){f.source[i*4]=f.source[i*4+1]=f.source[i*4+2]=f.actual[i*4]=f.actual[i*4+1]=f.actual[i*4+2]=96;f.source[i*4+3]=255;}o::NativeSpatialDiagnostic d{};Need(!o::ValidateNativeVisible(f.Source(),f.Actual(),f.bind,f.facts,nullptr,&d));Need(d.stage==6&&d.groupCount==1&&d.testedGroups==1);Need(d.groups[0].controls==0&&d.groups[0].controlReject==7);}
 {SpatialFixture f;for(unsigned i=0;i<f.W*f.H;++i)if(f.source[i*4+3]<250)f.actual[i*4]=f.actual[i*4+1]=f.actual[i*4+2]=96;o::NativeSpatialDiagnostic d{};Need(!o::ValidateNativeVisible(f.Source(),f.Actual(),f.bind,f.facts,nullptr,&d));Need(d.stage==6&&d.groups[0].controls>=16&&d.groups[0].contrasting==0&&d.groups[0].controlReject==8);}
 {SpatialFixture f;for(unsigned y=0;y<f.H;++y)for(unsigned x=f.W/20;x<f.W;++x)f.source[y*f.S+x*4+3]=111;o::NativeSpatialDiagnostic d{};Need(!o::MatchesMonochromeFootprint(f.Source(),f.Actual(),&d));Need(d.stage==6&&d.testedGroups==0&&(d.groups[0].anchorReject&2)&&d.groups[0].controlReject==16);}
 {SpatialFixture f;for(unsigned y=f.H/20;y<f.H;++y)for(unsigned x=0;x<f.W;++x)f.source[y*f.S+x*4+3]=111;o::NativeSpatialDiagnostic d{};Need(!o::MatchesMonochromeFootprint(f.Source(),f.Actual(),&d));Need(d.stage==6&&d.testedGroups==0&&(d.groups[0].anchorReject&4));}
 {SpatialFixture f;for(unsigned i=0;i<f.W*f.H;++i)f.source[i*4+3]=111;f.Patch(3,2,96,255);o::NativeSpatialDiagnostic d{};Need(!o::MatchesMonochromeFootprint(f.Source(),f.Actual(),&d));Need(d.stage==6&&d.groups[0].count==1&&(d.groups[0].anchorReject&1));}
 {SpatialFixture f;for(unsigned y=0;y<f.H;++y)for(unsigned x=0;x<f.W;++x){auto at=(y*f.W+x)*4;if(f.source[at+3]<250&&x<f.W*19/20)f.source[at]=f.source[at+1]=f.source[at+2]=96;}o::NativeSpatialDiagnostic d{};Need(!o::MatchesMonochromeFootprint(f.Source(),f.Actual(),&d));Need(d.stage==6&&d.groups[0].controls>=16&&(d.groups[0].controlReject&2));}
 {SpatialFixture f;for(unsigned y=0;y<f.H;++y)for(unsigned x=0;x<f.W;++x){auto at=(y*f.W+x)*4;if(f.source[at+3]<250&&y<f.H*19/20)f.source[at]=f.source[at+1]=f.source[at+2]=96;}o::NativeSpatialDiagnostic d{};Need(!o::MatchesMonochromeFootprint(f.Source(),f.Actual(),&d));Need(d.stage==6&&d.groups[0].controls>=16&&(d.groups[0].controlReject&4));}
 {SpatialFixture f;for(unsigned i=0;i<f.W*f.H;++i)if(f.source[i*4+3]<250)f.source[i*4]=f.source[i*4+1]=f.source[i*4+2]=96;o::NativeSpatialDiagnostic d{};Need(!o::MatchesMonochromeFootprint(f.Source(),f.Actual(),&d));Need(d.stage==6&&(d.groups[0].controlReject&1));}
 {SpatialFixture f;for(unsigned y=0;y<f.H;++y)for(unsigned x=0;x<f.W;++x){auto at=(y*f.W+x)*4;for(unsigned c=0;c<3;++c)f.source[at+c]=f.actual[at+c]=static_cast<std::uint8_t>(32+(x/80)%4*48);f.source[at+3]=255;}o::NativeSpatialDiagnostic d{};Need(!o::MatchesMonochromeFootprint(f.Source(),f.Actual(),&d));Need(d.stage==2&&d.groupCount==3);Need(o::ValidateNativeVisible(f.Source(),f.Actual(),f.bind,f.facts,nullptr,&d));Need(d.stage==0&&d.groupCount==0&&d.testedGroups==0);}
 {SpatialFixture f;o::NativeSpatialDiagnostic d{};d.stage=99;f.facts.minimumFrameQpc=0;Need(!o::ValidateNativeVisible(f.Source(),f.Actual(),f.bind,f.facts,nullptr,&d));Need(d.stage==0&&d.groupCount==0);f.facts.sourceQpc=2000;d.stage=99;Need(!o::ValidateNativeVisible(f.Source(),f.Actual(),f.bind,f.facts,nullptr,&d));Need(d.stage==0&&d.groupCount==0);}
}

// Renewal-only sparse opaque certificate: synthetic Home-like structure,
// not a replay of unavailable physical pixels.
struct SparseOpaqueFixture:SpatialFixture {
 SparseOpaqueFixture(unsigned anchor=0,unsigned glyph=96):SpatialFixture(){
  for(unsigned i=0;i<W*H;++i){for(unsigned c=0;c<3;++c)source[i*4+c]=actual[i*4+c]=0;source[i*4+3]=111;actual[i*4+3]=255;}
  for(unsigned gy:{0u,9u,18u,27u})for(unsigned gx=0;gx<64;++gx)if(!(gy==27&&gx>=62))Patch(gx,gy,anchor,255);
  Patch(2,3,glyph,255);for(unsigned gy:{4u,11u,20u})Patch(7,gy,40,255);
  for(unsigned gx=0;gx<20;++gx){Patch(gx*3,5,64,111,false);if(gx<6){const auto x=o::GridX(W,gx*3),y=o::GridY(H,5);actual[(y*W+x)*4]=actual[(y*W+x)*4+1]=actual[(y*W+x)*4+2]=70;}}
 }
 void UniformOutput(unsigned v){for(unsigned i=0;i<W*H;++i)for(unsigned c=0;c<3;++c)actual[i*4+c]=static_cast<std::uint8_t>(v);}
 void SetSourceColor(unsigned from,unsigned to){for(unsigned i=0;i<W*H;++i)if(source[i*4+3]>=250&&source[i*4]==from)for(unsigned c=0;c<3;++c)source[i*4+c]=static_cast<std::uint8_t>(to);}
};
void NativeVisibleSparseOpaqueChecks(const char* only=nullptr){
 const auto run=[&](const char* name){return !only||std::strcmp(only,name)==0;};
 if(run("sparse")){SparseOpaqueFixture f;o::Metrics m{};o::NativeSpatialDiagnostic d{};Need(o::ValidateNativeVisible(f.Source(),f.Actual(),f.bind,f.facts,&m,&d));Need(m.samples==258&&m.matched==258&&m.colorCount==3&&m.yBands==15);Need(d.stage==7&&d.groupCount==3);Need(d.groups[0].count==254&&d.groups[1].count==1&&d.groups[2].count==3);}
 if(run("outlier")){SparseOpaqueFixture f(15,19);f.SetSourceColor(40,19);f.Patch(0,0,0,255,false);f.UniformOutput(16);o::Metrics m{};Need(!o::ValidateNativeVisible(f.Source(),f.Actual(),f.bind,f.facts,&m));Need(m.samples==258&&m.matched==257&&m.colorCount==2);}
 if(only)return;
 // A17-level relative separation is sufficient; no absolute brightness or32-level contrast floor.
 {SparseOpaqueFixture f(96,113);for(unsigned i=0;i<f.W*f.H;++i)if(f.source[i*4+3]>=250&&f.source[i*4]==40)for(unsigned c=0;c<3;++c)f.source[i*4+c]=f.actual[i*4+c]=113;Need(f.Valid());}
 // All groups, including single bright and three gray glyph samples, must match.
 for(unsigned scenario=0;scenario<5;++scenario){SparseOpaqueFixture f;switch(scenario){case 0:f.UniformOutput(0);break;case 1:f.Patch(2,3,96,255,false);{auto x=o::GridX(f.W,2),y=o::GridY(f.H,3);for(unsigned c=0;c<3;++c)f.actual[(y*f.W+x)*4+c]=0;}break;case 2:f.UniformOutput(40);break;case 3:f.UniformOutput(96);break;case 4:for(unsigned i=3;i<f.actual.size();i+=4)f.actual[i]=0;break;}Need(!f.Valid());}
 // Overlapping tolerance intervals never prove contrast, even when all groups match.
 {SparseOpaqueFixture f(0,16);f.SetSourceColor(40,16);f.UniformOutput(8);Need(!f.Valid());}
 // A SOURCE-first reference can be an allowed group mismatch. Do not use that
 // unmatched outlier to certify a uniform output, despite95%group/global match.
 {SparseOpaqueFixture f;for(unsigned y=f.H/8;y<f.H;++y)for(unsigned x=0;x<f.W;++x)f.source[y*f.S+x*4+3]=111;Need(!f.Valid());}
 {SparseOpaqueFixture f;for(unsigned y=0;y<f.H;++y)for(unsigned x=f.W/20;x<f.W;++x)f.source[y*f.S+x*4+3]=111;Need(!f.Valid());}
 {SparseOpaqueFixture f;f.facts.minimumFrameQpc=0;Need(!f.Valid());}
 {SparseOpaqueFixture f;f.facts.nowQpc=1251;Need(!f.Valid());}
 {SparseOpaqueFixture f;f.facts.binding.generation++;Need(!f.Valid());}
 // Sparse exact row layouts use identical logical coordinates.
 {SparseOpaqueFixture f;std::vector<std::uint8_t> sr(f.S*o::GridRows*3),ar(f.S*o::GridRows);for(unsigned gy=0;gy<o::GridRows;++gy){for(int dy=-1;dy<=1;++dy)std::copy_n(f.source.data()+unsigned(int(o::GridY(f.H,gy))+dy)*f.S,f.S,sr.data()+unsigned(int(gy*3+1)+dy)*f.S);std::copy_n(f.actual.data()+o::GridY(f.H,gy)*f.S,f.S,ar.data()+gy*f.S);}auto s=f.Source(),a=f.Actual();s.bgra=sr.data();s.bytes=sr.size();s.layout=o::ImageLayout::SourceRows;s.grid={f.W,f.H};a.bgra=ar.data();a.bytes=ar.size();a.layout=o::ImageLayout::OutputRows;a.grid=s.grid;Need(o::ValidateNativeVisible(s,a,f.bind,f.facts));}
 const auto half=[](float value){std::uint32_t bits{};std::memcpy(&bits,&value,4);const unsigned sign=(bits>>16)&0x8000u;const int exp=int((bits>>23)&255u)-127+15;if(exp<=0)return std::uint16_t(sign);if(exp>=31)return std::uint16_t(sign|0x7c00u);return std::uint16_t(sign|(unsigned(exp)<<10)|((bits>>13)&1023u));};
 for(unsigned white:{1000u,3000u}){SparseOpaqueFixture f;std::vector<std::uint16_t> hdr(std::size_t(f.W)*f.H*4);for(unsigned i=0;i<f.W*f.H;++i){for(unsigned c=0;c<3;++c)hdr[i*4+c]=half(o::Linear(f.actual[i*4+2-c])*float(white)/1000.0f);hdr[i*4+3]=0x3c00;}o::ImageView a{f.W,f.H,f.W*8,reinterpret_cast<const std::uint8_t*>(hdr.data()),hdr.size()*2,o::PixelEncoding::ScRgbFloat16,white};Need(o::ValidateNativeVisible(f.Source(),a,f.bind,f.facts));
  const auto at=(o::GridY(f.H,3)*f.W+o::GridX(f.W,2))*4;
  for(auto bad:{std::uint16_t(0x7e00),std::uint16_t(0x7c00)}){const auto save=hdr[at];hdr[at]=bad;Need(!o::ValidateNativeVisible(f.Source(),a,f.bind,f.facts));hdr[at]=save;}
  for(auto bad:{std::uint16_t(0),std::uint16_t(0x7e00),std::uint16_t(0x7c00),half(.97f),half(1.03f)}){hdr[at+3]=bad;Need(!o::ValidateNativeVisible(f.Source(),a,f.bind,f.facts));}hdr[at+3]=0x3c00;
  std::vector<std::uint16_t> packed(std::size_t(f.W)*o::GridRows*4);for(unsigned gy=0;gy<o::GridRows;++gy)std::copy_n(hdr.data()+o::GridY(f.H,gy)*f.W*4,f.W*4,packed.data()+gy*f.W*4);a.bgra=reinterpret_cast<const std::uint8_t*>(packed.data());a.bytes=packed.size()*2;a.layout=o::ImageLayout::OutputRows;a.grid={f.W,f.H};Need(o::ValidateNativeVisible(f.Source(),a,f.bind,f.facts));
 }
}

int main(){
 NativeVisibleSparseOpaqueChecks();
 NativeVisibleSpatialChecks();NativeVisibleSpatialDiagnosticChecks();
 const unsigned w=128,h=128;std::vector<std::uint8_t> pixels(w*h*4);
 for(unsigned y=0;y<h;++y)for(unsigned x=0;x<w;++x){auto* p=pixels.data()+(y*w+x)*4;p[0]=static_cast<std::uint8_t>(20+x/16*18);p[1]=static_cast<std::uint8_t>(30+y/16*20);p[2]=static_cast<std::uint8_t>(80+(x/32+y/32)%4*35);p[3]=255;}
 o::ImageView source{w,h,w*4,pixels.data(),pixels.size()},actual=source;
 o::NativeVisibleBinding binding{1,6,7,5,2,10,11};o::NativeVisibleFacts facts{binding,1000,1001,1010,1020,1030,1000,false,true};o::Metrics metrics;
 Need(o::ValidateNativeVisible(source,actual,binding,facts,&metrics));Need(metrics.samples>=16&&metrics.matched==metrics.samples);
 // Real queued-before-call case: source is genuinely older than the fresh
 // physical frame, but that frame precedes this API call. It is native UI
 // already visible, not a claim of a newly shown destination.
 auto queued=facts;queued.sourceQpc=1000;queued.minimumFrameQpc=0;queued.framePresentQpc=1010;queued.frameAcquiredQpc=1050;queued.nowQpc=1060;
 Need(o::ValidateNativeVisible(source,actual,binding,queued));
 auto required=queued;required.minimumFrameQpc=1040;Need(!o::ValidateNativeVisible(source,actual,binding,required));
 required=queued;required.minimumFrameQpc=1010;Need(!o::ValidateNativeVisible(source,actual,binding,required));
 required=queued;required.minimumFrameQpc=1009;Need(o::ValidateNativeVisible(source,actual,binding,required));
 auto staleExisting=queued;staleExisting.nowQpc=1300;Need(!o::ValidateNativeVisible(source,actual,binding,staleExisting));

 // Causal parent refusal remains RED: unchanged output is NOT our new mirror.
 o::Binding own{1,6,7,8,9,10,11};o::Facts ownFacts{own,1000,990,995,1001,1010,1020,1030,1000,false,true};
 Need(!o::Validate(source,actual,actual,own,ownFacts,&metrics));Need(metrics.changed==0);
 std::uint64_t o::NativeVisibleBinding::* identityFields[]={&o::NativeVisibleBinding::generation,&o::NativeVisibleBinding::sourceWindow,&o::NativeVisibleBinding::sourceCookie,&o::NativeVisibleBinding::gameWindow,&o::NativeVisibleBinding::gameBirth,&o::NativeVisibleBinding::monitor,&o::NativeVisibleBinding::adapterLuid};
 for(auto field:identityFields){auto changed=facts;changed.binding.*field+=1;Need(!o::ValidateNativeVisible(source,actual,binding,changed));}
 std::uint64_t o::NativeVisibleFacts::* timeFields[]={&o::NativeVisibleFacts::sourceQpc,&o::NativeVisibleFacts::framePresentQpc,&o::NativeVisibleFacts::frameAcquiredQpc,&o::NativeVisibleFacts::nowQpc,&o::NativeVisibleFacts::qpcFrequency};
 for(auto field:timeFields){auto changed=facts;changed.*field=0;Need(!o::ValidateNativeVisible(source,actual,binding,changed));}
 auto f=facts;f.nowQpc=1300;Need(!o::ValidateNativeVisible(source,actual,binding,f));
 f=facts;f.minimumFrameQpc=f.framePresentQpc;Need(!o::ValidateNativeVisible(source,actual,binding,f));
 f=facts;f.sourceQpc=f.framePresentQpc+1;Need(!o::ValidateNativeVisible(source,actual,binding,f));
 f=facts;f.protectedContent=true;Need(!o::ValidateNativeVisible(source,actual,binding,f));
 f=facts;f.rotationIdentity=false;Need(!o::ValidateNativeVisible(source,actual,binding,f));
 auto partial=pixels;for(unsigned y=0;y<h;++y)for(unsigned x=0;x<w/2;++x)partial[(y*w+x)*4]=0;
 auto bad=actual;bad.bgra=partial.data();Need(!o::ValidateNativeVisible(source,bad,binding,facts));
 auto translucent=pixels;for(unsigned i=3;i<translucent.size();i+=4)translucent[i]=128;auto fade=source;fade.bgra=translucent.data();Need(!o::ValidateNativeVisible(fade,actual,binding,facts));
 auto uniform=pixels;for(unsigned i=0;i<w*h;++i){uniform[i*4]=50;uniform[i*4+1]=50;uniform[i*4+2]=50;}
 auto flat=source;flat.bgra=uniform.data();Need(!o::ValidateNativeVisible(flat,flat,binding,facts));
 auto l=Lease();NativeVisibleProbe first{1000,1250,1,2,100,100},second{1020,1270,3,4,100,100};
 a::NativeVisibleSession session;Need(session.Begin(l,first,9,10,11,1000));Need(session.Pending()&&!session.Active());
 Need(!session.ReadyForConfirm(l,false,3,1001));Need(session.Pending());Need(session.ReadyForConfirm(l,true,3,1002));
 Need(session.Confirm(l,second,12,true,true,1020));Need(session.Active());
 auto n=session.Read(true,true,1020);Need(a::NativeVisibleCurrent(n,1020));Need(n.inputAuthority==0&&n.visualAuthority==0&&n.ownCleanupComplete==1);
 // Cleanup snapshot is terminal for our layer, while original source is active.
 Need(n.phase==1&&n.sourceFrame>n.receiptFrame);Need(!a::NativeVisibleRetired(n,1020));
 auto renewed=l;renewed.expiresAt=2100;Need(session.Renew(renewed,true,true,1030));Need(session.Lease().expiresAt==2100);
 auto newFrame=second;newFrame.observedAt=1100;newFrame.expiresAt=1350;newFrame.frameQpc=5;Need(session.Update(renewed,newFrame,15,true,true,1100));
 Need(!session.Update(renewed,newFrame,15,true,true,1101));Need(!session.Active()); // cannot renew on stale/reused frame
 for(unsigned scenario=0;scenario<5;++scenario){
  a::NativeVisibleSession s;Need(s.Begin(l,first,9,10,11,1000));Need(s.ReadyForConfirm(l,true,3,1002));
  auto changed=l;if(scenario==0)changed.gameBirth++;
  const bool renderer=scenario!=1,cleanup=scenario!=2;auto receipt=second;if(scenario==3)receipt.matched=94;if(scenario==4)receipt.frameQpc=3;
  Need(!s.Confirm(changed,receipt,12,renderer,cleanup,1020));Need(!s.Active());
 }
 a::NativeVisibleSession timeout;Need(timeout.Begin(l,first,9,10,11,1000));Need(!timeout.ReadyForConfirm(l,false,3,1250));Need(!timeout.Held());
 a::NativeVisibleSession closed;Need(closed.Begin(l,first,9,10,11,1000));Need(closed.ReadyForConfirm(l,true,3,1002));Need(closed.Confirm(l,second,12,true,true,1020));
 Need(closed.Held()); // Real admission guard closes this source before any new Capture/generation.
 auto terminal=closed.Retired(l,true,false,1030);Need(a::NativeVisibleRetired(terminal,1030));Need(!a::NativeVisibleCurrent(terminal,1030));Need(!closed.Active());
 a::NativeVisibleMailbox box;box.Publish(n);auto observed=box.Read(1020);Need(a::NativeVisibleCurrent(observed,1020));
 a::NativeVisibleControlV1 command;command.action=2;command.expected=observed;Need(box.Queue(command,1020,1000));Need(box.Read(1020).phase==0);
 a::NativeVisibleControlV1 taken;Need(box.Take(taken)&&taken.action==2);box.Publish(terminal);Need(a::NativeVisibleRetired(box.Read(1030),1030));Need(!box.Queue(command,1030,1000));
 // Real source-close may occur long after the visible receipt expired.
 // Current hidden-source observation, not stale visible truth, acknowledges it.
 Need(box.Read(1400).phase==0);
 auto refreshed=closed.ObserveHidden(l,true,true,false,true,3000);
 Need(a::NativeVisibleRetired(refreshed,3000));Need(!a::NativeVisibleCurrent(refreshed,3000));
 Need(a::SameNativeVisible(refreshed,terminal));Need(refreshed.observedAt==3000&&refreshed.valveOpen==0);
 box.Publish(refreshed);Need(a::NativeVisibleRetired(box.Read(3000),3000));
 Need(closed.ObserveHidden(l,false,true,false,true,3001).phase==0);
 Need(closed.ObserveHidden(l,true,false,false,true,3001).phase==0);
 auto rawLatched=closed.ObserveHidden(l,true,true,true,true,3001);
 Need(a::NativeVisibleRetired(rawLatched,3001)&&rawLatched.valveOpen==1);
 Need(!a::NativeVisibleCurrent(rawLatched,3001)&&!rawLatched.inputAuthority&&!rawLatched.visualAuthority);
 Need(!rawLatched.physicalObservedAt&&!rawLatched.physicalExpiresAt);
 Need(a::SameNativeVisible(rawLatched,terminal)&&rawLatched.expiresAt==l.expiresAt);
 box.Publish(rawLatched);Need(box.Read(3252).phase==0); // no hidden reread, no freshness
 auto rawClosed=closed.ObserveHidden(l,true,true,false,true,3300);
 Need(a::NativeVisibleRetired(rawClosed,3300)&&rawClosed.valveOpen==0);
 Need(rawClosed.expiresAt==l.expiresAt&&a::SameNativeVisible(rawClosed,rawLatched));
 Need(closed.ObserveHidden(l,true,true,false,false,3001).phase==0);
 auto foreign=l;foreign.generation++;Need(closed.ObserveHidden(foreign,true,true,false,true,3001).phase==0);
 a::NativeVisibleSession empty;Need(empty.ObserveHidden(l,true,true,false,true,3000).phase==0);
 a::NativeVisibleSession terminalHeld;Need(terminalHeld.Begin(l,first,9,10,11,1000));
 Need(terminalHeld.ObserveHidden(l,true,true,false,true,1001).phase==0);Need(terminalHeld.Pending());
 // The exact helper used by Backend holds captured pixels/QPC through
 // subsequent Draw-like changes, then joins a newer physical desktop frame.
 using namespace playhub::mirror::native_observation;
 struct Payload{Tick presentQpc{};unsigned value{};};
 FrozenSource<Payload,unsigned> cache;auto same=[](unsigned x,unsigned y){return x==y;};
 cache.Store(Payload{1000,7},1,0);auto held=cache.Get(1,0,1010,1000,same);Need(held&&held->value==7&&held->presentQpc==1000);
 Payload newerDraw{1020,9};(void)newerDraw;held=cache.Get(1,0,1040,1000,same);Need(held&&held->value==7&&held->presentQpc==1000);
 Need(!cache.Get(2,0,1040,1000,same));cache.Store(Payload{1000,7},1,0);Need(!cache.Get(1,1,1040,1000,same));
 cache.Store(Payload{1000,7},1,0);Need(cache.Get(1,0,1250,1000,same)!=nullptr);Need(!cache.Get(1,0,1251,1000,same));
 cache.Store(Payload{1000,7},1,0);Need(!cache.Get(1,0,999,1000,same));
 struct Captured{Tick presentQpc{};};Captured out;unsigned attempts{},skipped{};Tick wall=10;unsigned call=0;
 auto acquire=[&](unsigned remaining,Captured& result){Need(remaining<=40&&remaining>0);wall+=12;result.presentQpc=++call==1?998:1015;return true;};
 Need(AcquireAligned(1000,0,50,out,acquire,[]{return false;},[]{return true;},[&]{return wall;},attempts,skipped)==AcquireResult::Ready);
 Need(call==2&&attempts==2&&skipped==1&&out.presentQpc==1015&&wall==34);
 wall=10;call=0;
 Need(AcquireAligned(1000,1015,50,out,acquire,[]{return false;},[]{return true;},[&]{return wall;},attempts,skipped)==AcquireResult::Pending);
 Need(attempts<=4&&skipped==attempts); // no frame after the required cleanup
 wall=10;bool owner=true;
 auto drift=[&](unsigned,Captured& result){wall+=1;result.presentQpc=1015;owner=false;return true;};
 Need(AcquireAligned(1000,0,50,out,drift,[]{return false;},[&]{return owner;},[&]{return wall;},attempts,skipped)==AcquireResult::Refused);
 wall=10;call=0;
 auto pointer=[&](unsigned,Captured& result){++call;wall+=8;if(call==1)return false;result.presentQpc=1015;return true;};
 Need(AcquireAligned(1000,0,50,out,pointer,[]{return true;},[]{return true;},[&]{return wall;},attempts,skipped)==AcquireResult::Ready);
 wall=10;call=0;auto protectedFailure=[&](unsigned,Captured&){++call;wall+=1;return false;};
 Need(AcquireAligned(1000,0,50,out,protectedFailure,[]{return false;},[]{return true;},[&]{return wall;},attempts,skipped)==AcquireResult::Refused);Need(call==1);
 wall=10;auto timeoutFrame=[&](unsigned,Captured& result){wall=50;result.presentQpc=1015;return true;};
 Need(AcquireAligned(1000,0,50,out,timeoutFrame,[]{return false;},[]{return true;},[&]{return wall;},attempts,skipped)==AcquireResult::Pending);
 // A terminal capture failure never becomes Pending merely because an
 // earlier queued frame was skipped and the same deadline just elapsed.
 wall=10;call=0;auto queuedThenTerminal=[&](unsigned,Captured& result){++call;if(call==1){wall=20;result.presentQpc=998;return true;}wall=50;return false;};
 Need(AcquireAligned(1000,0,50,out,queuedThenTerminal,[]{return false;},[]{return true;},[&]{return wall;},attempts,skipped)==AcquireResult::Refused);
 Need(attempts==2&&skipped==1);
 // Only actual transient sampler stages may preserve a still-fresh receipt.
 Need(o::NativeObservationRetryable(o::SamplerStage::NoDesktopPresent,DXGI_ERROR_WAIT_TIMEOUT));
 Need(o::NativeObservationRetryable(o::SamplerStage::AcquireNextFrame,DXGI_ERROR_WAIT_TIMEOUT));
 Need(o::NativeObservationRetryable(o::SamplerStage::MapTimeout,DXGI_ERROR_WAS_STILL_DRAWING));
 Need(o::NativeObservationRetryable(o::SamplerStage::ReadbackDeadline,DXGI_ERROR_WAIT_TIMEOUT));
 Need(!o::NativeObservationRetryable(o::SamplerStage::ReadbackDeadline,E_ACCESSDENIED));
 Need(!o::NativeObservationRetryable(o::SamplerStage::MapReadback,DXGI_ERROR_WAIT_TIMEOUT));
 Need(!o::NativeObservationRetryable(o::SamplerStage::OutputCurrentAfter,DXGI_ERROR_WAIT_TIMEOUT));
 Need(!o::NativeObservationRetryable(o::SamplerStage::ProtectedFrame,DXGI_ERROR_WAIT_TIMEOUT));
 Need(!o::NativeObservationRetryable(o::SamplerStage::AcquireNextFrame,DXGI_ERROR_ACCESS_LOST));
 Need(!o::NativeObservationRetryable(o::SamplerStage::NoDesktopPresent,E_ACCESSDENIED));
 std::printf("PASS %u actual native-visible physical/session/mailbox assertions; no OS calls\n",count);
}

