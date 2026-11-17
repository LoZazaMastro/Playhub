// SPDX-License-Identifier: MIT
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <dwmapi.h>
#include <d3d11.h>
#include <dxgi.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <atomic>
#include <chrono>
#include <iomanip>
#include <iostream>
#include <memory>
#include <thread>
#include "NativeGuard.h"
#include "ContentCore.h"
#pragma comment(lib,"windowsapp.lib")
#pragma comment(lib,"d3d11.lib")
#pragma comment(lib,"dxgi.lib")
#pragma comment(lib,"dwmapi.lib")
#pragma comment(lib,"user32.lib")
using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice;
int Checks();
namespace {
std::atomic_bool canceled{};
BOOL WINAPI Cancel(DWORD type){if(type==CTRL_C_EVENT||type==CTRL_BREAK_EVENT||type==CTRL_CLOSE_EVENT){canceled=true;return TRUE;}return FALSE;}
std::int64_t Ticks(){LARGE_INTEGER now{},frequency{};if(!QueryPerformanceCounter(&now)||!QueryPerformanceFrequency(&frequency)||frequency.QuadPart<=0)return 0;
 return now.QuadPart/frequency.QuadPart*10000000+(now.QuadPart%frequency.QuadPart)*10000000/frequency.QuadPart;}
struct Geometry {
 RECT window{},frame{},client{};POINT origin{};DWORD thread{},pid{};
 bool operator==(const Geometry& r)const{return EqualRect(&window,&r.window)&&EqualRect(&frame,&r.frame)&&EqualRect(&client,&r.client)&&origin.x==r.origin.x&&origin.y==r.origin.y&&thread==r.thread&&pid==r.pid;}
};
bool ReadGeometry(HWND hwnd,Geometry& g){DWORD cloak{};
 g.thread=GetWindowThreadProcessId(hwnd,&g.pid);
 return g.thread&&IsWindow(hwnd)&&IsWindowVisible(hwnd)&&!IsIconic(hwnd)
  &&SUCCEEDED(DwmGetWindowAttribute(hwnd,DWMWA_CLOAKED,&cloak,sizeof(cloak)))&&cloak==0
  &&GetWindowRect(hwnd,&g.window)&&SUCCEEDED(DwmGetWindowAttribute(hwnd,DWMWA_EXTENDED_FRAME_BOUNDS,&g.frame,sizeof(g.frame)))
  &&GetClientRect(hwnd,&g.client)&&ClientToScreen(hwnd,&g.origin)
  &&g.client.left==0&&g.client.top==0&&g.client.right>0&&g.client.bottom>0;
}
std::optional<content::Rect> Crop(const Geometry& g,unsigned width,unsigned height){
 std::optional<content::Rect> result;
 for(const RECT* bounds:{&g.window,&g.frame}){
  if(bounds->right-bounds->left!=static_cast<int>(width)||bounds->bottom-bounds->top!=static_cast<int>(height))continue;
  content::Rect next{g.origin.x-bounds->left,g.origin.y-bounds->top,g.client.right,g.client.bottom};
  if(!content::Bounds(width,height,next))return std::nullopt;
  if(result&&*result!=next)return std::nullopt;result=next;
 }
 return result;
}
struct State {
 com_ptr<ID3D11Device> d3d;
 com_ptr<ID3D11DeviceContext> context;
 IDirect3DDevice device{nullptr};
 GraphicsCaptureItem item{nullptr};
 Direct3D11CaptureFramePool pool{nullptr};
 GraphicsCaptureSession session{nullptr};
 std::unique_ptr<NativeGuard> guard;
 std::optional<progress::Target> target;
 std::optional<Geometry> boundGeometry;
 bool retireRequested{};
 ~State(){Close();}
 void Close()noexcept{try{if(session)session.Close();}catch(...){}try{if(pool)pool.Close();}catch(...){}
  session=nullptr;pool=nullptr;item=nullptr;guard.reset();boundGeometry.reset();retireRequested=false;}
 void Init(){
  if(!GraphicsCaptureSession::IsSupported())throw hresult_error(E_NOTIMPL);
  D3D_FEATURE_LEVEL level{};
  check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,d3d.put(),&level,context.put()));
  auto dxgi=d3d.as<IDXGIDevice>();com_ptr<::IInspectable> inspectable;
  check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(),inspectable.put()));device=inspectable.as<IDirect3DDevice>();
 }
 bool Same(const progress::Target& t)const{return target&&t.hwnd==target->hwnd&&t.pid==target->pid&&t.birth==target->birth&&t.image==target->image&&t.windowClass==target->windowClass;}
 struct GuardRefused {const char* reason;DWORD error;};
 void Bind(const progress::Target& t,const char*& stage){
  if(target){if(!Same(t)||!guard||!pool||!session)throw hresult_error(E_INVALIDARG);return;}
  stage="native-guard-open";
  target=t;guard=std::make_unique<NativeGuard>(*target);
  stage="native-guard-current";
  if(!guard->Current())throw GuardRefused{guard->Reason(),guard->Error()};
  stage="geometry";
  Geometry geometry{};
  if(!ReadGeometry(reinterpret_cast<HWND>(static_cast<ULONG_PTR>(t.hwnd)),geometry)||geometry.pid!=t.pid||geometry.thread!=guard->Thread())throw hresult_error(E_INVALIDARG);
  boundGeometry=geometry;
  stage="capture-item-factory";
  auto interop=get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();
  stage="create-for-window";
  check_hresult(interop->CreateForWindow(reinterpret_cast<HWND>(static_cast<ULONG_PTR>(t.hwnd)),guid_of<GraphicsCaptureItem>(),put_abi(item)));
  stage="item-size";
  auto size=item.Size();if(size.Width<=0||size.Height<=0||!content::Bounds(static_cast<unsigned>(size.Width),static_cast<unsigned>(size.Height),{0,0,size.Width,size.Height}))throw hresult_error(E_INVALIDARG);
  stage="frame-pool";
  pool=Direct3D11CaptureFramePool::CreateFreeThreaded(device,DirectXPixelFormat::B8G8R8A8UIntNormalized,2,size);
  stage="create-session";
  session=pool.CreateCaptureSession(item);
  stage="cursor-disable";
  session.IsCursorCaptureEnabled(false); // No cursor can turn a black surface into content.
  stage="cursor-readback";
  if(session.IsCursorCaptureEnabled())throw hresult_error(E_NOTIMPL);
  stage="native-guard-before-start";
  if(!guard->Current())throw GuardRefused{guard->Reason(),guard->Error()};
  stage="start-capture";
  session.StartCapture();
 }
};
struct FrameClose {Direct3D11CaptureFrame value{nullptr};~FrameClose(){try{if(value)value.Close();}catch(...){}}};
void Empty(const progress::Target& t,const char* state,const char* stage,HRESULT hr=S_OK,const char* guardReason="none",DWORD winError=0){
 std::cout<<"{\"id\":\""<<t.id<<"\",\"state\":\""<<state<<"\",\"stage\":\""<<stage<<"\",\"hresult\":"<<static_cast<std::int32_t>(hr)
  <<",\"guardReason\":\""<<guardReason<<"\",\"winError\":"<<winError<<",\"gameReadyClaimed\":false,\"inputAuthorityClaimed\":false,\"imagesPersisted\":false}"<<std::endl;
}
void Query(State& s,const progress::Target& t){
 const auto began=Ticks();const auto deadline=began+content::FreshTicks;const char* stage="bind";
 try{
  if(began<=0){Empty(t,"unsupported","qpc");return;}
  if(canceled){Empty(t,"canceled",stage);return;}
  if(s.target&&!s.Same(t)){s.retireRequested=true;Empty(t,"identity-changed","different-target");return;}
  s.Bind(t,stage);stage="frame";
  const HWND hwnd=reinterpret_cast<HWND>(static_cast<ULONG_PTR>(t.hwnd));
  while(!canceled&&Ticks()<=deadline){
   if(!s.guard->Current()){Empty(t,"identity-changed",stage,S_OK,s.guard->Reason(),s.guard->Error());s.Close();return;}
   FrameClose frame;frame.value=s.pool.TryGetNextFrame();
   if(!frame.value){std::this_thread::sleep_for(std::chrono::milliseconds(2));continue;}
   const auto timestamp=frame.value.SystemRelativeTime().count();
   if(!content::Fresh(timestamp,Ticks()))continue;
   const auto size=frame.value.ContentSize();Geometry geometry{};
   if(size.Width<=0||size.Height<=0||!ReadGeometry(hwnd,geometry)){Empty(t,"unknown","geometry");return;}
   if(!s.boundGeometry||geometry!=*s.boundGeometry){Empty(t,"identity-changed","geometry-changed");s.retireRequested=true;return;}
   auto crop=Crop(geometry,static_cast<unsigned>(size.Width),static_cast<unsigned>(size.Height));
   if(!crop){Empty(t,"unknown","client-crop");return;}
   stage="surface";auto access=frame.value.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();com_ptr<ID3D11Texture2D> source;
   check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D),source.put_void()));D3D11_TEXTURE2D_DESC desc{};source->GetDesc(&desc);
   if(desc.Format!=DXGI_FORMAT_B8G8R8A8_UNORM||desc.SampleDesc.Count!=1
    ||static_cast<unsigned>(size.Width)>desc.Width||static_cast<unsigned>(size.Height)>desc.Height||!content::Bounds(desc.Width,desc.Height,*crop)){
    Empty(t,"unsupported","format-or-size");return;
   }
   D3D11_TEXTURE2D_DESC cpu=desc;cpu.Width=static_cast<UINT>(crop->width);cpu.Height=static_cast<UINT>(crop->height);cpu.ArraySize=1;cpu.MipLevels=1;
   cpu.BindFlags=0;cpu.MiscFlags=0;cpu.Usage=D3D11_USAGE_STAGING;cpu.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
   com_ptr<ID3D11Texture2D> staging;check_hresult(s.d3d->CreateTexture2D(&cpu,nullptr,staging.put()));
   D3D11_BOX box{static_cast<UINT>(crop->x),static_cast<UINT>(crop->y),0,static_cast<UINT>(crop->x+crop->width),static_cast<UINT>(crop->y+crop->height),1};
   s.context->CopySubresourceRegion(staging.get(),0,0,0,0,source.get(),0,&box);s.context->Flush();stage="readback";
   D3D11_MAPPED_SUBRESOURCE mapped{};HRESULT hr=DXGI_ERROR_WAS_STILL_DRAWING;
   while(!canceled&&Ticks()<=deadline&&content::Fresh(timestamp,Ticks())){
    hr=s.context->Map(staging.get(),0,D3D11_MAP_READ,D3D11_MAP_FLAG_DO_NOT_WAIT,&mapped);
    if(hr!=DXGI_ERROR_WAS_STILL_DRAWING)break;std::this_thread::sleep_for(std::chrono::milliseconds(1));
   }
   if(hr==DXGI_ERROR_WAS_STILL_DRAWING){Empty(t,canceled?"canceled":"no-fresh-frame",stage);return;}
   check_hresult(hr);
   struct Unmap {ID3D11DeviceContext* c;ID3D11Resource* r;~Unmap(){c->Unmap(r,0);}} unmap{s.context.get(),staging.get()};
   auto stats=content::Measure(static_cast<const std::uint8_t*>(mapped.pData),static_cast<std::size_t>(mapped.RowPitch)*cpu.Height,mapped.RowPitch,cpu.Width,cpu.Height);
   Geometry after{};const bool current=ReadGeometry(hwnd,after)&&geometry==after&&s.boundGeometry&&after==*s.boundGeometry&&s.guard->Current();
   const bool finalCurrent=current&&s.guard->FinalCurrent();
   const bool actualForeground=finalCurrent&&s.guard->Foreground();
   const auto observed=Ticks();
   if(canceled||!stats||!finalCurrent||!content::Fresh(timestamp,observed)||observed>deadline){s.retireRequested=!finalCurrent;Empty(t,canceled?"canceled":"no-fresh-frame","final-currentness");return;}
   std::cout<<std::fixed<<std::setprecision(3)<<"{\"id\":\""<<t.id<<"\",\"state\":\"frame-facts\",\"sourceCurrent\":true,\"actualForeground\":"<<(actualForeground?"true":"false")
    <<",\"sourcePid\":"<<t.pid<<",\"sourceBirth\":\""<<t.birth<<"\",\"sourceHwnd\":\""<<t.hwnd<<"\",\"sourceThread\":"<<s.guard->Thread()
    <<",\"frameQpc100ns\":\""<<timestamp<<"\",\"observedQpc100ns\":\""<<observed<<"\",\"ageMs\":"<<static_cast<double>(observed-timestamp)/10000.0
    <<",\"queryMs\":"<<static_cast<double>(observed-began)/10000.0<<",\"format\":\"BGRA8\",\"contentWidth\":"<<size.Width<<",\"contentHeight\":"<<size.Height
    <<",\"clientX\":"<<crop->x<<",\"clientY\":"<<crop->y<<",\"clientWidth\":"<<cpu.Width<<",\"clientHeight\":"<<cpu.Height<<",\"samples\":"<<stats->samples<<",\"opaqueSamples\":"<<stats->opaque<<",\"nonBlackSamples\":"<<stats->nonBlack
    <<",\"meanLuma\":"<<stats->mean<<",\"varianceLuma\":"<<stats->variance<<",\"minLuma\":"<<stats->minimum<<",\"maxLuma\":"<<stats->maximum
    <<",\"gameReadyClaimed\":false,\"inputAuthorityClaimed\":false,\"hdrMeaningClaimed\":false,\"imagesPersisted\":false}"<<std::endl;return;
  }
  Empty(t,canceled?"canceled":"no-fresh-frame",stage);
 }catch(const State::GuardRefused& e){Empty(t,"identity-changed",stage,S_OK,e.reason,e.error);s.Close();}
 catch(const hresult_error& e){const auto code=e.code().value;Empty(t,code==E_NOTIMPL||code==E_NOINTERFACE||code==REGDB_E_CLASSNOTREG?"unsupported":code==E_ACCESSDENIED?"access-denied":"unavailable",stage,code);s.Close();}
}
bool NextLine(HANDLE input,std::string& pending,std::string& line,std::int64_t end){
 while(!canceled){
  const auto now=Ticks();if(now<=0||now>=end)return false;
  if(const auto at=pending.find('\n');at!=std::string::npos){line=pending.substr(0,at);pending.erase(0,at+1);return true;}
  if(pending.size()>16384)return false;
  DWORD available{};if(!PeekNamedPipe(input,nullptr,0,nullptr,&available,nullptr))return false;
  if(!available){std::this_thread::sleep_for(std::chrono::milliseconds(2));continue;}
  char bytes[1024];DWORD read{};
  if(!ReadFile(input,bytes,std::min<DWORD>(available,sizeof(bytes)),&read,nullptr)||!read)return false;
  for(DWORD i=0;i<read;i++)if(static_cast<unsigned char>(bytes[i])>127||bytes[i]=='\0')return false;
  pending.append(bytes,read);
 }
 return false;
}
}
int main(int argc,char** argv){
 if(argc==2&&std::string_view(argv[1])=="--checks")return Checks();
 if(argc!=2||std::string_view(argv[1])!="--server"){
  std::cout<<"{\"planOnly\":true,\"nativeCalls\":0,\"requires\":\"Root-only --server with exact six-field target TSV\",\"imagesPersisted\":false,\"gameReadyClaimed\":false}"<<std::endl;return 0;
 }
 try{
  const HANDLE input=GetStdHandle(STD_INPUT_HANDLE);
  if(!input||input==INVALID_HANDLE_VALUE||GetFileType(input)!=FILE_TYPE_PIPE){std::cout<<"{\"ready\":false,\"state\":\"owned-pipe-required\"}"<<std::endl;return 2;}
  if(Ticks()<=0){std::cout<<"{\"ready\":false,\"state\":\"unsupported-qpc\"}"<<std::endl;return 2;}
  init_apartment(apartment_type::multi_threaded);SetConsoleCtrlHandler(Cancel,TRUE);State state;state.Init();
  std::cout<<"{\"ready\":true,\"protocol\":1}"<<std::endl;const auto end=Ticks()+1200000000LL;
  std::string line,pending;
  for(unsigned count=0;count<4096&&NextLine(input,pending,line,end);count++){
   if(line.size()>16384)break;if(!line.empty()&&line.back()=='\r')line.pop_back();progress::Target target;
   if(!progress::Parse(line,target)){std::cout<<"{\"state\":\"invalid-target\"}"<<std::endl;continue;}Query(state,target);
   // The checked-out frame and staging map are already released before Close.
   if(state.retireRequested)state.Close();
  }
  return 0;
 }catch(const hresult_error& e){std::cout<<"{\"ready\":false,\"state\":\"unsupported\",\"hresult\":"<<static_cast<std::int32_t>(e.code().value)<<"}"<<std::endl;return 2;}
}
