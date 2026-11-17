from pathlib import Path
import re, hashlib
root=Path(__file__).resolve().parents[1]
hook=(root/'OverlayMirror/callback-v3/Hook.cpp').read_text()
start=hook.index('static HMODULE ownModule')
body=hook[start:]
begin=body.index('BOOL WINAPI DllMain(');end=body.index('static int Hex(',begin)
body=body[:begin]+body[end:]
names=['GetModuleFileNameW','GetModuleHandleW','GetProcAddress','VirtualQuery','OpenProcess','GetWindowThreadProcessId','GetPropW','IsWindowVisible','GetForegroundWindow','GetTickCount64','GetLastError','SetLastError','GetCurrentThreadId','GetCurrentProcessId','GetCurrentProcess','OpenFileMappingW','MapViewOfFile','UnmapViewOfFile','CloseHandle','CallNextHookEx','OpenEventW','SetEvent','IsValidSid','EqualSid']
for name in names:body=re.sub(r'\b'+name+r'\b','Fake_'+name,body)
prefix=r'''
// Generated from the complete, unchanged callback Hook.cpp. Native APIs below
// are inert fakes. This proves admission/commit flow and typed lowering, not OS
// bands, signatures, physical WPF visibility or actual Explorer execution.
#include "BandAuthorization.h"
#include "../OverlayMirror/callback-v3/Protocol.h"
#include <iostream>
#include <stdexcept>
#include <string>
#include <cstring>
#include <functional>
namespace test {
namespace wire=::proof;
static wire::Shared s{};static DWORD error{};static bool allow[12];static int writes{},downstream{},checks{};
static HWND const gameFrame=reinterpret_cast<HWND>(600),wpf=reinterpret_cast<HWND>(700),target=reinterpret_cast<HWND>(800),shellWindow=reinterpret_cast<HWND>(900);
static HMODULE const module=reinterpret_cast<HMODULE>(1000);
static std::function<void()> bandRead;
void Check(bool value,const char* text){++checks;if(!value)throw std::runtime_error(text);}
DWORD Fake_GetLastError(){return error;}void Fake_SetLastError(DWORD n){error=n;}
ULONGLONG Fake_GetTickCount64(){return 1010;}
DWORD Fake_GetModuleFileNameW(HMODULE,wchar_t* out,DWORD cap){if(!allow[1])return 0;wcscpy_s(out,cap,s.modulePath);return DWORD(wcslen(out));}
HMODULE Fake_GetModuleHandleW(const wchar_t*){return module;}
BOOL WINAPI FakeGetBandFn(HWND w,DWORD* b){if(bandRead)bandRead();if(w!=target)return FALSE;*b=writes?16:1;return TRUE;}
BOOL WINAPI FakeSetBandFn(HWND w,HWND,DWORD b){if(w!=target||b!=16)return FALSE;++writes;return TRUE;}
FARPROC Fake_GetProcAddress(HMODULE,const char* name){if(!allow[10])return nullptr;return strcmp(name,"GetWindowBand")==0?reinterpret_cast<FARPROC>(&FakeGetBandFn):strcmp(name,"SetWindowBand")==0?reinterpret_cast<FARPROC>(&FakeSetBandFn):nullptr;}
SIZE_T Fake_VirtualQuery(const void*,MEMORY_BASIC_INFORMATION* p,SIZE_T){p->AllocationBase=module;p->State=MEM_COMMIT;p->Protect=PAGE_EXECUTE_READ;return sizeof(*p);}
HANDLE Fake_OpenProcess(DWORD,BOOL,DWORD pid){return reinterpret_cast<HANDLE>(std::uintptr_t(pid));}
DWORD Fake_GetWindowThreadProcessId(HWND w,DWORD* pid){*pid=w==target?s.controllerPid:s.sourcePid;return w==target?s.targetThread:s.sourceThread;}
HANDLE Fake_GetPropW(HWND,const wchar_t* name){return reinterpret_cast<HANDLE>(wcsstr(name,L"Low")?s.nonceLow:s.nonceHigh);}
BOOL Fake_IsWindowVisible(HWND){return FALSE;}
HWND Fake_GetForegroundWindow(){return allow[7]?gameFrame:wpf;}
DWORD Fake_GetCurrentThreadId(){return s.shellThread;}
DWORD Fake_GetCurrentProcessId(){return s.shellPid;}
HANDLE Fake_GetCurrentProcess(){return reinterpret_cast<HANDLE>(std::uintptr_t(s.shellPid));}
HANDLE Fake_OpenFileMappingW(DWORD,BOOL,const wchar_t*){return reinterpret_cast<HANDLE>(1);}
void* Fake_MapViewOfFile(HANDLE,DWORD,DWORD,DWORD,SIZE_T){return &s;}
BOOL Fake_UnmapViewOfFile(const void*){return TRUE;}
BOOL Fake_CloseHandle(HANDLE){return TRUE;}
LRESULT Fake_CallNextHookEx(HHOOK,int,WPARAM,LPARAM){++downstream;return 7;}
HANDLE Fake_OpenEventW(DWORD,BOOL,const wchar_t*){return reinterpret_cast<HANDLE>(1);}
BOOL Fake_SetEvent(HANDLE){return TRUE;}
BOOL Fake_IsValidSid(PSID){return allow[5];}
BOOL Fake_EqualSid(PSID,PSID){return allow[5];}
namespace proof {
using namespace ::proof;
struct Security{BYTE sid[SECURITY_MAX_SID_SIZE]{};DWORD sidLength{},session{},integrity{};};
bool Alive(HANDLE,DWORD,std::uint64_t){return allow[2];}
std::uint64_t Birth(HANDLE){return s.shellBirth;}
bool Image(HANDLE,const wchar_t*){return allow[3];}
bool ExactShell(HWND w,DWORD pid,DWORD thread){return allow[4]&&w==shellWindow&&pid==s.shellPid&&thread==s.shellThread;}
bool ReadSecurity(HANDLE,Security& p){p.session=s.session;p.integrity=s.integrity;p.sidLength=s.sidLength;return allow[5];}
bool SameSecurity(const Security&,const Security&){return allow[5];}
bool DesktopCurrent(DWORD,const wchar_t*,const wchar_t*){return allow[6];}
bool ExactForeground(HWND w,DWORD pid,DWORD thread,HANDLE,std::uint64_t birth){return allow[7]&&w==gameFrame&&pid==s.sourcePid&&thread==s.sourceThread&&birth==s.sourceBirth&&Fake_GetForegroundWindow()==gameFrame;}
bool CurrentSource(const Shared& v){return allow[8]&&v.sourceForeground==reinterpret_cast<std::uintptr_t>(gameFrame)&&v.sourceForeground!=reinterpret_cast<std::uintptr_t>(wpf)&&v.gamePid==20&&v.sourcePid==30&&v.sourceBirth==31&&v.gameBirth==21&&v.sourceCore==610&&v.gameThread==41&&v.sourceKind==1;}
bool FastCurrentSource(const Shared& v,HANDLE,HANDLE){return CurrentSource(v)&&allow[11];}
bool ExactTarget(const Shared& v,bool){return allow[9]&&v.targetWindow==reinterpret_cast<std::uintptr_t>(target)&&v.controllerPid==10&&v.targetThread==50&&v.targetWindow!=v.sourceForeground&&v.targetWindow!=reinterpret_cast<std::uintptr_t>(wpf);}
bool NoInput(const Shared&){return allow[0];}
}
'''
suffix=r'''
struct HudSource:playhub::hud::ISourceAuthority{bool current=true;bool Current(const playhub::hud::Authority&)const override{return current;}};
void Reset(){for(auto& b:allow)b=true;s={};writes=downstream=0;bandRead={};s.magic=wire::Magic;s.version=wire::Version;s.size=sizeof(s);s.nonceLow=0x1111111111111111;s.nonceHigh=0x2222222222222222;s.controllerPid=10;s.controllerBirth=11;s.targetThread=50;s.targetWindow=reinterpret_cast<std::uintptr_t>(target);s.shellPid=100;s.shellBirth=101;s.shellThread=102;s.shellWindow=reinterpret_cast<std::uintptr_t>(shellWindow);s.session=1;s.integrity=0x2000;s.sidLength=12;s.sourceForeground=reinterpret_cast<std::uintptr_t>(gameFrame);s.sourcePid=30;s.sourceBirth=31;s.sourceThread=40;s.gamePid=20;s.gameBirth=21;s.gameThread=41;s.sourceCore=610;s.sourceKind=1;s.issued=1000;s.deadline=1400;s.armed=1;wcscpy_s(s.aumid,L"Package!Game");wcscpy_s(s.modulePath,L"C:\\private\\PlayhubExactBand-11111111111111112222222222222222.dll");wcscpy_s(s.controllerImage,L"C:\\GamingMode.exe");wcscpy_s(s.shellImage,L"C:\\Windows\\explorer.exe");wcscpy_s(s.station,L"WinSta0");wcscpy_s(s.desktop,L"Default");wcscpy_s(s.operationEvent,L"Local\\event");}
void Invoke(){CWPSTRUCT message{};message.message=WM_NULL;message.hwnd=shellWindow;message.wParam=WPARAM(s.nonceLow);message.lParam=LPARAM(s.nonceHigh);ShellCallback(0,0,reinterpret_cast<LPARAM>(&message));}
int Run(){
 Reset();Invoke();Check(s.received==1&&writes==1&&s.bandAfter==16&&s.operationPermit==LONG(wire::Permit::Finished),"whole actual callback admits exact hidden HUD target with real game foreground");Check(s.inflight==0&&downstream==1,"whole callback cleans inflight and forwards");
 for(unsigned i=0;i<12;i++){Reset();allow[i]=false;Invoke();Check(writes==0,"every full callback native validation/commit refusal suppresses write");Check(s.inflight==0&&downstream==1,"rejection forwards and cleans inflight");}
 Reset();s.revoked=1;Invoke();Check(writes==0,"revoked lease never writes");Reset();s.deadline=999;Invoke();Check(writes==0,"expired wire never writes");Reset();s.version++;Invoke();Check(writes==0,"wrong callback protocol rejected");Reset();s.claimed=1;Invoke();Check(writes==0,"old claimed authority cannot replay");
 Reset();bandRead=[](){allow[7]=false;};Invoke();Check(writes==0&&s.operationPermit==LONG(wire::Permit::Finished),"foreground drift during band getter cannot write after commit");Reset();bandRead=[](){s.revoked=1;};Invoke();Check(writes==0,"revoked during getter cannot write");
 playhub::hud::Authority a;a.purpose=playhub::hud::Purpose::PerformanceHud;a.generation=1;a.collectorEpoch=2;a.collectorLease=std::string(32,'a');a.ownerBuild=std::string(64,'b');a.appId=123;a.agentPid=10;a.agentBirth=11;a.gamePid=20;a.gameBirth=21;a.framePid=30;a.frameBirth=31;a.frameThread=40;a.sourceThread=55;a.session=1;a.gameWindow=reinterpret_cast<std::uintptr_t>(gameFrame);a.wpfWindow=reinterpret_cast<std::uintptr_t>(wpf);a.wpfInstance=1;a.gameRect={0,0,3840,2160};a.wpfRect={7,7,295,191};a.wpfClass=L"HwndWrapper[GamingMode;Playhub performance HUD;GUID]";
 auto source=std::make_shared<HudSource>();playhub::hud::BandLease hud;hud.authority=a;hud.issuedAt=1000;hud.expiresAt=5000;hud.source=source;std::fill_n(hud.authorizationNonce.data(),64,'a');wcscpy_s(hud.actualFrameAumid.data(),513,L"Package!Game");playhub::mirror::BandAuthorization lowered(hud);auto physical=lowered.Game();Check(lowered.Current(1010),"HUD authorization uses its own typed lease validator");Check(physical.gameWindow==a.gameWindow&&physical.gameWindow!=a.wpfWindow&&physical.framePid==30&&physical.gamePid==20&&physical.expectedFrameAumid[0]==L'P',"HUD lowering carries real game/frame not WPF/Steam identity");
 Reset();s.controllerPid=a.agentPid;s.controllerBirth=a.agentBirth;s.sourceForeground=physical.gameWindow;s.sourcePid=physical.framePid;s.sourceBirth=physical.frameBirth;s.sourceThread=physical.frameThread;s.gamePid=physical.gamePid;s.gameBirth=physical.gameBirth;s.session=physical.session;s.sourceRect={physical.gameRect.left,physical.gameRect.top,physical.gameRect.right,physical.gameRect.bottom};wcscpy_s(s.aumid,physical.expectedFrameAumid.data());Invoke();Check(s.received==1&&writes==1&&s.bandAfter==16,"actual typed HUD lowering feeds whole callback with separate WPF and native target threads");Check(a.sourceThread!=s.targetThread&&s.sourceThread==a.frameThread,"WPF UI thread is never confused with target worker or game frame thread");
 source->current=false;Check(!lowered.Current(1010),"revoked publisher/source rejects HUD lowering");source->current=true;hud.authority.purpose=playhub::hud::Purpose::None;Check(!playhub::mirror::BandAuthorization(hud).Current(1010),"untyped purpose cannot enter callback lowering");
 static_assert(sizeof(wire::Shared)==sizeof(playhub_band16::Shared));
 std::cout<<"{\"checks\":"<<checks<<",\"wholeActualCallback\":true,\"nativeApis\":\"inert-fakes\",\"passed\":true}\n";return 0;
}
}
int main(){try{return test::Run();}catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}}
'''
# Callback native Shared is included for a compile-time wire correspondence.
prefix=prefix.replace('#include "BandAuthorization.h"','#include "BandAuthorization.h"\n#include "band/Protocol.h"')
(root/'tests/HudCallbackChecks.generated.cpp').write_text(prefix+body+suffix)
(root/'tests/callback-source.sha256').write_text(hashlib.sha256(hook.encode()).hexdigest()+'\n')
