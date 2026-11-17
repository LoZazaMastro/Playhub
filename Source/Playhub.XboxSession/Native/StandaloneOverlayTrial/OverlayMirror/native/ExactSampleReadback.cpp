// SPDX-License-Identifier: GPL-3.0-only
#include "ExactSampleReadback.h"
#include <wrl/client.h>
namespace playhub::mirror::output {
bool CopyExactSampleRows(ID3D11DeviceContext* context,ID3D11Texture2D* source,ID3D11Texture2D* destination,ImageLayout layout,SampleGrid grid)noexcept{
 if(!context||!source||!destination||source==destination)return false;
 if(layout!=ImageLayout::Full&&layout!=ImageLayout::SourceRows&&layout!=ImageLayout::OutputRows)return false;
 D3D11_TEXTURE2D_DESC s{},d{};source->GetDesc(&s);destination->GetDesc(&d);
 if(s.Width<32||s.Height<32||s.Width>16384||s.Height>16384||s.ArraySize!=1||s.MipLevels!=1||s.SampleDesc.Count!=1||(s.Format!=DXGI_FORMAT_B8G8R8A8_UNORM&&s.Format!=DXGI_FORMAT_R16G16B16A16_FLOAT))return false;
 if(layout==ImageLayout::Full?(grid.sourceWidth||grid.sourceHeight):!ValidGrid(grid))return false;
 if(layout==ImageLayout::SourceRows&&(s.Format!=DXGI_FORMAT_B8G8R8A8_UNORM||grid.sourceWidth!=s.Width||grid.sourceHeight!=s.Height))return false;
 if(d.Width!=s.Width||d.Height!=StoredRows(layout,s.Height)||d.Format!=s.Format||d.ArraySize!=1||d.MipLevels!=1||d.SampleDesc.Count!=1||d.Usage!=D3D11_USAGE_STAGING||d.BindFlags||d.MiscFlags||d.CPUAccessFlags!=D3D11_CPU_ACCESS_READ)return false;
 Microsoft::WRL::ComPtr<ID3D11Device> a,b,c;context->GetDevice(&a);source->GetDevice(&b);destination->GetDevice(&c);if(!a||a.Get()!=b.Get()||a.Get()!=c.Get())return false;
 if(layout==ImageLayout::Full){context->CopyResource(destination,source);return true;}
 const unsigned rows=layout==ImageLayout::SourceRows?3u:1u;
 for(unsigned gy=0;gy<GridRows;++gy){
  const unsigned y=layout==ImageLayout::SourceRows?GridY(s.Height,gy)-1:OutputGridY(grid.sourceHeight,s.Height,gy);
  if(y>=s.Height||rows>s.Height-y)return false;
  const D3D11_BOX box{0,y,0,s.Width,y+rows,1};context->CopySubresourceRegion(destination,0,0,gy*rows,0,source,0,&box);
 }
 return true;
}
}
