// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "CallerImage.h"
#include "ForegroundViewPolicy.h"
#include "ObservationTracePolicy.h"
#include "CaptureView.h"
#include "PassiveResumeIntent.h"
#include <cstdint>
namespace view {
enum class SurfacePurpose { VisibleBand16, OccludedTextureProbeBand1 };
struct Request {
 bool authenticatedGuide{};std::uint64_t sequence{},expiresAt{};
 HANDLE retainedGame{};DWORD gamePid{};std::uint64_t gameBirth{};
 HWND exactSource{},ownSurface{};const wchar_t* expectedFrameAumid{};
 std::uintptr_t ownLifetimeCookie{}; // exact Surface object stored in GWLP_USERDATA
 SurfacePurpose surfacePurpose=SurfacePurpose::VisibleBand16;
};
struct Counts {std::uint64_t calls{},routed{},foreignCaller{},inactiveScope{};unsigned inFlight{};bool patchDisabled{},processLifetimeStorageRetained{};unsigned expectedSurfaceBand{};std::uint64_t activeGeneration{};};
// Private TEMP prototype, no exported IPC or automatic initialization.
// The installed module/trampoline are intentionally retained through process exit,
// even after Close revokes the lease and disables the patch. No global hook.
class ScopedForegroundView {
 bool installed{},ownsMinHook{};
 ArmDiagnostic armDiagnostic{};
 HRESULT ArmCurrent(const Request& request,bool preparation,bool continuation=false);
public:
 ScopedForegroundView()=default;ScopedForegroundView(const ScopedForegroundView&)=delete;ScopedForegroundView& operator=(const ScopedForegroundView&)=delete;
 HRESULT Install(std::shared_ptr<const PinnedRenderer> exactRenderer);
 HRESULT Arm(const Request& request);
 // Fresh Guide only; a fixed5s initial admission budget, never a renewal.
 HRESULT ArmPreparation(const Request& request);
 // Native one-shot continuation of an already accepted real Guide intent.
 HRESULT ArmContinuation(const Request& request,playhub::resume_intent::ContinuationGrant& grant);
 // Same original Guide generation only, with a fresh exact source attestation.
 HRESULT Renew(const Request& request);
 void Disarm()noexcept;
 HWND ActualForeground()const noexcept;
 Counts ReadCounts()const noexcept;
 CaptureDiagnostic ReadCaptureDiagnostic()const;
 observation::Diagnostic ReadObservationDiagnostic()const noexcept; // Copy-only bounded records, no native query.
 ArmDiagnostic ReadArmDiagnostic()const; // Copy only; no native getter or authority.
 HRESULT Close(unsigned cooperativeBudgetMs=250)noexcept;
 ~ScopedForegroundView();
};
}
