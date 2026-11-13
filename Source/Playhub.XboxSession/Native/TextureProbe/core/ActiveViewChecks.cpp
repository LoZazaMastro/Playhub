#include <windows.h>
#include <intrin.h>
#include <functional>
#include <iostream>
#include <stdexcept>
namespace fake {
DWORD error=7,seen{},thread=55;ULONGLONG clock=100;unsigned calls{},reads{};HWND fg=(HWND)0x200,actual{};
bool pinned=true,current=true;std::uint64_t generation=1,own=0x100,cookie=123,birth=134389429512941331ULL;std::function<void()> inside;
DWORD Error(){return error;}void Error(DWORD e){error=e;}DWORD Thread(){return thread;}ULONGLONG Tick(){return clock;}
const void* Caller(){return (void*)0xABC;}HWND WINAPI Foreground(){error=999;return fg;}
HWND WINAPI GetActive(){calls++;seen=error;if(inside)inside();error=0x1234;return actual;}
HWND WINAPI GetFocus(){error=0x1235;return (HWND)0x333;}
HWND WINAPI Setter(HWND h){error=0x1236;return (HWND)0x444;}
BOOL WINAPI SetForeground(HWND h){error=0x1237;return FALSE;}
}
#define GetLastError fake::Error
#define SetLastError fake::Error
#define GetCurrentThreadId fake::Thread
#define GetTickCount64 fake::Tick
#define _ReturnAddress fake::Caller
#include "ObservationTraceHooks.h"
using namespace view::observation;
bool ScopeRead(const void*,HWND,Scope&s)noexcept{
 fake::reads++;fake::error=888;s.callerPinned=fake::pinned;if(!fake::pinned)return false;
 s.generation=fake::generation;s.ownWindow=fake::own;s.ownCookie=fake::cookie;s.ownerBirth=fake::birth;s.ownerPid=10;s.ownThread=55;
 s.gamePid=20;s.gameBirth=134389429512941332ULL;s.gameWindow=0x200;s.framePid=20;s.frameBirth=s.gameBirth;s.frameThread=56;s.expiresAt=500;
 return fake::current&&fake::fg==(HWND)0x200;
}
unsigned checks{};void Need(bool v,const char*n){checks++;if(!v)throw std::runtime_error(n);}
void Reset(){fake::error=7;fake::seen=0;fake::thread=55;fake::clock=100;fake::calls=fake::reads=0;fake::fg=(HWND)0x200;fake::actual=nullptr;
 fake::pinned=fake::current=true;fake::generation=1;fake::own=0x100;fake::cookie=123;fake::birth=134389429512941331ULL;fake::inside={};detail::ready.store(true);}
void Denied(const char*n){auto value=detail::GetActive();Need(value==fake::actual,n);Need(fake::calls==1&&fake::seen==7&&fake::error==0x1234,"denied view preserves original once and LastError");}
int main(){try{
 detail::readScope=ScopeRead;detail::actualGetter=fake::Foreground;detail::activeGetterOriginal=fake::GetActive;detail::focusGetterOriginal=fake::GetFocus;detail::focusOriginal=fake::Setter;detail::activeOriginal=fake::Setter;detail::foregroundOriginal=fake::SetForeground;
 Reset();Need(detail::GetActive()==(HWND)0x100,"exact pinned own-thread NULL returns logical own");Need(fake::calls==1&&fake::seen==7&&fake::error==0x1234,"positive calls original once and preserves entry/output error");Need(fake::fg==(HWND)0x200,"real game foreground never changed");
 Reset();fake::actual=(HWND)0x999;Denied("nonNULL original unchanged");
 Reset();fake::pinned=false;Denied("foreign caller unchanged");
 Reset();fake::thread=56;Denied("other GUI thread unchanged");
 Reset();fake::fg=nullptr;Denied("native FG0 never admitted");
 Reset();fake::current=false;Denied("missing exact lease unchanged");
 Reset();detail::ready.store(false);Denied("partial hook installation cannot grant view");
 Reset();fake::inside=[] {fake::clock=500;};Denied("expiry during original refuses");
 Reset();fake::inside=[] {fake::fg=nullptr;};Denied("post-original foreground loss refuses");
 Reset();fake::inside=[] {fake::generation=2;};Denied("new generation during original refuses");
 Reset();fake::inside=[] {fake::own=0x101;};Denied("HWND replacement refuses");
 Reset();fake::inside=[] {fake::cookie++;};Denied("cookie replacement refuses");
 Reset();fake::inside=[] {fake::birth++;};Denied("adjacent birth64 replacement refuses");
 Reset();fake::inside=[] {fake::thread=56;};Denied("post-original thread check refuses");
 Reset();fake::inside=[] {fake::current=false;};Denied("reentrant revoke refuses");
 Reset();auto previous=detail::logicalActiveReturns.load();detail::GetActive();auto d=Read();Need(d.activeWindowViewCompiled&&d.logicalActiveReturns==previous+1,"functional counter/compile flag explicit");
 bool found=false;for(unsigned i=0;i<d.rows;i++)if(d.records[i].activeWindowView){found=true;Need(d.records[i].returned==0&&d.records[i].logicalReturned==0x100,"diagnostic separates actual NULL from logical own");}Need(found,"logical view record exists");
 Reset();Need(detail::GetFocused()==(HWND)0x333&&fake::error==0x1235,"GetFocus remains original");Need(detail::Focus((HWND)0x100)==(HWND)0x444,"SetFocus remains original");Need(detail::Active((HWND)0x100)==(HWND)0x444,"SetActiveWindow remains original");Need(detail::Foreground((HWND)0x100)==FALSE,"SetForegroundWindow remains original");Need(InFlight()==0,"detour accounting balanced");
 std::cout<<checks<<" actual NULL-only GetActiveWindow fake checks PASS; no OS/hook/Valve calls\n";return 0;
 }catch(const std::exception&e){std::cerr<<e.what()<<'\n';return 1;}}
