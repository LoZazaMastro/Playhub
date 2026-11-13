#include "ContentCore.h"
#include "TargetCore.h"
#include <iostream>
#include <stdexcept>
#include <vector>
int Checks(){int n=0;auto check=[&](bool v){if(!v)throw std::runtime_error("pure content check failed");++n;};
 check(content::Fresh(1,2000001));check(!content::Fresh(1,2000002));check(!content::Fresh(2,1));check(!content::Fresh(0,1));
 check(content::Bounds(1920,1080,{0,0,1920,1080}));check(!content::Bounds(1920,1080,{-1,0,100,100}));
 check(!content::Bounds(1920,1080,{1900,0,100,100}));check(!content::Bounds(16384,16384,{0,0,32,18}));
 std::vector<std::uint8_t> pixels(64*36*4);for(std::size_t i=3;i<pixels.size();i+=4)pixels[i]=255;
 auto black=content::Measure(pixels.data(),pixels.size(),64*4,64,36);check(black&&black->samples==576&&black->opaque==576&&black->nonBlack==0&&black->mean==0);
 for(std::size_t i=0;i<pixels.size();i+=4)pixels[i]=pixels[i+1]=pixels[i+2]=255;
 auto white=content::Measure(pixels.data(),pixels.size(),64*4,64,36);check(white&&white->nonBlack==576&&white->mean==255&&white->variance==0);
 for(std::size_t i=3;i<pixels.size();i+=4)pixels[i]=0;
 auto transparent=content::Measure(pixels.data(),pixels.size(),64*4,64,36);check(transparent&&transparent->nonBlack==0&&transparent->opaque==0);
 check(!content::Measure(pixels.data(),pixels.size()-1,64*4,64,36));check(!content::Measure(pixels.data(),pixels.size(),1,64,36));
 check(!content::Measure(nullptr,0,0,64,36));
 for(unsigned y=0;y<36;y++)for(unsigned x=0;x<64;x++){auto i=(y*64+x)*4;const auto v=static_cast<std::uint8_t>(x<32?0:255);pixels[i]=pixels[i+1]=pixels[i+2]=v;pixels[i+3]=255;}
 auto split=content::Measure(pixels.data(),pixels.size(),64*4,64,36);check(split&&split->variance>10000&&split->nonBlack==288);
 progress::Target target;check(!progress::Parse("not-a-target",target));std::uint32_t value{};check(!progress::Decimal("4294967296",value));
 std::cout<<"PASS "<<n<<" pure stats/freshness/bounds/target checks; nativeCalls0\n";return 0;}
