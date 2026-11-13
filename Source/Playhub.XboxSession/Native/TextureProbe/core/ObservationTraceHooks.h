// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "ObservationTracePolicy.h"
#include <windows.h>
#include <intrin.h>
#include <MinHook.h>
namespace view::observation {
using ScopeReader=bool(*)(const void*,HWND,Scope&)noexcept;
using ActualGetter=HWND(WINAPI*)();using WindowSetter=HWND(WINAPI*)(HWND);using ForegroundSetter=BOOL(WINAPI*)(HWND);
namespace detail {
inline ScopeReader readScope{};inline ActualGetter actualGetter{},activeGetterOriginal{},focusGetterOriginal{};
inline WindowSetter focusOriginal{},activeOriginal{};inline ForegroundSetter foregroundOriginal{};
inline std::array<void*,5> targets{};inline std::array<bool,5> created{};
inline Ring<32,24> ring;inline std::atomic<std::uint64_t> actualGetActiveNull{},logicalActiveReturns{};inline std::atomic<unsigned> inFlight{},configuredMask{},enabledMask{};
inline std::array<std::atomic<std::uint64_t>,5> seen{},pinned{},admitted{},delegated{};
inline std::atomic<bool> attempted{},ready{};inline std::atomic<std::int32_t> installResult{};
inline thread_local bool observing=false;
struct Ops {DWORD Error()noexcept{return GetLastError();}void Error(DWORD value)noexcept{SetLastError(value);}};
struct Flight {Flight(){inFlight.fetch_add(1,std::memory_order_acq_rel);}~Flight(){inFlight.fetch_sub(1,std::memory_order_acq_rel);}};
struct Recursion {bool previous;Recursion():previous(observing){observing=true;}~Recursion(){observing=previous;}};
template<class Original> auto Invoke(Kind kind,Original original,HWND target,const void*caller){
 Flight flight;Recursion recursion;Ops ops;const auto index=std::uint32_t(kind)-1;
 return ObservedCall(ops,[&]{auto answer=original();delegated[index].fetch_add(1);
  if constexpr(std::is_pointer_v<decltype(answer)>)if(kind==Kind::GetActiveWindow&&answer==nullptr)actualGetActiveNull.fetch_add(1);
  return answer;},[&](Record&r){
  seen[index].fetch_add(1);
  if(recursion.previous||!readScope||!actualGetter)return false;
  const bool current=readScope(caller,target,r.before);if(r.before.callerPinned)pinned[index].fetch_add(1);
  if(!current)return false;admitted[index].fetch_add(1);
  r.kind=kind;r.caller=reinterpret_cast<std::uintptr_t>(caller);r.requested=reinterpret_cast<std::uintptr_t>(target);
  r.thread=GetCurrentThreadId();r.begin=GetTickCount64();r.foregroundBefore=reinterpret_cast<std::uintptr_t>(actualGetter());return true;
 },[&](Record&r,auto answer,DWORD error){
  if constexpr(std::is_pointer_v<decltype(answer)>)r.returned=reinterpret_cast<std::uintptr_t>(answer);else r.returned=static_cast<std::uint64_t>(answer);
  r.logicalReturned=r.returned;r.lastErrorAfterOriginal=error;r.foregroundAfter=reinterpret_cast<std::uintptr_t>(actualGetter());r.end=GetTickCount64();
  r.currentAfter=readScope(caller,target,r.after)&&Same(r.before,r.after);
 },[&](Record&r,auto answer){
#if defined(PLAYHUB_VALVE_ACTIVE_WINDOW_VIEW)
  if constexpr(std::is_pointer_v<decltype(answer)>){
   if(ActiveWindowEligible(r,ready.load(std::memory_order_acquire),GetCurrentThreadId(),GetTickCount64())){
    r.activeWindowView=true;r.logicalReturned=r.before.ownWindow;logicalActiveReturns.fetch_add(1);
    return reinterpret_cast<decltype(answer)>(r.before.ownWindow);
   }
  }
#endif
  return answer;
 },[&](const Record&r){ring.Push(r);});
}
__declspec(noinline) inline HWND WINAPI Focus(HWND h){return Invoke(Kind::SetFocus,[&]{return focusOriginal(h);},h,_ReturnAddress());}
__declspec(noinline) inline HWND WINAPI Active(HWND h){return Invoke(Kind::SetActiveWindow,[&]{return activeOriginal(h);},h,_ReturnAddress());}
__declspec(noinline) inline BOOL WINAPI Foreground(HWND h){return Invoke(Kind::SetForegroundWindow,[&]{return foregroundOriginal(h);},h,_ReturnAddress());}
__declspec(noinline) inline HWND WINAPI GetActive(){return Invoke(Kind::GetActiveWindow,[&]{return activeGetterOriginal();},nullptr,_ReturnAddress());}
__declspec(noinline) inline HWND WINAPI GetFocused(){return Invoke(Kind::GetFocus,[&]{return focusGetterOriginal();},nullptr,_ReturnAddress());}
inline bool Disabled(MH_STATUS s)noexcept{return s==MH_OK||s==MH_ERROR_DISABLED;}
}
// Parent owns MinHook initialization and retains this module before enable.
// Originals always run. Only GetActiveWindow may use an explicit NULL-only
// logical view; no hook grants native foreground or input authority.
inline bool DisableHooks()noexcept{
 const DWORD error=GetLastError();bool ok=true;detail::ready.store(false,std::memory_order_release);
 for(std::size_t i=0;i<5;i++)if(detail::created[i]){
  MH_QueueDisableHook(detail::targets[i]);const bool disabled=detail::Disabled(MH_DisableHook(detail::targets[i]));
  if(disabled)detail::enabledMask.fetch_and(~(1u<<i));ok=disabled&&ok;
 }
 SetLastError(error);return ok;
}
inline HRESULT InstallHooks(ScopeReader callback,ActualGetter actual)noexcept{
 const DWORD error=GetLastError();struct Restore{DWORD e;~Restore(){SetLastError(e);}} restore{error};
 if(detail::attempted.exchange(true))return HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS);
 auto fail=[](HRESULT hr){detail::installResult.store(hr);return hr;};
 if(!callback||!actual)return fail(E_INVALIDARG);
 HMODULE user=GetModuleHandleW(L"user32.dll");const char*names[]{"SetFocus","SetActiveWindow","SetForegroundWindow","GetActiveWindow","GetFocus"};
 void*detours[]{reinterpret_cast<void*>(&detail::Focus),reinterpret_cast<void*>(&detail::Active),reinterpret_cast<void*>(&detail::Foreground),reinterpret_cast<void*>(&detail::GetActive),reinterpret_cast<void*>(&detail::GetFocused)};
 void**originals[]{reinterpret_cast<void**>(&detail::focusOriginal),reinterpret_cast<void**>(&detail::activeOriginal),reinterpret_cast<void**>(&detail::foregroundOriginal),reinterpret_cast<void**>(&detail::activeGetterOriginal),reinterpret_cast<void**>(&detail::focusGetterOriginal)};
 detail::readScope=callback;detail::actualGetter=actual;
 for(std::size_t i=0;i<5;i++){
  auto target=reinterpret_cast<void*>(GetProcAddress(user,names[i]));MEMORY_BASIC_INFORMATION info{};
  bool valid=target&&VirtualQuery(target,&info,sizeof(info))&&info.AllocationBase==user&&info.Type==MEM_IMAGE;
  if(!valid||MH_CreateHook(target,detours[i],originals[i])!=MH_OK){
   for(std::size_t j=0;j<i;j++)if(detail::created[j])MH_RemoveHook(detail::targets[j]);
   detail::created={};detail::configuredMask.store(0);return fail(E_ACCESSDENIED);
  }
  detail::targets[i]=target;detail::created[i]=true;detail::configuredMask.fetch_or(1u<<i);
 }
 bool queued=true;for(auto target:detail::targets)queued=(MH_QueueEnableHook(target)==MH_OK)&&queued;
 // Retain every created trampoline after this point, including failed apply.
 detail::enabledMask.store(31);
 if(!queued||MH_ApplyQueued()!=MH_OK){DisableHooks();return fail(E_FAIL);}
 detail::ready.store(true,std::memory_order_release);return fail(S_OK);
}
inline unsigned InFlight()noexcept{return detail::inFlight.load(std::memory_order_acquire);}
inline Diagnostic Read()noexcept{
 const DWORD error=GetLastError();Diagnostic d;d.compiled=true;d.attempted=detail::attempted.load();d.ready=detail::ready.load();d.installResult=detail::installResult.load();
 #if defined(PLAYHUB_VALVE_ACTIVE_WINDOW_VIEW)
 d.activeWindowViewCompiled=true;
#endif
 d.actualGetActiveNull=detail::actualGetActiveNull.load();d.logicalActiveReturns=detail::logicalActiveReturns.load();
 d.configuredMask=detail::configuredMask.load();d.enabledMask=detail::enabledMask.load();d.inFlight=InFlight();
 for(unsigned i=0;i<5;i++){d.seen[i]=detail::seen[i].load();d.pinned[i]=detail::pinned[i].load();d.admitted[i]=detail::admitted[i].load();d.delegated[i]=detail::delegated[i].load();}
 std::size_t rows{};d.copyKnown=detail::ring.Copy(d.records,rows,d.dropped);d.rows=std::uint32_t(rows);SetLastError(error);return d;
}
}
