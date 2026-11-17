// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <windows.h>
#include <d3d11.h>
#include <dcomp.h>
#include <wrl/client.h>
#include <cstdint>
namespace playhub::mirror::direct {
// Content-only renderer. It creates no HWND, never activates a window, changes
// no band/input and grants no visibility authority. The caller retains all
// source/game/lease proof and must independently verify physical output.
class CompositionSurface final {
public:
 CompositionSurface()=default;
 CompositionSurface(const CompositionSurface&)=delete;
 CompositionSurface& operator=(const CompositionSurface&)=delete;
 HRESULT Attach(ID3D11Device* sourceDevice,HWND ownedTarget,UINT sourceWidth,UINT sourceHeight,UINT outputWidth,UINT outputHeight);
 HRESULT Update(ID3D11Texture2D* exactSource,std::uint64_t sourcePresentQpc);
 HRESULT Close();
 bool Attached()const noexcept{return surface_&&visual_&&target_&&composition_;}
 std::uint64_t SourceQpc()const noexcept{return sourceQpc_;}
 std::uint64_t CommitQpc()const noexcept{return commitQpc_;}
 HRESULT LastResult()const noexcept{return lastResult_;}
 ~CompositionSurface();
private:
 bool Own()const noexcept;
 HRESULT Fail(HRESULT result)noexcept{lastResult_=result;return result;}
 Microsoft::WRL::ComPtr<ID3D11Device> device_;
 Microsoft::WRL::ComPtr<ID3D11DeviceContext> context_;
 Microsoft::WRL::ComPtr<IDCompositionDevice> composition_;
 Microsoft::WRL::ComPtr<IDCompositionTarget> target_;
 Microsoft::WRL::ComPtr<IDCompositionVisual> visual_;
 Microsoft::WRL::ComPtr<IDCompositionSurface> surface_;
 HWND window_{};DWORD thread_{};UINT width_{},height_{};std::uint64_t sourceQpc_{},commitQpc_{};HRESULT lastResult_=S_FALSE;
};
}
