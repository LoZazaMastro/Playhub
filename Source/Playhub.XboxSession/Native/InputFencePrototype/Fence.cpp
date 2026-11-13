// SPDX-License-Identifier: GPL-3.0-only
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <xinput.h>
#include <sddl.h>
#include <bcrypt.h>
#include <memory>
#include <atomic>
#include <string>
#include <vector>
#include <intrin.h>
#include <psapi.h>
#include "FenceReadback.h"
#include "MinHook/include/MinHook.h"
#include "FencePolicy.h"
#pragma comment(lib,"advapi32.lib")
#pragma comment(lib,"bcrypt.lib")
#pragma comment(lib,"user32.lib")
extern "C" { __declspec(dllexport) fence::Readback FenceReadback{}; }
namespace {
using GetState=DWORD(WINAPI*)(DWORD,XINPUT_STATE*);
using GetKey=DWORD(WINAPI*)(DWORD,DWORD,XINPUT_KEYSTROKE*);
struct Slot {void* target{};GetState state{};GetKey key{};bool installed{};};
Slot slots[3];std::unique_ptr<fence::Policy> policy;
HANDLE producer{},mapping{};const fence::Frame* view{};fence::Identity identity;
std::atomic<bool> enabled{};std::atomic<unsigned> calls{};std::mutex control;
thread_local unsigned hookDepth{};
struct Flight {Flight(){++hookDepth;calls.fetch_add(1);}~Flight(){calls.fetch_sub(1);--hookDepth;}};
HWND gameWindow{};
HMODULE rendererModule{};HANDLE rendererFile{};fence::ModuleRange rendererRange{},gameImageRange{};
std::mutex diagnosticLock;
std::atomic<fence::RendererMode> rendererMode{fence::RendererMode::Unknown};std::atomic<bool> rendererRevoked{};
void DiagnosticStage(fence::AttachStage stage,HRESULT result){std::lock_guard<std::mutex> guard(diagnosticLock);
 ++FenceReadback.sequence;MemoryBarrier();FenceReadback.attachStage=static_cast<std::uint32_t>(stage);
 FenceReadback.attachError=static_cast<std::uint32_t>(result);FenceReadback.rendererMode=static_cast<std::uint32_t>(rendererMode.load());
 FenceReadback.sampledAt=GetTickCount64();MemoryBarrier();++FenceReadback.sequence;}
HRESULT Refuse(fence::AttachStage stage,HRESULT result){DiagnosticStage(stage,result);return result;}
bool RendererIdentityCurrent(){
 if(rendererRevoked.load(std::memory_order_acquire))return false;
 SetLastError(ERROR_SUCCESS);HMODULE observed=GetModuleHandleW(L"GameOverlayRenderer64.dll");DWORD error=observed?ERROR_SUCCESS:GetLastError();
 if(fence::RendererMatches(rendererMode.load(),reinterpret_cast<std::uintptr_t>(rendererModule),reinterpret_cast<std::uintptr_t>(observed),error))return true;
 enabled.store(false,std::memory_order_release);
 if(!rendererRevoked.exchange(true)){
  DiagnosticStage(fence::AttachStage::RendererChanged,E_ACCESSDENIED);
  std::lock_guard<std::mutex> guard(diagnosticLock);++FenceReadback.sequence;MemoryBarrier();FenceReadback.flags|=16;
  MemoryBarrier();++FenceReadback.sequence;
 }
 return false;
}
void DropRenderer(){if(rendererFile)CloseHandle(rendererFile);rendererFile=nullptr;
 if(rendererModule)FreeLibrary(rendererModule);rendererModule=nullptr;rendererRange={};}
bool RendererCaller(const void* caller){MEMORY_BASIC_INFORMATION memory{};
 return fence::Inside(rendererRange,caller)&&VirtualQuery(caller,&memory,sizeof memory)==sizeof memory
  &&memory.Type==MEM_IMAGE&&memory.AllocationBase==rendererModule
  &&!(memory.Protect&(PAGE_GUARD|PAGE_NOACCESS))&&(memory.Protect&(PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY));}
void Observe(unsigned api,const void* caller,bool valve,bool guide,bool captured){
 std::lock_guard<std::mutex> guard(diagnosticLock);auto& d=FenceReadback;
 ++d.sequence;MemoryBarrier();
 auto increment=[](std::uint32_t& value){if(value!=0xffffffff)++value;};
 if(valve)increment(d.valveReads);else{
  if(api==0)increment(d.stateReads);else if(api==1)increment(d.exReads);else increment(d.keyReads);
  if(fence::Inside(gameImageRange,caller))increment(d.gameImageReads);
  if(guide)increment(d.guideSeen);if(captured)increment(d.captures);
 }
 d.sampledAt=GetTickCount64();MemoryBarrier();++d.sequence;
}
void InitializeReadback(){auto& d=FenceReadback;d={};d.session=identity.session;d.gameBirth=identity.gameBirth;d.producerBirth=identity.producerBirth;
 d.gamePid=identity.gamePid;d.producerPid=identity.producerPid;d.flags=0;d.sampledAt=GetTickCount64();d.sequence=2;}
bool FilePe(HANDLE file,std::uint32_t imageSize,std::uint32_t timestamp){
 LARGE_INTEGER bytes{},zero{},offset{};IMAGE_DOS_HEADER dos{};IMAGE_NT_HEADERS64 nt{};DWORD got{};
 if(!GetFileSizeEx(file,&bytes)||bytes.QuadPart<4096||bytes.QuadPart>134217728||!SetFilePointerEx(file,zero,nullptr,FILE_BEGIN)
  ||!ReadFile(file,&dos,sizeof dos,&got,nullptr)||got!=sizeof dos||dos.e_magic!=IMAGE_DOS_SIGNATURE||dos.e_lfanew<64
  ||dos.e_lfanew>bytes.QuadPart-static_cast<LONGLONG>(sizeof nt))return false;
 offset.QuadPart=dos.e_lfanew;
 return SetFilePointerEx(file,offset,nullptr,FILE_BEGIN)&&ReadFile(file,&nt,sizeof nt,&got,nullptr)&&got==sizeof nt
  &&nt.Signature==IMAGE_NT_SIGNATURE&&nt.FileHeader.Machine==IMAGE_FILE_MACHINE_AMD64&&nt.OptionalHeader.Magic==IMAGE_NT_OPTIONAL_HDR64_MAGIC
  &&nt.OptionalHeader.SizeOfImage==imageSize&&imageSize>=4096&&nt.FileHeader.TimeDateStamp==timestamp;
}
bool FileHash(HANDLE file,const std::array<BYTE,32>& expected){
 LARGE_INTEGER zero{};if(!SetFilePointerEx(file,zero,nullptr,FILE_BEGIN))return false;
 BCRYPT_ALG_HANDLE algorithm{};BCRYPT_HASH_HANDLE hash{};DWORD objectBytes{},got{};bool ok=false;
 if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)return false;
 if(BCryptGetProperty(algorithm,BCRYPT_OBJECT_LENGTH,reinterpret_cast<BYTE*>(&objectBytes),sizeof objectBytes,&got,0)>=0&&objectBytes&&objectBytes<=1048576){
  std::vector<BYTE> object(objectBytes);std::array<BYTE,65536> buffer{};std::array<BYTE,32> digest{};
  if(BCryptCreateHash(algorithm,&hash,object.data(),objectBytes,nullptr,0,0)>=0){
   DWORD read{};std::uint64_t total{};ok=true;
   while(ok){if(!ReadFile(file,buffer.data(),static_cast<DWORD>(buffer.size()),&read,nullptr)){ok=false;break;}
    if(!read)break;total+=read;if(total>134217728||BCryptHashData(hash,buffer.data(),read,0)<0)ok=false;}
   ok=ok&&BCryptFinishHash(hash,digest.data(),static_cast<ULONG>(digest.size()),0)>=0&&digest==expected;
   BCryptDestroyHash(hash);
  }
 }
 BCryptCloseAlgorithmProvider(algorithm,0);return ok;
}
fence::AttachStage RetainRenderer(const wchar_t* expected,const std::array<BYTE,32>& sha,std::uint32_t imageSize,std::uint32_t timestamp){
 using S=fence::AttachStage;SetLastError(ERROR_SUCCESS);HMODULE observed=GetModuleHandleW(L"GameOverlayRenderer64.dll");
 if(!observed&&GetLastError()!=ERROR_MOD_NOT_FOUND)return S::RendererObserve;
 if(!expected||!*expected)return S::RendererFile;
 rendererFile=CreateFileW(expected,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
 if(rendererFile==INVALID_HANDLE_VALUE){rendererFile=nullptr;return S::RendererFile;}
 if(!FilePe(rendererFile,imageSize,timestamp))return S::RendererPe;
 if(!FileHash(rendererFile,sha))return S::RendererHash;
 if(observed){
  if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,reinterpret_cast<LPCWSTR>(observed),&rendererModule)||rendererModule!=observed)return S::RendererPin;
  wchar_t path[32768]{};MODULEINFO info{};
  if(!GetModuleFileNameW(rendererModule,path,32768)||_wcsicmp(path,expected)
   ||!GetModuleInformation(GetCurrentProcess(),rendererModule,&info,sizeof info)||info.SizeOfImage!=imageSize)return S::RendererLoadedIdentity;
  auto dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(rendererModule);
  if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<64||static_cast<std::uint32_t>(dos->e_lfanew)>imageSize-sizeof(IMAGE_NT_HEADERS64))return S::RendererLoadedIdentity;
  auto nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(reinterpret_cast<const BYTE*>(rendererModule)+dos->e_lfanew);
  if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC
   ||nt->FileHeader.TimeDateStamp!=timestamp||nt->OptionalHeader.SizeOfImage!=imageSize)return S::RendererLoadedIdentity;
  rendererRange={reinterpret_cast<std::uintptr_t>(rendererModule),imageSize};rendererMode.store(fence::RendererMode::Present);
 }else rendererMode.store(fence::RendererMode::Absent);
 if(!RendererIdentityCurrent())return S::RendererChanged;
 HMODULE game=GetModuleHandleW(nullptr);MODULEINFO gameInfo{};
 if(!game||!GetModuleInformation(GetCurrentProcess(),game,&gameInfo,sizeof gameInfo))return S::GameImage;
 gameImageRange={reinterpret_cast<std::uintptr_t>(game),gameInfo.SizeOfImage};return S::None;
}
void DropRenderer();
void DropTransport(){DropRenderer();if(view)UnmapViewOfFile(view);view=nullptr;if(mapping)CloseHandle(mapping);if(producer)CloseHandle(producer);mapping=producer=nullptr;}
bool Birth(HANDLE p,std::uint64_t birth){FILETIME a{},b{},c{},d{};return GetProcessTimes(p,&a,&b,&c,&d)&&((std::uint64_t(a.dwHighDateTime)<<32)|a.dwLowDateTime)==birth;}
bool SameUser(HANDLE a,HANDLE b){HANDLE ta{},tb{};if(!OpenProcessToken(a,TOKEN_QUERY,&ta))return false;
 if(!OpenProcessToken(b,TOKEN_QUERY,&tb)){CloseHandle(ta);return false;}
 BYTE aa[2048]{},bb[2048]{};DWORD na{},nb{};
 bool ok=GetTokenInformation(ta,TokenUser,aa,sizeof aa,&na)&&GetTokenInformation(tb,TokenUser,bb,sizeof bb,&nb)
  &&EqualSid(reinterpret_cast<TOKEN_USER*>(aa)->User.Sid,reinterpret_cast<TOKEN_USER*>(bb)->User.Sid);
 CloseHandle(ta);CloseHandle(tb);return ok;}
