// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <windows.h>
#include <memory>
#include <string>
namespace view {
class RendererAttestation {
 struct State;std::unique_ptr<State> state;
 explicit RendererAttestation(std::unique_ptr<State>);
public:
 ~RendererAttestation();
 RendererAttestation(const RendererAttestation&)=delete;
 RendererAttestation& operator=(const RendererAttestation&)=delete;
 // Retains the registered Steam directory, Valve Steam.exe and renderer file.
 static std::shared_ptr<const RendererAttestation> OpenFile(const wchar_t* canonicalPath);
 const char* Digest()const noexcept;
 bool VerifyLoaded(HMODULE module)const;
 bool VerifyUnchanged()const;
};
}
