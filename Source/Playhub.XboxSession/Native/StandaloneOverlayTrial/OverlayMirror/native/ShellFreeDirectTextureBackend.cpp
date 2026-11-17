// SPDX-License-Identifier: GPL-3.0-only
#include "ShellFreeWin32MirrorBackend.h"
#include "DesktopOutputSampler.h"
#include "ShellFreeOutputReceipt.h"
#include "PixelMismatchDiagnostic.h"
#if defined(PLAYHUB_COMPANION_NATIVE_VISIBLE)
#include "NativeVisibleOutputReceipt.h"
#include "NativeVisibleAlignedAcquire.h"
#endif
#include "DirectCompositionSurface.h"
#include <wrl/client.h>
#include <dwmapi.h>
#include <atomic>
#include <string>
#include <vector>
#include <cstdio>
namespace playhub::mirror {namespace {
// Per-call, GUI-owned facts. All security values come from the original
// getter invocation. No logging/query occurs until Current has already failed.
struct CurrentProbe {
 const char* field=nullptr;Id observed{},expected{};Rect actualRect{},expectedRect{};bool rectangle{};
 bool Equal(const char* name,Id actual,Id wanted){if(actual==wanted)return true;if(!field){field=name;observed=actual;expected=wanted;}return false;}
 bool RectEqual(const char* name,Rect actual,Rect wanted){if(Same(actual,wanted))return true;if(!field){field=name;actualRect=actual;expectedRect=wanted;rectangle=true;}return false;}
};
inline bool Equal(CurrentProbe* p,const char* name,Id actual,Id wanted){return p?p->Equal(name,actual,wanted):actual==wanted;}
inline bool RectEqual(CurrentProbe* p,const char* name,Rect actual,Rect wanted){return p?p->RectEqual(name,actual,wanted):Same(actual,wanted);}
inline Id ClassHash(const wchar_t* value){Id h=1469598103934665603ull;for(unsigned i=0;i<128&&value[i];++i){h^=static_cast<std::uint16_t>(value[i]);h*=1099511628211ull;}return h;}
inline bool ClassEqual(CurrentProbe* p,const char* field,const wchar_t* actual,const wchar_t* expected){if(wcscmp(actual,expected)==0)return true;if(p&&!p->field){p->field=field;p->observed=ClassHash(actual);p->expected=ClassHash(expected);}return false;}
inline bool ClassEqual(CurrentProbe* p,const char* field,const wchar_t* actual,const std::wstring& expected){if(expected==actual)return true;if(p&&!p->field){p->field=field;p->observed=ClassHash(actual);p->expected=ClassHash(expected.c_str());}return false;}
Id Birth(HANDLE p){FILETIME c{},e{},k{},u{};return GetProcessTimes(p,&c,&e,&k,&u)?(Id(c.dwHighDateTime)<<32)|c.dwLowDateTime:0;}
bool Alive(HANDLE h,DWORD pid,Id birth,CurrentProbe* p=nullptr,const char* group="process"){
 const bool game=group[0]=='g';
 return Equal(p,game?"game-handle":"frame-handle",h!=nullptr,1)&&Equal(p,game?"game-process-id":"frame-process-id",GetProcessId(h),pid)
  &&Equal(p,game?"game-birth":"frame-birth",Birth(h),birth)&&Equal(p,game?"game-wait":"frame-wait",WaitForSingleObject(h,0),WAIT_TIMEOUT);
}
bool SameUser(HANDLE a,HANDLE b){HANDLE x{},y{};if(!OpenProcessToken(a,TOKEN_QUERY,&x))return false;if(!OpenProcessToken(b,TOKEN_QUERY,&y)){CloseHandle(x);return false;}BYTE ax[1024]{},by[1024]{};DWORD n{};bool ok=GetTokenInformation(x,TokenUser,ax,sizeof(ax),&n)&&GetTokenInformation(y,TokenUser,by,sizeof(by),&n)&&IsValidSid(reinterpret_cast<TOKEN_USER*>(ax)->User.Sid)&&IsValidSid(reinterpret_cast<TOKEN_USER*>(by)->User.Sid)&&EqualSid(reinterpret_cast<TOKEN_USER*>(ax)->User.Sid,reinterpret_cast<TOKEN_USER*>(by)->User.Sid);CloseHandle(x);CloseHandle(y);return ok;}
bool GetRect(HWND h,Rect& result,CurrentProbe* p=nullptr,const char* field="rectangle-read"){RECT r{};if(!Equal(p,field,GetWindowRect(h,&r)!=FALSE,1))return false;result={r.left,r.top,r.right,r.bottom};return Equal(p,field,ValidRect(result),1);}
bool Band(HWND h,DWORD expected,CurrentProbe* p=nullptr,const char* field="band"){using Get=BOOL(WINAPI*)(HWND,DWORD*);auto get=reinterpret_cast<Get>(GetProcAddress(GetModuleHandleW(L"user32.dll"),"GetWindowBand"));DWORD band{};return Equal(p,field,get!=nullptr,1)&&Equal(p,field,get(h,&band)!=FALSE,1)&&Equal(p,field,band,expected);}
bool Desktop(DWORD frameThread,CurrentProbe* p=nullptr){wchar_t own[128]{},frame[128]{},active[128]{};DWORD n{};auto name=[&](HANDLE h,wchar_t* target){return h&&GetUserObjectInformationW(h,UOI_NAME,target,128*sizeof(wchar_t),&n)&&n>sizeof(wchar_t)&&n<=128*sizeof(wchar_t);};HDESK input=OpenInputDesktop(0,FALSE,DESKTOP_READOBJECTS);bool ok=name(GetThreadDesktop(GetCurrentThreadId()),own)&&name(GetThreadDesktop(frameThread),frame)&&name(input,active)&&wcscmp(own,frame)==0&&wcscmp(own,active)==0;if(input)CloseDesktop(input);return Equal(p,"desktop",ok,1);}
struct Native final {
 SourceLease source{};BandTarget target{};DWORD ownerThread=GetCurrentThreadId();std::wstring className;HANDLE game{},frame{};direct::CompositionSurface compositor;bool retired=true,glassReady{},cancelled{};Id cookie{},reportedFailureGeneration{};unsigned diagnosticRows{};
 Microsoft::WRL::ComPtr<ID3D11Device> sourceDevice;SourcePixelsReader sourceReader;SourceTextureReader textureReader;output::Sampler sampler;output::Pixels baseline;output::Binding outputBinding{};Id showQpc{},receiptQpc{},lastProbeMs{};bool outputShown{};bool samplerReady{};const char* outputFailure="not-shown";output::Metrics outputMetrics{};
 struct ImageDiagnostic {unsigned width{},height{},stride{},layout{},encoding{},gridWidth{},gridHeight{},white{};std::uint64_t bytes{};};
 bool pixelDiagnosticCaptured{},pixelDiagnosticPending{};output::diagnostic::Report pixelDiagnostic{};
 output::Facts outputFacts{};output::SourceSampleDiagnostic sourceSampleDiagnostic{};ImageDiagnostic expectedDiagnostic{},baselineDiagnostic{},actualDiagnostic{};
 static ImageDiagnostic Describe(const output::ImageView& i){return {i.width,i.height,i.stride,static_cast<unsigned>(i.layout),static_cast<unsigned>(i.encoding),i.grid.sourceWidth,i.grid.sourceHeight,i.sdrWhiteLevel,static_cast<std::uint64_t>(i.bytes)};}

#if defined(PLAYHUB_COMPANION_NATIVE_VISIBLE)
 native_observation::FrozenSource<SourcePixels,SourceLease> nativeSourceCache;
#endif
 Native(ID3D11Device* device,SourcePixelsReader reader,SourceTextureReader textures):sourceDevice(device),sourceReader(std::move(reader)),textureReader(std::move(textures)){}
 static Id Qpc(){LARGE_INTEGER q{};return QueryPerformanceCounter(&q)&&q.QuadPart>0?static_cast<Id>(q.QuadPart):0;}
 static Id Frequency(){LARGE_INTEGER q{};return QueryPerformanceFrequency(&q)&&q.QuadPart>0?static_cast<Id>(q.QuadPart):0;}
 output::Binding Binding(const SourceLease& l)const{const auto luid=sampler.AdapterLuid();return {l.generation,l.sourceWindow,l.sourceCookie,reinterpret_cast<Id>(target.window),target.cookie,reinterpret_cast<Id>(MonitorFromWindow(reinterpret_cast<HWND>(l.gameWindow),MONITOR_DEFAULTTONULL)),(Id(static_cast<std::uint32_t>(luid.HighPart))<<32)|luid.LowPart};}
 void RevokeOutput(const char* reason){receiptQpc=0;outputFailure=reason;}
 void ReportOutput(const SourceLease& l,const char* reason){
  // Counts and timestamps only: never persist captured desktop/source pixels.
  wchar_t path[32768]{};DWORD n=GetTempPathW(32768,path);if(!n||n>=32700)return;std::wstring folder=std::wstring(path)+L"Playhub-Live-Mirror-20261112";if(!CreateDirectoryW(folder.c_str(),nullptr)&&GetLastError()!=ERROR_ALREADY_EXISTS)return;
  const auto attributes=GetFileAttributesW(folder.c_str());if(attributes==INVALID_FILE_ATTRIBUTES||(attributes&FILE_ATTRIBUTE_REPARSE_POINT)||!(attributes&FILE_ATTRIBUTE_DIRECTORY))return;
  std::wstring file=folder+L"\\output-"+std::to_wstring(l.ownerPid)+L"-"+std::to_wstring(l.ownerBirth)+L"-g"+std::to_wstring(l.generation)+L".jsonl";HANDLE h=CreateFileW(file.c_str(),FILE_APPEND_DATA,FILE_SHARE_READ,nullptr,OPEN_ALWAYS,FILE_FLAG_OPEN_REPARSE_POINT,nullptr);if(h==INVALID_HANDLE_VALUE)return;
  BY_HANDLE_FILE_INFORMATION info{};if(!GetFileInformationByHandle(h,&info)||(info.dwFileAttributes&(FILE_ATTRIBUTE_REPARSE_POINT|FILE_ATTRIBUTE_DIRECTORY))){CloseHandle(h);return;}
  const auto sd=sampler.ReadDiagnostic();char row[4096]{};int len=snprintf(row,sizeof(row),"{\"stage\":\"shell-free-output-receipt\",\"compositionBackend\":\"same-device-dcomp-surface\",\"textureHr\":\"0x%08X\",\"textureSourceQpc\":%llu,\"textureCommitQpc\":%llu,\"generation\":%llu,\"reason\":\"%s\",\"samples\":%u,\"matched\":%u,\"baselineChanged\":%u,\"colors\":%u,\"showQpc\":%llu,\"receiptQpc\":%llu,\"nowQpc\":%llu,\"samplerStage\":\"%s\",\"samplerHr\":\"0x%08X\",\"sourceDevice\":%s,\"sourceReader\":%s,\"adapterLuid\":\"%08X%08X\",\"wantedMonitor\":\"%llu\",\"observedMonitor\":\"%llu\",\"outputIndex\":%u,\"outputRotation\":%u,\"duplicateRotation\":%u,\"desktopWidth\":%u,\"desktopHeight\":%u,\"duplicateWidth\":%u,\"duplicateHeight\":%u,\"duplicateFormat\":%u,\"featureLevel\":%u,\"duplicateApi\":%u,\"sdrWhiteLevel\":%u,\"frameFormat\":%u,\"acquireBudgetMs\":%u,\"acquireWaitMs\":%u,\"mapWaitMs\":%u,\"copyMs\":%u,\"acquireElapsedMs\":%u,\"framePresentQpc\":%llu,\"outputBounds\":[%ld,%ld,%ld,%ld],\"monitorBounds\":[%ld,%ld,%ld,%ld],\"callerBounds\":[%ld,%ld,%ld,%ld],\"physicalReceipt\":%s,\"sourceGridStats\":[%u,%u,%u,%u,%u,%u,%u,%u,%u,%u,%u,%u],\"sourceQpc\":%llu,\"baselinePresentQpc\":%llu,\"baselineAcquiredQpc\":%llu,\"outputPresentQpc\":%llu,\"outputAcquiredQpc\":%llu,\"validationNowQpc\":%llu,\"qpcFrequency\":%llu,\"sourceImage\":[%u,%u,%u,%llu,%u,%u,%u,%u,%u],\"baselineImage\":[%u,%u,%u,%llu,%u,%u,%u,%u,%u],\"outputImage\":[%u,%u,%u,%llu,%u,%u,%u,%u,%u],\"rawPixelsSaved\":false}\n",static_cast<unsigned>(compositor.LastResult()),static_cast<unsigned long long>(compositor.SourceQpc()),static_cast<unsigned long long>(compositor.CommitQpc()),static_cast<unsigned long long>(l.generation),reason,outputMetrics.samples,outputMetrics.matched,outputMetrics.changed,outputMetrics.colorCount,static_cast<unsigned long long>(showQpc),static_cast<unsigned long long>(receiptQpc),static_cast<unsigned long long>(Qpc()),output::StageName(sd.stage),static_cast<unsigned>(sd.result),sourceDevice?"true":"false",sourceReader?"true":"false",static_cast<unsigned>(sd.adapter.HighPart),sd.adapter.LowPart,static_cast<unsigned long long>(sd.wantedMonitor),static_cast<unsigned long long>(sd.observedMonitor),sd.outputIndex,sd.outputRotation,sd.duplicateRotation,sd.desktopWidth,sd.desktopHeight,sd.duplicateWidth,sd.duplicateHeight,sd.duplicateFormat,sd.featureLevel,sd.duplicateApi,sd.sdrWhiteLevel,sd.frameFormat,sd.acquireBudgetMs,sd.acquireWaitMs,sd.mapWaitMs,sd.copyMs,sd.acquireElapsedMs,static_cast<unsigned long long>(sd.framePresentQpc),sd.outputBounds.left,sd.outputBounds.top,sd.outputBounds.right,sd.outputBounds.bottom,sd.monitorBounds.left,sd.monitorBounds.top,sd.monitorBounds.right,sd.monitorBounds.bottom,sd.callerBounds.left,sd.callerBounds.top,sd.callerBounds.right,sd.callerBounds.bottom,receiptQpc?"true":"false",sourceSampleDiagnostic.centers,sourceSampleDiagnostic.alphaZero,sourceSampleDiagnostic.alphaOpaque,sourceSampleDiagnostic.alpha255,sourceSampleDiagnostic.alphaMin,sourceSampleDiagnostic.alphaMax,sourceSampleDiagnostic.rgbNonzero,sourceSampleDiagnostic.rgbSmooth,sourceSampleDiagnostic.opaqueSmooth,sourceSampleDiagnostic.colors,sourceSampleDiagnostic.smoothColors,sourceSampleDiagnostic.smoothBands,static_cast<unsigned long long>(outputFacts.sourceQpc),static_cast<unsigned long long>(outputFacts.baselinePresentQpc),static_cast<unsigned long long>(outputFacts.baselineAcquiredQpc),static_cast<unsigned long long>(outputFacts.framePresentQpc),static_cast<unsigned long long>(outputFacts.frameAcquiredQpc),static_cast<unsigned long long>(outputFacts.nowQpc),static_cast<unsigned long long>(outputFacts.qpcFrequency),expectedDiagnostic.width,expectedDiagnostic.height,expectedDiagnostic.stride,static_cast<unsigned long long>(expectedDiagnostic.bytes),expectedDiagnostic.layout,expectedDiagnostic.encoding,expectedDiagnostic.gridWidth,expectedDiagnostic.gridHeight,expectedDiagnostic.white,baselineDiagnostic.width,baselineDiagnostic.height,baselineDiagnostic.stride,static_cast<unsigned long long>(baselineDiagnostic.bytes),baselineDiagnostic.layout,baselineDiagnostic.encoding,baselineDiagnostic.gridWidth,baselineDiagnostic.gridHeight,baselineDiagnostic.white,actualDiagnostic.width,actualDiagnostic.height,actualDiagnostic.stride,static_cast<unsigned long long>(actualDiagnostic.bytes),actualDiagnostic.layout,actualDiagnostic.encoding,actualDiagnostic.gridWidth,actualDiagnostic.gridHeight,actualDiagnostic.white);if(len>0&&len<int(sizeof(row))){DWORD written{};WriteFile(h,row,DWORD(len),&written,nullptr);}
  // Explicit diagnostic only: up to twelve existing source/baseline/output points,
  // once for the first failing generation of this backend. No GPU work or proof.
  if(pixelDiagnosticPending){pixelDiagnosticPending=false;char pixels[16384]{};const auto size=output::diagnostic::Format(pixelDiagnostic,pixels,sizeof(pixels));if(size){DWORD written{};WriteFile(h,pixels,static_cast<DWORD>(size),&written,nullptr);}}CloseHandle(h);
 }
 bool CheckOutput(const SourceLease& l,DWORD waitMs){
  const auto deadline=GetTickCount64()+waitMs;
  RevokeOutput("output-read-failed");outputMetrics={};outputFacts={};sourceSampleDiagnostic={};expectedDiagnostic={};baselineDiagnostic={};actualDiagnostic={};if(!outputShown||!sourceReader||!SameAuthority(source,l)||!Own()||!Context(l)||!output::SameBinding(outputBinding,Binding(l))){outputFailure="output-identity";return false;}
  SourcePixels actualSource;if(!sourceReader(actualSource)){outputFailure="source-readback";return false;}
  const auto capturedAt=GetTickCount64();if(capturedAt>=deadline){outputFailure="source-readback-timeout";return false;}
  output::Pixels frame;if(!sampler.Acquire(static_cast<DWORD>(deadline-capturedAt),frame,baseline.grid)){outputFailure="duplication-unavailable-or-timeout";return false;}
  if(!Own()||!Context(l)||!output::SameBinding(outputBinding,Binding(l))){outputFailure="output-identity-after-read";return false;}
  const output::ImageView expected{actualSource.width,actualSource.height,actualSource.stride,actualSource.bgra.data(),actualSource.bgra.size(),output::PixelEncoding::Bgra8,1000,actualSource.layout,actualSource.grid},before{baseline.width,baseline.height,baseline.stride,baseline.bgra.data(),baseline.bgra.size(),baseline.scRgbFloat16?output::PixelEncoding::ScRgbFloat16:output::PixelEncoding::Bgra8,baseline.sdrWhiteLevel,baseline.layout,baseline.grid},after{frame.width,frame.height,frame.stride,frame.bgra.data(),frame.bgra.size(),frame.scRgbFloat16?output::PixelEncoding::ScRgbFloat16:output::PixelEncoding::Bgra8,frame.sdrWhiteLevel,frame.layout,frame.grid};
  output::Facts facts{Binding(l),actualSource.presentQpc,baseline.presentQpc,baseline.acquiredQpc,showQpc,frame.presentQpc,frame.acquiredQpc,Qpc(),Frequency(),frame.protectedContent,true};
  sourceSampleDiagnostic=output::InspectSourceSamples(expected);outputFacts=facts;expectedDiagnostic=Describe(expected);baselineDiagnostic=Describe(before);actualDiagnostic=Describe(after);
  if(!output::Validate(expected,before,after,outputBinding,facts,&outputMetrics)){outputFailure=output::ExplainValidationFailure(expected,before,after,outputBinding,facts,outputMetrics);if(!pixelDiagnosticCaptured){pixelDiagnosticCaptured=true;pixelDiagnostic=output::diagnostic::Capture(expected,before,after,outputBinding,facts);pixelDiagnosticPending=pixelDiagnostic.count!=0;}return false;}
  receiptQpc=frame.presentQpc;lastProbeMs=GetTickCount64();outputFailure="verified";return true;
 }
 bool OutputCurrent(const SourceLease& l){if(!outputShown)return true;const auto qpc=Qpc(),frequency=Frequency();if(!receiptQpc||!qpc||!frequency||qpc<receiptQpc||qpc-receiptQpc>frequency/4){RevokeOutput("output-stale");return false;}if(GetTickCount64()-lastProbeMs<100)return true;const bool fresh=CheckOutput(l,20);if(!fresh)ReportOutput(l,outputFailure);return fresh;}
 bool Thread(CurrentProbe* p=nullptr)const{return Equal(p,"owner-thread",GetCurrentThreadId(),ownerThread);}
 bool Own(CurrentProbe* p=nullptr)const{DWORD pid{};wchar_t cls[128]{};
 return Thread(p)&&Equal(p,"target-present",target.window!=nullptr,1)&&Equal(p,"target-thread",GetWindowThreadProcessId(target.window,&pid),ownerThread)
  &&Equal(p,"target-pid",pid,GetCurrentProcessId())&&Equal(p,"target-root",reinterpret_cast<Id>(GetAncestor(target.window,GA_ROOT)),reinterpret_cast<Id>(target.window))
  &&Equal(p,"target-owner",reinterpret_cast<Id>(GetWindow(target.window,GW_OWNER)),0)&&Equal(p,"target-cookie",static_cast<Id>(GetWindowLongPtrW(target.window,GWLP_USERDATA)),reinterpret_cast<Id>(this))
  &&Equal(p,"target-class-read",GetClassNameW(target.window,cls,128)!=0,1)&&ClassEqual(p,"target-class",cls,className)
  &&Equal(p,"target-birth",Birth(GetCurrentProcess()),target.processBirth);
 }
 bool Context(const SourceLease& l,CurrentProbe* p=nullptr){DWORD pid{},session{};Rect sourceBounds{},gameBounds{};wchar_t cls[128]{};HWND sw=reinterpret_cast<HWND>(l.sourceWindow),gw=reinterpret_cast<HWND>(l.gameWindow);
  return Thread(p)&&Equal(p,"cancelled",cancelled,0)&&Equal(p,"source-owner-pid",GetCurrentProcessId(),l.ownerPid)&&Equal(p,"source-owner-birth",Birth(GetCurrentProcess()),l.ownerBirth)
   &&Alive(game,l.gamePid,l.gameBirth,p,"game-alive")&&Alive(frame,l.framePid,l.frameBirth,p,"frame-alive")
   &&Equal(p,"game-session-read",ProcessIdToSessionId(l.gamePid,&session)!=FALSE,1)&&Equal(p,"game-session",session,l.session)
   &&Equal(p,"frame-session-read",ProcessIdToSessionId(l.framePid,&session)!=FALSE,1)&&Equal(p,"frame-session",session,l.session)
   &&Equal(p,"owner-session-read",ProcessIdToSessionId(l.ownerPid,&session)!=FALSE,1)&&Equal(p,"owner-session",session,l.session)
   &&Equal(p,"source-thread",GetWindowThreadProcessId(sw,&pid),l.sourceThread)&&Equal(p,"source-pid",pid,l.ownerPid)
   &&Equal(p,"source-root",reinterpret_cast<Id>(GetAncestor(sw,GA_ROOT)),reinterpret_cast<Id>(sw))&&Equal(p,"source-owner",reinterpret_cast<Id>(GetWindow(sw,GW_OWNER)),0)
   &&Equal(p,"source-class-read",GetClassNameW(sw,cls,128)!=0,1)&&ClassEqual(p,"source-class",cls,L"PlayhubSteamCompanion")
   &&Equal(p,"source-cookie",static_cast<Id>(GetWindowLongPtrW(sw,GWLP_USERDATA)),l.sourceCookie)&&Equal(p,"source-visible",IsWindowVisible(sw)!=FALSE,1)&&Equal(p,"source-enabled",IsWindowEnabled(sw)!=FALSE,1)
   &&GetRect(sw,sourceBounds,p,"source-rect-read")&&RectEqual(p,"source-rect",sourceBounds,l.sourceRect)&&Band(sw,1,p,"source-band")
   &&Equal(p,"game-thread",GetWindowThreadProcessId(gw,&pid),l.frameThread)&&Equal(p,"game-pid",pid,l.framePid)&&Equal(p,"game-root",reinterpret_cast<Id>(GetAncestor(gw,GA_ROOT)),reinterpret_cast<Id>(gw))
   &&Equal(p,"game-visible",IsWindowVisible(gw)!=FALSE,1)&&Equal(p,"game-enabled",IsWindowEnabled(gw)!=FALSE,1)&&Equal(p,"game-iconic",IsIconic(gw)!=FALSE,0)
   &&GetRect(gw,gameBounds,p,"game-rect-read")&&RectEqual(p,"game-rect",gameBounds,l.gameRect)&&Desktop(l.frameThread,p)&&Equal(p,"game-foreground",reinterpret_cast<Id>(GetForegroundWindow()),reinterpret_cast<Id>(gw));
 }
 void ReportCurrentFailure(const SourceLease& l,const CurrentProbe& p)noexcept{
  struct PreserveError {DWORD value;~PreserveError(){SetLastError(value);}} preserve{GetLastError()};
  try{
  if(!p.field||!l.generation||!l.ownerPid||!l.ownerBirth||reportedFailureGeneration==l.generation||diagnosticRows>=32)return;
  reportedFailureGeneration=l.generation;++diagnosticRows;
  // Separate file avoids corrupting the existing EventLog writer's file cursor.
  // These diagnostic file/timestamp calls are not authority/security re-queries.
  wchar_t temp[32768]{};DWORD n=GetTempPathW(32768,temp);if(!n||n>=32768)return;std::wstring folder=std::wstring(temp)+L"Playhub-Live-Mirror-20261112";
  if(!CreateDirectoryW(folder.c_str(),nullptr)&&GetLastError()!=ERROR_ALREADY_EXISTS)return;
  DWORD attributes=GetFileAttributesW(folder.c_str());if(attributes==INVALID_FILE_ATTRIBUTES||!(attributes&FILE_ATTRIBUTE_DIRECTORY)||(attributes&FILE_ATTRIBUTE_REPARSE_POINT))return;
  std::wstring path=folder+L"\\backend-"+std::to_wstring(l.ownerPid)+L"-"+std::to_wstring(l.ownerBirth)+L"-g"+std::to_wstring(l.generation)+L".jsonl";
  HANDLE file=CreateFileW(path.c_str(),FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL|FILE_FLAG_OPEN_REPARSE_POINT,nullptr);if(file==INVALID_HANDLE_VALUE)return;
  BY_HANDLE_FILE_INFORMATION info{};if(!GetFileInformationByHandle(file,&info)||(info.dwFileAttributes&(FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT))){CloseHandle(file);return;}
  char text[1536]{};int bytes=snprintf(text,sizeof(text),"{\"stage\":\"backend-current-refused\",\"bootMs\":%llu,\"generation\":%llu,\"field\":\"%s\",\"observed\":\"%llu\",\"expected\":\"%llu\",\"rectangle\":%s,\"actualRect\":[%d,%d,%d,%d],\"expectedRect\":[%d,%d,%d,%d],\"gameGuiSampled\":false,\"securityGuardRelaxed\":false}\n",
   static_cast<unsigned long long>(GetTickCount64()),static_cast<unsigned long long>(l.generation),p.field,static_cast<unsigned long long>(p.observed),static_cast<unsigned long long>(p.expected),p.rectangle?"true":"false",p.actualRect.left,p.actualRect.top,p.actualRect.right,p.actualRect.bottom,p.expectedRect.left,p.expectedRect.top,p.expectedRect.right,p.expectedRect.bottom);
  if(bytes>0&&bytes<int(sizeof(text))){DWORD written{};WriteFile(file,text,DWORD(bytes),&written,nullptr);}CloseHandle(file);
  }catch(...){/* Diagnostic failure cannot alter the false Current result. */}
 }

