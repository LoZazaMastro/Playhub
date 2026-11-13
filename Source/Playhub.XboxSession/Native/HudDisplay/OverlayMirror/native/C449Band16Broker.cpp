// SPDX-License-Identifier: GPL-3.0-only
// In-process client for unchanged nonce-bound C449 Explorer callback.
#include "Win32MirrorBackend.h"
#include "BandAuthorization.h"
#include "BandBrokerPolicy.h"
#include "ExpectedBandCallback.h"
#include "band/Target.h"
#include <bcrypt.h>
#include <sddl.h>
#include <psapi.h>
#include <wintrust.h>
#include <softpub.h>
#include <wincrypt.h>
#include <array>
#include <algorithm>
#include <string>
#include <vector>
namespace playhub::mirror {namespace {
namespace p=playhub_band16;
constexpr const wchar_t* CallbackSha=ExpectedExactBandSha;
struct Handle {HANDLE value{};Handle()=default;explicit Handle(HANDLE v):value(v){};Handle(const Handle&)=delete;Handle& operator=(const Handle&)=delete;~Handle(){if(value&&value!=INVALID_HANDLE_VALUE)CloseHandle(value);}operator HANDLE()const{return value;}explicit operator bool()const{return value&&value!=INVALID_HANDLE_VALUE;}};
static std::wstring ModulePath(){wchar_t p[p::PathCapacity]{};DWORD n=GetModuleFileNameW(nullptr,p,p::PathCapacity);return n&&n<p::PathCapacity?std::wstring(p):L"";}
static bool LocalPlainFile(const std::wstring& path){DWORD a=GetFileAttributesW(path.c_str());if(a==INVALID_FILE_ATTRIBUTES||(a&(FILE_ATTRIBUTE_REPARSE_POINT|FILE_ATTRIBUTE_DIRECTORY)))return false;wchar_t volume[MAX_PATH]{};return GetVolumePathNameW(path.c_str(),volume,MAX_PATH)&&GetDriveTypeW(volume)==DRIVE_FIXED;}
static std::wstring Hash(HANDLE file){
 BCRYPT_ALG_HANDLE alg{};BCRYPT_HASH_HANDLE hash{};std::wstring result;DWORD n=0,size=0;LARGE_INTEGER zero{};
 if(BCryptOpenAlgorithmProvider(&alg,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)return result;
 if(BCryptGetProperty(alg,BCRYPT_OBJECT_LENGTH,reinterpret_cast<PUCHAR>(&size),sizeof(size),&n,0)>=0){
  std::vector<BYTE> object(size);BYTE digest[32]{};
  if(BCryptCreateHash(alg,&hash,object.data(),size,nullptr,0,0)>=0){
   bool ok=SetFilePointerEx(file,zero,nullptr,FILE_BEGIN)!=FALSE;std::array<BYTE,65536> data{};DWORD read=0;
   while(ok){if(!ReadFile(file,data.data(),DWORD(data.size()),&read,nullptr)){ok=false;break;}if(!read)break;if(BCryptHashData(hash,data.data(),read,0)<0){ok=false;break;}}
   if(ok&&BCryptFinishHash(hash,digest,32,0)>=0){constexpr wchar_t hex[]=L"0123456789ABCDEF";for(BYTE b:digest){result+=hex[b>>4];result+=hex[b&15];}}
   BCryptDestroyHash(hash);
  }
 }
 BCryptCloseAlgorithmProvider(alg,0);return result;
}
static bool MicrosoftSignature(const std::wstring& path,HANDLE file){
 WINTRUST_FILE_INFO fi{sizeof(fi)};fi.pcwszFilePath=path.c_str();fi.hFile=file;
 WINTRUST_DATA data{sizeof(data)};data.dwUIChoice=WTD_UI_NONE;data.fdwRevocationChecks=WTD_REVOKE_NONE;data.dwUnionChoice=WTD_CHOICE_FILE;data.pFile=&fi;data.dwStateAction=WTD_STATEACTION_VERIFY;
 data.dwProvFlags=WTD_CACHE_ONLY_URL_RETRIEVAL|WTD_REVOCATION_CHECK_NONE;GUID action=WINTRUST_ACTION_GENERIC_VERIFY_V2;
 LONG status=WinVerifyTrust(nullptr,&action,&data);bool ok=false;
 if(status==ERROR_SUCCESS){auto provider=WTHelperProvDataFromStateData(data.hWVTStateData);auto signer=provider?WTHelperGetProvSignerFromChain(provider,0,FALSE,0):nullptr;
  if(signer&&signer->csCertChain&&signer->pasCertChain[0].pCert){wchar_t org[128]{};DWORD n=CertGetNameStringW(signer->pasCertChain[0].pCert,CERT_NAME_ATTR_TYPE,0,const_cast<char*>(szOID_ORGANIZATION_NAME),org,128);ok=n>1&&n<=128&&wcscmp(org,L"Microsoft Corporation")==0;}}
 data.dwStateAction=WTD_STATEACTION_CLOSE;WinVerifyTrust(nullptr,&action,&data);return ok;
}
static bool ModulePresent(HANDLE shell,const std::wstring& expected,bool& present){
 HMODULE modules[4096]{};DWORD size=0;present=false;if(!EnumProcessModulesEx(shell,modules,sizeof(modules),&size,LIST_MODULES_64BIT)||size>sizeof(modules))return false;
 for(DWORD i=0;i<size/sizeof(HMODULE);i++){wchar_t path[p::PathCapacity]{};DWORD n=GetModuleFileNameExW(shell,modules[i],path,p::PathCapacity);if(!n||n>=p::PathCapacity)return false;if(_wcsicmp(path,expected.c_str())==0){present=true;break;}}
 return true;
}
bool PlainDirectory(const std::wstring& path){DWORD a=GetFileAttributesW(path.c_str());return a!=INVALID_FILE_ATTRIBUTES&&(a&FILE_ATTRIBUTE_DIRECTORY)&&!(a&FILE_ATTRIBUTE_REPARSE_POINT);}
struct Operation {
 DWORD ownerThread=GetCurrentThreadId(),shellPid{},shellThread{};Id shellBirth{};HWND shellWindow{};BandTarget target{};BandGameProof lease{};
 Handle shell,frame,game,pinnedFile,pinnedDirectory,operationDone;HANDLE mapping{};p::Shared* shared{};HMODULE module{};HHOOK hook{};
 std::wstring copyPath,runFolder;bool installed{},unhooked{},barrier{},complete{},failed{};Id absentSince{};
 bool SameTarget(const BandTarget& t)const{return target.window==t.window&&target.processId==t.processId&&target.threadId==t.threadId&&target.processBirth==t.processBirth&&target.cookie==t.cookie&&Same(target.rectangle,t.rectangle);}
 bool Thread()const{return ownerThread==GetCurrentThreadId();}
 bool Retire(){
  if(complete)return true;if(!Thread())return false;
  if(shared){InterlockedExchange(&shared->revoked,1);InterlockedExchange(&shared->armed,0);InterlockedCompareExchange(&shared->operationPermit,LONG(p::Permit::Revoked),LONG(p::Permit::Ready));MemoryBarrier();}
  const bool shellExited=shell&&WaitForSingleObject(shell,0)==WAIT_OBJECT_0;
  if(hook){if(!UnhookWindowsHookEx(hook)&&!shellExited)return false;hook=nullptr;unhooked=true;}else if(!installed)unhooked=true;
  if(MayReleaseCallback({shellExited})){if(module){FreeLibrary(module);module=nullptr;}complete=true;return true;}
  if(shared&&!p::MayDestroy(static_cast<p::Permit>(InterlockedCompareExchange(&shared->operationPermit,0,0)))&&!shellExited)return false;
  if(!unhooked)return false;
  if(installed&&!shellExited&&!barrier){if(!p::Alive(shell,shellPid,shellBirth)||!p::ExactShell(shellWindow,shellPid,shellThread))return false;DWORD_PTR ignored{};barrier=SendMessageTimeoutW(shellWindow,WM_NULL,0,0,SMTO_ABORTIFHUNG|SMTO_ERRORONEXIT,200,&ignored)!=0;if(!barrier)return false;}else if(!installed||shellExited)barrier=true;
  LONG inflight=shared?InterlockedCompareExchange(&shared->inflight,0,0):0;bool absent=!installed||shellExited;
  if(installed&&!shellExited){bool present=true;if(!p::Alive(shell,shellPid,shellBirth)||!ModulePresent(shell,copyPath,present))return false;absent=!present;}
  if(!absent||inflight){absentSince=0;return false;}if(!absentSince)absentSince=GetTickCount64();
  RetirementFacts proof;proof.unhooked=unhooked;proof.operationFinishedOrRevoked=!shared||p::MayDestroy(static_cast<p::Permit>(InterlockedCompareExchange(&shared->operationPermit,0,0)));proof.barrierReturned=barrier;proof.absenceRead=absent;proof.moduleAbsent=absent;proof.inFlight=inflight;proof.absentForMs=installed?GetTickCount64()-absentSince:100;
  if(!MayReleaseCallback(proof))return false;
  if(module){FreeLibrary(module);module=nullptr;}complete=true;return true;
 }
 ~Operation(){if(shared)UnmapViewOfFile(shared);if(mapping)CloseHandle(mapping);if(complete){if(pinnedFile.value){CloseHandle(pinnedFile.value);pinnedFile.value=nullptr;}if(!copyPath.empty())DeleteFileW(copyPath.c_str());if(pinnedDirectory.value){CloseHandle(pinnedDirectory.value);pinnedDirectory.value=nullptr;}if(!runFolder.empty())RemoveDirectoryW(runFolder.c_str());}}
};
// Incomplete native operations are intentionally retained. The owner GUI thread
// and component must keep pumping retirement before host shutdown/unload.
std::vector<Operation*>& Pending(){static auto* value=new std::vector<Operation*>;return *value;}
class ExactBandBroker {
 std::wstring callback,root;std::unique_ptr<Operation> operation;DeferredBandAdmission admission;BandTarget pendingTarget{};BandAuthorization pendingAuthority{SourceLease{}};FirstBrokerFailure diagnostic;QuietBandAdmission quiet;NoInputFacts lastInput;
 static NoInputFacts InputAt(std::int32_t x,std::int32_t y){return ReadNoInputFacts(x,y,[](int vk){return GetAsyncKeyState(vk);},[](std::int32_t& a,std::int32_t& b){POINT pos{};bool ok=GetPhysicalCursorPos(&pos)!=FALSE;if(ok){a=pos.x;b=pos.y;}return ok;});}
 bool PendingMatches(const BandTarget& t)const{return pendingTarget.window==t.window&&pendingTarget.processId==t.processId&&pendingTarget.threadId==t.threadId&&pendingTarget.processBirth==t.processBirth&&pendingTarget.cookie==t.cookie&&Same(pendingTarget.rectangle,t.rectangle);}
 bool Setup(Operation& o,const BandTarget& target,const BandAuthorization& authorization){
   const auto l=authorization.Game();
  if(target.processId!=GetCurrentProcessId()||target.processBirth!=p::Birth(GetCurrentProcess())||target.threadId!=GetCurrentThreadId()||!target.window||!p::NativeX64(GetCurrentProcess())||!CanonicalSession(l.sessionId)||!authorization.Current(GetTickCount64())||!PlainDirectory(root)||!LocalPlainFile(callback))return diagnostic.Reject(BrokerStage::InitialTarget);
  if(l.expectedFrameAumid[512])return diagnostic.Reject(BrokerStage::FrameKind);bool uwp=l.expectedFrameAumid[0]!=0;if(uwp&&!wcschr(l.expectedFrameAumid.data(),L'!'))return diagnostic.Reject(BrokerStage::FrameKind);if(!uwp&&(l.framePid!=l.gamePid||l.frameBirth!=l.gameBirth))return diagnostic.Reject(BrokerStage::FrameKind);
  Handle original(CreateFileW(callback.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr));if(!original)return diagnostic.Reject(BrokerStage::CallbackOpen,true,GetLastError());if(Hash(original)!=CallbackSha)return diagnostic.Reject(BrokerStage::CallbackHash);
  o.target=target;o.lease=l;o.shellWindow=GetShellWindow();o.shellThread=GetWindowThreadProcessId(o.shellWindow,&o.shellPid);if(!o.shellWindow||o.shellThread==GetCurrentThreadId()||o.shellPid==GetCurrentProcessId()||!p::ExactShell(o.shellWindow,o.shellPid,o.shellThread))return diagnostic.Reject(BrokerStage::ShellWindow);
  o.shell.value=OpenProcess(PROCESS_QUERY_INFORMATION|PROCESS_QUERY_LIMITED_INFORMATION|PROCESS_VM_READ|SYNCHRONIZE,FALSE,o.shellPid);o.shellBirth=p::Birth(o.shell);
  wchar_t windows[p::PathCapacity]{};UINT n=GetWindowsDirectoryW(windows,p::PathCapacity);if(!n||n>=p::PathCapacity)return diagnostic.Reject(BrokerStage::ShellImage);auto shellImage=std::wstring(windows)+L"\\explorer.exe";Handle shellFile(CreateFileW(shellImage.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr));
  if(!o.shell||!shellFile||!p::Alive(o.shell,o.shellPid,o.shellBirth)||!p::NativeX64(o.shell)||!p::Image(o.shell,shellImage.c_str())||!LocalPlainFile(shellImage)||!MicrosoftSignature(shellImage,shellFile))return diagnostic.Reject(BrokerStage::ShellImage);
  p::Security own{},shellSecurity{};if(!p::ReadSecurity(GetCurrentProcess(),own)||!p::ReadSecurity(o.shell,shellSecurity)||!p::SameSecurity(own,shellSecurity)||own.session!=l.session)return diagnostic.Reject(BrokerStage::ShellSecurity);
  wchar_t station[64]{},desktop[64]{};if(!p::ObjectName(GetProcessWindowStation(),station,64)||wcscmp(station,L"WinSta0")||!p::ObjectName(GetThreadDesktop(GetCurrentThreadId()),desktop,64)||!p::DesktopCurrent(o.shellThread,station,desktop))return diagnostic.Reject(BrokerStage::Desktop);
  o.frame.value=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,l.framePid);o.game.value=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,l.gamePid);if(!p::Alive(o.frame,l.framePid,l.frameBirth)||!p::Alive(o.game,l.gamePid,l.gameBirth))return diagnostic.Reject(BrokerStage::RetainedSource);
  BYTE random[16]{};if(BCryptGenRandom(nullptr,random,sizeof(random),BCRYPT_USE_SYSTEM_PREFERRED_RNG)<0)return diagnostic.Reject(BrokerStage::RandomNonce);Id low{},high{};constexpr wchar_t hex[]=L"0123456789abcdef";std::wstring nonce;for(unsigned i=0;i<16;i++){nonce+=hex[random[i]>>4];nonce+=hex[random[i]&15];if(i<8)low=(low<<8)|random[i];else high=(high<<8)|random[i];}if(!low||!high)return diagnostic.Reject(BrokerStage::RandomNonce);
  LPWSTR sidText{};if(!ConvertSidToStringSidW(own.sid,&sidText))return diagnostic.Reject(BrokerStage::SecurityDescriptor);std::wstring sddl=L"D:P(A;;GA;;;SY)(A;;GA;;;";sddl+=sidText;sddl+=L")";LocalFree(sidText);PSECURITY_DESCRIPTOR descriptor{};if(!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(),SDDL_REVISION_1,&descriptor,nullptr))return diagnostic.Reject(BrokerStage::SecurityDescriptor);
  struct Local {PSECURITY_DESCRIPTOR value;~Local(){LocalFree(value);}} security{descriptor};SECURITY_ATTRIBUTES attributes{sizeof(attributes),descriptor,FALSE};o.runFolder=root+L"\\callback-"+nonce;
  if(!CreateDirectoryW(o.runFolder.c_str(),&attributes)||!PlainDirectory(o.runFolder))return diagnostic.Reject(BrokerStage::PrivateDirectory);
  o.pinnedDirectory.value=CreateFileW(o.runFolder.c_str(),FILE_READ_ATTRIBUTES,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_EXISTING,FILE_FLAG_BACKUP_SEMANTICS,nullptr);o.copyPath=o.runFolder+L"\\"+p::FilePrefix+nonce+L".dll";
  if(!o.pinnedDirectory||!CopyFileW(callback.c_str(),o.copyPath.c_str(),TRUE))return diagnostic.Reject(BrokerStage::PrivateCopy);o.pinnedFile.value=CreateFileW(o.copyPath.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);if(!o.pinnedFile||Hash(o.pinnedFile)!=CallbackSha)return diagnostic.Reject(BrokerStage::PrivateCopy);
  auto mapName=std::wstring(p::MapPrefix)+nonce;SetLastError(0);o.mapping=CreateFileMappingW(INVALID_HANDLE_VALUE,&attributes,PAGE_READWRITE,0,sizeof(p::Shared),mapName.c_str());DWORD mapError=GetLastError();auto eventName=mapName+L"-done";SetLastError(0);o.operationDone.value=CreateEventW(&attributes,TRUE,FALSE,eventName.c_str());DWORD eventError=GetLastError();if(!o.mapping||!o.operationDone||mapError==ERROR_ALREADY_EXISTS||eventError==ERROR_ALREADY_EXISTS)return diagnostic.Reject(BrokerStage::SharedObjects);
  o.shared=static_cast<p::Shared*>(MapViewOfFile(o.mapping,FILE_MAP_READ|FILE_MAP_WRITE,0,0,sizeof(p::Shared)));if(!o.shared)return diagnostic.Reject(BrokerStage::MapView);auto& s=*o.shared;ZeroMemory(&s,sizeof(s));s.magic=p::Magic;s.version=p::Version;s.size=sizeof(s);s.nonceLow=low;s.nonceHigh=high;s.controllerPid=target.processId;s.controllerBirth=target.processBirth;s.shellPid=o.shellPid;s.shellBirth=o.shellBirth;s.shellThread=o.shellThread;s.shellWindow=reinterpret_cast<Id>(o.shellWindow);s.sourceForeground=l.gameWindow;s.sourcePid=l.framePid;s.sourceBirth=l.frameBirth;s.sourceThread=l.frameThread;s.gamePid=l.gamePid;s.gameBirth=l.gameBirth;
  s.sourceKind=uwp?1:2;if(uwp){GUITHREADINFO gui{sizeof(gui)};DWORD corePid{};if(!GetGUIThreadInfo(l.frameThread,&gui)||!gui.hwndFocus)return diagnostic.Reject(BrokerStage::CoreFocus);s.sourceCore=reinterpret_cast<Id>(gui.hwndFocus);s.gameThread=GetWindowThreadProcessId(gui.hwndFocus,&corePid);if(corePid!=l.gamePid)return diagnostic.Reject(BrokerStage::CoreFocus);}else{s.sourceCore=l.gameWindow;s.gameThread=l.frameThread;DWORD length=p::PathCapacity;if(!QueryFullProcessImageNameW(o.game,0,s.expectedGameImage,&length)||length>=p::PathCapacity||!GetClassNameW(reinterpret_cast<HWND>(l.gameWindow),s.expectedSourceClass,128))return diagnostic.Reject(BrokerStage::SourceImage);}
  s.sourceRect={l.gameRect.left,l.gameRect.top,l.gameRect.right,l.gameRect.bottom};s.targetWindow=reinterpret_cast<Id>(target.window);s.targetThread=target.threadId;s.targetRect={target.rectangle.left,target.rectangle.top,target.rectangle.right,target.rectangle.bottom};s.session=own.session;s.integrity=own.integrity;s.sidLength=own.sidLength;CopyMemory(s.sid,own.sid,own.sidLength);
  auto ownImage=ModulePath();if(ownImage.empty()||!LocalPlainFile(ownImage))return diagnostic.Reject(BrokerStage::ControllerImage);wcscpy_s(s.controllerImage,ownImage.c_str());wcscpy_s(s.shellImage,shellImage.c_str());wcscpy_s(s.modulePath,o.copyPath.c_str());wcscpy_s(s.station,station);wcscpy_s(s.desktop,desktop);wcscpy_s(s.operationEvent,eventName.c_str());wcscpy_s(s.aumid,l.expectedFrameAumid.data());
  if(!GetClassNameW(target.window,s.targetClass,128))return diagnostic.Reject(BrokerStage::TargetClass,true,GetLastError());
  if(!GetPhysicalCursorPos(&s.initialCursor))return diagnostic.Reject(BrokerStage::CursorRead,true,GetLastError());
  if(!SetPropW(target.window,L"PlayhubBandNonceLow",reinterpret_cast<HANDLE>(low)))return diagnostic.Reject(BrokerStage::TargetNonce,true,GetLastError());
  if(!SetPropW(target.window,L"PlayhubBandNonceHigh",reinterpret_cast<HANDLE>(high)))return diagnostic.Reject(BrokerStage::TargetNonce,true,GetLastError());
  if(!p::ControllerOwnsTarget(s)||!p::ExactTarget(s,false)||IsWindowVisible(target.window)||GetWindowLongPtrW(target.window,GWLP_USERDATA)!=static_cast<LONG_PTR>(target.cookie))return diagnostic.Reject(BrokerStage::TargetIdentity);
  if(!p::CurrentSource(s))return diagnostic.Reject(BrokerStage::InitialSource);
  lastInput=InputAt(s.initialCursor.x,s.initialCursor.y);if(!lastInput.passed){diagnostic.InputBlocked(lastInput);return diagnostic.Reject(BrokerStage::InitialNoInput);}
  bool present=true;if(!ModulePresent(o.shell,o.copyPath,present)||present)return diagnostic.Reject(BrokerStage::ModuleAbsence);o.module=LoadLibraryExW(o.copyPath.c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);if(!o.module)return diagnostic.Reject(BrokerStage::ModuleLoad,true,GetLastError());auto callbackProc=reinterpret_cast<HOOKPROC>(GetProcAddress(o.module,"ShellCallback"));if(!callbackProc)return diagnostic.Reject(BrokerStage::CallbackExport,true,GetLastError());
  if(!p::Alive(o.shell,o.shellPid,o.shellBirth)||!p::ExactShell(o.shellWindow,o.shellPid,o.shellThread)||!p::CurrentSource(s)||!p::ExactTarget(s,false)||IsWindowVisible(target.window))return diagnostic.Reject(BrokerStage::PreHookSource);
  lastInput=InputAt(s.initialCursor.x,s.initialCursor.y);if(!lastInput.passed){diagnostic.InputBlocked(lastInput);return diagnostic.Reject(BrokerStage::PreHookNoInput);}
  if(!authorization.CurtainCurrent(GetTickCount64()))return diagnostic.Reject(BrokerStage::PreHookSource);
  if(GetTickCount64()>=l.expiresAt)return diagnostic.Reject(BrokerStage::Deadline);
  s.issued=GetTickCount64();s.deadline=std::min(s.issued+p::MaxLifetimeMs,l.expiresAt);if(s.deadline<=s.issued)return diagnostic.Reject(BrokerStage::Deadline);InterlockedExchange(&s.armed,1);MemoryBarrier();o.hook=SetWindowsHookExW(WH_CALLWNDPROC,callbackProc,o.module,o.shellThread);o.installed=o.hook!=nullptr;if(!o.installed)return diagnostic.Reject(BrokerStage::HookInstall,true,GetLastError());
  Id now=GetTickCount64();if(!p::Live(s.issued,s.deadline,now)||!p::CurrentSource(s)||!p::ExactTarget(s,false))return diagnostic.Reject(BrokerStage::PreSendSource);
  if(!p::NoInput(s))return diagnostic.Reject(BrokerStage::PreSendNoInput);if(!authorization.CurtainCurrent(GetTickCount64()))return diagnostic.Reject(BrokerStage::PreSendSource);now=GetTickCount64();if(now>=s.deadline)return diagnostic.Reject(BrokerStage::Deadline);
  DWORD_PTR ignored{};SendMessageTimeoutW(o.shellWindow,WM_NULL,WPARAM(low),LPARAM(high),SMTO_ABORTIFHUNG|SMTO_ERRORONEXIT,UINT(s.deadline-now),&ignored);
  // WM_NULL return is not acknowledgement. Commit may still be in flight.
  return true;
 }
 bool Succeeded()const{if(!operation||!operation->shared)return false;auto& s=*operation->shared;return InterlockedCompareExchange(&s.received,0,0)==1&&s.callbackPid==operation->shellPid&&s.callbackThread==operation->shellThread&&s.callbackNonceLow==s.nonceLow&&s.callbackNonceHigh==s.nonceHigh&&p::Live(s.issued,s.deadline,s.callbackAt)&&s.bandInvoked&&s.bandReturned&&s.bandAfterRead&&s.bandAfter==16&&p::Live(s.issued,s.deadline,s.bandAt)&&InterlockedCompareExchange(&s.operationPermit,0,0)==LONG(p::Permit::Finished);}
 enum class Attempt {Continue,Deferred,Refused};
 Attempt StartAttempt(){
  diagnostic.RestartAttempt();operation=std::make_unique<Operation>();
  if(Setup(*operation,pendingTarget,pendingAuthority))return Attempt::Continue;
  const auto stage=diagnostic.Read().stage;
  if(MayDeferNoInput(stage,operation->installed,operation->hook!=nullptr)
      &&quiet.Defer(GetTickCount64(),pendingAuthority.Game().expiresAt)){
   diagnostic.DeferInput();
   // No hook was ever installed. Complete local module/map/file cleanup BEFORE
   // admitting another Setup against this same retained generation and target.
   if(operation->Retire()){operation.reset();quiet.CleanupComplete();}
   return Attempt::Deferred;
  }
  operation->failed=true;operation->Retire();CapturePollRefusal();return Attempt::Refused;
 }
public:
 ExactBandBroker(std::wstring dll,std::wstring runtime):callback(std::move(dll)),root(std::move(runtime)){}
 ~ExactBandBroker(){if(operation&&!operation->Retire())Pending().push_back(operation.release());}
 BandState Begin(const BandTarget& target,const BandAuthorization& authorization){if(operation||!admission.Reserve())return BandState::Refused;diagnostic.Begin();quiet.Reset();lastInput={};pendingTarget=target;pendingAuthority=authorization;return BandState::Pending;}
 void CapturePollRefusal(){
  BrokerDiagnostic fields;fields.hookInstalled=operation&&operation->installed;
  if(operation&&operation->shared){auto& x=*operation->shared;fields.permit=InterlockedCompareExchange(&x.operationPermit,0,0);
   // Plain result scalars are meaningful only after foreign work is quiescent.
   if(InterlockedCompareExchange(&x.inflight,0,0)==0&&p::MayDestroy(static_cast<p::Permit>(fields.permit))){MemoryBarrier();fields.sharedRead=true;
    fields.received=InterlockedCompareExchange(&x.received,0,0);fields.rejected=InterlockedCompareExchange(&x.rejected,0,0);fields.refusalMask=InterlockedCompareExchange(&x.refusalMask,0,0);
    fields.bandInvoked=x.bandInvoked;fields.bandReturned=x.bandReturned;fields.bandError=x.bandError;
    fields.callbackReadReason=x.callbackError;fields.callbackReadBits=std::uint32_t(x.inputChanged);
    fields.bandBeforeRead=x.bandBeforeRead;fields.bandBefore=x.bandBefore;fields.bandBeforeError=x.bandBeforeError;
    fields.callbackIssued=x.issued;fields.callbackDeadline=x.deadline;fields.callbackAt=x.callbackAt;}}
  diagnostic.PollRefused(fields);
 }
 BandState Poll(const BandTarget& target){
  if(quiet.Pending()){
   const bool current=PendingMatches(target)&&pendingAuthority.Current(GetTickCount64());
   if(!current){quiet.Reset();diagnostic.RestartAttempt();diagnostic.Reject(BrokerStage::Deadline);CapturePollRefusal();return BandState::Refused;}
   const auto step=quiet.Poll(current,[]{return InputAt(0,0);},[]{return GetTickCount64();});
   if(step==QuietStep::Refused){diagnostic.RestartAttempt();diagnostic.Reject(BrokerStage::QuietInputBudget);CapturePollRefusal();return BandState::Refused;}
   if(operation){if(!operation->Retire())return BandState::Pending;operation.reset();quiet.CleanupComplete();return BandState::Pending;}
   if(step==QuietStep::Pending)return BandState::Pending;
   const auto next=StartAttempt();if(next!=Attempt::Continue)return next==Attempt::Deferred?BandState::Pending:BandState::Refused;
  }else if(admission.TakeStart()){
   const auto first=StartAttempt();if(first!=Attempt::Continue)return first==Attempt::Deferred?BandState::Pending:BandState::Refused;
  }
  if(!operation||!operation->Thread()||!operation->SameTarget(target)||operation->failed){CapturePollRefusal();return BandState::Refused;}
  auto* s=operation->shared;if(!s){CapturePollRefusal();return BandState::Refused;}
  auto permit=static_cast<p::Permit>(InterlockedCompareExchange(&s->operationPermit,0,0));
  if(permit==p::Permit::Committed)return BandState::Pending;
  if(permit==p::Permit::Ready&&GetTickCount64()<=s->deadline)return BandState::Pending;
  bool success=Succeeded();if(!operation->Retire())return BandState::Pending;
  if(!success||!pendingAuthority.CurtainCurrent(GetTickCount64())){operation->failed=true;CapturePollRefusal();return BandState::Refused;}return BandState::Ready;
 }
 BrokerDiagnostic Diagnostics()const{return diagnostic.Read();}
 bool Retire(const BandTarget& target){if(!operation){admission.Reset();quiet.Reset();pendingAuthority=BandAuthorization(SourceLease{});return true;}if(!operation->SameTarget(target))return false;if(!operation->Retire())return false;operation.reset();admission.Reset();quiet.Reset();pendingAuthority=BandAuthorization(SourceLease{});return true;}
};
class C449Broker final:public IBand16Broker {
 ExactBandBroker inner;
public:
 C449Broker(std::wstring dll,std::wstring root):inner(std::move(dll),std::move(root)){}
 BandState Begin(const BandTarget& t,const SourceLease& l)override{return inner.Begin(t,BandAuthorization(l));}
 BandState Poll(const BandTarget& t)override{return inner.Poll(t);}
 bool Retire(const BandTarget& t)override{return inner.Retire(t);}
 BrokerDiagnostic Diagnostics()const override{return inner.Diagnostics();}
};
class HudBandBroker final:public hud::IBand16HudBroker {
 ExactBandBroker inner;
public:
 HudBandBroker(std::wstring dll,std::wstring root):inner(std::move(dll),std::move(root)){}
 BandState Begin(const BandTarget& t,const hud::BandLease& l)override{return inner.Begin(t,BandAuthorization(l));}
 BandState Poll(const BandTarget& t)override{return inner.Poll(t);}
 bool Retire(const BandTarget& t)override{return inner.Retire(t);}
 BrokerDiagnostic Diagnostics()const override{return inner.Diagnostics();}
};
class CurtainBandBroker final:public curtain::IBand16CurtainBroker {
 ExactBandBroker inner;
public:
 CurtainBandBroker(std::wstring dll,std::wstring root):inner(std::move(dll),std::move(root)){}
 BandState Begin(const BandTarget& t,const curtain::BandLease& l)override{return inner.Begin(t,BandAuthorization(l));}
 BandState Poll(const BandTarget& t)override{return inner.Poll(t);}
 bool Retire(const BandTarget& t)override{return inner.Retire(t);}
 BrokerDiagnostic Diagnostics()const override{return inner.Diagnostics();}
};
}
std::shared_ptr<IBand16Broker> CreateExactBand16Broker(const wchar_t* callbackDll,const wchar_t* privateRuntimeRoot){if(!callbackDll||!privateRuntimeRoot)return nullptr;std::wstring file(callbackDll),root(privateRuntimeRoot);auto absolute=[](const std::wstring& p){return p.size()>3&&p.size()<p::PathCapacity&&p[1]==L':'&&p[2]==L'\\'&&p.find(L"..") == std::wstring::npos;};if(!absolute(file)||!absolute(root))return nullptr;return std::make_shared<C449Broker>(std::move(file),std::move(root));}
}

