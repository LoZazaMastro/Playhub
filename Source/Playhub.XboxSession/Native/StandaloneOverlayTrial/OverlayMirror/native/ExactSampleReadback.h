// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "ExactSampleLayout.h"
#include <d3d11.h>
namespace playhub::mirror::output {
// Bit-exact GPU copies of the original rows used by Validate. No shader,
// filtering, colour conversion or graphics-pipeline state mutation.
bool CopyExactSampleRows(ID3D11DeviceContext* context,ID3D11Texture2D* source,ID3D11Texture2D* destination,ImageLayout layout,SampleGrid grid)noexcept;
}
