// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "ExactSampleLayout.h"
#include <windows.h>
#include <d3d11.h>
#include <cstdint>
#include <memory>
#include <vector>

namespace playhub::mirror::output {
inline bool FrameDescriptionAccepted(const D3D11_TEXTURE2D_DESC& d,UINT width,UINT height)noexcept{
 return d.Width==width&&d.Height==height&&(d.Format==DXGI_FORMAT_B8G8R8A8_UNORM||d.Format==DXGI_FORMAT_R16G16B16A16_FLOAT)
  &&d.ArraySize==1&&d.MipLevels==1&&d.SampleDesc.Count==1;
}
inline D3D11_TEXTURE2D_DESC ReadbackDescription(UINT width,UINT height,DXGI_FORMAT format)noexcept{
 D3D11_TEXTURE2D_DESC d{};d.Width=width;d.Height=height;d.MipLevels=d.ArraySize=1;d.Format=format;
 d.SampleDesc.Count=1;d.Usage=D3D11_USAGE_STAGING;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;return d;
}
enum class SamplerStage : std::uint32_t {None,Input,SourceInterface,SourceAdapter,AdapterDescription,EnumerateOutput,OutputDescription,AmbiguousOutput,DetachedOutput,OutputRotation,OutputDimensions,OutputNotFound,OutputCurrentBefore,CreateDevice,OutputInterface,DuplicateOutput,DuplicateDescription,CreateStaging,OutputCurrentAfter,Ready,Allocation,CallerBounds,PhysicalDpiRead,PhysicalDpiRestore,WhiteLevel,FrameFormat,AcquireInput,AcquireCurrent,AcquireDeadline,AcquireNextFrame,ProtectedFrame,NoDesktopPresent,FrameResource,FrameInterface,MapTimeout,MapReadback,MappedShape,ReadbackDeadline,ReadbackClock,FrameRelease,Acquired};
inline bool NativeObservationRetryable(SamplerStage stage,HRESULT result)noexcept{
 return (stage==SamplerStage::NoDesktopPresent&&result==DXGI_ERROR_WAIT_TIMEOUT)
  ||(stage==SamplerStage::AcquireNextFrame&&result==DXGI_ERROR_WAIT_TIMEOUT)
  ||(stage==SamplerStage::MapTimeout&&result==DXGI_ERROR_WAS_STILL_DRAWING)
  ||(stage==SamplerStage::ReadbackDeadline&&result==DXGI_ERROR_WAIT_TIMEOUT);
}
struct SamplerDiagnostic {SamplerStage stage{};HRESULT result{};LUID adapter{};std::uintptr_t wantedMonitor{},observedMonitor{};std::uint32_t outputIndex{},outputRotation{},duplicateRotation{},desktopWidth{},desktopHeight{},duplicateWidth{},duplicateHeight{},duplicateFormat{},featureLevel{},duplicateApi{},sdrWhiteLevel{},frameFormat{},acquireBudgetMs{},acquireWaitMs{},mapWaitMs{},copyMs{},acquireElapsedMs{};std::uint64_t framePresentQpc{};RECT outputBounds{},monitorBounds{},callerBounds{};};
inline const char* StageName(SamplerStage stage)noexcept{switch(stage){case SamplerStage::None:return "not-called";case SamplerStage::Input:return "input";case SamplerStage::SourceInterface:return "source-interface";case SamplerStage::SourceAdapter:return "source-adapter";case SamplerStage::AdapterDescription:return "adapter-description";case SamplerStage::EnumerateOutput:return "enumerate-output";case SamplerStage::OutputDescription:return "output-description";case SamplerStage::AmbiguousOutput:return "ambiguous-output";case SamplerStage::DetachedOutput:return "detached-output";case SamplerStage::OutputRotation:return "output-rotation";case SamplerStage::OutputDimensions:return "output-dimensions";case SamplerStage::OutputNotFound:return "output-not-found";case SamplerStage::OutputCurrentBefore:return "output-current-before";case SamplerStage::CreateDevice:return "create-device";case SamplerStage::OutputInterface:return "output-interface";case SamplerStage::DuplicateOutput:return "duplicate-output";case SamplerStage::DuplicateDescription:return "duplicate-description";case SamplerStage::CreateStaging:return "create-staging";case SamplerStage::OutputCurrentAfter:return "output-current-after";case SamplerStage::Ready:return "ready";case SamplerStage::Allocation:return "allocation";case SamplerStage::CallerBounds:return "caller-bounds";case SamplerStage::PhysicalDpiRead:return "physical-dpi-read";case SamplerStage::PhysicalDpiRestore:return "physical-dpi-restore";case SamplerStage::WhiteLevel:return "sdr-white-level";case SamplerStage::FrameFormat:return "frame-format";case SamplerStage::AcquireInput:return "acquire-input";case SamplerStage::AcquireCurrent:return "acquire-current";case SamplerStage::AcquireDeadline:return "acquire-deadline";case SamplerStage::AcquireNextFrame:return "acquire-next-frame";case SamplerStage::ProtectedFrame:return "protected-frame";case SamplerStage::NoDesktopPresent:return "no-desktop-present";case SamplerStage::FrameResource:return "frame-resource";case SamplerStage::FrameInterface:return "frame-interface";case SamplerStage::MapTimeout:return "map-timeout";case SamplerStage::MapReadback:return "map-readback";case SamplerStage::MappedShape:return "mapped-shape";case SamplerStage::ReadbackDeadline:return "readback-deadline";case SamplerStage::ReadbackClock:return "readback-clock";case SamplerStage::FrameRelease:return "frame-release";case SamplerStage::Acquired:return "acquired";}return "unknown-stage";}
struct Pixels {
 std::uint32_t width{},height{},stride{};
 std::vector<std::uint8_t> bgra;
 std::uint64_t presentQpc{},acquiredQpc{};ImageLayout layout{};SampleGrid grid{};
 bool protectedContent{};
 bool scRgbFloat16{};std::uint32_t sdrWhiteLevel{1000};
};
// GUI-thread-owned sampler. Pixels are temporary memory, never persisted.
// The caller must compare presentQpc with its own post-Show QPC and source receipt.
class Sampler final {
 struct State;
 std::unique_ptr<State> state;
 SamplerDiagnostic diagnostic{};
 bool Fail(SamplerStage stage,HRESULT result)noexcept{diagnostic.stage=stage;diagnostic.result=result;return false;}
public:
 Sampler();
 ~Sampler();
 Sampler(const Sampler&)=delete;
 Sampler& operator=(const Sampler&)=delete;
 bool Open(ID3D11Device* sourceDevice,HMONITOR exactMonitor);
 bool Acquire(std::uint32_t timeoutMs,Pixels& result,SampleGrid grid={});
 void Close();
 RECT Bounds()const noexcept;
 RECT CallerBounds()const noexcept;
 LUID AdapterLuid()const noexcept;
 SamplerDiagnostic ReadDiagnostic()const noexcept{return diagnostic;}
};
}
