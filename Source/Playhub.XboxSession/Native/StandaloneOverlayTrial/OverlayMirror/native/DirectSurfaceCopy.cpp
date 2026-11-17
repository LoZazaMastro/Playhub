// SPDX-License-Identifier: GPL-3.0-only
#include "DirectSurfaceCopy.h"
#include <wrl/client.h>
#include <cstdint>
namespace playhub::mirror::direct {
HRESULT CopySurfacePixels(ID3D11DeviceContext* context,ID3D11Texture2D* source,ID3D11Texture2D* update,POINT offset,UINT width,UINT height)noexcept{
 if(!context||!source||!update||source==update||width<32||height<32||width>8192||height>8192||offset.x<0||offset.y<0)return E_INVALIDARG;
 D3D11_TEXTURE2D_DESC s{},d{};source->GetDesc(&s);update->GetDesc(&d);
 if(s.Width!=width||s.Height!=height||s.Format!=DXGI_FORMAT_B8G8R8A8_UNORM||s.ArraySize!=1||s.MipLevels!=1||s.SampleDesc.Count!=1||s.SampleDesc.Quality!=0
  ||d.Format!=s.Format||d.ArraySize!=1||d.MipLevels!=1||d.SampleDesc.Count!=1||d.SampleDesc.Quality!=0||d.Usage!=D3D11_USAGE_DEFAULT
  ||std::uint64_t(static_cast<UINT>(offset.x))+width>d.Width||std::uint64_t(static_cast<UINT>(offset.y))+height>d.Height)return E_INVALIDARG;
 Microsoft::WRL::ComPtr<ID3D11Device> a,b,c;context->GetDevice(&a);source->GetDevice(&b);update->GetDevice(&c);
 if(!a||a.Get()!=b.Get()||a.Get()!=c.Get())return E_INVALIDARG;
 if(FAILED(a->GetDeviceRemovedReason()))return a->GetDeviceRemovedReason();
 const D3D11_BOX box{0,0,0,width,height,1};context->CopySubresourceRegion(update,0,static_cast<UINT>(offset.x),static_cast<UINT>(offset.y),0,source,0,&box);
 return a->GetDeviceRemovedReason();
}
}
