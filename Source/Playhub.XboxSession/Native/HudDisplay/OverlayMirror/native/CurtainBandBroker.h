// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "Win32MirrorBackend.h"
#include "CurtainDisplayLease.h"
namespace playhub::curtain {
struct IBand16CurtainBroker {
 virtual mirror::BandState Begin(const mirror::BandTarget&,const BandLease&)=0;
 virtual mirror::BandState Poll(const mirror::BandTarget&)=0;
 virtual bool Retire(const mirror::BandTarget&)=0;
 virtual mirror::BrokerDiagnostic Diagnostics()const{return {};}
 virtual ~IBand16CurtainBroker()=default;
};
std::shared_ptr<IBand16CurtainBroker> CreateCurtainBand16Broker(const wchar_t* callbackDll,const wchar_t* privateRuntimeRoot);
}
