// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <windows.h>
#include <d3d11_1.h>
#include <dxgi1_2.h>
#include <dcomp.h>
#include <wrl/client.h>
#include <array>

namespace playhub::diagnostic {
struct PixelSamples {
 std::array<unsigned char,4> transparentBgra{};
 std::array<unsigned char,4> panelBgra{};
 std::array<unsigned char,4> sentinelBgra{};
 bool transparentPixel{},premultipliedPixel{};
};

// Ordinary per-pixel-alpha compositor host. It owns no input hooks, Steam state,
// band broker or privilege. The HWND and texture must be supplied by its caller.
// PresentTexture is the reusable same-device frame path; the diagnostic drawing
// is only a test producer and is not an implementation of Steam/QAM content.
class CompositionPresenter final {
public:
 HRESULT Initialize(HWND target,UINT width,UINT height);
 HRESULT RenderDiagnosticFrame(unsigned frame,PixelSamples* beforePresentation=nullptr);
 HRESULT PresentTexture(ID3D11Texture2D* source,PixelSamples* beforePresentation=nullptr);
 HRESULT ReadbackSamples(PixelSamples& output);
 HRESULT Commit();
 HRESULT FrameStatistics(DCOMPOSITION_FRAME_STATISTICS& output);
 ID3D11Device* Device() const {return device_.Get();}
 HRESULT DeviceRemovedReason() const {return device_?device_->GetDeviceRemovedReason():E_UNEXPECTED;}
 D3D_FEATURE_LEVEL FeatureLevel() const {return featureLevel_;}
private:
 HRESULT Present();
 UINT width_{},height_{};
 D3D_FEATURE_LEVEL featureLevel_{};
 Microsoft::WRL::ComPtr<ID3D11Device> device_;
 Microsoft::WRL::ComPtr<ID3D11DeviceContext> context_;
 Microsoft::WRL::ComPtr<ID3D11DeviceContext1> context1_;
 Microsoft::WRL::ComPtr<IDXGISwapChain1> swap_;
 Microsoft::WRL::ComPtr<ID3D11Texture2D> back_,staging_;
 Microsoft::WRL::ComPtr<ID3D11RenderTargetView> target_;
 Microsoft::WRL::ComPtr<IDCompositionDevice> composition_;
 Microsoft::WRL::ComPtr<IDCompositionTarget> hwndTarget_;
 Microsoft::WRL::ComPtr<IDCompositionVisual> visual_;
};
}

