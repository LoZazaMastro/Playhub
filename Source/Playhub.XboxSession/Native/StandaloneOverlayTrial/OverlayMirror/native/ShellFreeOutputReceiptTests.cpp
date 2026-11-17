// SPDX-License-Identifier: GPL-3.0-only
#include "ShellFreeOutputReceipt.h"
#include <algorithm>
#include <cstdint>
#include <functional>
#include <iostream>
#include <stdexcept>
#include <vector>
using namespace playhub::mirror::output;
unsigned checks=0;void Check(bool ok,const char* why){++checks;if(!ok)throw std::runtime_error(why);}
struct Fixture {
 static constexpr unsigned W=320,H=180,S=W*4;
 std::vector<std::uint8_t> source,baseline,actual;
 Binding expected{};Facts facts{};
 Fixture():source(S*H),baseline(S*H),actual(S*H){
  for(unsigned y=0;y<H;y++)for(unsigned x=0;x<W;x++){
   auto i=y*S+x*4;source[i]=std::uint8_t(32+((x/20)%5)*24);source[i+1]=std::uint8_t(32+((y/15)%5)*24);source[i+2]=std::uint8_t(48+(((x/20)+(y/15))%5)*24);source[i+3]=255;baseline[i+3]=255;
  }actual=source;
  expected.generation=7;expected.sourceWindow=1000;expected.sourceCookie=2000;expected.destinationWindow=3000;expected.destinationCookie=4000;expected.monitor=5000;expected.adapterLuid=6000;
  facts.binding=expected;facts.sourceQpc=1000;facts.baselinePresentQpc=980;facts.baselineAcquiredQpc=990;facts.showQpc=1010;facts.framePresentQpc=1020;facts.frameAcquiredQpc=1025;facts.nowQpc=1030;facts.qpcFrequency=1000;facts.protectedContent=false;facts.rotationIdentity=true;
 }
 ImageView Src()const{return {W,H,S,source.data(),source.size()};}ImageView Base()const{return {W,H,S,baseline.data(),baseline.size()};}ImageView Actual()const{return {W,H,S,actual.data(),actual.size()};}
 bool Valid()const{return Validate(Src(),Base(),Actual(),expected,facts);}
};
// Real half-float encoding fixture; production only decodes selected samples.
std::uint16_t EncodeHalf(float f){std::uint32_t b{};std::memcpy(&b,&f,4);unsigned sign=(b>>16)&0x8000u;int e=int((b>>23)&255u)-127+15;unsigned m=b&0x7fffffu;if(e<=0)return std::uint16_t(sign);if(e>=31)return std::uint16_t(sign|0x7c00u);m+=0x1000u;if(m&0x800000u){m=0;++e;}return std::uint16_t(sign|(unsigned(e)<<10)|(m>>13));}
struct HdrFixture:Fixture {
 std::vector<std::uint8_t> hdrBase,hdrActual;std::uint32_t white=2500;
 HdrFixture():hdrBase(W*H*8),hdrActual(W*H*8){Fill();}
 void Fill(){for(unsigned y=0;y<H;++y)for(unsigned x=0;x<W;++x){auto* s=source.data()+y*S+x*4;std::uint16_t rgba[4]={EncodeHalf(Linear(s[2])*float(white)/1000.0f),EncodeHalf(Linear(s[1])*float(white)/1000.0f),EncodeHalf(Linear(s[0])*float(white)/1000.0f),EncodeHalf(1.0f)},black[4]={0,0,0,EncodeHalf(1.0f)};std::memcpy(hdrActual.data()+(y*W+x)*8,rgba,8);std::memcpy(hdrBase.data()+(y*W+x)*8,black,8);}}
 ImageView HB()const{return {W,H,W*8,hdrBase.data(),hdrBase.size(),PixelEncoding::ScRgbFloat16,white};}ImageView HA()const{return {W,H,W*8,hdrActual.data(),hdrActual.size(),PixelEncoding::ScRgbFloat16,white};}
 bool HValid()const{return Validate(Src(),HB(),HA(),expected,facts);}
 void SetAll(std::uint16_t value){for(size_t i=0;i<hdrActual.size();i+=8)for(unsigned c=0;c<3;++c)std::memcpy(hdrActual.data()+i+c*2,&value,2);}
};
void HdrChecks(){
 for(auto white:{1000u,2500u,4000u}){HdrFixture f;f.white=white;f.Fill();Check(f.HValid(),"HDR exact scRGB SDR-reference match");}
 {HdrFixture f;f.hdrBase=f.hdrActual;Check(!f.HValid(),"HDR alreadyvisible baseline rejected");}
 {HdrFixture f;f.hdrActual=f.hdrBase;Check(!f.HValid(),"HDR game-only frame rejected");}
 {HdrFixture f;auto a=f.HA();a.sdrWhiteLevel=4000;Check(!Validate(f.Src(),f.HB(),a,f.expected,f.facts),"whitelevel changed across receipt rejected");}
 {HdrFixture f;auto a=f.HA(),b=f.HB();a.sdrWhiteLevel=b.sdrWhiteLevel=1000;Check(!Validate(f.Src(),b,a,f.expected,f.facts),"wrong referencewhite does not match");}
 {HdrFixture f;f.SetAll(0x7e00);Check(!f.HValid(),"NaN HDR rejected");}
 {HdrFixture f;f.SetAll(0x7c00);Check(!f.HValid(),"infinite HDR rejected");}
 {HdrFixture f;f.SetAll(EncodeHalf(16));Check(!f.HValid(),"HDR highlights neverclipped to SDR match");}
 {HdrFixture f;auto a=f.HA();a.stride=Fixture::W*4;Check(!Validate(f.Src(),f.HB(),a,f.expected,f.facts),"FP16 undersized stride rejected");}
 {HdrFixture f;auto a=f.HA();a.sdrWhiteLevel=0;Check(!Validate(f.Src(),f.HB(),a,f.expected,f.facts),"unknown SDRwhite rejected");}
 {HdrFixture f;for(size_t i=6;i<f.hdrActual.size();i+=8){std::uint16_t zero{};std::memcpy(f.hdrActual.data()+i,&zero,2);}Check(!f.HValid(),"transparent FP16 output rejected");}
 Check(Half(0x3c00)==1.0f&&Half(0x4000)==2.0f&&Half(0xbc00)==-1.0f,"IEEE754 half finite decode");
 Check(std::isnan(Half(0x7e00))&&!std::isfinite(Half(0x7c00)),"IEEE754 half nonfinite decode");
}
void PreparedSourceChecks(){
 {Fixture f;Check(SourceCanSatisfyOutputProof(f.Src()),"opaque distributed source can attempt unchanged output proof");}
 for(auto alpha:{0u,10u,112u,128u,249u,250u,255u}){Fixture f;for(std::size_t i=3;i<f.source.size();i+=4)f.source[i]=static_cast<std::uint8_t>(alpha);Check(SourceCanSatisfyOutputProof(f.Src())==(alpha>=250),"source readiness keeps exact opacity threshold");}
 {Fixture f;auto source=f.Src();source.bytes=4;Check(!SourceCanSatisfyOutputProof(source),"short source rejected before sampling");source=f.Src();source.bgra=nullptr;Check(!SourceCanSatisfyOutputProof(source),"null source rejected before sampling");source=f.Src();source.layout=ImageLayout::OutputRows;Check(!SourceCanSatisfyOutputProof(source),"output rows cannot become source eligibility");}
 {Fixture f;for(std::size_t i=0;i<f.source.size();i+=4)f.source[i]=f.source[i+1]=f.source[i+2]=64;Check(!SourceCanSatisfyOutputProof(f.Src()),"uniform opaque source remains inadmissible");}
 {Fixture f;for(unsigned y=Fixture::H/4;y<Fixture::H;++y)for(unsigned x=0;x<Fixture::W;++x)f.source[y*Fixture::S+x*4+3]=0;Check(!SourceCanSatisfyOutputProof(f.Src()),"one vertical band remains inadmissible");}
 {Fixture f;for(unsigned y=0;y<Fixture::H;++y)for(unsigned x=Fixture::W/16;x<Fixture::W;++x)f.source[y*Fixture::S+x*4+3]=0;Check(!SourceCanSatisfyOutputProof(f.Src()),"narrow horizontal span remains inadmissible");}
}
int main(){try{
 {Fixture f;Check(f.Valid(),"new distributed opaque source pixels observed on post-show output");}
 std::vector<std::pair<const char*,std::function<void(Fixture&)>>> bad={
 {"changed generation",[](auto&f){++f.facts.binding.generation;}},{"changed source HWND",[](auto&f){++f.facts.binding.sourceWindow;}},{"changed source cookie",[](auto&f){++f.facts.binding.sourceCookie;}},{"changed destination HWND",[](auto&f){++f.facts.binding.destinationWindow;}},{"changed destination cookie",[](auto&f){++f.facts.binding.destinationCookie;}},{"changed monitor",[](auto&f){++f.facts.binding.monitor;}},{"changed adapter",[](auto&f){++f.facts.binding.adapterLuid;}},
 {"zero QPC frequency",[](auto&f){f.facts.qpcFrequency=0;}},{"future source",[](auto&f){f.facts.sourceQpc=f.facts.nowQpc+1;}},{"zero source timestamp",[](auto&f){f.facts.sourceQpc=0;}},{"source older than250ms",[](auto&f){f.facts.nowQpc=1251;}},{"baseline acquired after show",[](auto&f){f.facts.baselineAcquiredQpc=1011;}},{"stale baseline from old attempt",[](auto&f){f.facts.baselinePresentQpc=490;f.facts.baselineAcquiredQpc=500;}},{"zero baseline present",[](auto&f){f.facts.baselinePresentQpc=0;}},{"baseline present after acquire",[](auto&f){f.facts.baselinePresentQpc=991;}},{"post-show frame actually pre-show",[](auto&f){f.facts.framePresentQpc=1009;}},{"frame older than source",[](auto&f){f.facts.sourceQpc=1021;}},{"frame present in future",[](auto&f){f.facts.framePresentQpc=1031;}},{"frame acquired in future",[](auto&f){f.facts.frameAcquiredQpc=1031;}},{"frame acquire before present",[](auto&f){f.facts.frameAcquiredQpc=1019;}},{"mouse-only zero present time",[](auto&f){f.facts.framePresentQpc=0;}},{"protected content masked",[](auto&f){f.facts.protectedContent=true;}},{"unimplemented display rotation",[](auto&f){f.facts.rotationIdentity=false;}},
 {"ordinary game output contains no overlay",[](auto&f){f.actual=f.baseline;}},{"source pixels already present before show",[](auto&f){f.baseline=f.source;}},{"uniform low-information source",[](auto&f){for(size_t i=0;i<f.source.size();i+=4){f.source[i]=64;f.source[i+1]=64;f.source[i+2]=64;}f.actual=f.source;}},{"transparent source grants no pixel proof",[](auto&f){for(size_t i=3;i<f.source.size();i+=4)f.source[i]=0;f.actual=f.source;}},{"fresh acquire cannot revive frozen old output",[](auto&f){f.facts.sourceQpc=1250;f.facts.nowQpc=1280;f.facts.frameAcquiredQpc=1279;}}
 };
 for(auto&c:bad){Fixture f;c.second(f);Check(!f.Valid(),c.first);}
 {Fixture f;f.facts.nowQpc=1250;Check(f.Valid(),"exact250ms age boundary remains eligible");}
 {Fixture f;auto s=f.Src();s.bgra=nullptr;Check(!Validate(s,f.Base(),f.Actual(),f.expected,f.facts),"null source rejected");s=f.Src();s.stride=4;Check(!Validate(s,f.Base(),f.Actual(),f.expected,f.facts),"short source stride rejected");auto b=f.Base();b.width++;Check(!Validate(f.Src(),b,f.Actual(),f.expected,f.facts),"baseline dimensions mismatch rejected");auto a=f.Actual();a.height--;Check(!Validate(f.Src(),f.Base(),a,f.expected,f.facts),"output dimensions mismatch rejected");}
 HdrChecks();PreparedSourceChecks();
 std::cout<<"{\"passed\":true,\"checks\":"<<checks<<",\"actualHeader\":\"ShellFreeOutputReceipt.h\",\"osCalls\":0,\"screensCaptured\":0,\"windowsCreated\":0,\"inputSent\":0}\n";return 0;
 }catch(const std::exception&e){std::cerr<<"FAIL after "<<checks<<": "<<e.what()<<"\n";return 1;}}
