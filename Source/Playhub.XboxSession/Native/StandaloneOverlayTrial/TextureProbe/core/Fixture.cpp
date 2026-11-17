// SPDX-License-Identifier: GPL-3.0-only
#include <windows.h>
#include <cstdint>
using Getter=std::uintptr_t(WINAPI*)();
extern "C" __declspec(dllexport) __declspec(noinline) std::uintptr_t WINAPI Invoke(Getter getter){volatile auto result=getter();return result;}
extern "C" __declspec(dllexport) const void* WINAPI DataAddress(){static const unsigned data=7;return &data;}
BOOL WINAPI DllMain(HINSTANCE,DWORD,LPVOID){return TRUE;}
