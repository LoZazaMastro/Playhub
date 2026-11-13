#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <cwchar>

// Same message layout as the pinned WSGM bridge. This fixture does not load Steam code.
struct BrokerRequest
{
    uint32_t version, operation, access, flags, sizeHigh, sizeLow;
    uint64_t result;
    uint32_t error, reserved;
    wchar_t name[256];
};
static_assert(sizeof(BrokerRequest) == 552);
static_assert(offsetof(BrokerRequest, name) == 40);

static HANDLE ReadHandle(const wchar_t* name)
{
    wchar_t text[32]{};
    const DWORD length = GetEnvironmentVariableW(name, text, _countof(text));
    if (!length || length >= _countof(text)) return nullptr;
    wchar_t* end = nullptr;
    auto value = wcstoull(text, &end, 16);
    return end && *end == 0 ? reinterpret_cast<HANDLE>(value) : nullptr;
}

int wmain()
{
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return 11;
    DWORD isContainer = 0, length = 0;
    const BOOL tokenRead = GetTokenInformation(token, TokenIsAppContainer, &isContainer, sizeof(isContainer), &length);
    CloseHandle(token);
    if (!tokenRead || isContainer != 1) return 12;

    // The parent creates this event in its own desktop namespace. The sandbox must not open it directly.
    wchar_t name[128]{};
    if (swprintf_s(name, L"GameOverlay_GameExitingEvent_%lu-IPCWrapper", GetCurrentProcessId()) < 0) return 10;
    HANDLE direct = OpenEventW(EVENT_MODIFY_STATE, FALSE, name);
    if (direct) { CloseHandle(direct); return 13; }

    const auto deadline = GetTickCount64() + 30000;
    wchar_t ready[2]{};
    while (!GetEnvironmentVariableW(L"PLAYHUB_FIXTURE_GO", ready, _countof(ready)))
    {
        if (GetTickCount64() >= deadline) return 14;
        Sleep(25);
    }
    if (!GetModuleHandleW(L"WsgmUwpBridge.dll")) return 15;
    if (GetEnvironmentVariableW(L"WSGM_UWP_BRIDGE_MAPPING", ready, _countof(ready)) != 0) return 16;
    HANDLE mapping = ReadHandle(L"PLAYHUB_FIXTURE_MAPPING");
    HANDLE requestEvent = ReadHandle(L"PLAYHUB_FIXTURE_REQUEST");
    HANDLE responseEvent = ReadHandle(L"PLAYHUB_FIXTURE_RESPONSE");
    if (!mapping || !requestEvent || !responseEvent) return 17;
    auto request = static_cast<BrokerRequest*>(MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(BrokerRequest)));
    if (!request || request->version != 1) return 18;
    request->operation = 6;
    request->access = EVENT_MODIFY_STATE;
    request->flags = request->sizeHigh = request->sizeLow = request->reserved = 0;
    request->result = 0;
    request->error = ERROR_IO_PENDING;
    if (wcscpy_s(request->name, name) != 0) return 19;
    if (!SetEvent(requestEvent) || WaitForSingleObject(responseEvent, 5000) != WAIT_OBJECT_0) return 20;
    if (request->error != ERROR_SUCCESS || !request->result) return 21;
    HANDLE granted = reinterpret_cast<HANDLE>(request->result);
    const BOOL signaled = SetEvent(granted);
    CloseHandle(granted);
    UnmapViewOfFile(request);
    CloseHandle(responseEvent);
    CloseHandle(requestEvent);
    CloseHandle(mapping);
    return signaled ? 0 : 22;
}