bool Current(){DWORD session{};
 DWORD windowPid{};
 return enabled.load(std::memory_order_acquire)&&RendererIdentityCurrent()&&producer&&WaitForSingleObject(producer,0)==WAIT_TIMEOUT
  &&GetProcessId(producer)==identity.producerPid&&Birth(producer,identity.producerBirth)
  &&GetCurrentProcessId()==identity.gamePid&&Birth(GetCurrentProcess(),identity.gameBirth)
  &&ProcessIdToSessionId(identity.gamePid,&session)&&session==identity.windowsSession
  &&gameWindow&&GetForegroundWindow()==gameWindow&&GetAncestor(gameWindow,GA_ROOT)==gameWindow
  &&GetWindowThreadProcessId(gameWindow,&windowPid)&&windowPid==identity.gamePid&&GetForegroundWindow()==gameWindow;
}
bool Read(fence::Frame& f){if(!view)return false;auto first=*reinterpret_cast<const volatile LONG*>(&view->sequence);
 if(first<=0||(first&1))return false;MemoryBarrier();std::memcpy(&f,view,sizeof f);MemoryBarrier();
 auto last=*reinterpret_cast<const volatile LONG*>(&view->sequence);
 return first==last&&f.sequence==std::uint32_t(last)&&f.gameId==identity.gameId;
}
bool EnableInstalledHooks(){
 enabled.store(false,std::memory_order_release);
 for(auto& slot:slots)if(slot.installed&&MH_EnableHook(slot.target)!=MH_OK){
  for(auto& installed:slots)if(installed.installed)MH_DisableHook(installed.target);
  return false;
 }
 if(!RendererIdentityCurrent()){
  for(auto& slot:slots)if(slot.installed)MH_DisableHook(slot.target);
  return false;
 }
 enabled.store(true,std::memory_order_release);return true;
}
bool RequiredExports(){return slots[0].target&&slots[1].target&&slots[2].target;}
bool Guide(DWORD index,bool& held){if(index>=4||!slots[1].state)return false;XINPUT_STATE ex{};
 if(slots[1].state(index,&ex)!=ERROR_SUCCESS)return false;held=(ex.Gamepad.wButtons&0x0400)!=0;return true;}
