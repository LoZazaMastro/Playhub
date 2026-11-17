// SPDX-License-Identifier: GPL-3.0-only
#include "DirectSurfaceCopy.h"
#include <wrl/client.h>
#include <vector>
#include <algorithm>
#include <cstdio>
#include <stdexcept>
using Microsoft::WRL::ComPtr;
using playhub::mirror::direct::CopySurfacePixels;
unsigned checks=0;
void Check(bool ok){++checks;if(!ok)throw std::runtime_error("GPU copy contract");}
ComPtr<ID3D11Texture2D> Texture(ID3D11Device* device,UINT width,UINT height,DXGI_FORMAT format,D3D11_USAGE usage,UINT array=1,UINT mips=1,const std::vector<unsigned char>* pixels=nullptr){
 D3D11_TEXTURE2D_DESC d{};d.Width=width;d.Height=height;d.MipLevels=mips;d.ArraySize=array;d.Format=format;d.SampleDesc.Count=1;d.Usage=usage;d.BindFlags=usage==D3D11_USAGE_STAGING?0:D3D11_BIND_SHADER_RESOURCE;d.CPUAccessFlags=usage==D3D11_USAGE_STAGING?D3D11_CPU_ACCESS_READ:0;
 D3D11_SUBRESOURCE_DATA initial{};if(pixels){initial.pSysMem=pixels->data();initial.SysMemPitch=width*4;}
 ComPtr<ID3D11Texture2D> texture;Check(SUCCEEDED(device->CreateTexture2D(&d,pixels?&initial:nullptr,&texture)));return texture;
}
std::vector<unsigned char> Read(ID3D11Device* device,ID3D11DeviceContext* context,ID3D11Texture2D* texture){D3D11_TEXTURE2D_DESC d{};texture->GetDesc(&d);auto stage=Texture(device,d.Width,d.Height,d.Format,D3D11_USAGE_STAGING);context->CopyResource(stage.Get(),texture);D3D11_MAPPED_SUBRESOURCE map{};Check(SUCCEEDED(context->Map(stage.Get(),0,D3D11_MAP_READ,0,&map)));std::vector<unsigned char> pixels(std::size_t(d.Width)*d.Height*4);for(UINT y=0;y<d.Height;++y)std::copy_n(static_cast<unsigned char*>(map.pData)+std::size_t(y)*map.RowPitch,d.Width*4,pixels.data()+std::size_t(y)*d.Width*4);context->Unmap(stage.Get(),0);return pixels;}
int main(){try{
 ComPtr<ID3D11Device> device,foreign;ComPtr<ID3D11DeviceContext> context,foreignContext;D3D_FEATURE_LEVEL feature{};
 Check(SUCCEEDED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,&feature,&context)));
 Check(SUCCEEDED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&foreign,&feature,&foreignContext)));
 for(auto size:{32u,64u,127u}){
  const UINT width=size,height=size+3,dw=width+17,dh=height+19;
  std::vector<unsigned char> original(std::size_t(width)*height*4),blank(std::size_t(dw)*dh*4,37);
  for(UINT y=0;y<height;++y)for(UINT x=0;x<width;++x){const auto p=(std::size_t(y)*width+x)*4;original[p]=static_cast<unsigned char>(x*7);original[p+1]=static_cast<unsigned char>(y*11);original[p+2]=static_cast<unsigned char>(x+y);original[p+3]=static_cast<unsigned char>((x+y*3)%256);}
  auto source=Texture(device.Get(),width,height,DXGI_FORMAT_B8G8R8A8_UNORM,D3D11_USAGE_IMMUTABLE,1,1,&original);
  auto target=Texture(device.Get(),dw,dh,DXGI_FORMAT_B8G8R8A8_UNORM,D3D11_USAGE_DEFAULT,1,1,&blank);
  const auto untouched=Read(device.Get(),context.Get(),target.Get());Check(untouched==blank);bool priorMatches=true;
  for(UINT y=0;y<height;++y)for(UINT x=0;x<width;++x)for(UINT c=0;c<4;++c)if(untouched[(std::size_t(y+7)*dw+x+5)*4+c]!=original[(std::size_t(y)*width+x)*4+c])priorMatches=false;
  Check(!priorMatches); // Actual target region lacks the source before the GPU copy.
  Check(SUCCEEDED(CopySurfacePixels(context.Get(),source.Get(),target.Get(),{5,7},width,height)));
  auto copied=Read(device.Get(),context.Get(),target.Get());
  for(UINT y=0;y<dh;++y)for(UINT x=0;x<dw;++x){const bool inside=x>=5&&x<width+5&&y>=7&&y<height+7;for(UINT c=0;c<4;++c){const auto expected=inside?original[(std::size_t(y-7)*width+(x-5))*4+c]:37;Check(copied[(std::size_t(y)*dw+x)*4+c]==expected);}}
  Check(Read(device.Get(),context.Get(),source.Get())==original);
  Check(FAILED(CopySurfacePixels(nullptr,source.Get(),target.Get(),{0,0},width,height)));
  Check(FAILED(CopySurfacePixels(context.Get(),nullptr,target.Get(),{0,0},width,height)));
  Check(FAILED(CopySurfacePixels(context.Get(),source.Get(),source.Get(),{0,0},width,height)));
  Check(FAILED(CopySurfacePixels(context.Get(),source.Get(),target.Get(),{-1,0},width,height)));
  Check(FAILED(CopySurfacePixels(context.Get(),source.Get(),target.Get(),{0,-1},width,height)));
  Check(FAILED(CopySurfacePixels(context.Get(),source.Get(),target.Get(),{18,0},width,height)));
  Check(FAILED(CopySurfacePixels(context.Get(),source.Get(),target.Get(),{0,20},width,height)));
  Check(FAILED(CopySurfacePixels(context.Get(),source.Get(),target.Get(),{0,0},width+1,height)));
  Check(FAILED(CopySurfacePixels(foreignContext.Get(),source.Get(),target.Get(),{0,0},width,height)));
  auto other=Texture(foreign.Get(),width,height,DXGI_FORMAT_B8G8R8A8_UNORM,D3D11_USAGE_DEFAULT);
  Check(FAILED(CopySurfacePixels(context.Get(),other.Get(),target.Get(),{0,0},width,height)));
  auto wrongFormat=Texture(device.Get(),dw,dh,DXGI_FORMAT_R8G8B8A8_UNORM,D3D11_USAGE_DEFAULT);
  Check(FAILED(CopySurfacePixels(context.Get(),source.Get(),wrongFormat.Get(),{0,0},width,height)));
  auto stage=Texture(device.Get(),dw,dh,DXGI_FORMAT_B8G8R8A8_UNORM,D3D11_USAGE_STAGING);
  Check(FAILED(CopySurfacePixels(context.Get(),source.Get(),stage.Get(),{0,0},width,height)));
  auto array=Texture(device.Get(),width,height,DXGI_FORMAT_B8G8R8A8_UNORM,D3D11_USAGE_DEFAULT,2);
  Check(FAILED(CopySurfacePixels(context.Get(),array.Get(),target.Get(),{0,0},width,height)));
  auto mips=Texture(device.Get(),width,height,DXGI_FORMAT_B8G8R8A8_UNORM,D3D11_USAGE_DEFAULT,1,2);
  Check(FAILED(CopySurfacePixels(context.Get(),mips.Get(),target.Get(),{0,0},width,height)));
 }
 std::printf("{\"passed\":true,\"checks\":%u,\"generatedGpuTexturesOnly\":true,\"alphaBytesUnchanged\":true,\"windowsCreated\":0,\"desktopFramesCaptured\":0,\"inputSent\":0}\n",checks);return 0;
}catch(const std::exception& e){std::fprintf(stderr,"FAIL check%u: %s\n",checks,e.what());return 1;}}
