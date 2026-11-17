// SPDX-License-Identifier: GPL-3.0-only
#include "ExactSampleReadback.h"
#include "DesktopOutputSampler.h"
#include <wrl/client.h>
#include <vector>
#include <iostream>
#include <stdexcept>
#include <cstring>
#include <string>
using Microsoft::WRL::ComPtr;
using namespace playhub::mirror::output;
namespace {
unsigned checks=0;
void Check(bool value){++checks;if(!value)throw std::runtime_error("gather GPU check "+std::to_string(checks));}
ComPtr<ID3D11Texture2D> Source(ID3D11Device* device,unsigned width,unsigned height,DXGI_FORMAT format,const std::vector<std::uint8_t>& data){
 const unsigned bpp=format==DXGI_FORMAT_B8G8R8A8_UNORM?4:8;D3D11_TEXTURE2D_DESC d{};d.Width=width;d.Height=height;d.MipLevels=d.ArraySize=d.SampleDesc.Count=1;d.Format=format;d.Usage=D3D11_USAGE_IMMUTABLE;d.BindFlags=D3D11_BIND_SHADER_RESOURCE;
 const D3D11_SUBRESOURCE_DATA initial{data.data(),width*bpp,0};ComPtr<ID3D11Texture2D> texture;Check(SUCCEEDED(device->CreateTexture2D(&d,&initial,&texture)));return texture;
}
ComPtr<ID3D11Texture2D> Staging(ID3D11Device* device,unsigned width,unsigned height,DXGI_FORMAT format){const auto d=ReadbackDescription(width,height,format);ComPtr<ID3D11Texture2D> texture;Check(SUCCEEDED(device->CreateTexture2D(&d,nullptr,&texture)));return texture;}
}
int main(){try{
 ComPtr<ID3D11Device> device,foreign;ComPtr<ID3D11DeviceContext> context;D3D_FEATURE_LEVEL level{};const D3D_FEATURE_LEVEL levels[]={D3D_FEATURE_LEVEL_11_0};
 Check(SUCCEEDED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,levels,1,D3D11_SDK_VERSION,&device,&level,&context)));
 Check(SUCCEEDED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,levels,1,D3D11_SDK_VERSION,&foreign,&level,nullptr)));
 const unsigned dimensions[][2]={{32,32},{127,79},{1920,1080},{3840,2160},{16384,32},{32,16384}};
 std::size_t comparedBytes=0;
 for(const auto& size:dimensions)for(const auto format:{DXGI_FORMAT_B8G8R8A8_UNORM,DXGI_FORMAT_R16G16B16A16_FLOAT}){
  const unsigned w=size[0],h=size[1],bpp=format==DXGI_FORMAT_B8G8R8A8_UNORM?4:8;std::vector<std::uint8_t> data(std::size_t(w)*h*bpp);
  for(unsigned y=0;y<h;++y)for(unsigned x=0;x<w;++x)for(unsigned c=0;c<bpp;++c)data[(std::size_t(y)*w+x)*bpp+c]=static_cast<std::uint8_t>((x*31u+y*17u+c*47u)^(x>>8)^(y>>8));
  auto source=Source(device.Get(),w,h,format,data);
  for(const auto layout:{ImageLayout::SourceRows,ImageLayout::OutputRows}){
   const SampleGrid grid=layout==ImageLayout::SourceRows?SampleGrid{w,h}:SampleGrid{853,481};
   auto staging=Staging(device.Get(),w,StoredRows(layout,h),format);
   if(layout==ImageLayout::SourceRows&&format!=DXGI_FORMAT_B8G8R8A8_UNORM){Check(!CopyExactSampleRows(context.Get(),source.Get(),staging.Get(),layout,grid));continue;}
   Check(CopyExactSampleRows(context.Get(),source.Get(),staging.Get(),layout,grid));context->Flush();
   D3D11_MAPPED_SUBRESOURCE mapped{};Check(SUCCEEDED(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped)));Check(mapped.pData&&mapped.RowPitch>=w*bpp);
   const unsigned rows=layout==ImageLayout::SourceRows?3:1;bool equal=true;
   for(unsigned gy=0;gy<GridRows;++gy){const unsigned y=layout==ImageLayout::SourceRows?GridY(h,gy)-1:OutputGridY(grid.sourceHeight,h,gy);
    for(unsigned row=0;row<rows;++row){equal=equal&&std::memcmp(static_cast<const std::uint8_t*>(mapped.pData)+std::size_t(gy*rows+row)*mapped.RowPitch,data.data()+std::size_t(y+row)*w*bpp,std::size_t(w)*bpp)==0;comparedBytes+=std::size_t(w)*bpp;}
   }
   context->Unmap(staging.Get(),0);Check(equal);
  }
 }
 std::vector<std::uint8_t> data(64*64*4,123);auto source=Source(device.Get(),64,64,DXGI_FORMAT_B8G8R8A8_UNORM,data);
 auto proper=Staging(device.Get(),64,108,DXGI_FORMAT_B8G8R8A8_UNORM),wrongSize=Staging(device.Get(),63,108,DXGI_FORMAT_B8G8R8A8_UNORM),wrongFormat=Staging(device.Get(),64,108,DXGI_FORMAT_R16G16B16A16_FLOAT),foreignStaging=Staging(foreign.Get(),64,108,DXGI_FORMAT_B8G8R8A8_UNORM);
 Check(!CopyExactSampleRows(nullptr,source.Get(),proper.Get(),ImageLayout::SourceRows,{64,64}));
 Check(!CopyExactSampleRows(context.Get(),nullptr,proper.Get(),ImageLayout::SourceRows,{64,64}));
 Check(!CopyExactSampleRows(context.Get(),source.Get(),nullptr,ImageLayout::SourceRows,{64,64}));
 Check(!CopyExactSampleRows(context.Get(),source.Get(),source.Get(),ImageLayout::SourceRows,{64,64}));
 Check(!CopyExactSampleRows(context.Get(),source.Get(),wrongSize.Get(),ImageLayout::SourceRows,{64,64}));
 Check(!CopyExactSampleRows(context.Get(),source.Get(),wrongFormat.Get(),ImageLayout::SourceRows,{64,64}));
 Check(!CopyExactSampleRows(context.Get(),source.Get(),foreignStaging.Get(),ImageLayout::SourceRows,{64,64}));
 Check(!CopyExactSampleRows(context.Get(),source.Get(),proper.Get(),static_cast<ImageLayout>(77),{64,64}));
 Check(!CopyExactSampleRows(context.Get(),source.Get(),proper.Get(),ImageLayout::SourceRows,{63,64}));
 Check(!CopyExactSampleRows(context.Get(),source.Get(),proper.Get(),ImageLayout::SourceRows,{0,0}));
 Check(!CopyExactSampleRows(context.Get(),source.Get(),proper.Get(),ImageLayout::Full,{64,64}));
 Check(CopyExactSampleRows(context.Get(),source.Get(),proper.Get(),ImageLayout::SourceRows,{64,64}));
 std::cout<<checks<<" exact row-gather GPU checks PASS; "<<comparedBytes<<" bytes bit-identical. Generated textures only; no desktop/window/input/capture.\n";
 }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}return 0;
}