namespace playhub::hud {
std::shared_ptr<IBand16HudBroker> CreateHudDisplayBand16Broker(const wchar_t* callbackDll,const wchar_t* privateRuntimeRoot){
 if(!callbackDll||!privateRuntimeRoot)return nullptr;std::wstring file(callbackDll),root(privateRuntimeRoot);
 auto absolute=[](const std::wstring& p){return p.size()>3&&p.size()<playhub_band16::PathCapacity&&p[1]==L':'&&p[2]==L'\\'&&p.find(L"..") == std::wstring::npos;};
 if(!absolute(file)||!absolute(root))return nullptr;
 return std::make_shared<mirror::HudBandBroker>(std::move(file),std::move(root));
}
}

namespace playhub::curtain {
std::shared_ptr<IBand16CurtainBroker> CreateCurtainBand16Broker(const wchar_t* callbackDll,const wchar_t* privateRuntimeRoot){
 if(!callbackDll||!privateRuntimeRoot)return nullptr;std::wstring file(callbackDll),root(privateRuntimeRoot);
 auto absolute=[](const std::wstring& p){return p.size()>3&&p.size()<playhub_band16::PathCapacity&&p[1]==L':'&&p[2]==L'\\'&&p.find(L"..") == std::wstring::npos;};
 if(!absolute(file)||!absolute(root))return nullptr;
 return std::make_shared<mirror::CurtainBandBroker>(std::move(file),std::move(root));
}
}
