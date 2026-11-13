// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "CurtainDisplayLease.h"
#include <memory>
namespace playhub::curtain {
enum class Prepare {Pending,Ready,Refused};
struct DisplayFacts {std::uint32_t stage{},error{};std::int32_t result{};Id destination{};bool sourceCurrent{},hidden{true},band16{},thumbnail{},presented{},cleanupComplete{true};};
// This is a display-only backend. It never supplies game readiness or input
// authority. Only an internal, independently verified source may provide leases.
struct IWindowsBackend {
 virtual ~IWindowsBackend()=default;
 virtual bool Begin(const BandLease&)=0;
 virtual Prepare Poll(const BandLease&)=0;
 virtual bool Present(const BandLease&)=0;
 virtual bool Renew(const BandLease&)=0;
 // Always withdraw this owned destination, even after source revocation.
 // False means callback/DWM/window cleanup debt: keep object and module alive.
 virtual bool Close()=0;
 virtual DisplayFacts Facts()const noexcept=0;
};
// Construct/use on one pumping GUI worker. Construction performs no OS calls.
// Unknown cleanup is retained rather than freeing a live callback target.
std::shared_ptr<IWindowsBackend> CreateCurtainWindowsBackend(const wchar_t* callbackDll,const wchar_t* privateRuntimeRoot);
}