 bool Paint(HDC dc){return Own()&&dc;}
 bool Glass(){glassReady=false;if(!Own())return false;BOOL enabled{};glassReady=SUCCEEDED(DwmIsCompositionEnabled(&enabled))&&enabled;return glassReady;}
 void ReportTextureFailure(const SourceLease& l,HRESULT result){
  outputFailure="direct-texture-update-refused";ReportOutput(l,outputFailure);
  wchar_t text[160]{};swprintf_s(text,L"Playhub direct texture update refused hr=0x%08X",static_cast<unsigned>(result));OutputDebugStringW(text);
 }
 bool Refresh(const SourceLease& l){
  if(!compositor.Attached())return true;
  if(!Own()||!Context(l)||GetTickCount64()>=l.expiresAt||!textureReader)return false;
  SourceTextureFrame frame;if(!textureReader(frame)||!frame.texture||!frame.presentQpc||!Own()||!Context(l))return false;
  if(frame.presentQpc==compositor.SourceQpc())return true;
  HRESULT result=compositor.Update(frame.texture.Get(),frame.presentQpc);if(FAILED(result)){ReportTextureFailure(l,result);return false;}
  return Own()&&Context(l)&&GetTickCount64()<l.expiresAt;
 }
 static LRESULT CALLBACK Proc(HWND h,UINT m,WPARAM w,LPARAM l){Native* self=reinterpret_cast<Native*>(GetWindowLongPtrW(h,GWLP_USERDATA));if(m==WM_NCCREATE){auto create=reinterpret_cast<CREATESTRUCTW*>(l);self=static_cast<Native*>(create->lpCreateParams);SetWindowLongPtrW(h,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(self));}if(self){if(m==WM_MOUSEACTIVATE)return MA_NOACTIVATE;if(m==WM_CLOSE){self->cancelled=true;SetWindowPos(h,nullptr,0,0,0,0,SWP_HIDEWINDOW|SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE|SWP_NOZORDER);return 0;}if(m==WM_DWMCOMPOSITIONCHANGED){self->Glass();return 0;}if(m==WM_ERASEBKGND){bool painted=self->Paint(reinterpret_cast<HDC>(w));if(!painted)self->glassReady=false;return painted?1:0;}if(m==WM_PAINT){PAINTSTRUCT ps{};HDC dc=BeginPaint(h,&ps);bool painted=self->Paint(dc);EndPaint(h,&ps);if(!painted)self->glassReady=false;return 0;}}
  return DefWindowProcW(h,m,w,l);
 }
 bool Create(const SourceLease& l){if(!Thread()||target.window||!Valid(l,GetTickCount64()))return false;source=l;cancelled=false;
  game=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,l.gamePid);frame=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,l.framePid);
  if(!Alive(game,l.gamePid,l.gameBirth)||!Alive(frame,l.framePid,l.frameBirth)||!SameUser(game,GetCurrentProcess())||!SameUser(frame,GetCurrentProcess())||!Context(l))return false;
  static std::atomic<Id> next{0};cookie=++next;className=L"Playhub.ShellFreeMirror."+std::to_wstring(GetCurrentProcessId())+L"."+std::to_wstring(cookie);
  WNDCLASSEXW cls{sizeof(cls)};cls.lpfnWndProc=Proc;cls.hInstance=GetModuleHandleW(nullptr);cls.lpszClassName=className.c_str();cls.hbrBackground=static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH));if(!RegisterClassExW(&cls))return false;
  target.processId=GetCurrentProcessId();target.threadId=ownerThread;target.processBirth=Birth(GetCurrentProcess());target.cookie=reinterpret_cast<Id>(this);
  target.rectangle={l.gameRect.left,l.gameRect.top,l.gameRect.left+128,l.gameRect.top+64};
  target.window=CreateWindowExW(WS_EX_TOPMOST|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_NOREDIRECTIONBITMAP,className.c_str(),L"",WS_POPUP,target.rectangle.left,target.rectangle.top,128,64,nullptr,nullptr,cls.hInstance,this);
  return Own()&&!IsWindowVisible(target.window)&&Context(l)&&Band(target.window,1)&&GetTickCount64()<l.expiresAt;
 }
 // Historical IBackend name: Ready here means ordinary compositor readiness.
 // It is NOT a C449 callback receipt or a claim that the destination is band16.
 BandState Poll(){return Thread()&&Own()&&!IsWindowVisible(target.window)&&Context(source)&&Band(target.window,1)&&GetTickCount64()<source.expiresAt?BandState::Ready:BandState::Refused;}
 bool Attach(const SourceLease& l){if(!Own()||!retired||compositor.Attached()||IsWindowVisible(target.window)||!Context(l)||!Band(target.window,1))return false;MONITORINFO monitor{sizeof(monitor)};auto display=MonitorFromWindow(reinterpret_cast<HWND>(l.gameWindow),MONITOR_DEFAULTTONULL);if(!display||!GetMonitorInfoW(display,&monitor))return false;Rect r{monitor.rcMonitor.left,monitor.rcMonitor.top,monitor.rcMonitor.right,monitor.rcMonitor.bottom};if(!ValidRect(r))return false;if(!SetWindowPos(target.window,nullptr,r.left,r.top,r.right-r.left,r.bottom-r.top,SWP_NOACTIVATE|SWP_NOZORDER|SWP_NOOWNERZORDER))return false;Rect actual{};if(!Own()||!GetRect(target.window,actual)||!Same(actual,r)||IsWindowVisible(target.window)||!Context(l))return false;target.rectangle=actual;if(!Glass()||!Context(l)||GetTickCount64()>=l.expiresAt)return false;
  if(!sourceDevice||!sourceReader||!sampler.Open(sourceDevice.Get(),display)){outputFailure="duplication-adapter-output-or-rotation";ReportOutput(l,outputFailure);return false;}samplerReady=true;const auto callerBounds=sampler.CallerBounds();if(callerBounds.left!=r.left||callerBounds.top!=r.top||callerBounds.right!=r.right||callerBounds.bottom!=r.bottom)return false;
  SourceTextureFrame frame;if(!textureReader||!textureReader(frame)||!frame.texture||!frame.presentQpc||!Context(l)||!Own())return false;
  D3D11_TEXTURE2D_DESC d{};frame.texture->GetDesc(&d);
  HRESULT result=compositor.Attach(sourceDevice.Get(),target.window,d.Width,d.Height,static_cast<UINT>(r.right-r.left),static_cast<UINT>(r.bottom-r.top));
  if(SUCCEEDED(result))result=compositor.Update(frame.texture.Get(),frame.presentQpc);
  if(FAILED(result)){ReportTextureFailure(l,result);return false;}
  return compositor.Attached()&&Context(l)&&Own()&&Band(target.window,1)&&GetTickCount64()<l.expiresAt;
 }
