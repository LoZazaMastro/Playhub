// SPDX-License-Identifier: GPL-3.0-only
#include "CompositionPresenter.h"
#include <dwmapi.h>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <string>
#include <vector>
#include <algorithm>
#include <iostream>
namespace fs=std::filesystem;
using playhub::diagnostic::CompositionPresenter;
std::wstring ObjectName(HANDLE object){wchar_t text[256]{};DWORD bytes{};return object&&GetUserObjectInformationW(object,UOI_NAME,text,sizeof(text),&bytes)?text:L"";}
std::wstring InputDesktop(){HDESK d=OpenInputDesktop(0,FALSE,DESKTOP_READOBJECTS);auto n=ObjectName(d);if(d)CloseDesktop(d);return n;}
std::string Narrow(std::wstring s){std::string result;for(wchar_t c:s)result+=c<128?static_cast<char>(c):'?';return result;}
std::string Hex(HRESULT hr){std::ostringstream o;o<<"0x"<<std::hex<<std::uppercase<<std::setw(8)<<std::setfill('0')<<static_cast<unsigned long>(hr);return o.str();}
bool Write(const fs::path& path,const std::string& text){
 HANDLE f=CreateFileW(path.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
 if(f==INVALID_HANDLE_VALUE)return false;DWORD written{};
 const bool ok=WriteFile(f,text.data(),static_cast<DWORD>(text.size()),&written,nullptr)&&written==text.size()&&FlushFileBuffers(f);
 CloseHandle(f);return ok;
}
int UiAccess(){HANDLE token{};DWORD value{},bytes{};if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token))return -1;
 const BOOL ok=GetTokenInformation(token,TokenUIAccess,&value,sizeof(value),&bytes);CloseHandle(token);return ok?static_cast<int>(value):-1;}
