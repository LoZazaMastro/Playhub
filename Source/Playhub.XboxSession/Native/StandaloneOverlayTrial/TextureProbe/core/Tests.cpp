// SPDX-License-Identifier: GPL-3.0-only
#include "ForegroundViewPolicy.h"
#include "CallerImage.h"
#include "ScopedForegroundView.h"
#include "LastErrorReturn.h"
#include "MinHook.h"
#include <intrin.h>
#include <iostream>
#include <atomic>
#include <string>
#include <vector>
static unsigned checks=0;
static void Check(bool value,const char* name){++checks;if(!value){std::cerr<<"FAIL "<<name<<"\n";ExitProcess(1);}}
using Value=std::uintptr_t;
using Getter=Value(WINAPI*)();
using Invoke=Value(WINAPI*)(Getter);
static std::unique_ptr<view::ExecutableImage> allowed;
static Getter realFake{};
static bool activeFixture=false;
static std::atomic<unsigned> calls{},routes{};
__declspec(noinline) static Value WINAPI FakeForeground(){volatile Value original=0x1111;return original;}
__declspec(noinline) static Value WINAPI Detour(){auto caller=_ReturnAddress();Value actual=realFake();++calls;if(activeFixture&&allowed&&allowed->Contains(caller)){++routes;return 0x2222;}return actual;}
__declspec(noinline) static Value WINAPI CallerAddress(){return reinterpret_cast<Value>(_ReturnAddress());}

static void CapturePure(){
 using namespace view;CaptureKey key{1,2,0x100,0x200,50};
 struct Ops {CaptureKey key;CaptureId actual{},lastRequested{};std::vector<std::string> trace;bool grant=true,releaseResult=true;
  CaptureKey Grant(CaptureId w){trace.push_back("Grant");return grant&&w==key.window?key:CaptureKey{};}
  CaptureId SnapshotCapture(){trace.push_back("ActualGetCapture");return actual;}
  CaptureId Set(CaptureId w){trace.push_back("OriginalSetCapture");lastRequested=w;auto prior=actual;actual=w;return prior;}
  CaptureId Get(){trace.push_back("OriginalGetCapture");return actual;}
  bool Release(){trace.push_back("OriginalReleaseCapture");actual=0;return releaseResult;}
 };
 auto count=[](const Ops& o,const char* n){return std::count(o.trace.begin(),o.trace.end(),n);};
 CaptureLedger l;Ops o{key};Check(CaptureSet(l,true,key.window,o)==0,"first virtual Set returns prior NULL, not failure");Check(l.held&&l.pending&&SameCapture(l.key,key),"virtual capture binds full key");Check(o.actual==0&&count(o,"OriginalSetCapture")==0,"eligible Valve never invokes OS setter");Check(o.trace==std::vector<std::string>{"ActualGetCapture","Grant"},"actual getter precedes final reattestation");
 Check(CaptureGet(l,true,o)==key.window&&count(o,"OriginalGetCapture")==0,"eligible Valve getter sees logical capture");Check(CaptureSet(l,true,key.window,o)==key.window,"repeat Set returns previous logical window");Check(CaptureRelease(l,true,o),"paired virtual release succeeds");Check(!l.pending&&!l.held&&count(o,"OriginalReleaseCapture")==0,"paired virtual release has zero OS release");
 o.actual=0x777;CaptureSet(l,true,key.window,o);Check(CaptureRelease(l,true,o)&&o.actual==0x777,"virtual release preserves unrelated real capture");
 CaptureSet(l,true,key.window,o);o.trace.clear();auto prior=CaptureSet(l,false,key.window,o);Check(prior==0x777&&o.actual==key.window,"foreign setter returns and writes actual original");Check(o.trace==std::vector<std::string>{"OriginalSetCapture"},"foreign setter calls original once without extra getter");Check(!l.pending,"foreign real transition retires local ledger");o.trace.clear();CaptureRelease(l,true,o);Check(o.trace==std::vector<std::string>{"OriginalReleaseCapture"},"release after foreign transition remains original");
 CaptureSet(l,true,key.window,o);o.trace.clear();CaptureSet(l,true,0x999,o);Check(o.actual==0x999&&o.lastRequested==0x999,"pinned unrelated target uses real setter");Check(count(o,"OriginalSetCapture")==1&&!l.pending,"unrelated Set invokes exactly once and clears tombstone");o.trace.clear();CaptureRelease(l,true,o);Check(o.trace==std::vector<std::string>{"OriginalReleaseCapture"},"unrelated real release cannot be swallowed");
 CaptureSet(l,true,key.window,o);o.grant=false;o.actual=0x888;o.trace.clear();Check(CaptureGet(l,true,o)==0x888,"expiry/revocation getter reports actual state");Check(!l.held&&l.pending,"revoked ledger retains cleanup-only tombstone");o.trace.clear();Check(CaptureRelease(l,true,o)&&o.actual==0x888,"late paired release after revocation does not release actual foreign capture");Check(o.trace.empty(),"cleanup-only release requires no authority/native setter/query");
 o.grant=true;CaptureSet(l,true,key.window,o);auto renewed=o.key;Check(CaptureGet(l,true,o)==key.window&&SameCapture(renewed,l.key),"same generation nonce renewal preserves logical capture");o.key.generation=2;Check(CaptureGet(l,true,o)==o.actual&&!l.held,"new generation cannot read old logical ownership");Check(CaptureRelease(l,true,o)&&o.actual==0x888,"old generation cleanup remains virtual only");Check(CaptureSet(l,true,o.key.window,o)==0x888&&l.key.generation==2,"new generation starts from actual prior capture");
 o.key.cookie++;Check(CaptureGet(l,true,o)==o.actual&&!l.held,"replacement cookie revokes virtual getter");CaptureRelease(l,true,o);o.key.window=0x101;CaptureSet(l,true,o.key.window,o);o.key.window=0x102;Check(CaptureGet(l,true,o)==o.actual&&!l.held,"replacement source window revokes old view");CaptureRelease(l,true,o);
 for(unsigned missing=0;missing<5;missing++){CaptureKey a=key;if(missing==0)a.generation=0;if(missing==1)a.nonce=0;if(missing==2)a.window=0;if(missing==3)a.cookie=0;if(missing==4)a.thread=0;Check(!SameCapture(a,a),"every capture key identity field required");}
 o.key=key;o.grant=false;o.trace.clear();CaptureSet(l,true,key.window,o);Check(count(o,"OriginalSetCapture")==1&&!l.pending,"unready/foreign FG refuses virtual admission without suppressing original");
 o.grant=true;CaptureSet(l,true,key.window,o);o.trace.clear();Check(CaptureGet(l,false,o)==o.actual&&o.trace==std::vector<std::string>{"OriginalGetCapture"},"our/foreign observer always sees actual getter");Check(l.pending,"foreign getter cannot consume logical owner");o.trace.clear();o.releaseResult=false;Check(!CaptureRelease(l,false,o)&&!l.pending&&o.trace==std::vector<std::string>{"OriginalReleaseCapture"},"foreign release preserves false result and retires local bookkeeping");
}