template<unsigned I> DWORD WINAPI StateHook(DWORD index,XINPUT_STATE* state){
 const void* caller=_ReturnAddress();auto original=slots[I].state;if(hookDepth)return original(index,state);
 DWORD incoming=GetLastError();Flight flight;const bool rendererCurrent=RendererIdentityCurrent();const bool valve=rendererCurrent&&RendererCaller(caller);SetLastError(incoming);
 DWORD status=original(index,state);DWORD error=GetLastError();bool guideObserved=false,captured=false;
 if(!valve&&enabled.load()&&RendererIdentityCurrent()&&policy){auto saved=state&&status==ERROR_SUCCESS?*state:XINPUT_STATE{};try{bool held{},known{};
  if constexpr(I==1){known=status==ERROR_SUCCESS&&state;held=known&&(state->Gamepad.wButtons&0x0400)!=0;}
  else known=Guide(index,held);
  guideObserved=known&&held;captured=policy->StateResult(index,status,reinterpret_cast<fence::State*>(state),known,held);
 }catch(...){if(state&&status==ERROR_SUCCESS)*state=saved;}}
 try{Observe(I,caller,valve,guideObserved,captured);}catch(...){}
 SetLastError(error);return status;
}
std::uint16_t DigitalKey(WORD key){switch(key){
 case VK_PAD_A:return XINPUT_GAMEPAD_A;case VK_PAD_B:return XINPUT_GAMEPAD_B;case VK_PAD_X:return XINPUT_GAMEPAD_X;case VK_PAD_Y:return XINPUT_GAMEPAD_Y;
 case VK_PAD_LSHOULDER:return XINPUT_GAMEPAD_LEFT_SHOULDER;case VK_PAD_RSHOULDER:return XINPUT_GAMEPAD_RIGHT_SHOULDER;
 case VK_PAD_START:return XINPUT_GAMEPAD_START;case VK_PAD_BACK:return XINPUT_GAMEPAD_BACK;
 case VK_PAD_LTHUMB_PRESS:return XINPUT_GAMEPAD_LEFT_THUMB;case VK_PAD_RTHUMB_PRESS:return XINPUT_GAMEPAD_RIGHT_THUMB;
 case VK_PAD_DPAD_UP:return XINPUT_GAMEPAD_DPAD_UP;case VK_PAD_DPAD_DOWN:return XINPUT_GAMEPAD_DPAD_DOWN;
 case VK_PAD_DPAD_LEFT:return XINPUT_GAMEPAD_DPAD_LEFT;case VK_PAD_DPAD_RIGHT:return XINPUT_GAMEPAD_DPAD_RIGHT;default:return 0;}}