bool TexturePathChecks(CompositionPresenter& presenter,std::string& report){
 using Microsoft::WRL::ComPtr;
 constexpr UINT w=960,h=540;std::vector<unsigned char> pixels(w*h*4,0);
 for(UINT y=32;y<h-32;y++)for(UINT x=32;x<w-32;x++){
  auto p=pixels.data()+(y*w+x)*4;p[0]=96;p[1]=64;p[2]=32;p[3]=128;
 }
 auto p=pixels.data()+(64*w+64)*4;p[0]=8;p[1]=16;p[2]=24;p[3]=32;
 D3D11_TEXTURE2D_DESC desc{};desc.Width=w;desc.Height=h;desc.MipLevels=1;desc.ArraySize=1;
 desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.Usage=D3D11_USAGE_DEFAULT;
 D3D11_SUBRESOURCE_DATA data{pixels.data(),w*4,0};ComPtr<ID3D11Texture2D> frame;
 HRESULT create=presenter.Device()->CreateTexture2D(&desc,&data,&frame);
 playhub::diagnostic::PixelSamples samples{};HRESULT submit=FAILED(create)?create:presenter.PresentTexture(frame.Get(),&samples);
 bool content=SUCCEEDED(submit)&&samples.transparentPixel&&samples.premultipliedPixel&&samples.sentinelBgra==std::array<unsigned char,4>{8,16,24,32};
 ComPtr<ID3D11Texture2D> wrongSize,wrongFormat,foreignTexture;
 desc.Width=64;desc.Height=64;HRESULT sizeCreate=presenter.Device()->CreateTexture2D(&desc,nullptr,&wrongSize);
 const bool sizeReject=SUCCEEDED(sizeCreate)&&presenter.PresentTexture(wrongSize.Get())==E_INVALIDARG;
 desc.Width=w;desc.Height=h;desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
 HRESULT formatCreate=presenter.Device()->CreateTexture2D(&desc,nullptr,&wrongFormat);
 const bool formatReject=SUCCEEDED(formatCreate)&&presenter.PresentTexture(wrongFormat.Get())==E_INVALIDARG;
 ComPtr<ID3D11Device> foreignDevice;HRESULT foreignCreate=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,
  D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&foreignDevice,nullptr,nullptr);
 desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
 if(SUCCEEDED(foreignCreate))foreignCreate=foreignDevice->CreateTexture2D(&desc,nullptr,&foreignTexture);
 const bool foreignReject=SUCCEEDED(foreignCreate)&&presenter.PresentTexture(foreignTexture.Get())==E_INVALIDARG;
 const bool nullReject=presenter.PresentTexture(nullptr)==E_INVALIDARG;
 std::ostringstream o;o<<std::boolalpha<<"{\"generatedTextureReadbackValidated\":"<<content<<",\"sentinelBgra\":["<<unsigned(samples.sentinelBgra[0])<<","<<unsigned(samples.sentinelBgra[1])<<","<<unsigned(samples.sentinelBgra[2])<<","<<unsigned(samples.sentinelBgra[3])<<"],\"submitHresult\":\""<<Hex(submit)<<"\",\"rejectWrongDimensions\":"<<sizeReject<<",\"rejectWrongFormat\":"<<formatReject<<",\"rejectForeignDevice\":"<<foreignReject<<",\"rejectNull\":"<<nullReject<<",\"actualSteamContent\":false}";
 report=o.str();return content&&sizeReject&&formatReject&&foreignReject&&nullReject;
}
LRESULT CALLBACK WindowProc(HWND h,UINT m,WPARAM w,LPARAM l){
 if(m==WM_MOUSEACTIVATE)return MA_NOACTIVATE;
 if(m==WM_NCHITTEST)return HTTRANSPARENT;
 if(m==WM_CLOSE){DestroyWindow(h);return 0;}
 return DefWindowProcW(h,m,w,l);
}
int Child(const fs::path& output,const std::wstring& expected){
 const auto desktop=ObjectName(GetThreadDesktop(GetCurrentThreadId())),inputBefore=InputDesktop();
 const bool shellAbsent=GetShellWindow()==nullptr;
 const bool isolated=!desktop.empty()&&desktop==expected&&desktop!=inputBefore;
 auto failure=[&](const char* stage,HRESULT hr){std::ostringstream o;o<<"{\"passed\":false,\"stage\":\""<<stage<<"\",\"hresult\":\""<<Hex(hr)<<"\",\"shellAbsent\":"<<(shellAbsent?"true":"false")<<",\"desktop\":\""<<Narrow(desktop)<<"\",\"inputDesktop\":\""<<Narrow(inputBefore)<<"\",\"liveDesktopSwitched\":false}";Write(output,o.str());return 2;};
 if(!isolated||!shellAbsent)return failure("desktop-admission",E_ACCESSDENIED);
 HRESULT com=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);if(FAILED(com))return failure("COM",com);
 WNDCLASSEXW wc{sizeof(wc)};wc.lpfnWndProc=WindowProc;wc.hInstance=GetModuleHandleW(nullptr);wc.lpszClassName=L"Playhub.ExplorerFree.Diagnostic";
 if(!RegisterClassExW(&wc)){CoUninitialize();return failure("RegisterClass",HRESULT_FROM_WIN32(GetLastError()));}
 HWND hwnd=CreateWindowExW(WS_EX_TOPMOST|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TRANSPARENT|WS_EX_NOREDIRECTIONBITMAP,
  wc.lpszClassName,L"Playhub isolated compositor diagnostic",WS_POPUP,100,100,960,540,nullptr,nullptr,wc.hInstance,nullptr);
 if(!hwnd){UnregisterClassW(wc.lpszClassName,wc.hInstance);CoUninitialize();return failure("CreateWindow",HRESULT_FROM_WIN32(GetLastError()));}
 int code=0;std::string report;
 {CompositionPresenter presenter;HRESULT init=presenter.Initialize(hwnd,960,540);
 if(FAILED(init))code=failure("D3D11-DirectComposition-initialize",init);
 else {
  ShowWindow(hwnd,SW_SHOWNOACTIVATE);SetWindowPos(hwnd,HWND_TOPMOST,100,100,960,540,SWP_NOACTIVATE|SWP_SHOWWINDOW);
  std::vector<double> timings;unsigned accepted=0,occluded=0,stillDrawing=0,failed=0,readbacks=0;std::vector<HRESULT> codes;
  playhub::diagnostic::PixelSamples samples{};bool alpha=true;DCOMPOSITION_FRAME_STATISTICS before{},after{};
  HRESULT statsBefore=presenter.FrameStatistics(before);const auto start=std::chrono::steady_clock::now();
  for(unsigned frame=0;frame<120;frame++){
   MSG msg{};while(PeekMessageW(&msg,nullptr,0,0,PM_REMOVE)){TranslateMessage(&msg);DispatchMessageW(&msg);}
   const bool sample=frame==0||frame==59||frame==119;
   const auto begin=std::chrono::steady_clock::now();HRESULT hr=presenter.RenderDiagnosticFrame(frame,sample?&samples:nullptr);
   timings.push_back(std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-begin).count());
   codes.push_back(hr);if(hr==S_OK)accepted++;else if(hr==DXGI_STATUS_OCCLUDED)occluded++;else if(hr==DXGI_ERROR_WAS_STILL_DRAWING)stillDrawing++;else if(FAILED(hr))failed++;
   if(sample){alpha&=SUCCEEDED(hr)&&samples.transparentPixel&&samples.premultipliedPixel;if(SUCCEEDED(hr))readbacks++;}
   Sleep(16);
  }
  std::string textureReport;const bool textureChecks=TexturePathChecks(presenter,textureReport);
  CompositionPresenter invalidBounds;const bool rejectSmall=invalidBounds.Initialize(hwnd,1,1)==E_INVALIDARG;
  HRESULT commit=presenter.Commit(),statsAfter=presenter.FrameStatistics(after);const auto inputAfter=InputDesktop();
  const double elapsed=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();
  std::sort(timings.begin(),timings.end());
  const bool safe=GetShellWindow()==nullptr&&inputBefore==inputAfter&&desktop==ObjectName(GetThreadDesktop(GetCurrentThreadId()));
  using BandQuery=BOOL(WINAPI*)(HWND,DWORD*);auto bandQuery=reinterpret_cast<BandQuery>(GetProcAddress(GetModuleHandleW(L"user32.dll"),"GetWindowBand"));DWORD band{};
  const bool bandKnown=bandQuery&&bandQuery(hwnd,&band)!=FALSE;const int uiAccess=UiAccess();
  const bool pass=safe&&alpha&&readbacks==3&&failed==0&&textureChecks&&rejectSmall&&SUCCEEDED(commit)&&SUCCEEDED(presenter.DeviceRemovedReason());
  std::ostringstream o;o<<std::boolalpha<<std::fixed<<std::setprecision(3)
   <<"{\"passed\":"<<pass<<",\"desktop\":\""<<Narrow(desktop)<<"\",\"inputDesktopBefore\":\""<<Narrow(inputBefore)<<"\",\"inputDesktopAfter\":\""<<Narrow(inputAfter)<<"\",\"shellAbsent\":"<<(GetShellWindow()==nullptr)
   <<",\"inactiveDesktop\":"<<isolated<<",\"liveDesktopSwitched\":false,\"ordinaryTopmostWindow\":"<<((GetWindowLongPtrW(hwnd,GWL_EXSTYLE)&WS_EX_TOPMOST)!=0)
   <<",\"alphaMode\":\"premultiplied\",\"gpuReadbacks\":"<<readbacks<<",\"gpuReadbackAlphaValidated\":"<<alpha
   <<",\"tokenUIAccess\":"<<uiAccess<<",\"windowBandQueryAvailable\":"<<(bandQuery!=nullptr)<<",\"windowBandKnown\":"<<bandKnown<<",\"windowBand\":"<<band
   <<",\"texturePathChecks\":"<<textureReport<<",\"rejectSmallDiagnosticBounds\":"<<rejectSmall
   <<",\"transparentBgra\":["<<unsigned(samples.transparentBgra[0])<<","<<unsigned(samples.transparentBgra[1])<<","<<unsigned(samples.transparentBgra[2])<<","<<unsigned(samples.transparentBgra[3])<<"]"
   <<",\"panelBgra\":["<<unsigned(samples.panelBgra[0])<<","<<unsigned(samples.panelBgra[1])<<","<<unsigned(samples.panelBgra[2])<<","<<unsigned(samples.panelBgra[3])<<"]"
   <<",\"attemptedFrames\":120,\"presentSOk\":"<<accepted<<",\"presentOccluded\":"<<occluded<<",\"presentStillDrawing\":"<<stillDrawing<<",\"presentFailures\":"<<failed
   <<",\"elapsedMs\":"<<elapsed<<",\"submitMedianMs\":"<<timings[timings.size()/2]<<",\"submitP95Ms\":"<<timings[timings.size()*95/100]
   <<",\"commitHresult\":\""<<Hex(commit)<<"\",\"frameStatsBeforeHresult\":\""<<Hex(statsBefore)<<"\",\"frameStatsAfterHresult\":\""<<Hex(statsAfter)<<"\",\"dcompLastFrameBefore\":"<<before.lastFrameTime.QuadPart<<",\"dcompLastFrameAfter\":"<<after.lastFrameTime.QuadPart
   <<",\"physicallyPresentedVerified\":false,\"exclusiveFullscreenTested\":false,\"steamContentTested\":false,\"inputTested\":false,\"presentHresults\":[";
  for(size_t i=0;i<codes.size();i++){if(i)o<<",";o<<"\""<<Hex(codes[i])<<"\"";}o<<"]";report=o.str();code=pass?0:3;
 }
 }
 const bool hidden=ShowWindow(hwnd,SW_HIDE)!=FALSE||!IsWindowVisible(hwnd);
 const bool destroyed=DestroyWindow(hwnd)!=FALSE&&!IsWindow(hwnd);
 const bool unregistered=UnregisterClassW(wc.lpszClassName,wc.hInstance)!=FALSE;
 CoUninitialize();
 if(!report.empty()){
  std::ostringstream suffix;suffix<<std::boolalpha<<",\"hiddenBeforeDestroy\":"<<hidden<<",\"ownedWindowDestroyed\":"<<destroyed<<",\"ownedClassUnregistered\":"<<unregistered<<"}";
  if(!Write(output,report+suffix.str()))return 74;
 }
 return hidden&&destroyed&&unregistered?code:75;
}
int wmain(int argc,wchar_t** argv){
 if(argc==4&&std::wstring(argv[1])==L"--child")return Child(fs::path(argv[2]),argv[3]);
 if(argc!=2){std::cerr<<"Usage: ExplorerFreeDiagnostic.exe absolute-report.json\n";return 64;}
 fs::path output=fs::absolute(argv[1]);if(!output.is_absolute()||!fs::exists(output.parent_path())||fs::exists(output))return 64;
 const auto originalInput=InputDesktop();if(originalInput.empty())return 65;
 const std::wstring name=L"Playhub.ExplorerFree."+std::to_wstring(GetCurrentProcessId())+L"."+std::to_wstring(GetTickCount64());
 HDESK desktop=CreateDesktopW(name.c_str(),nullptr,nullptr,0,DESKTOP_CREATEWINDOW|DESKTOP_READOBJECTS|DESKTOP_WRITEOBJECTS|DESKTOP_ENUMERATE,nullptr);
 if(!desktop){std::cerr<<"CreateDesktop failed "<<GetLastError()<<"\n";return 66;}
 wchar_t own[32768]{};GetModuleFileNameW(nullptr,own,32768);
 std::wstring station=ObjectName(GetProcessWindowStation()),full=station+L"\\"+name;
 std::vector<std::string> reports;bool allPassed=true;DWORD lastExit=0;
 for(unsigned cycle=1;cycle<=5;cycle++){
  fs::path childOutput=output.parent_path()/(output.stem().wstring()+L".cycle"+std::to_wstring(cycle)+L".json");
  if(fs::exists(childOutput)){lastExit=73;allPassed=false;break;}
  std::wstring command=L"\""+std::wstring(own)+L"\" --child \""+childOutput.wstring()+L"\" \""+name+L"\"";
  STARTUPINFOW si{sizeof(si)};si.lpDesktop=full.data();si.dwFlags=STARTF_FORCEOFFFEEDBACK;PROCESS_INFORMATION pi{};
  const BOOL created=CreateProcessW(own,command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,nullptr,&si,&pi);
  if(!created){lastExit=67;allPassed=false;break;}
  CloseHandle(pi.hThread);DWORD waited=WaitForSingleObject(pi.hProcess,20000);DWORD exit=68;
  if(waited==WAIT_OBJECT_0)GetExitCodeProcess(pi.hProcess,&exit);
  else {TerminateProcess(pi.hProcess,68);WaitForSingleObject(pi.hProcess,2000);}
  CloseHandle(pi.hProcess);allPassed&=exit==0;lastExit=exit;
  if(fs::exists(childOutput)){
   std::ifstream f(childOutput,std::ios::binary);std::string child{std::istreambuf_iterator<char>(f),std::istreambuf_iterator<char>()};
   if(child.size()>65536||child.empty()||child.front()!='{'||child.back()!='}'||child.find("\"desktop\":\""+Narrow(name)+"\"")==std::string::npos){allPassed=false;reports.emplace_back("{\"passed\":false,\"stage\":\"invalid-child-report\"}");}
   else reports.push_back(std::move(child));
  }
  else reports.emplace_back("{\"passed\":false,\"stage\":\"missing-child-report\"}");
 }
 CloseDesktop(desktop);
 if(InputDesktop()!=originalInput){std::cerr<<"Input desktop unexpectedly changed\n";return 69;}
 std::ostringstream aggregate;aggregate<<std::boolalpha<<"{\"passed\":"<<(allPassed&&reports.size()==5)<<",\"completedCycles\":"<<reports.size()<<",\"inputDesktopUnchanged\":true,\"liveDesktopSwitched\":false,\"physicallyPresentedVerified\":false,\"cycles\":[";
 for(size_t i=0;i<reports.size();i++){if(i)aggregate<<",";aggregate<<reports[i];}aggregate<<"]}";if(!Write(output,aggregate.str()))return 74;
 std::wcout<<L"Five-cycle report: "<<output.wstring()<<L"; last exit="<<lastExit<<L"; input desktop unchanged\n";return allPassed?0:static_cast<int>(lastExit?lastExit:70);
}
