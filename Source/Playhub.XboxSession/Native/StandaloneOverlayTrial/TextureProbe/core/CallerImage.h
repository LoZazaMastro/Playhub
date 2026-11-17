// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <windows.h>
#include <cstdint>
#include <vector>
#include <memory>
#include <string>
namespace view {
class RendererAttestation;
// Exact caller-image membership is shared by production and inert native tests.
class ExecutableImage {
 HMODULE module{};std::vector<std::pair<std::uintptr_t,std::uintptr_t>> ranges;
public:
 static std::unique_ptr<ExecutableImage> Read(HMODULE module);
 bool Contains(const void* address)const noexcept;
 HMODULE Module()const noexcept{return module;}
};
class PinnedRenderer {
 HMODULE module{};HANDLE file=INVALID_HANDLE_VALUE;std::unique_ptr<ExecutableImage> executable;
 std::shared_ptr<const RendererAttestation> attestation;
 PinnedRenderer()=default;
 static std::shared_ptr<const PinnedRenderer> OpenInternal(const wchar_t*,const char*,std::shared_ptr<const RendererAttestation>);
public:
 ~PinnedRenderer();PinnedRenderer(const PinnedRenderer&)=delete;PinnedRenderer& operator=(const PinnedRenderer&)=delete;
 // Does not load or execute Valve code. The exact module must already be loaded.
 static std::shared_ptr<const PinnedRenderer> Open(const wchar_t* exactPath,const char* expectedSha256);
 static std::shared_ptr<const PinnedRenderer> OpenAttestedSteamRenderer(const wchar_t* exactPath);
 bool Caller(const void* address)const noexcept{return executable&&executable->Contains(address);}
};
}
