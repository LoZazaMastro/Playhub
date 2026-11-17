// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "MirrorPresentation.h"
#include "BrokerDiagnostics.h"
#include <windows.h>
namespace playhub::mirror {
struct BandTarget {
 HWND window{};DWORD processId{},threadId{};Id processBirth{},cookie{};Rect rectangle{};
};
// Adapter to the existing nonce-bound C449 callback. Begin must authenticate the
// exact current UWP/AFH game and Explorer user/session/path/birth, while hidden.
// Ready requires callback completion+revocation+band16 readback, not an ACK only.
// Retire=false means callback lifetime remains uncertain; keep the HWND alive.
struct IBand16Broker {
 virtual BandState Begin(const BandTarget&,const SourceLease&)=0;
 virtual BandState Poll(const BandTarget&)=0;
 virtual bool Retire(const BandTarget&)=0;
 virtual BrokerDiagnostic Diagnostics()const{return {};}
 virtual ~IBand16Broker()=default;
};
// Explicit configured runtime root and exact callback pin; does not initialize
// anything until Begin. This adapter preserves the C449 UWP/AFH capability only.
std::shared_ptr<IBand16Broker> CreateExactBand16Broker(const wchar_t* callbackDll,const wchar_t* privateRuntimeRoot);
class Win32Clock final:public IClock {public:Id Now()const noexcept override{return GetTickCount64();}};
std::shared_ptr<IBackend> CreateWin32MirrorBackend(std::shared_ptr<IBand16Broker> broker);
// Must be called on the original owner GUI thread, including after Close until
// cleanup is complete. A destructor never destroys a possibly committed HWND.
void DrainRetainedMirrorCleanup();
}
