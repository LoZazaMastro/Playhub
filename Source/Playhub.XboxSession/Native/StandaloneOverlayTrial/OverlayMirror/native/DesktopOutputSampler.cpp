// SPDX-License-Identifier: GPL-3.0-only
#include "DesktopOutputSampler.h"
#include "ExactSampleReadback.h"
#include <dxgi1_5.h>
#include <wrl/client.h>
#include <algorithm>
#include <cstring>
#include <new>
#include <cwchar>

namespace playhub::mirror::output {
using Microsoft::WRL::ComPtr;
namespace {
constexpr std::uint64_t MaximumPixels=64ull*1024ull*1024ull;
bool SameRect(const RECT& a,const RECT& b)noexcept{return a.left==b.left&&a.top==b.top&&a.right==b.right&&a.bottom==b.bottom;}
// Only this sampler's own DXGI operations use physical pixels. Restore the
// source/window/lease coordinate context before returning to the caller.
class PhysicalReadScope final {
 DPI_AWARENESS_CONTEXT previous{};
public:
 PhysicalReadScope()noexcept:previous(SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)){}
 bool Valid()const noexcept{return previous&&AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(),DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);}
 bool Restore()noexcept{if(!previous)return false;auto original=previous;if(!SetThreadDpiAwarenessContext(original)||!AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(),original))return false;previous=nullptr;return true;}
 ~PhysicalReadScope(){if(previous)SetThreadDpiAwarenessContext(previous);}
};
bool PhysicalDescription(IDXGIOutput* output,DXGI_OUTPUT_DESC& desc,MONITORINFO* monitor,HRESULT* result=nullptr)noexcept{
 PhysicalReadScope dpi;if(!dpi.Valid()){if(result)*result=E_ACCESSDENIED;return false;}
 const HRESULT hr=output->GetDesc(&desc);const bool read=SUCCEEDED(hr)&&(!monitor||GetMonitorInfoW(desc.Monitor,monitor));const bool restored=dpi.Restore();
 if(result)*result=FAILED(hr)?hr:read&&restored?S_OK:E_ACCESSDENIED;return read&&restored;
}
bool ReadWhiteLevel(HMONITOR monitor,const LUID& adapter,std::uint32_t& level)noexcept{
 level=0;try{MONITORINFOEXW info{};info.cbSize=sizeof(info);if(!GetMonitorInfoW(monitor,&info))return false;
 UINT32 paths=0,modes=0;if(GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS,&paths,&modes)!=ERROR_SUCCESS||!paths||paths>64||modes>128)return false;
 std::vector<DISPLAYCONFIG_PATH_INFO> p(paths);std::vector<DISPLAYCONFIG_MODE_INFO> m(modes);if(QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS,&paths,p.data(),&modes,m.data(),nullptr)!=ERROR_SUCCESS)return false;
 unsigned matches=0;for(UINT32 i=0;i<paths;++i){const auto& path=p[i];if(path.sourceInfo.adapterId.HighPart!=adapter.HighPart||path.sourceInfo.adapterId.LowPart!=adapter.LowPart)continue;
 DISPLAYCONFIG_SOURCE_DEVICE_NAME name{};name.header={DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,sizeof(name),path.sourceInfo.adapterId,path.sourceInfo.id};if(DisplayConfigGetDeviceInfo(&name.header)!=ERROR_SUCCESS)return false;if(_wcsicmp(name.viewGdiDeviceName,info.szDevice)!=0)continue;
 DISPLAYCONFIG_SDR_WHITE_LEVEL white{};white.header={DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL,sizeof(white),path.targetInfo.adapterId,path.targetInfo.id};if(DisplayConfigGetDeviceInfo(&white.header)!=ERROR_SUCCESS||!white.SDRWhiteLevel||white.SDRWhiteLevel>1000000)return false;
 // Cloned targets may have distinct HDR brightness. A monitor must resolve
 // uniquely; do not guess which target's reference white produced these pixels.
 if(++matches!=1)return false;level=white.SDRWhiteLevel;}
 return matches==1;
 }catch(const std::bad_alloc&){return false;}
}
bool Dimensions(const RECT& r,UINT& width,UINT& height)noexcept{
 const auto w=std::int64_t(r.right)-r.left,h=std::int64_t(r.bottom)-r.top;
 if(w<=0||h<=0||w>16384||h>16384||std::uint64_t(w)*std::uint64_t(h)>MaximumPixels)return false;
 width=static_cast<UINT>(w);height=static_cast<UINT>(h);return true;
}
struct Frame final {
 ComPtr<IDXGIOutputDuplication> output;bool held{};
 HRESULT Release()noexcept{if(!held)return S_OK;held=false;return output->ReleaseFrame();}
 ~Frame(){Release();}
};
struct Mapped final {
 ID3D11DeviceContext* context{};ID3D11Texture2D* texture{};bool held{};
 ~Mapped(){if(held)context->Unmap(texture,0);}
};
}
struct Sampler::State {
 DWORD ownerThread=GetCurrentThreadId();HMONITOR monitor{};RECT bounds{},callerBounds{};LUID adapter{};UINT width{},height{};DXGI_FORMAT format=DXGI_FORMAT_UNKNOWN;
 ComPtr<IDXGIOutput> output;ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;
 ComPtr<IDXGIOutputDuplication> duplicate;ComPtr<ID3D11Texture2D> staging;
 bool Current(SamplerDiagnostic* diagnostic=nullptr)const noexcept{
  DXGI_OUTPUT_DESC desc{};MONITORINFO info{sizeof(info)};
  const bool current=GetCurrentThreadId()==ownerThread&&output&&PhysicalDescription(output.Get(),desc,&info)&&desc.AttachedToDesktop
   &&desc.Monitor==monitor&&desc.Rotation==DXGI_MODE_ROTATION_IDENTITY&&SameRect(desc.DesktopCoordinates,bounds)
   &&SameRect(info.rcMonitor,bounds);
  if(diagnostic){diagnostic->outputBounds=desc.DesktopCoordinates;diagnostic->monitorBounds=info.rcMonitor;}return current;
 }
};
Sampler::Sampler()=default;
Sampler::~Sampler(){Close();}
void Sampler::Close(){state.reset();}
RECT Sampler::Bounds()const noexcept{return state?state->bounds:RECT{};}
RECT Sampler::CallerBounds()const noexcept{return state?state->callerBounds:RECT{};}
LUID Sampler::AdapterLuid()const noexcept{return state?state->adapter:LUID{};}
bool Sampler::Open(ID3D11Device* sourceDevice,HMONITOR exactMonitor){
 Close();diagnostic={};diagnostic.wantedMonitor=reinterpret_cast<std::uintptr_t>(exactMonitor);if(!sourceDevice||!exactMonitor)return Fail(SamplerStage::Input,E_INVALIDARG);
 try{
  auto next=std::make_unique<State>();ComPtr<IDXGIDevice> source;ComPtr<IDXGIAdapter> adapter;MONITORINFO caller{sizeof(caller)};if(!GetMonitorInfoW(exactMonitor,&caller))return Fail(SamplerStage::CallerBounds,E_ACCESSDENIED);diagnostic.callerBounds=caller.rcMonitor;next->callerBounds=caller.rcMonitor;PhysicalReadScope physical;if(!physical.Valid())return Fail(SamplerStage::PhysicalDpiRead,E_ACCESSDENIED);
  HRESULT hr=sourceDevice->QueryInterface(IID_PPV_ARGS(&source));if(FAILED(hr))return Fail(SamplerStage::SourceInterface,hr);hr=source->GetAdapter(&adapter);if(FAILED(hr))return Fail(SamplerStage::SourceAdapter,hr);
  DXGI_ADAPTER_DESC adapterDesc{};hr=adapter->GetDesc(&adapterDesc);if(FAILED(hr))return Fail(SamplerStage::AdapterDescription,hr);diagnostic.adapter=adapterDesc.AdapterLuid;
  next->adapter=adapterDesc.AdapterLuid;next->monitor=exactMonitor;
  for(UINT index=0;index<64;++index){
   diagnostic.outputIndex=index;ComPtr<IDXGIOutput> output;hr=adapter->EnumOutputs(index,&output);
   if(hr==DXGI_ERROR_NOT_FOUND)break;if(FAILED(hr))return Fail(SamplerStage::EnumerateOutput,hr);
   DXGI_OUTPUT_DESC desc{};if(!PhysicalDescription(output.Get(),desc,nullptr,&hr))return Fail(SamplerStage::OutputDescription,hr);if(!next->output||desc.Monitor==exactMonitor){diagnostic.observedMonitor=reinterpret_cast<std::uintptr_t>(desc.Monitor);diagnostic.outputRotation=desc.Rotation;}
   if(desc.Monitor!=exactMonitor)continue;
   if(next->output)return Fail(SamplerStage::AmbiguousOutput,E_UNEXPECTED);if(!desc.AttachedToDesktop)return Fail(SamplerStage::DetachedOutput,E_ACCESSDENIED);if(desc.Rotation!=DXGI_MODE_ROTATION_IDENTITY)return Fail(SamplerStage::OutputRotation,DXGI_ERROR_UNSUPPORTED);
   if(!Dimensions(desc.DesktopCoordinates,next->width,next->height))return Fail(SamplerStage::OutputDimensions,E_INVALIDARG);diagnostic.desktopWidth=next->width;diagnostic.desktopHeight=next->height;
   next->bounds=desc.DesktopCoordinates;next->output=output;
  }
  if(!next->output)return Fail(SamplerStage::OutputNotFound,DXGI_ERROR_NOT_FOUND);if(!next->Current(&diagnostic))return Fail(SamplerStage::OutputCurrentBefore,E_ACCESSDENIED);
  constexpr D3D_FEATURE_LEVEL levels[]={D3D_FEATURE_LEVEL_11_0};D3D_FEATURE_LEVEL obtained{};
  hr=D3D11CreateDevice(adapter.Get(),D3D_DRIVER_TYPE_UNKNOWN,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
    levels,1,D3D11_SDK_VERSION,&next->device,&obtained,&next->context);diagnostic.featureLevel=obtained;if(FAILED(hr)||obtained<D3D_FEATURE_LEVEL_11_0)return Fail(SamplerStage::CreateDevice,FAILED(hr)?hr:DXGI_ERROR_UNSUPPORTED);
  ComPtr<IDXGIOutput5> output5;hr=next->output.As(&output5);
  if(SUCCEEDED(hr)){
#if defined(PLAYHUB_SAMPLER_NATIVE_FP16_TRIAL)
   // Preserve a native HDR FP16 desktop when available. DXGI converts native
   // formats absent from this array; BGRA remains included for SDR desktops.
   constexpr DXGI_FORMAT formats[]={DXGI_FORMAT_R16G16B16A16_FLOAT,DXGI_FORMAT_B8G8R8A8_UNORM};
#else
   constexpr DXGI_FORMAT formats[]={DXGI_FORMAT_B8G8R8A8_UNORM};
#endif
   diagnostic.duplicateApi=1;hr=output5->DuplicateOutput1(next->device.Get(),0,static_cast<UINT>(sizeof(formats)/sizeof(formats[0])),formats,&next->duplicate);
  }
  else if(hr==E_NOINTERFACE){ComPtr<IDXGIOutput1> output1;hr=next->output.As(&output1);if(FAILED(hr))return Fail(SamplerStage::OutputInterface,hr);diagnostic.duplicateApi=0;hr=output1->DuplicateOutput(next->device.Get(),&next->duplicate);}
  else return Fail(SamplerStage::OutputInterface,hr);
  if(FAILED(hr))return Fail(SamplerStage::DuplicateOutput,hr);
  DXGI_OUTDUPL_DESC desc{};next->duplicate->GetDesc(&desc);
  diagnostic.duplicateRotation=desc.Rotation;diagnostic.duplicateWidth=desc.ModeDesc.Width;diagnostic.duplicateHeight=desc.ModeDesc.Height;diagnostic.duplicateFormat=desc.ModeDesc.Format;
  if(desc.Rotation!=DXGI_MODE_ROTATION_IDENTITY||desc.ModeDesc.Width!=next->width
    ||desc.ModeDesc.Height!=next->height||(desc.ModeDesc.Format!=DXGI_FORMAT_B8G8R8A8_UNORM&&desc.ModeDesc.Format!=DXGI_FORMAT_R16G16B16A16_FLOAT))return Fail(SamplerStage::DuplicateDescription,DXGI_ERROR_UNSUPPORTED);
  next->format=desc.ModeDesc.Format;
  // The acquired resource decides both format and compact readback layout.
  // Do not allocate an unused full-monitor staging texture during Open.
  if(!next->Current(&diagnostic))return Fail(SamplerStage::OutputCurrentAfter,E_ACCESSDENIED);
  if(!physical.Restore())return Fail(SamplerStage::PhysicalDpiRestore,E_ACCESSDENIED);state=std::move(next);diagnostic.stage=SamplerStage::Ready;diagnostic.result=S_OK;return true;
 }catch(const std::bad_alloc&){return Fail(SamplerStage::Allocation,E_OUTOFMEMORY);}
}
bool Sampler::Acquire(std::uint32_t timeoutMs,Pixels& result,SampleGrid grid){
 result={};diagnostic.acquireBudgetMs=timeoutMs;diagnostic.acquireWaitMs=diagnostic.mapWaitMs=diagnostic.copyMs=diagnostic.acquireElapsedMs=0;diagnostic.framePresentQpc=0;
 const auto acquireBegan=GetTickCount64();struct Elapsed final{SamplerDiagnostic& diagnostic;ULONGLONG began;~Elapsed(){diagnostic.acquireElapsedMs=static_cast<std::uint32_t>(GetTickCount64()-began);}}elapsed{diagnostic,acquireBegan};
 if(!state||timeoutMs==0||timeoutMs>500||(!ValidGrid(grid)&&(grid.sourceWidth||grid.sourceHeight)))return Fail(SamplerStage::AcquireInput,E_INVALIDARG);
 if(!state->Current())return Fail(SamplerStage::AcquireCurrent,E_ACCESSDENIED);
 PhysicalReadScope physical;if(!physical.Valid())return Fail(SamplerStage::PhysicalDpiRead,E_ACCESSDENIED);auto& s=*state;const ULONGLONG began=GetTickCount64(),deadline=began+timeoutMs;
 if(deadline<began)return Fail(SamplerStage::AcquireDeadline,E_INVALIDARG);
 Frame frame;frame.output=s.duplicate;DXGI_OUTDUPL_FRAME_INFO info{};ComPtr<IDXGIResource> resource;
 const auto waitBegan=GetTickCount64();const HRESULT acquired=s.duplicate->AcquireNextFrame(timeoutMs,&info,&resource);
 diagnostic.acquireWaitMs=static_cast<std::uint32_t>(GetTickCount64()-waitBegan);diagnostic.framePresentQpc=static_cast<std::uint64_t>(info.LastPresentTime.QuadPart);
 if(FAILED(acquired)){if(acquired==DXGI_ERROR_ACCESS_LOST)Close();return Fail(SamplerStage::AcquireNextFrame,acquired);}frame.held=true;
 if(info.ProtectedContentMaskedOut){result.protectedContent=true;return Fail(SamplerStage::ProtectedFrame,E_ACCESSDENIED);}
 if(info.LastPresentTime.QuadPart<=0)return Fail(SamplerStage::NoDesktopPresent,DXGI_ERROR_WAIT_TIMEOUT);
 if(!resource)return Fail(SamplerStage::FrameResource,E_POINTER);
 if(!s.Current())return Fail(SamplerStage::OutputCurrentAfter,E_ACCESSDENIED);
 ComPtr<ID3D11Texture2D> texture;const HRESULT textureResult=resource.As(&texture);if(FAILED(textureResult))return Fail(SamplerStage::FrameInterface,textureResult);
 D3D11_TEXTURE2D_DESC desc{};texture->GetDesc(&desc);
 diagnostic.frameFormat=desc.Format;
 if(!FrameDescriptionAccepted(desc,s.width,s.height))return Fail(SamplerStage::FrameFormat,DXGI_ERROR_UNSUPPORTED);
 // DuplicateOutput's mode format describes the output, not necessarily the
 // acquired resource. Read and encode the actual resource without conversion.
 const auto layout=ValidGrid(grid)?ImageLayout::OutputRows:ImageLayout::Full;
 const auto storedRows=StoredRows(layout,s.height);D3D11_TEXTURE2D_DESC existing{};if(s.staging)s.staging->GetDesc(&existing);
 if(!s.staging||desc.Format!=s.format||existing.Width!=s.width||existing.Height!=storedRows){
  const auto staging=ReadbackDescription(s.width,storedRows,desc.Format);ComPtr<ID3D11Texture2D> replacement;
  const HRESULT created=s.device->CreateTexture2D(&staging,nullptr,&replacement);
  if(FAILED(created))return Fail(SamplerStage::CreateStaging,created);
  if(!s.Current())return Fail(SamplerStage::OutputCurrentAfter,E_ACCESSDENIED);
  s.staging=std::move(replacement);s.format=desc.Format;
 }
 std::uint32_t white=1000;if(s.format==DXGI_FORMAT_R16G16B16A16_FLOAT&&!ReadWhiteLevel(s.monitor,s.adapter,white))return Fail(SamplerStage::WhiteLevel,E_ACCESSDENIED);diagnostic.sdrWhiteLevel=white;
 if(!CopyExactSampleRows(s.context.Get(),texture.Get(),s.staging.Get(),layout,grid))return Fail(SamplerStage::FrameFormat,E_UNEXPECTED);s.context->Flush();
 D3D11_MAPPED_SUBRESOURCE mapped{};Mapped mapping{s.context.Get(),s.staging.Get(),false};HRESULT hr=E_FAIL;const auto mapBegan=GetTickCount64();
 for(;;){
  hr=s.context->Map(s.staging.Get(),0,D3D11_MAP_READ,D3D11_MAP_FLAG_DO_NOT_WAIT,&mapped);
  if(hr!=DXGI_ERROR_WAS_STILL_DRAWING)break;
  if(GetTickCount64()>=deadline){diagnostic.mapWaitMs=static_cast<std::uint32_t>(GetTickCount64()-mapBegan);return Fail(SamplerStage::MapTimeout,hr);}
  Sleep(1);
 }
 diagnostic.mapWaitMs=static_cast<std::uint32_t>(GetTickCount64()-mapBegan);if(FAILED(hr))return Fail(SamplerStage::MapReadback,hr);mapping.held=true;
 const std::uint32_t stride=s.width*(s.format==DXGI_FORMAT_R16G16B16A16_FLOAT?8u:4u);
 if(!mapped.pData||mapped.RowPitch<stride)return Fail(SamplerStage::MappedShape,E_UNEXPECTED);
 if(!s.Current())return Fail(SamplerStage::OutputCurrentAfter,E_ACCESSDENIED);
 Pixels observed;observed.width=s.width;observed.height=s.height;observed.stride=stride;observed.layout=layout;observed.grid=grid;
 observed.scRgbFloat16=s.format==DXGI_FORMAT_R16G16B16A16_FLOAT;observed.sdrWhiteLevel=white;
 observed.presentQpc=static_cast<std::uint64_t>(info.LastPresentTime.QuadPart);
 const auto copyBegan=GetTickCount64();try{
  observed.bgra.resize(std::size_t(stride)*storedRows);
  for(UINT row=0;row<storedRows;++row)std::memcpy(observed.bgra.data()+std::size_t(row)*stride,
    static_cast<const std::uint8_t*>(mapped.pData)+std::size_t(row)*mapped.RowPitch,stride);
 }catch(const std::bad_alloc&){return Fail(SamplerStage::Allocation,E_OUTOFMEMORY);}
  diagnostic.copyMs=static_cast<std::uint32_t>(GetTickCount64()-copyBegan);LARGE_INTEGER qpc{};
 if(GetTickCount64()>deadline)return Fail(SamplerStage::ReadbackDeadline,DXGI_ERROR_WAIT_TIMEOUT);
 if(!QueryPerformanceCounter(&qpc)||qpc.QuadPart<=info.LastPresentTime.QuadPart)return Fail(SamplerStage::ReadbackClock,E_UNEXPECTED);
 if(!s.Current())return Fail(SamplerStage::OutputCurrentAfter,E_ACCESSDENIED);
 std::uint32_t whiteAfter=1000;if(observed.scRgbFloat16&&(!ReadWhiteLevel(s.monitor,s.adapter,whiteAfter)||whiteAfter!=white))return Fail(SamplerStage::WhiteLevel,E_ACCESSDENIED);
 observed.acquiredQpc=static_cast<std::uint64_t>(qpc.QuadPart);
 s.context->Unmap(s.staging.Get(),0);mapping.held=false;
 const HRESULT released=frame.Release();if(FAILED(released))return Fail(SamplerStage::FrameRelease,released);
 if(!physical.Restore())return Fail(SamplerStage::PhysicalDpiRestore,E_ACCESSDENIED);diagnostic.stage=SamplerStage::Acquired;diagnostic.result=S_OK;result=std::move(observed);return true;
}
}