DWORD WINAPI KeyHook(DWORD index,DWORD reserved,XINPUT_KEYSTROKE* key){
 const void* caller=_ReturnAddress();if(hookDepth)return slots[2].key(index,reserved,key);
 DWORD incoming=GetLastError();Flight flight;const bool rendererCurrent=RendererIdentityCurrent();const bool valve=rendererCurrent&&RendererCaller(caller);SetLastError(incoming);
 DWORD status=slots[2].key(index,reserved,key);DWORD error=GetLastError();bool guideObserved=false,captured=false;
 if(!valve&&enabled.load()&&RendererIdentityCurrent()&&policy){auto saved=key&&status==ERROR_SUCCESS?*key:XINPUT_KEYSTROKE{};try{bool held{};DWORD actualIndex=index==XUSER_INDEX_ANY&&key&&status==ERROR_SUCCESS?key->UserIndex:index;bool known=Guide(actualIndex,held);
  guideObserved=known&&held;captured=policy->KeystrokeResult(status,key,sizeof(*key),known,held,actualIndex,key&&status==ERROR_SUCCESS?DigitalKey(key->VirtualKey):0,key&&status==ERROR_SUCCESS&&(key->Flags&XINPUT_KEYSTROKE_KEYUP)!=0);if(captured)status=ERROR_EMPTY;
 }catch(...){if(key&&status==ERROR_SUCCESS)*key=saved;}}
 try{Observe(2,caller,valve,guideObserved,captured);}catch(...){}
 SetLastError(error);return status;
}
bool ImageCurrent(const wchar_t* expected){wchar_t actual[32768]{};DWORD n=32768;
 return expected&&*expected&&QueryFullProcessImageNameW(GetCurrentProcess(),0,actual,&n)&&!_wcsicmp(actual,expected);
}
}
// Root-only prototype ABI. The mapping must be delivered by the authenticated
// installed companion private IPC. No injector/IPC activation path is included
// here, so this DLL is NOT shippable or ready for a live target attachment.
struct Attach {std::uint32_t size,version;fence::Identity identity;HWND gameWindow;
 HANDLE producerHandle;HANDLE readMappingHandle;wchar_t exactImage[1024];
 wchar_t exactRenderer[768];std::array<BYTE,32> rendererSha;std::uint32_t rendererSize,rendererTimestamp;};
