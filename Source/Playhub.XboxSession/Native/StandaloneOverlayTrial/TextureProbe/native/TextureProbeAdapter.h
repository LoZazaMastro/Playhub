// SPDX-License-Identifier: GPL-3.0-only
// TEMP-only alternate RunSteamCompanion body. Included after Surface/ValveOverlay.
#include "ScopedForegroundView.h"
#include "Identity.h"
#include "TextureProbePolicy.h"
#include <cstdio>
#include <algorithm>
static constexpr wchar_t ProbeModeProperty[]=L"Playhub.TextureProbe.Mode";
static constexpr wchar_t ProbeGenerationProperty[]=L"Playhub.TextureProbe.Generation";
static constexpr wchar_t ProbeExpiryProperty[]=L"Playhub.TextureProbe.ExpiresAt";
struct ProbeLog {
 HANDLE file=INVALID_HANDLE_VALUE;
 unsigned mode{},admittedProbes{},rows{};uint64_t nativeRequests{};
 ProbeLog(){
  wchar_t temp[32768]{};DWORD n=GetTempPathW(32768,temp);if(!n||n>=32768)return;std::wstring folder=std::wstring(temp)+L"Playhub-Renderer-Texture-Probe-20261111";
  if(!CreateDirectoryW(folder.c_str(),nullptr)&&GetLastError()!=ERROR_ALREADY_EXISTS)return;DWORD attrs=GetFileAttributesW(folder.c_str());if(attrs==INVALID_FILE_ATTRIBUTES||!(attrs&FILE_ATTRIBUTE_DIRECTORY)||(attrs&FILE_ATTRIBUTE_REPARSE_POINT))return;
  wchar_t name[256]{};swprintf_s(name,L"\\probe-%u-%llu.jsonl",GetCurrentProcessId(),static_cast<unsigned long long>(proof::Birth(GetCurrentProcess())));auto path=folder+name;
  file=CreateFileW(path.c_str(),GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL|FILE_FLAG_OPEN_REPARSE_POINT,nullptr);
 }
 ~ProbeLog(){if(file!=INVALID_HANDLE_VALUE)CloseHandle(file);}
 void Write(const char* stage,Surface& surface,view::ScopedForegroundView& foreground,HANDLE game,DWORD pid,uint64_t birth,HWND source,uint64_t generation,ULONGLONG expiry,HRESULT arm,unsigned presents,HRESULT present,unsigned posted,unsigned dequeued,bool rawOpen){
  if(file==INVALID_HANDLE_VALUE||rows++>=256)return;using GetBand=BOOL(WINAPI*)(HWND,DWORD*);auto getBand=reinterpret_cast<GetBand>(GetProcAddress(GetModuleHandleW(L"user32.dll"),"GetWindowBand"));DWORD band{};bool bandRead=getBand&&getBand(surface.window,&band);auto count=foreground.ReadCounts();HWND actual=foreground.ActualForeground();BYTE keys[256]{};bool keyboardRead=GetKeyboardState(keys)!=FALSE;unsigned local=(keyboardRead&&keys[VK_SHIFT]&0x80?1:0)|(keyboardRead&&keys[VK_TAB]&0x80?2:0);unsigned async=(GetAsyncKeyState(VK_SHIFT)&0x8000?1:0)|(GetAsyncKeyState(VK_TAB)&0x8000?2:0);
  char row[4096]{};int length=snprintf(row,sizeof(row),"{\"stage\":\"%s\",\"mode\":%u,\"admittedProbes\":%u,\"nativeRequests\":%llu,\"legacyOverlayEventsEmitted\":false,\"keyboardAtWritten\":false,\"bootMs\":%llu,\"companionPid\":%u,\"companionBirth\":%llu,\"gamePid\":%u,\"gameBirth\":%llu,\"gameAlive\":%s,\"sourceHwnd\":%llu,\"surfaceHwnd\":%llu,\"surfaceCookie\":%llu,\"generation\":%llu,\"expiresAt\":%llu,\"surfaceVisibleStyle\":%s,\"bandRead\":%s,\"surfaceBand\":%u,\"sourceStillForeground\":%s,\"actualOwnForeground\":%s,\"armHResult\":%d,\"presentCalls\":%u,\"lastPresentHResult\":%d,\"textureWidth\":%d,\"textureHeight\":%d,\"shimCalls\":%llu,\"shimRouted\":%llu,\"shimForeignCaller\":%llu,\"shimInactiveScope\":%llu,\"ownMessagesPosted\":%u,\"ownMessagesDequeued\":%u,\"keyboardStateRead\":%s,\"threadShiftTabBits\":%u,\"asyncShiftTabBits\":%u,\"rawOverlayOpen\":%s,\"snapshotUsed\":false,\"focusCallsByAdapter\":0,\"globalInputSent\":0,\"keyboardStateWrites\":0,\"patchDisabled\":%s,\"processLifetimeStorageRetained\":%s}\n",stage,mode,admittedProbes,static_cast<unsigned long long>(nativeRequests),static_cast<unsigned long long>(GetTickCount64()),GetCurrentProcessId(),static_cast<unsigned long long>(proof::Birth(GetCurrentProcess())),pid,static_cast<unsigned long long>(birth),SameGame(game,pid,birth)?"true":"false",static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(source)),static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(surface.window)),static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(&surface)),static_cast<unsigned long long>(generation),static_cast<unsigned long long>(expiry),IsWindowVisible(surface.window)?"true":"false",bandRead?"true":"false",band,actual==source?"true":"false",actual==surface.window?"true":"false",static_cast<int>(arm),presents,static_cast<int>(present),surface.ProbeTextureSize().cx,surface.ProbeTextureSize().cy,static_cast<unsigned long long>(count.calls),static_cast<unsigned long long>(count.routed),static_cast<unsigned long long>(count.foreignCaller),static_cast<unsigned long long>(count.inactiveScope),posted,dequeued,keyboardRead?"true":"false",local,async,rawOpen?"true":"false",count.patchDisabled?"true":"false",count.processLifetimeStorageRetained?"true":"false");
  if(length>0&&length<sizeof(row)){DWORD wrote{};WriteFile(file,row,DWORD(length),&wrote,nullptr);}
 }
};
struct OwnKeyMessages {
 HWND window{};uintptr_t cookie{};bool shift{},tab{};unsigned posted{},dequeued{};
 bool Own()const{DWORD pid{};return window&&GetWindowThreadProcessId(window,&pid)==GetCurrentThreadId()&&pid==GetCurrentProcessId()&&static_cast<uintptr_t>(GetWindowLongPtrW(window,GWLP_USERDATA))==cookie;}
 bool Post(UINT message,UINT key){if(!Own())return false;LPARAM bits=1|(LPARAM(MapVirtualKeyW(key,MAPVK_VK_TO_VSC))<<16);if(message==WM_KEYUP)bits|=LPARAM(1u<<30)|LPARAM(1u<<31);if(!PostMessageW(window,message,key,bits))return false;++posted;return true;}
 template<class Guard>bool Down(Guard&& exact){return texture::PostDown([&](){return Own()&&exact();},[&](){return shift=Post(WM_KEYDOWN,VK_SHIFT);},[&](){return tab=Post(WM_KEYDOWN,VK_TAB);});}
 void Up(){if(tab&&Post(WM_KEYUP,VK_TAB))tab=false;if(shift&&Post(WM_KEYUP,VK_SHIFT))shift=false;}
 void Saw(const MSG& message){if(message.hwnd==window&&(message.message==WM_KEYDOWN||message.message==WM_KEYUP)&&(message.wParam==VK_SHIFT||message.wParam==VK_TAB))++dequeued;}
 ~OwnKeyMessages(){Up();}
};
static int32_t RunTextureProbe(Surface& surface,const ValveOverlay& valve,HANDLE game,HANDLE owner,DWORD pid,uint64_t birth,const wchar_t* rendererPath,const wchar_t* frameAumid,Report report,TakeGuideRequest takeGuideRequest){
 ProbeLog log;if(log.file==INVALID_HANDLE_VALUE)return HRESULT_FROM_WIN32(ERROR_CANNOT_MAKE);
 view::ScopedForegroundView foreground;auto pin=view::OpenConfiguredRenderer(rendererPath);HRESULT installed=pin?foreground.Install(pin):E_ACCESSDENIED;HRESULT arm=installed,lastPresent=S_OK;HWND source{};uint64_t sequence=0;ULONGLONG expiry=0,releaseAt=0;unsigned presents=0;bool active=false,exposed=false,reportedGame=false,modeSet=false,generationSet=false,expirySet=false;int lastScope=0;OwnKeyMessages keys{surface.window,reinterpret_cast<uintptr_t>(&surface)};
 log.Write(FAILED(installed)?"install-refused":"installed-passive",surface,foreground,game,pid,birth,source,sequence,expiry,arm,presents,lastPresent,keys.posted,keys.dequeued,valve.Open());if(FAILED(installed))return installed;
 report(1,0);HANDLE processes[]{game,owner};HRESULT result=S_OK;texture::AdmissionBarrier admission;
 auto own=[&](){return keys.Own()&&IsWindow(surface.window);};
 auto sourceExact=[&](){return SameGame(game,pid,birth)&&source&&foreground.ActualForeground()==source&&GameWindow(source,pid)&&foreground.ActualForeground()==source;};
 auto clearProperties=[&](){if(own()){if(modeSet&&reinterpret_cast<uintptr_t>(GetPropW(surface.window,ProbeModeProperty))==log.mode)RemovePropW(surface.window,ProbeModeProperty);if(generationSet&&reinterpret_cast<uintptr_t>(GetPropW(surface.window,ProbeGenerationProperty))==sequence)RemovePropW(surface.window,ProbeGenerationProperty);if(expirySet&&reinterpret_cast<uintptr_t>(GetPropW(surface.window,ProbeExpiryProperty))==expiry)RemovePropW(surface.window,ProbeExpiryProperty);}modeSet=generationSet=expirySet=false;};
 auto stop=[&](const char* reason){keys.Up();MSG queued{};for(unsigned i=0;i<64&&PeekMessageW(&queued,nullptr,0,0,PM_REMOVE);i++){keys.Saw(queued);TranslateMessage(&queued);DispatchMessageW(&queued);}foreground.Disarm();clearProperties();surface.Hide();active=false;expiry=0;admission.Retire(GetTickCount64());for(unsigned i=0;i<4;i++){bool pending=takeGuideRequest()!=0;if(pending){++log.nativeRequests;report(13,0);}admission.Consume(pending,GetTickCount64());if(!pending)break;}log.Write(reason,surface,foreground,game,pid,birth,source,sequence,expiry,arm,presents,lastPresent,keys.posted,keys.dequeued,valve.Open());};
 while(SameGame(game,pid,birth)&&IsWindow(surface.window)){
  DWORD waited=MsgWaitForMultipleObjectsEx(_countof(processes),processes,active?8:33,QS_ALLINPUT,MWMO_INPUTAVAILABLE);if(waited==WAIT_OBJECT_0||waited==WAIT_OBJECT_0+1)break;if(waited==WAIT_FAILED){result=HRESULT_FROM_WIN32(GetLastError());break;}
  MSG message{};texture::PumpBounded([&](){return PeekMessageW(&message,nullptr,0,0,PM_REMOVE)!=FALSE;},[&](){keys.Saw(message);TranslateMessage(&message);DispatchMessageW(&message);});
  HWND actual=foreground.ActualForeground();bool exactGame=GameWindow(actual,pid)&&SameGame(game,pid,birth);bool pendingRequest=takeGuideRequest()!=0;
  if(pendingRequest){++log.nativeRequests;report(13,0);}bool waiting=admission.Waiting();bool requested=admission.Consume(pendingRequest,GetTickCount64());
  if(waiting&&pendingRequest)log.Write("request-refused-during-retirement",surface,foreground,game,pid,birth,source,sequence,expiry,E_PENDING,presents,lastPresent,keys.posted,keys.dequeued,valve.Open());
  if(waiting&&!admission.Waiting())log.Write("admission-barrier-zero-observed",surface,foreground,game,pid,birth,source,sequence,expiry,S_OK,presents,lastPresent,keys.posted,keys.dequeued,valve.Open());
  if(requested){
   if(active){log.Write("authenticated-request-refused-while-active",surface,foreground,game,pid,birth,source,sequence,expiry,E_PENDING,presents,lastPresent,keys.posted,keys.dequeued,valve.Open());}
   else
   if(!texture::MayAdmitProbe(log.admittedProbes,false,keys.shift||keys.tab,GetPropW(surface.window,L"Playhub.ForegroundView.OwnNonce")!=nullptr)){log.Write("request-limit-or-retirement-refused",surface,foreground,game,pid,birth,source,sequence,expiry,E_ACCESSDENIED,presents,lastPresent,keys.posted,keys.dequeued,valve.Open());}
   else {
   source=actual;
   if(texture::MayBegin(true,SameGame(game,pid,birth),exactGame,own())){
    ++log.admittedProbes;log.mode=log.admittedProbes;sequence=log.nativeRequests;
    HRESULT aligned=surface.Align(source,false);if(SUCCEEDED(aligned))aligned=surface.Draw();if(SUCCEEDED(aligned)&&sourceExact())aligned=surface.Align(source,true);
    if(SUCCEEDED(aligned)&&sourceExact()){
     expiry=GetTickCount64()+1800;view::Request request;request.authenticatedGuide=true;request.sequence=sequence;request.expiresAt=expiry;request.retainedGame=game;request.gamePid=pid;request.gameBirth=birth;request.exactSource=source;request.ownSurface=surface.window;request.expectedFrameAumid=frameAumid;request.ownLifetimeCookie=reinterpret_cast<uintptr_t>(&surface);request.surfacePurpose=view::SurfacePurpose::OccludedTextureProbeBand1;
     arm=foreground.Arm(request);active=SUCCEEDED(arm);presents=0;keys.posted=keys.dequeued=0;
     if(active){modeSet=SetPropW(surface.window,ProbeModeProperty,reinterpret_cast<HANDLE>(static_cast<uintptr_t>(log.mode)))!=FALSE;generationSet=SetPropW(surface.window,ProbeGenerationProperty,reinterpret_cast<HANDLE>(sequence))!=FALSE;expirySet=SetPropW(surface.window,ProbeExpiryProperty,reinterpret_cast<HANDLE>(expiry))!=FALSE;if(!modeSet||!generationSet||!expirySet){stop("metadata-refused");continue;}releaseAt=GetTickCount64()+120;log.Write("request-armed-texture-source",surface,foreground,game,pid,birth,source,sequence,expiry,arm,presents,lastPresent,keys.posted,keys.dequeued,valve.Open());}
     else stop("arm-refused");
    }else{arm=FAILED(aligned)?aligned:E_ACCESSDENIED;stop("align-or-source-refused");}
   }else log.Write("authenticated-request-source-refused",surface,foreground,game,pid,birth,source,sequence,expiry,E_ACCESSDENIED,presents,lastPresent,keys.posted,keys.dequeued,valve.Open());
   }
  }
  auto reason=texture::ShouldStop(active,SameGame(game,pid,birth),sourceExact(),GetTickCount64(),expiry);
  if(reason!=texture::Stop::None)stop(reason==texture::Stop::Expired?"request-expired":reason==texture::Stop::SourceLost?"actual-source-lost":"game-ended");
  if(active){
   if(log.mode==2&&presents==4&&!valve.Open()&&texture::MayPost(true,active,sourceExact(),own(),GetTickCount64()<expiry)){
    if(!keys.Down([&](){return active&&sourceExact()&&GetTickCount64()<expiry;})){stop("own-message-refused");continue;}releaseAt=GetTickCount64()+120;log.Write("own-keydown-posted",surface,foreground,game,pid,birth,source,sequence,expiry,arm,presents,lastPresent,keys.posted,keys.dequeued,valve.Open());
   }
   if((keys.shift||keys.tab)&&GetTickCount64()>=releaseAt){keys.Up();log.Write("own-keyup-posted",surface,foreground,game,pid,birth,source,sequence,expiry,arm,presents,lastPresent,keys.posted,keys.dequeued,valve.Open());}
   lastPresent=surface.Draw();++presents;if(FAILED(lastPresent)){result=lastPresent;stop("present-failed");break;}
   if(presents==1||presents==12)log.Write("live-present-sample",surface,foreground,game,pid,birth,source,sequence,expiry,arm,presents,lastPresent,keys.posted,keys.dequeued,valve.Open());
  }else if(valve.needsFrame()){lastPresent=surface.Draw();if(FAILED(lastPresent)){result=lastPresent;break;}}
  bool rawOpen=valve.Open();if(rawOpen!=exposed){exposed=rawOpen;log.Write("probe-raw-open-state-changed",surface,foreground,game,pid,birth,source,sequence,expiry,arm,presents,lastPresent,keys.posted,keys.dequeued,rawOpen);}
  actual=foreground.ActualForeground();bool gameForeground=GameWindow(actual,pid)&&SameGame(game,pid,birth);bool ownForeground=actual==surface.window&&source&&GameWindow(source,pid);int scope=texture::ActualScope(gameForeground,ownForeground,source&&CanAttemptCompanion(source));
  if(scope!=lastScope){report(11,scope);lastScope=scope;}if(gameForeground!=reportedGame){report(9,gameForeground?1:0);reportedGame=gameForeground;}
  if(!active)source=gameForeground?actual:source;
 }
 if(active)stop("session-ended");else{keys.Up();foreground.Disarm();clearProperties();surface.Hide();}
 HRESULT closed=foreground.Close();log.Write(SUCCEEDED(closed)?"patch-disabled-storage-retained":"patch-disable-incomplete-retained",surface,foreground,game,pid,birth,source,sequence,expiry,closed,presents,lastPresent,keys.posted,keys.dequeued,valve.Open());
 if(lastScope)report(11,0);if(reportedGame)report(9,0);
 return FAILED(result)?result:FAILED(closed)?closed:(keys.shift||keys.tab)?HRESULT_FROM_WIN32(ERROR_IO_INCOMPLETE):0;
}
