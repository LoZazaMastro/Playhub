// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "HudDisplayEngine.h"
#include <memory>
#include <functional>
namespace playhub::hud {
struct NativeDiagnostics {std::uint32_t stage{},error{};std::int32_t hresult{};Id destination{},dwmUpdates{};bool band16{},disabled{},thumbnail{},visible{},sourceCurrent{};};
struct IWindowsBackend: IBackend {virtual void SetPublisherGuard(std::function<Id(const Authority&)>)=0;virtual NativeDiagnostics Diagnostics()const noexcept=0;};
// Construct/use/destroy on the same pumping GUI worker. A null factory performs
// no OS work; initialization and Begin are explicit publisher operations.
std::unique_ptr<IWindowsBackend> CreateHudWindowsBackend(const wchar_t* callback,const wchar_t* runtimeRoot);
}
