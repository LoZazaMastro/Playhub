// SPDX-License-Identifier: GPL-3.0-only
#include "DirectCompositionSurface.h"
#include "DirectSurfaceCopy.h"
#include <dxgi1_2.h>
namespace playhub::mirror::direct {
bool CompositionSurface::Own()const noexcept{DWORD pid{};return window_&&thread_==GetCurrentThreadId()&&GetWindowThreadProcessId(window_,&pid)==thread_&&pid==GetCurrentProcessId()&&GetAncestor(window_,GA_ROOT)==window_;}
HRESULT CompositionSurface::Attach(ID3D11Device* device,HWND window,UINT width,UINT height,UINT outputWidth,UINT outputHeight){
 if(Attached()||window_||!device||!window||width<32||height<32||width>8192||height>8192||outputWidth<32||outputHeight<32||outputWidth>16384||outputHeight>16384)return Fail(E_INVALIDARG);
 window_=window;thread_=GetCurrentThreadId();width_=width;height_=height;
 if(!Own()){window_=nullptr;return Fail(E_ACCESSDENIED);}
 device_=device;device_->GetImmediateContext(&context_);if(!context_){Close();return Fail(E_UNEXPECTED);}
 Microsoft::WRL::ComPtr<IDXGIDevice> dxgi;HRESULT result=device_.As(&dxgi);
 if(SUCCEEDED(result))result=DCompositionCreateDevice(dxgi.Get(),IID_PPV_ARGS(&composition_));
 if(SUCCEEDED(result))result=composition_->CreateTargetForHwnd(window_,TRUE,&target_);
 if(SUCCEEDED(result))result=composition_->CreateVisual(&visual_);
 if(SUCCEEDED(result))result=composition_->CreateSurface(width,height,DXGI_FORMAT_B8G8R8A8_UNORM,DXGI_ALPHA_MODE_PREMULTIPLIED,&surface_);
 if(SUCCEEDED(result))result=visual_->SetContent(surface_.Get());
 if(SUCCEEDED(result))result=visual_->SetBitmapInterpolationMode(DCOMPOSITION_BITMAP_INTERPOLATION_MODE_NEAREST_NEIGHBOR);
 const D2D_MATRIX_3X2_F scale{float(outputWidth)/float(width),0,0,float(outputHeight)/float(height),0,0};
 if(SUCCEEDED(result))result=visual_->SetTransform(scale);
 if(SUCCEEDED(result))result=target_->SetRoot(visual_.Get());
 // No Commit before the first complete source update: newly created surfaces
 // contain no initialized pixels. Attach is not a frame/visibility receipt.
 if(FAILED(result)||!Own()){const HRESULT failure=FAILED(result)?result:E_ACCESSDENIED;Close();return Fail(failure);}
 return Fail(S_OK);
}
HRESULT CompositionSurface::Update(ID3D11Texture2D* source,std::uint64_t presentQpc){
 if(!Attached()||!Own()||!source||!presentQpc||presentQpc<sourceQpc_)return Fail(E_ACCESSDENIED);
 LARGE_INTEGER now{};if(!QueryPerformanceCounter(&now)||now.QuadPart<=0||presentQpc>static_cast<std::uint64_t>(now.QuadPart))return Fail(E_INVALIDARG);
 Microsoft::WRL::ComPtr<IDXGISurface> update;POINT offset{};
 HRESULT result=surface_->BeginDraw(nullptr,IID_PPV_ARGS(&update),&offset);if(FAILED(result))return Fail(result);
 Microsoft::WRL::ComPtr<ID3D11Texture2D> texture;result=update.As(&texture);
 if(FAILED(result)){Microsoft::WRL::ComPtr<IDXGISurface2> surface2;result=update.As(&surface2);if(SUCCEEDED(result)){UINT subresource{};result=surface2->GetResource(IID_PPV_ARGS(&texture),&subresource);if(SUCCEEDED(result)&&subresource!=0)result=E_INVALIDARG;}}
 if(SUCCEEDED(result))result=CopySurfacePixels(context_.Get(),source,texture.Get(),offset,width_,height_);
 if(SUCCEEDED(result))context_->Flush();
 // EndDraw is mandatory after every successful BeginDraw, including a failed
 // QI/geometry/device check. No partial copy is ever committed as success.
 const HRESULT ended=surface_->EndDraw();if(FAILED(result))return Fail(result);if(FAILED(ended))return Fail(ended);
 if(!Own())return Fail(E_ACCESSDENIED);
 result=composition_->Commit();if(FAILED(result))return Fail(result);
 if(!Own()||!QueryPerformanceCounter(&now)||now.QuadPart<=0)return Fail(E_ACCESSDENIED);
 sourceQpc_=presentQpc;commitQpc_=static_cast<std::uint64_t>(now.QuadPart);return Fail(S_OK);
}
HRESULT CompositionSurface::Close(){
 if(window_&&thread_!=GetCurrentThreadId())return Fail(RPC_E_WRONG_THREAD);
 HRESULT result=S_OK;if(target_)result=target_->SetRoot(nullptr);if(SUCCEEDED(result)&&composition_)result=composition_->Commit();
 surface_.Reset();visual_.Reset();target_.Reset();composition_.Reset();context_.Reset();device_.Reset();window_=nullptr;thread_=0;width_=height_=0;sourceQpc_=commitQpc_=0;return Fail(result);
}
CompositionSurface::~CompositionSurface(){Close();}
}