static_assert(sizeof(Attach)==3728&&offsetof(Attach,exactRenderer)==2152&&offsetof(Attach,rendererSha)==3688&&offsetof(Attach,identity)==8&&offsetof(Attach,gameWindow)==80&&offsetof(Attach,exactImage)==104);
extern "C" __declspec(dllexport) HRESULT WINAPI FenceAttach(const Attach* config){
 std::lock_guard<std::mutex> held(control);if(!config||config->size!=sizeof(Attach)||config->version!=2||policy
  ||config->exactImage[1023]!=0||config->exactRenderer[767]!=0)return E_INVALIDARG;
 identity=config->identity;InitializeReadback();gameWindow=config->gameWindow;DWORD gameSession{},producerSession{},windowPid{};
 if(identity.gamePid!=GetCurrentProcessId()||!identity.gamePid||identity.gamePid==identity.producerPid
  ||static_cast<std::uint32_t>(identity.gameId)!=0x02000000||identity.gameId>>32<0x80000000
  ||!(identity.session[0]|identity.session[1]|identity.session[2]|identity.session[3])
  ||!identity.gameBirth||!identity.producerBirth||!ImageCurrent(config->exactImage)
  ||!gameWindow||!GetWindowThreadProcessId(gameWindow,&windowPid)||windowPid!=identity.gamePid||GetAncestor(gameWindow,GA_ROOT)!=gameWindow
  ||!Birth(GetCurrentProcess(),identity.gameBirth)||!ProcessIdToSessionId(identity.gamePid,&gameSession)
  ||gameSession!=identity.windowsSession||GetProcessId(config->producerHandle)!=identity.producerPid
  ||!Birth(config->producerHandle,identity.producerBirth)||WaitForSingleObject(config->producerHandle,0)!=WAIT_TIMEOUT
  ||!ProcessIdToSessionId(identity.producerPid,&producerSession)||producerSession!=gameSession
  ||!SameUser(GetCurrentProcess(),config->producerHandle))return Refuse(fence::AttachStage::Identity,E_ACCESSDENIED);
 if(!DuplicateHandle(GetCurrentProcess(),config->producerHandle,GetCurrentProcess(),&producer,0,FALSE,DUPLICATE_SAME_ACCESS))return Refuse(fence::AttachStage::Producer,E_ACCESSDENIED);
 if(!DuplicateHandle(GetCurrentProcess(),config->readMappingHandle,GetCurrentProcess(),&mapping,0,FALSE,DUPLICATE_SAME_ACCESS)){CloseHandle(producer);producer=nullptr;return Refuse(fence::AttachStage::Mapping,E_ACCESSDENIED);}
 view=static_cast<const fence::Frame*>(MapViewOfFile(mapping,FILE_MAP_READ,0,0,sizeof(fence::Frame)));
 if(!view){CloseHandle(mapping);CloseHandle(producer);mapping=producer=nullptr;return Refuse(fence::AttachStage::View,E_ACCESSDENIED);}
 // Deliberately only already-loaded Windows XInput1_4. No DLL search-path
 // loading, global injection, late-module callbacks or proxy replacement.
 HMODULE module=GetModuleHandleW(L"xinput1_4.dll");
 wchar_t modulePath[32768]{},systemPath[32768]{};DWORD sys=GetSystemDirectoryW(systemPath,32768);
 if(!sys||sys>=32750){DropTransport();return Refuse(fence::AttachStage::XInputModule,E_ACCESSDENIED);}wcscat_s(systemPath,L"\\xinput1_4.dll");
 if(!module||!GetModuleFileNameW(module,modulePath,32768)||_wcsicmp(modulePath,systemPath)){DropTransport();return Refuse(fence::AttachStage::XInputModule,E_ACCESSDENIED);}
 slots[0].target=module?reinterpret_cast<void*>(GetProcAddress(module,"XInputGetState")):nullptr;
 slots[1].target=module?reinterpret_cast<void*>(GetProcAddress(module,MAKEINTRESOURCEA(100))):nullptr;
 slots[2].target=module?reinterpret_cast<void*>(GetProcAddress(module,"XInputGetKeystroke")):nullptr;
 if(!module||!RequiredExports()){DropTransport();return Refuse(fence::AttachStage::Exports,HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED));}
 auto rendererStage=RetainRenderer(config->exactRenderer,config->rendererSha,config->rendererSize,config->rendererTimestamp);
 if(rendererStage!=fence::AttachStage::None){DropTransport();return Refuse(rendererStage,E_ACCESSDENIED);}
 auto status=MH_Initialize();if(status!=MH_OK){DropTransport();return Refuse(fence::AttachStage::HookInitialize,E_FAIL);} // no shared MinHook ownership assumed
 void* hooks[]{reinterpret_cast<void*>(&StateHook<0>),reinterpret_cast<void*>(&StateHook<1>),reinterpret_cast<void*>(&KeyHook)};
 for(unsigned i=0;i<3;++i)if(slots[i].target){void** original=i==2?reinterpret_cast<void**>(&slots[i].key):reinterpret_cast<void**>(&slots[i].state);
  if(MH_CreateHook(slots[i].target,hooks[i],original)!=MH_OK){for(unsigned j=0;j<i;++j)if(slots[j].installed){MH_RemoveHook(slots[j].target);slots[j].installed=false;}MH_Uninitialize();DropTransport();return Refuse(fence::AttachStage::HookCreate,E_FAIL);}slots[i].installed=true;}
 policy=std::make_unique<fence::Policy>(identity,Read,Current,[]{return GetTickCount64();});
 if(!EnableInstalledHooks())return Refuse(rendererRevoked.load()?fence::AttachStage::RendererChanged:fence::AttachStage::HookEnable,E_FAIL);
 {std::lock_guard<std::mutex> guard(diagnosticLock);++FenceReadback.sequence;MemoryBarrier();FenceReadback.flags=3|(rendererMode.load()==fence::RendererMode::Present?4:8);FenceReadback.attachStage=static_cast<std::uint32_t>(fence::AttachStage::Attached);FenceReadback.attachError=S_OK;FenceReadback.rendererMode=static_cast<std::uint32_t>(rendererMode.load());FenceReadback.sampledAt=GetTickCount64();MemoryBarrier();++FenceReadback.sequence;}return S_OK;
}
extern "C" __declspec(dllexport) void WINAPI FenceRevoke(){enabled.store(false,std::memory_order_release);}
// Revoke is fail-open immediately. The DLL remains loaded until the host has
// joined all call sites; no FreeLibrary race or in-flight trampoline removal.
BOOL WINAPI DllMain(HINSTANCE,DWORD,LPVOID){return TRUE;}
