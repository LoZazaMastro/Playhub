// Playhub Xbox Session. Distributed under GPL-3.0-only.
#pragma once
#include <windows.h>
#include <cstdint>
#include <memory>

class PublicSteamContext
{
public:
    using Report = void (WINAPI*)(uint32_t, int32_t);
    using Current = bool (*) (void*);
    static std::unique_ptr<PublicSteamContext> Read(const wchar_t* exactRenderer, Current current, void* state, Report report);
    ~PublicSteamContext();
    PublicSteamContext(const PublicSteamContext&) = delete;
    PublicSteamContext& operator=(const PublicSteamContext&) = delete;
private:
    PublicSteamContext();
    struct State;
    std::unique_ptr<State> value;
};
