// SPDX-License-Identifier: GPL-3.0-only
#include "ShellFreeOutputReceipt.h"
#include <vector>
#include <cstdio>
#include <cstdlib>
using namespace playhub::mirror::output;
static unsigned checks=0;
static void Check(bool v,const char*n){++checks;if(!v){std::fprintf(stderr,"FAIL %s\n",n);std::exit(1);}}
struct Img{
 ImageView view;std::vector<unsigned char> data;
 Img(unsigned w,unsigned h,PixelEncoding e=PixelEncoding::Bgra8):data(size_t(w)*h*(e==PixelEncoding::Bgra8?4:8)){view={w,h,w*(e==PixelEncoding::Bgra8?4:8),data.data(),data.size(),e,1000};}
 Img(const Img&)=delete;Img&operator=(const Img&)=delete;
};
static std::uint16_t HalfBits(float f){std::uint32_t b;std::memcpy(&b,&f,4);const int e=int((b>>23)&255)-127+15;unsigned m=b&0x7fffff;if(e<=0){if(e< -10)return 0;return std::uint16_t((m|0x800000)>>(14-e));}if(e>=31)return 0x7c00;return std::uint16_t((e<<10)|(m>>13));}
static void Color(Img&i,unsigned x,unsigned y,const unsigned char*s){auto*p=i.data.data()+size_t(y)*i.view.stride+size_t(x)*(i.view.encoding==PixelEncoding::Bgra8?4:8);if(i.view.encoding==PixelEncoding::Bgra8)std::memcpy(p,s,4);else{std::uint16_t q[4]={HalfBits(Linear(s[2])*i.view.sdrWhiteLevel/1000.f),HalfBits(Linear(s[1])*i.view.sdrWhiteLevel/1000.f),HalfBits(Linear(s[0])*i.view.sdrWhiteLevel/1000.f),HalfBits(1.f)};std::memcpy(p,q,8);}}
static std::vector<unsigned char> Pack(const Img&i,ImageLayout l,unsigned sh){unsigned rows=StoredRows(l,i.view.height);std::vector<unsigned char> p(size_t(i.view.stride)*rows);for(unsigned gy=0;gy<36;++gy){if(l==ImageLayout::SourceRows){unsigned y=GridY(i.view.height,gy);for(int dy=-1;dy<=1;++dy)std::memcpy(p.data()+size_t(gy*3+dy+1)*i.view.stride,i.data.data()+size_t(int(y)+dy)*i.view.stride,i.view.stride);}else{unsigned y=OutputGridY(sh,i.view.height,gy);std::memcpy(p.data()+size_t(gy)*i.view.stride,i.data.data()+size_t(y)*i.view.stride,i.view.stride);}}return p;}
static bool Same(Metrics a,Metrics b){return a.samples==b.samples&&a.matched==b.matched&&a.changed==b.changed&&a.colorCount==b.colorCount&&a.yBands==b.yBands&&a.changedBands==b.changedBands;}
static void Case(unsigned sw,unsigned sh,unsigned ow,unsigned oh,PixelEncoding encoding,unsigned variant){
 Img source(sw,sh),baseline(ow,oh,encoding),actual(ow,oh,encoding);baseline.view.sdrWhiteLevel=actual.view.sdrWhiteLevel=variant==6?3000:1000;
 for(unsigned y=0;y<sh;++y)for(unsigned x=0;x<sw;++x){unsigned char c[4]={static_cast<unsigned char>(32+(x*4/sw)*48),static_cast<unsigned char>(24+(y*4/sh)*48),static_cast<unsigned char>(80+((x*4/sw+y*4/sh)%3)*48),255};if(variant==1)c[3]=249;if(variant==2)c[0]=static_cast<unsigned char>((x+y)%255);if(variant==3)c[0]=c[1]=c[2]=20;Color(source,x,y,c);}
 for(unsigned gy=0;gy<36;++gy)for(unsigned gx=0;gx<64;++gx){const unsigned x=GridX(sw,gx),y=GridY(sh,gy),ox=std::min(ow-1,unsigned((std::uint64_t(x)*2+1)*ow/(std::uint64_t(sw)*2))),oy=OutputGridY(sh,oh,gy);auto*s=source.data.data()+size_t(y)*source.view.stride+x*4;Color(actual,ox,oy,s);if(variant==4)Color(baseline,ox,oy,s);}
 if(variant==5)for(size_t k=0;k<actual.data.size();++k)actual.data[k]=0;
 auto ps=Pack(source,ImageLayout::SourceRows,sh),pb=Pack(baseline,ImageLayout::OutputRows,sh),pa=Pack(actual,ImageLayout::OutputRows,sh);
 ImageView ss=source.view,bs=baseline.view,as=actual.view;ss.bgra=ps.data();ss.bytes=ps.size();ss.layout=ImageLayout::SourceRows;ss.grid={sw,sh};bs.bgra=pb.data();bs.bytes=pb.size();bs.layout=ImageLayout::OutputRows;bs.grid={sw,sh};as.bgra=pa.data();as.bytes=pa.size();as.layout=ImageLayout::OutputRows;as.grid={sw,sh};
 Binding bind{1,2,3,4,5,6,7};Facts f{bind,1000,950,960,980,1010,1020,1030,1000,false,true};Metrics mf,ms;
 bool full=Validate(source.view,baseline.view,actual.view,bind,f,&mf),sparse=Validate(ss,bs,as,bind,f,&ms);Check(full==sparse&&Same(mf,ms),"full/sparse decision+all six metrics");
 Metrics mm;Check(Validate(ss,baseline.view,actual.view,bind,f,&mm)==full&&Same(mf,mm),"source compact only");Check(Validate(source.view,bs,as,bind,f,&mm)==full&&Same(mf,mm),"output compact only");
 if(variant==0&&sw>=128&&ow>=128)Check(full,"positive exact samples");
 auto bad=as;bad.bytes--;Check(!Validate(ss,bs,bad,bind,f),"truncated output bytes");bad=ss;bad.bytes--;Check(!Validate(bad,bs,as,bind,f),"truncated source bytes");bad=as;bad.grid.sourceHeight++;Check(!Validate(ss,bs,bad,bind,f),"foreign grid");bad=ss;bad.layout=ImageLayout::OutputRows;Check(!Validate(bad,bs,as,bind,f),"source wrong layout");bad=as;bad.layout=ImageLayout::SourceRows;Check(!Validate(ss,bs,bad,bind,f),"output wrong layout");
 auto stale=f;stale.nowQpc=1300;Check(!Validate(ss,bs,as,bind,stale),"stale source/output");auto foreign=f;foreign.binding.sourceCookie++;Check(!Validate(ss,bs,as,bind,foreign),"source lease identity");
 if(encoding==PixelEncoding::ScRgbFloat16){bad=as;bad.sdrWhiteLevel++;Check(!Validate(ss,bs,bad,bind,f),"HDR white consistency");}
}
int main(){for(auto e:{PixelEncoding::Bgra8,PixelEncoding::ScRgbFloat16})for(unsigned v=0;v<7;++v){Case(128,144,256,288,e,v);Case(1920,1080,1280,720,e,v);Case(32,33,64,32,e,v);Case(16384,32,128,64,e,v);}std::printf("PASS %u actual header checks, 56 full/compact fixtures\n",checks);}