#if defined(PLAYHUB_COMPANION_NATIVE_VISIBLE)
 bool NativeVisible(const SourceLease& l,Id minimumQpc,NativeVisibleProbe& receipt){
  receipt={};receipt.minimumQpc=minimumQpc;receipt.observationKind=minimumQpc?2u:1u;
  auto refuse=[&](const char* reason,bool pending=false,bool keepSnapshot=false){receipt.reason=reason;receipt.pending=pending;if(!keepSnapshot)nativeSourceCache.Clear();if(!receipt.nowQpc)receipt.nowQpc=Qpc();if(!receipt.frequency)receipt.frequency=Frequency();const auto diagnostic=sampler.ReadDiagnostic();receipt.samplerStage=static_cast<std::uint32_t>(diagnostic.stage);receipt.samplerResult=diagnostic.result;if(!receipt.frameQpc)receipt.frameQpc=diagnostic.framePresentQpc;return false;};
  if(!Thread()||!SameAuthority(source,l)||GetTickCount64()>=l.expiresAt)return refuse("native-context-before");
  const bool hiddenTarget=target.window&&Own()&&!IsWindowVisible(target.window)&&compositor.Attached();
  const bool ownedClean=!target.window&&className.empty()&&!compositor.Attached()&&retired;
  if(!hiddenTarget&&!ownedClean)return refuse("owned-target-not-hidden-or-clean");
  if((hiddenTarget&&minimumQpc)||(ownedClean&&!minimumQpc))return refuse("native-observation-purpose");
  if(!game)game=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,l.gamePid);
  if(!frame)frame=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,l.framePid);
  if(!Context(l)||!SameUser(game,GetCurrentProcess())||!SameUser(frame,GetCurrentProcess()))return refuse("native-source-or-game-context");
  const auto monitor=MonitorFromWindow(reinterpret_cast<HWND>(l.gameWindow),MONITOR_DEFAULTTONULL);
  if(!monitor||!sourceDevice||!sourceReader||!textureReader)return refuse("native-monitor-or-source");
  if(!samplerReady){if(!sampler.Open(sourceDevice.Get(),monitor))return refuse("native-sampler-open");samplerReady=true;}
  const auto identity=[&](){const auto luid=sampler.AdapterLuid();return output::NativeVisibleBinding{l.generation,l.sourceWindow,l.sourceCookie,l.gameWindow,l.gameBirth,reinterpret_cast<Id>(monitor),(Id(static_cast<std::uint32_t>(luid.HighPart))<<32)|luid.LowPart};};
  const auto binding=identity();const auto same=[](const SourceLease&a,const SourceLease&b){return SameAuthority(a,b);};
  const SourcePixels* frozen=nativeSourceCache.Get(l,minimumQpc,Qpc(),Frequency(),same);
  if(!frozen){
   SourcePixels captured;if(!sourceReader(captured)||!Context(l))return refuse("native-source-read");
   nativeSourceCache.Store(std::move(captured),l,minimumQpc);
   frozen=nativeSourceCache.Get(l,minimumQpc,Qpc(),Frequency(),same);
   if(!frozen)return refuse("native-source-snapshot-stale",true);
  }
  const auto& pixels=*frozen;receipt.sourceQpc=pixels.presentQpc;
  // The CPU pixels remain immutable while Draw/Present can proceed between
  // retries. Validate current GPU dimensions/device without resampling pixels
  // or replacing the captured source QPC with a later frame or clock value.
  SourceTextureFrame currentTexture;if(!textureReader(currentTexture)||!currentTexture.texture)return refuse("native-source-texture");
  D3D11_TEXTURE2D_DESC description{};currentTexture.texture->GetDesc(&description);Microsoft::WRL::ComPtr<ID3D11Device> textureDevice;currentTexture.texture->GetDevice(&textureDevice);
  if(textureDevice.Get()!=sourceDevice.Get()||description.Width!=pixels.width||description.Height!=pixels.height||description.Format!=DXGI_FORMAT_B8G8R8A8_UNORM||description.SampleDesc.Count!=1)return refuse("native-source-dimensions-or-device");
  const auto grid=pixels.layout==output::ImageLayout::SourceRows?pixels.grid:output::SampleGrid{};
  const auto still=[&](){return Context(l)&&SameAuthority(source,l)&&output::SameNativeBinding(binding,identity())&&MonitorFromWindow(reinterpret_cast<HWND>(l.gameWindow),MONITOR_DEFAULTTONULL)==monitor
   &&(target.window?(Own()&&!IsWindowVisible(target.window)):(className.empty()&&!compositor.Attached()&&retired))&&GetTickCount64()<l.expiresAt;};
  output::Pixels framePixels;unsigned attempts{},skipped{};
  const auto aligned=native_observation::AcquireAligned(pixels.presentQpc,minimumQpc,GetTickCount64()+40,framePixels,
   [&](unsigned remaining,output::Pixels& out){return sampler.Acquire(remaining,out,grid);},
   [&](){const auto diag=sampler.ReadDiagnostic();return output::NativeObservationRetryable(diag.stage,diag.result);},
   still,[](){return GetTickCount64();},attempts,skipped);
  receipt.frameQpc=framePixels.presentQpc;
  if(aligned==native_observation::AcquireResult::Pending)return refuse("native-output-awaiting-aligned-frame",true,true);
  if(aligned!=native_observation::AcquireResult::Ready)return refuse("native-output-acquire");
  if(!still())return refuse("native-context-after-output");
  const output::ImageView expected{pixels.width,pixels.height,pixels.stride,pixels.bgra.data(),pixels.bgra.size(),output::PixelEncoding::Bgra8,1000,pixels.layout,pixels.grid};
  const output::ImageView actual{framePixels.width,framePixels.height,framePixels.stride,framePixels.bgra.data(),framePixels.bgra.size(),framePixels.scRgbFloat16?output::PixelEncoding::ScRgbFloat16:output::PixelEncoding::Bgra8,framePixels.sdrWhiteLevel,framePixels.layout,framePixels.grid};
  const output::NativeVisibleFacts facts{binding,pixels.presentQpc,minimumQpc,framePixels.presentQpc,framePixels.acquiredQpc,Qpc(),Frequency(),framePixels.protectedContent,true};output::Metrics metrics{};output::NativeSpatialDiagnostic spatial{};
  const bool matched=output::ValidateNativeVisible(expected,actual,binding,facts,&metrics,&spatial);receipt.samples=metrics.samples;receipt.matched=metrics.matched;receipt.nowQpc=facts.nowQpc;receipt.frequency=facts.qpcFrequency;
  if(!matched){
    receipt.spatialSummary={spatial.stage,spatial.groupCount,spatial.failedGroup,spatial.testedGroups};
    for(unsigned i=0;i<spatial.groups.size();++i){const auto& g=spatial.groups[i];const unsigned offset=i*10;
     receipt.spatialGroups[offset]=g.count;receipt.spatialGroups[offset+1]=g.matched;receipt.spatialGroups[offset+2]=g.spanX;receipt.spatialGroups[offset+3]=g.spanY;receipt.spatialGroups[offset+4]=g.anchorReject;
     receipt.spatialGroups[offset+5]=g.controls;receipt.spatialGroups[offset+6]=g.contrasting;receipt.spatialGroups[offset+7]=g.controlSpanX;receipt.spatialGroups[offset+8]=g.controlSpanY;receipt.spatialGroups[offset+9]=g.controlReject;
    }
    receipt.frameAcquiredQpc=facts.frameAcquiredQpc;receipt.validationFailure=output::ExplainNativeVisibleFailure(expected,actual,binding,facts,metrics);
    receipt.diagnosticFlags=(output::ValidImage(expected)?1u:0u)|(output::ValidImage(actual)?2u:0u)|(output::SameNativeBinding(binding,facts.binding)?4u:0u)|(facts.rotationIdentity?8u:0u)|(facts.protectedContent?16u:0u);
    const auto describe=[](const output::ImageView& i){return std::array<Id,9>{i.width,i.height,i.stride,Id(i.bytes),Id(i.layout),Id(i.encoding),i.grid.sourceWidth,i.grid.sourceHeight,i.sdrWhiteLevel};};
    receipt.sourceImage=describe(expected);receipt.actualImage=describe(actual);
    const auto stats=output::InspectSourceSamples(expected);receipt.sourceGridStats={stats.centers,stats.alphaZero,stats.alphaOpaque,stats.alpha255,stats.alphaMin,stats.alphaMax,stats.rgbNonzero,stats.rgbSmooth,stats.opaqueSmooth,stats.colors,stats.smoothColors,stats.smoothBands};
    if(facts.nowQpc>=facts.sourceQpc&&facts.nowQpc-facts.sourceQpc>facts.qpcFrequency/4)return refuse("native-source-snapshot-expired",true);
   return refuse(metrics.samples>=16&&metrics.matched*100ull<metrics.samples*95ull?"native-pixel-match":"native-information-or-output-match");
  }
  if(!still())return refuse("native-context-after-proof");
  const auto now=GetTickCount64();receipt.observedAt=now;receipt.expiresAt=std::min<Id>(now+250,l.expiresAt);receipt.reason="native-visible-verified";
  const auto diagnostic=sampler.ReadDiagnostic();receipt.samplerStage=static_cast<std::uint32_t>(diagnostic.stage);receipt.samplerResult=diagnostic.result;
  nativeSourceCache.Clear();return true;
 }
