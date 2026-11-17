// SPDX-License-Identifier: GPL-3.0-only
#include "DesktopOutputSampler.h"
#include <cstdio>
int main(){using namespace playhub::mirror::output;unsigned checks=0;
 auto check=[&](bool okay){if(!okay){std::fprintf(stderr,"format check %u failed\n",checks+1);return false;}++checks;return true;};
 auto actual=ReadbackDescription(3840,2160,DXGI_FORMAT_B8G8R8A8_UNORM);
 // Real failure: duplication metadata FP16(10), acquired texture BGRA8(87).
 const DXGI_FORMAT metadata=DXGI_FORMAT_R16G16B16A16_FLOAT;
 if(!check(actual.Format!=metadata)||!check(FrameDescriptionAccepted(actual,3840,2160)))return 1;
 for(auto format:{DXGI_FORMAT_B8G8R8A8_UNORM,DXGI_FORMAT_R16G16B16A16_FLOAT}){
  auto d=ReadbackDescription(3840,2160,format);
  if(!check(FrameDescriptionAccepted(d,3840,2160))||!check(d.Format==format&&d.Usage==D3D11_USAGE_STAGING&&d.CPUAccessFlags==D3D11_CPU_ACCESS_READ&&d.BindFlags==0&&d.MiscFlags==0))return 1;
  for(unsigned bad=0;bad<6;++bad){auto altered=d;switch(bad){case 0:altered.Width--;break;case 1:altered.Height--;break;case 2:altered.Format=DXGI_FORMAT_R8G8B8A8_UNORM;break;case 3:altered.ArraySize=2;break;case 4:altered.MipLevels=2;break;case 5:altered.SampleDesc.Count=2;break;}
   if(!check(!FrameDescriptionAccepted(altered,3840,2160)))return 1;
  }
 }
 std::printf("%u actual frame format/readback checks PASS; no GPU or OS calls\n",checks);return 0;
}
