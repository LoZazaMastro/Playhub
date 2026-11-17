// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "Win32MirrorBackend.h"
#include "ExactSampleLayout.h"
#include <d3d11.h>
#include <functional>
#include <wrl/client.h>
#include <vector>
namespace playhub::mirror {
struct SourcePixels {std::uint32_t width{},height{},stride{};std::vector<std::uint8_t> bgra;std::uint64_t presentQpc{};output::ImageLayout layout{};output::SampleGrid grid{};};
struct SourceTextureFrame {Microsoft::WRL::ComPtr<ID3D11Texture2D> texture;std::uint64_t presentQpc{};};
using SourceTextureReader=std::function<bool(SourceTextureFrame&)>;
using SourcePixelsReader=std::function<bool(SourcePixels&)>;
// Experimental ordinary compositor destination. Its read-back band is exactly1;
// it never invokes C449 or represents its readiness as a band16 receipt.
// SourceLease/authority/input ownership remain the existing exact contracts.
std::shared_ptr<IBackend> CreateShellFreeWin32MirrorBackend(ID3D11Device* sourceDevice=nullptr,SourcePixelsReader reader={},SourceTextureReader textureReader={});
void DrainRetainedShellFreeMirrorCleanup();
}