#endif
 bool Show(const SourceLease& l){
  const auto deadline=GetTickCount64()+250;RevokeOutput("not-shown");if(!Own()||!retired||!compositor.Attached()||!glassReady||!Context(l)||GetTickCount64()>=l.expiresAt)return false;
  SourcePixels sourcePlan;if(!sourceReader||!sourceReader(sourcePlan)){outputFailure="baseline-source-unavailable";ReportOutput(l,outputFailure);return false;}
  const output::ImageView planned{sourcePlan.width,sourcePlan.height,sourcePlan.stride,sourcePlan.bgra.data(),sourcePlan.bgra.size(),output::PixelEncoding::Bgra8,1000,sourcePlan.layout,sourcePlan.grid};
  if(!output::ValidImage(planned)||(sourcePlan.layout!=output::ImageLayout::Full&&sourcePlan.layout!=output::ImageLayout::SourceRows)){outputFailure="baseline-source-layout";ReportOutput(l,outputFailure);return false;}
  const auto grid=sourcePlan.layout==output::ImageLayout::SourceRows?sourcePlan.grid:output::SampleGrid{};
  if(!sampler.Acquire(40,baseline,grid)){outputFailure="baseline-unavailable";ReportOutput(l,outputFailure);return false;}outputBinding=Binding(l);
  if(!Context(l)||!Own()||!output::SameBinding(outputBinding,Binding(l)))return false;
  const bool shown=SetWindowPos(target.window,HWND_TOPMOST,0,0,0,0,SWP_SHOWWINDOW|SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE|SWP_NOOWNERZORDER)!=FALSE;showQpc=Qpc();outputShown=shown;
  if(!shown||!showQpc||!Own()||!IsWindowVisible(target.window)||!Context(l)||!Band(target.window,1))return false;
  do{const auto now=GetTickCount64();if(now>=deadline||deadline-now<30||now>=l.expiresAt)break;if(CheckOutput(l,static_cast<DWORD>(std::min<Id>(40,deadline-now)))){ReportOutput(l,"verified");return true;}}while(GetTickCount64()<deadline&&Own()&&Context(l)&&IsWindowVisible(target.window));
  RevokeOutput(outputFailure);ReportOutput(l,outputFailure);return false;
 }
 bool Hide(){outputShown=false;RevokeOutput("hidden");if(!target.window)return true;if(!Own())return false;if(IsWindowVisible(target.window)&&!SetWindowPos(target.window,nullptr,0,0,0,0,SWP_HIDEWINDOW|SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE|SWP_NOZORDER|SWP_NOOWNERZORDER))return false;return !IsWindowVisible(target.window);}
 bool Detach(){if(!Thread())return false;sampler.Close();samplerReady=false;
#if defined(PLAYHUB_COMPANION_NATIVE_VISIBLE)
 nativeSourceCache.Clear();
#endif
 baseline={};outputBinding={};return SUCCEEDED(compositor.Close());}
 bool Retire(){return Thread()&&retired;}
 bool Destroy(){if(!Thread()||!retired||compositor.Attached())return false;if(target.window){if(!Own()||IsWindowVisible(target.window)||!DestroyWindow(target.window))return false;target.window=nullptr;}if(!className.empty()){if(!UnregisterClassW(className.c_str(),GetModuleHandleW(nullptr)))return false;className.clear();}if(game){CloseHandle(game);game=nullptr;}if(frame){CloseHandle(frame);frame=nullptr;}return true;}
 bool Cleanup(){if(!Thread())return false;bool hidden=Hide(),detached=Detach(),safe=Retire();return hidden&&detached&&safe&&Destroy();}
};
std::mutex retiredMutex;std::vector<std::unique_ptr<Native>>& Retained(){static auto* list=new std::vector<std::unique_ptr<Native>>;return *list;}
class Backend final:public IBackend {
 std::unique_ptr<Native> native;
public:
 Backend(ID3D11Device* device,SourcePixelsReader reader,SourceTextureReader textureReader):native(std::make_unique<Native>(device,std::move(reader),std::move(textureReader))){}
 ~Backend(){if(native&&!native->Cleanup()){std::lock_guard<std::mutex> lock(retiredMutex);Retained().push_back(std::move(native));}}
 bool RefreshFrame(const SourceLease& l)override{return native->Refresh(l);}
#if defined(PLAYHUB_COMPANION_NATIVE_VISIBLE)
 bool ProbeNativeVisible(const SourceLease& l,Id minimumQpc,NativeVisibleProbe& receipt)override{return native->NativeVisible(l,minimumQpc,receipt);}
#endif
 bool CreateHidden(const SourceLease& l)override{return native->Create(l);}BandState PollBand()override{return native->Poll();}bool AttachGpu(const SourceLease& l)override{return native->Attach(l);}bool ShowNoActivate(const SourceLease& l)override{return native->Show(l);}bool Current(const SourceLease& l)override{Rect current{};CurrentProbe p;
  bool valid=native->Own(&p)&&GetRect(native->target.window,current,&p,"target-rect-read")&&RectEqual(&p,"target-rect",current,native->target.rectangle)
   &&(!native->compositor.Attached()||(Equal(&p,"glass-ready",native->glassReady,1)&&Band(native->target.window,1,&p,"target-band")))&&native->Context(l,&p)&&Equal(&p,"output-receipt",native->OutputCurrent(l),1);
  if(!valid)native->ReportCurrentFailure(l,p);return valid;
 }
 bool Hide()override{return native->Hide();}bool DetachGpu()override{return native->Detach();}bool RetireBand()override{return native->Retire();}bool DestroyOwned()override{return native->Destroy();}Id Window()const override{return reinterpret_cast<Id>(native->target.window);}Id Cookie()const override{return native->target.cookie;}bool IsShown()const override{return native->Own()&&IsWindowVisible(native->target.window);}bool HasCompositionSource()const override{return native->compositor.Attached();}
};
}
std::shared_ptr<IBackend> CreateShellFreeWin32MirrorBackend(ID3D11Device* device,SourcePixelsReader reader,SourceTextureReader textureReader){return std::make_shared<Backend>(device,std::move(reader),std::move(textureReader));}
void DrainRetainedShellFreeMirrorCleanup(){std::vector<std::unique_ptr<Native>> pending;{std::lock_guard<std::mutex> lock(retiredMutex);pending.swap(Retained());}for(auto& owner:pending)if(owner&&owner->Thread()&&owner->Cleanup())owner.reset();{std::lock_guard<std::mutex> lock(retiredMutex);for(auto& owner:pending)if(owner)Retained().push_back(std::move(owner));}}
}