static void Pure(){
 CapturePure();
 view::ArmDiagnostic d;d.serial=7;d.generation=3;d.previousGeneration=2;d.observedAt=100;d.expiresAt=5100;d.preparation=true;
 view::NoteArmRefusal(&d,view::ArmStage::NativeCleanGui);view::NoteArmRefusal(&d,view::ArmStage::OwnBandMismatch);
 Check(d.stage==view::ArmStage::NativeCleanGui,"first existing guard refusal survives later cleanup");
 auto old=d;d={};Check(old.serial==7&&old.generation==3&&old.previousGeneration==2&&old.preparation&&old.expiresAt==5100,"diagnostic is an immutable value copy across next attempts");
 d.stage=view::ArmStage::Accepted;view::NoteArmRefusal(&d,view::ArmStage::Expired);Check(d.stage==view::ArmStage::Accepted,"accepted snapshot cannot be overwritten by unrelated diagnostics");
 view::NoteArmRefusal(nullptr,view::ArmStage::Expired);Check(true,"foreign/default path has no diagnostics owner");
 for(unsigned i=0;i<=static_cast<unsigned>(view::ArmStage::Expired);++i)Check(std::string(view::ArmStageName(static_cast<view::ArmStage>(i)))!="Unknown","every typed stage has a bounded fixed name");
 Check(std::string(view::ArmStageName(static_cast<view::ArmStage>(999)))=="Unknown","unknown enum logs no arbitrary data");
 view::Facts good{true,true,true,true,true,true,true,true};Check(view::Route(good),"complete scoped caller routes");
 for(unsigned missing=0;missing<8;missing++){bool f[]{true,true,true,true,true,true,true,true};f[missing]=false;Check(!view::Route({f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7]}),"every trust/scope/caller/lifetime fact required");}
 Check(view::FreshRequest(0,1,100,2100),"maximum bounded fresh request");Check(!view::FreshRequest(0,1,100,2101),"no lifetime broadening");Check(!view::FreshRequest(1,1,100,200),"same request cannot replay");Check(!view::FreshRequest(2,1,100,200),"older request cannot replay");Check(!view::FreshRequest(0,1,100,100),"expired request");Check(!view::FreshRequest(0,1,0,100),"unset clock");Check(view::FreshRequest(2,3,100,101),"new request can reacquire");
 Check(view::PreparationLifetimeMs==5000,"preparation budget fixed, not configuration supplied");Check(view::FreshPreparationRequest(0,1,100,5100),"explicit preparation accepts5s maximum");Check(!view::FreshPreparationRequest(0,1,100,5101),"preparation5s cap strict");Check(!view::FreshRequest(0,1,100,5100),"legacy Arm cannot take preparation budget");Check(!view::FreshPreparationRequest(1,1,100,200),"preparation is not a same-generation renewal");Check(!view::FreshPreparationRequest(2,1,100,200),"old preparation cannot replay");Check(!view::FreshPreparationRequest(0,1,100,100),"expired preparation denied");Check(!view::FreshPreparationRequest(0,1,0,100),"preparation requires real clock");Check(!view::FreshPreparationRequest(0,0,100,200),"preparation requires fresh Guide generation");Check(view::RenewableRequest(1,1,100,5100,2100,true)==false,"renew cannot shrink initial budget");Check(!view::RenewableRequest(1,1,100,200,5100,true),"renew cannot receive5s cap");Check(view::RenewableRequest(1,1,4500,5100,6500,true),"same-generation renewal remains2s maximum");Check(!view::RenewableRequest(1,1,4500,5100,6501,true),"renew2s cap remains strict");
 Check(view::Contains(100,50,100),"range starts inclusive");Check(view::Contains(100,50,149),"range last byte");Check(!view::Contains(100,50,150),"range end excluded");Check(!view::Contains(100,50,99),"before image excluded");Check(!view::Contains(0,50,1),"null base excluded");Check(!view::Contains(100,0,100),"zero range excluded");Check(view::Contains(UINTPTR_MAX-2,3,UINTPTR_MAX),"range math does not overflow");
 Check(view::CleanupAllowed(true,0,true),"inert fixture removal completed");Check(!view::CleanupAllowed(false,0,true),"disable failure not cleanup");Check(!view::CleanupAllowed(true,1,true),"inflight not cleanup");Check(!view::CleanupAllowed(true,0,false),"remove failure not cleanup");
 Check(!view::PinnedRenderer::Open(nullptr,nullptr),"null renderer refuses before file/API work");Check(!view::PinnedRenderer::Open(L"C:\\owned-fixture.dll",nullptr),"wrong basename refuses before file/API work");
 // A deferred proof must be re-evaluated against current generation/time.
 auto delayed=good;delayed.sameGeneration=false;Check(!view::Route(delayed),"revocation during verification does not route");delayed=good;delayed.notExpired=false;Check(!view::Route(delayed),"deferred expiry does not route");delayed=good;delayed.exactSource=false;Check(!view::Route(delayed),"real foreign foreground never routes");delayed=good;delayed.exactOwn=false;Check(!view::Route(delayed),"replaced own surface never routes");
 view::TargetIdentity identity{7,100,200,300};Check(view::SameTarget(identity,identity),"same retained game and exact Surface object");Check(!view::SameTarget(identity,{8,100,200,300}),"new game PID rejected");Check(!view::SameTarget(identity,{7,101,200,300}),"recycled PID rejected");Check(!view::SameTarget(identity,{7,100,201,300}),"replacement HWND rejected");Check(!view::SameTarget(identity,{7,100,200,301}),"replacement Surface object rejected");Check(!view::SameTarget({},{}),"empty identity never matches");
}
int wmain(int argc,wchar_t** argv){
 Pure();
 if(argc==1||(argc==2&&wcscmp(argv[1],L"--pure")==0)){std::cout<<"{\"checks\":"<<checks<<",\"passed\":true,\"nativeFixtureInvoked\":false,\"uiCreated\":false,\"inputSent\":0}\n";return 0;}
 if(argc!=2||wcscmp(argv[1],L"--native-fixture")){std::cerr<<"Use --pure or --native-fixture (owned inert DLL/fake-getter test only).\n";return 2;}
 struct DropError{unsigned* dropped;~DropError(){++*dropped;SetLastError(0xbad);}};unsigned drops=0;
 auto ownerA=std::make_shared<DropError>(DropError{&drops}),ownerB=std::make_shared<DropError>(DropError{&drops});auto localA=ownerA,localB=ownerB;ownerA.reset();ownerB.reset();drops=0;
 auto answer=view::FinishReturn(Value(0x1111),DWORD(0x1234),localA,localB);DWORD finalError=GetLastError();Check(drops==2&&!localA&&!localB,"deferred last-reference teardown occurs before API return");Check(answer==0x1111&&finalError==0x1234,"original getter LastError survives scope teardown");
 wchar_t path[32768]{};DWORD n=GetModuleFileNameW(nullptr,path,32768);Check(n&&n<32768,"own executable path");std::wstring folder(path,n);folder.resize(folder.find_last_of(L'\\')+1);
 HMODULE a=LoadLibraryExW((folder+L"RendererFixture.dll").c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32),b=LoadLibraryExW((folder+L"ForeignFixture.dll").c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
 Check(a&&b,"two owned inert DLLs loaded");auto invokeA=reinterpret_cast<Invoke>(GetProcAddress(a,"Invoke")),invokeB=reinterpret_cast<Invoke>(GetProcAddress(b,"Invoke"));Check(invokeA&&invokeB,"owned fixture exports");
 allowed=view::ExecutableImage::Read(a);auto foreign=view::ExecutableImage::Read(b);Check(allowed&&foreign,"production executable PE-range reader");
 auto addressA=reinterpret_cast<void*>(invokeA(CallerAddress)),addressB=reinterpret_cast<void*>(invokeB(CallerAddress));Check(allowed->Contains(addressA),"real return address from allowed fixture");Check(!allowed->Contains(addressB),"real foreign DLL return address rejected");Check(foreign->Contains(addressB),"foreign fixture address recognized only in its own image");Check(!allowed->Contains(reinterpret_cast<void*>(CallerAddress)),"own observer code excluded");
 auto data=reinterpret_cast<const void*(WINAPI*)()>(GetProcAddress(a,"DataAddress"));Check(data&&!allowed->Contains(data()),"same image data is not executable caller");Check(!allowed->Contains(nullptr),"null caller rejected");
 Check(MH_Initialize()==MH_OK,"own inert MinHook initialize");Check(MH_CreateHook(reinterpret_cast<void*>(FakeForeground),reinterpret_cast<void*>(Detour),reinterpret_cast<void**>(&realFake))==MH_OK,"hook only owned fake getter");Check(MH_EnableHook(reinterpret_cast<void*>(FakeForeground))==MH_OK,"enable only owned fake getter");
 activeFixture=false;Check(invokeA(FakeForeground)==0x1111,"no active scope preserves even allowed caller");activeFixture=true;Check(invokeA(FakeForeground)==0x2222,"actual MinHook return-address routing from allowed DLL");Check(invokeB(FakeForeground)==0x1111,"foreign DLL continues actual getter");Check(FakeForeground()==0x1111,"own observers continue actual getter");
 activeFixture=false;Check(invokeA(FakeForeground)==0x1111,"revoked fixture immediately continues actual getter");Check(calls==5&&routes==1,"only one eligible native fixture call routed");
 Check(MH_DisableHook(reinterpret_cast<void*>(FakeForeground))==MH_OK,"disable owned fake target");Check(invokeA(FakeForeground)==0x1111,"restored fake getter independent of route state");Check(MH_RemoveHook(reinterpret_cast<void*>(FakeForeground))==MH_OK,"single-thread fixture can remove trampoline");Check(MH_Uninitialize()==MH_OK,"owned fixture engine uninitialized");allowed.reset();foreign.reset();FreeLibrary(a);FreeLibrary(b);
 std::cout<<"{\"checks\":"<<checks<<",\"passed\":true,\"nativeReturnAddressFixture\":true,\"ownFakeGetterHookOnly\":true,\"steamApiInvoked\":false,\"valveModuleExplicitlyLoaded\":false,\"user32Hooked\":false,\"uiCreated\":false,\"inputSent\":0}\n";return 0;
}
