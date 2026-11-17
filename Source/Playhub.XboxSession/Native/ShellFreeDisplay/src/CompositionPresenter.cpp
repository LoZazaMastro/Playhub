// SPDX-License-Identifier: GPL-3.0-only
#include "CompositionPresenter.h"
#include <algorithm>
#include <cstring>
using Microsoft::WRL::ComPtr;
namespace playhub::diagnostic {
HRESULT CompositionPresenter::Initialize(HWND hwnd,UINT width,UINT height){
 if(!hwnd||!IsWindow(hwnd)||width<144||height<128||width>8192||height>8192)return E_INVALIDARG;
 width_=width;height_=height;
 HRESULT hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
  nullptr,0,D3D11_SDK_VERSION,&device_,&featureLevel_,&context_);
 if(FAILED(hr))return hr;
 if(FAILED(hr=context_.As(&context1_)))return hr;
 ComPtr<IDXGIDevice> dxgi;ComPtr<IDXGIAdapter> adapter;ComPtr<IDXGIFactory2> factory;
 if(FAILED(hr=device_.As(&dxgi))||FAILED(hr=dxgi->GetAdapter(&adapter))||
    FAILED(hr=adapter->GetParent(IID_PPV_ARGS(&factory))))return hr;
 DXGI_SWAP_CHAIN_DESC1 desc{};desc.Width=width;desc.Height=height;desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
 desc.SampleDesc.Count=1;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.BufferCount=2;
 desc.Scaling=DXGI_SCALING_STRETCH;desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
 desc.AlphaMode=DXGI_ALPHA_MODE_PREMULTIPLIED;
 if(FAILED(hr=factory->CreateSwapChainForComposition(device_.Get(),&desc,nullptr,&swap_))||
    FAILED(hr=swap_->GetBuffer(0,IID_PPV_ARGS(&back_)))||
    FAILED(hr=device_->CreateRenderTargetView(back_.Get(),nullptr,&target_)))return hr;
 D3D11_TEXTURE2D_DESC readDesc{};back_->GetDesc(&readDesc);readDesc.Usage=D3D11_USAGE_STAGING;
 readDesc.BindFlags=0;readDesc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;readDesc.MiscFlags=0;
 if(FAILED(hr=device_->CreateTexture2D(&readDesc,nullptr,&staging_))||
    FAILED(hr=DCompositionCreateDevice(dxgi.Get(),IID_PPV_ARGS(&composition_)))||
    FAILED(hr=composition_->CreateTargetForHwnd(hwnd,TRUE,&hwndTarget_))||
    FAILED(hr=composition_->CreateVisual(&visual_))||
    FAILED(hr=visual_->SetContent(swap_.Get()))||
    FAILED(hr=hwndTarget_->SetRoot(visual_.Get())))return hr;
 return Commit();
}
HRESULT CompositionPresenter::RenderDiagnosticFrame(unsigned frame,PixelSamples* beforePresentation){
 if(!context1_||!target_)return E_UNEXPECTED;
 const float clear[4]={0,0,0,0};context_->ClearRenderTargetView(target_.Get(),clear);
 // Premultiplied 50% alpha: RGB is already multiplied by alpha.
 const float panel[4]={0.125f,0.25f,0.375f,0.5f};
 const D3D11_RECT panelRect{32,32,static_cast<LONG>(width_)-32,static_cast<LONG>(height_)-32};
 context1_->ClearView(target_.Get(),panel,&panelRect,1);
 const LONG x=48+static_cast<LONG>(frame%120)*static_cast<LONG>(width_-144)/120;
 const D3D11_RECT indicator{x,static_cast<LONG>(height_)-64,x+32,static_cast<LONG>(height_)-48};
 const float yellow[4]={1,0.8f,0,1};context1_->ClearView(target_.Get(),yellow,&indicator,1);
 if(beforePresentation){HRESULT hr=ReadbackSamples(*beforePresentation);if(FAILED(hr))return hr;}
 return Present();
}
HRESULT CompositionPresenter::PresentTexture(ID3D11Texture2D* source,PixelSamples* beforePresentation){
 if(!source||!back_)return E_INVALIDARG;
 ComPtr<ID3D11Device> owner;source->GetDevice(&owner);
 if(owner.Get()!=device_.Get())return E_INVALIDARG;
 D3D11_TEXTURE2D_DESC a{},b{};source->GetDesc(&a);back_->GetDesc(&b);
 if(a.Width!=b.Width||a.Height!=b.Height||a.Format!=b.Format||a.SampleDesc.Count!=1||a.ArraySize!=1||a.MipLevels!=1)
  return E_INVALIDARG;
 context_->CopyResource(back_.Get(),source);
 if(beforePresentation){HRESULT hr=ReadbackSamples(*beforePresentation);if(FAILED(hr))return hr;}
 return Present();
}
HRESULT CompositionPresenter::Present(){
 // Never wait for inactive-desktop vblank. S_OK/OCCLUDED is recorded verbatim;
 // Present success is not claimed as a physically visible display frame.
 return swap_->Present(0,DXGI_PRESENT_DO_NOT_WAIT);
}
HRESULT CompositionPresenter::ReadbackSamples(PixelSamples& output){
 if(!staging_||!back_)return E_UNEXPECTED;
 context_->CopyResource(staging_.Get(),back_.Get());
 D3D11_MAPPED_SUBRESOURCE mapped{};HRESULT hr=context_->Map(staging_.Get(),0,D3D11_MAP_READ,0,&mapped);
 if(FAILED(hr))return hr;
 const auto* p=static_cast<const unsigned char*>(mapped.pData);
 std::memcpy(output.transparentBgra.data(),p+4*4+mapped.RowPitch*4,4);
 std::memcpy(output.panelBgra.data(),p+4*(width_/2)+mapped.RowPitch*(height_/2),4);
 std::memcpy(output.sentinelBgra.data(),p+4*64+mapped.RowPitch*64,4);
 context_->Unmap(staging_.Get(),0);
 output.transparentPixel=std::all_of(output.transparentBgra.begin(),output.transparentBgra.end(),[](auto v){return v==0;});
 const auto& v=output.panelBgra;
 output.premultipliedPixel=v[0]>=94&&v[0]<=98&&v[1]>=62&&v[1]<=66&&v[2]>=30&&v[2]<=34&&v[3]>=126&&v[3]<=130;
 return S_OK;
}
HRESULT CompositionPresenter::Commit(){return composition_?composition_->Commit():E_UNEXPECTED;}
HRESULT CompositionPresenter::FrameStatistics(DCOMPOSITION_FRAME_STATISTICS& output){
 return composition_?composition_->GetFrameStatistics(&output):E_UNEXPECTED;
}
}

