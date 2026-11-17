// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <d3d11.h>
namespace playhub::mirror::direct {
// Exact GPU copy only. No alpha conversion, shader, sampling, scaling, CPU map
// or Present hook. The DComp update offset is not necessarily (0,0).
HRESULT CopySurfacePixels(ID3D11DeviceContext* context,ID3D11Texture2D* source,
 ID3D11Texture2D* update,POINT offset,UINT width,UINT height)noexcept;
}
