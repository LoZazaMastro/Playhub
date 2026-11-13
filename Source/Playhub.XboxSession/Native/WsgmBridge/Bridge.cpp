#include <windows.h>
#include <intrin.h>
#include <cstdint>
#include <cstddef>
#include <cstdlib>
#include <cwchar>
#include "MinHook.h"

// Wire layout shared with OverlayObjectBroker: one serialized request in an unnamed mapping.
// The wrapper duplicates mapping/event handles into this process; it enforces the exact object allowlist.
struct Request
{
    uint32_t version;
    uint32_t operation;
    uint32_t access;
    uint32_t flags;
    uint32_t sizeHigh;
    uint32_t sizeLow;
    uint64_t result;
    uint32_t error;
    uint32_t reserved;
    wchar_t name[256];
};
static_assert(offsetof(Request, name) == 40);
static SRWLOCK requestLock = SRWLOCK_INIT;
static Request* request;
static HANDLE requestEvent;
static HANDLE responseEvent;
static bool failed;

static bool IsRenderer(void* caller)
{
    MEMORY_BASIC_INFORMATION memory{};
    if (!VirtualQuery(caller, &memory, sizeof(memory))) return false;
    wchar_t path[MAX_PATH]{};
    if (!GetModuleFileNameW(static_cast<HMODULE>(memory.AllocationBase), path, MAX_PATH)) return false;
    auto file = wcsrchr(path, L'\\');
    return _wcsicmp(file ? file + 1 : path, L"GameOverlayRenderer64.dll") == 0;
}

static bool IsSteamObject(const wchar_t* name)
{
    if (!name) return false;
    return wcscmp(name, L"SteamWebHelper_GPUProcRenderEvent") == 0
        || wcsncmp(name, L"GameOverlay", 11) == 0
        || wcsncmp(name, L"SteamXInput_", 12) == 0
        || wcsncmp(name, L"SteamOverlayRunning_", 20) == 0
        || wcsncmp(name, L"SteamGameStream_", 16) == 0;
}

static HANDLE Exchange(uint32_t op, const wchar_t* name, uint32_t access, uint32_t flags,
    uint32_t high = 0, uint32_t low = 0)
{
    AcquireSRWLockExclusive(&requestLock);
    HANDLE result = nullptr;
    DWORD error = ERROR_BROKEN_PIPE;
    if (!failed && request && wcslen(name) < 256)
    {
        request->operation = op;
        request->access = access;
        request->flags = flags;
        request->sizeHigh = high;
        request->sizeLow = low;
        request->result = 0;
        request->error = ERROR_IO_PENDING;
        wcscpy_s(request->name, name);
        if (SetEvent(requestEvent) && WaitForSingleObject(responseEvent, 5000) == WAIT_OBJECT_0)
        {
            result = reinterpret_cast<HANDLE>(request->result);
            error = request->error;
        }
        else
        {
            // Do not reuse the slot after an uncertain reply or fall back to private objects.
            failed = true;
            error = ERROR_TIMEOUT;
        }
    }
    ReleaseSRWLockExclusive(&requestLock);
    SetLastError(error);
    return result;
}

static bool WideName(const char* name, wchar_t (&wide)[256])
{
    return name && MultiByteToWideChar(CP_ACP, 0, name, -1, wide, 256) > 0;
}

static decltype(&CreateFileMappingA) realCreateMappingA;
static decltype(&CreateFileMappingW) realCreateMappingW;
static decltype(&OpenFileMappingA) realOpenMappingA;
static decltype(&OpenFileMappingW) realOpenMappingW;
static decltype(&CreateMutexA) realCreateMutexA;
static decltype(&CreateMutexW) realCreateMutexW;
static decltype(&OpenMutexA) realOpenMutexA;
static decltype(&OpenMutexW) realOpenMutexW;
static decltype(&CreateEventA) realCreateEventA;
static decltype(&CreateEventW) realCreateEventW;
static decltype(&OpenEventA) realOpenEventA;
static decltype(&OpenEventW) realOpenEventW;
static decltype(&CreateFileW) realCreateFileW;
static decltype(&CreateFileA) realCreateFileA;

#define ROUTE_W(name) (IsSteamObject(name) && IsRenderer(_ReturnAddress()))
#define ROUTE_A(name) wchar_t wide[256]{}; const bool route = WideName(name, wide) && IsSteamObject(wide) && IsRenderer(_ReturnAddress())

static HANDLE WINAPI CreateMappingA(HANDLE file, LPSECURITY_ATTRIBUTES sa, DWORD protect, DWORD hi, DWORD lo, LPCSTR name)
{
    ROUTE_A(name);
    if (route && file == INVALID_HANDLE_VALUE) return Exchange(1, wide, protect, 0, hi, lo);
    return realCreateMappingA(file, sa, protect, hi, lo, name);
}
static HANDLE WINAPI CreateMappingW(HANDLE file, LPSECURITY_ATTRIBUTES sa, DWORD protect, DWORD hi, DWORD lo, LPCWSTR name)
{
    if (ROUTE_W(name) && file == INVALID_HANDLE_VALUE) return Exchange(1, name, protect, 0, hi, lo);
    return realCreateMappingW(file, sa, protect, hi, lo, name);
}
static HANDLE WINAPI OpenMappingA(DWORD access, BOOL inherit, LPCSTR name)
{
    ROUTE_A(name);
    return route ? Exchange(2, wide, access, 0) : realOpenMappingA(access, inherit, name);
}
static HANDLE WINAPI OpenMappingW(DWORD access, BOOL inherit, LPCWSTR name)
{
    return ROUTE_W(name) ? Exchange(2, name, access, 0) : realOpenMappingW(access, inherit, name);
}
// Whether the renderer has ever asked for a mutex it wants to own from the moment it exists.
// Read by InitializeBridge's caller through BridgeInitialOwnerRequested.
static volatile LONG initialOwnerRequested;

static HANDLE BrokerMutex(const wchar_t* name, BOOL owner)
{
    // A cross-process broker cannot preserve CreateMutex's atomic initial ownership. Creating it
    // unowned and waiting here exposes a race; WAIT_ABANDONED also cannot establish valid protected
    // state. Refuse and count these requests rather than approximating that contract.
    if (owner)
    {
        InterlockedIncrement(&initialOwnerRequested);
        failed = true;
        SetLastError(ERROR_NOT_SUPPORTED);
        return nullptr;
    }

    HANDLE handle = Exchange(3, name, 0, 0);
    DWORD error = GetLastError();
    SetLastError(error);
    return handle;
}
static HANDLE WINAPI NewMutexA(LPSECURITY_ATTRIBUTES sa, BOOL owner, LPCSTR name)
{
    ROUTE_A(name);
    if (route) return BrokerMutex(wide, owner);
    return realCreateMutexA(sa, owner, name);
}
static HANDLE WINAPI NewMutexW(LPSECURITY_ATTRIBUTES sa, BOOL owner, LPCWSTR name)
{
    if (ROUTE_W(name)) return BrokerMutex(name, owner);
    return realCreateMutexW(sa, owner, name);
}
static HANDLE WINAPI ExistingMutexA(DWORD access, BOOL inherit, LPCSTR name)
{
    ROUTE_A(name);
    return route ? Exchange(4, wide, access, 0) : realOpenMutexA(access, inherit, name);
}
static HANDLE WINAPI ExistingMutexW(DWORD access, BOOL inherit, LPCWSTR name)
{
    return ROUTE_W(name) ? Exchange(4, name, access, 0) : realOpenMutexW(access, inherit, name);
}
static HANDLE WINAPI NewEventA(LPSECURITY_ATTRIBUTES sa, BOOL manual, BOOL initial, LPCSTR name)
{
    ROUTE_A(name);
    return route ? Exchange(5, wide, 0, (manual ? 1 : 0) | (initial ? 2 : 0)) : realCreateEventA(sa, manual, initial, name);
}
static HANDLE WINAPI NewEventW(LPSECURITY_ATTRIBUTES sa, BOOL manual, BOOL initial, LPCWSTR name)
{
    return ROUTE_W(name) ? Exchange(5, name, 0, (manual ? 1 : 0) | (initial ? 2 : 0)) : realCreateEventW(sa, manual, initial, name);
}
static HANDLE WINAPI ExistingEventA(DWORD access, BOOL inherit, LPCSTR name)
{
    ROUTE_A(name);
    return route ? Exchange(6, wide, access, 0) : realOpenEventA(access, inherit, name);
}
static HANDLE WINAPI ExistingEventW(DWORD access, BOOL inherit, LPCWSTR name)
{
    return ROUTE_W(name) ? Exchange(6, name, access, 0) : realOpenEventW(access, inherit, name);
}

static bool IsRendererLog(const wchar_t* name)
{
    if (!name) return false;
    const wchar_t* file = wcsrchr(name, L'\\');
    return _wcsicmp(file ? file + 1 : name, L"gameoverlay_renderer.txt") == 0;
}
static HANDLE WINAPI RendererFileW(LPCWSTR name, DWORD access, DWORD share, LPSECURITY_ATTRIBUTES sa, DWORD disposition, DWORD attributes, HANDLE templateFile)
{
    if (IsRendererLog(name) && IsRenderer(_ReturnAddress()))
    {
        HANDLE handle = Exchange(7, L"RendererLog", access, disposition, share, attributes);
        return handle ? handle : INVALID_HANDLE_VALUE;
    }
    return realCreateFileW(name, access, share, sa, disposition, attributes, templateFile);
}
static HANDLE WINAPI RendererFileA(LPCSTR name, DWORD access, DWORD share, LPSECURITY_ATTRIBUTES sa, DWORD disposition, DWORD attributes, HANDLE templateFile)
{
    wchar_t wide[256]{};
    if (WideName(name, wide) && IsRendererLog(wide) && IsRenderer(_ReturnAddress()))
    {
        HANDLE handle = Exchange(7, L"RendererLog", access, disposition, share, attributes);
        return handle ? handle : INVALID_HANDLE_VALUE;
    }
    return realCreateFileA(name, access, share, sa, disposition, attributes, templateFile);
}

static HANDLE EnvironmentHandle(const wchar_t* name)
{
    wchar_t value[32]{};
    if (!GetEnvironmentVariableW(name, value, 32)) return nullptr;
    return reinterpret_cast<HANDLE>(_wcstoui64(value, nullptr, 16));
}

extern "C" __declspec(dllexport) DWORD WINAPI InitializeBridge(void*)
{
    if (request) return ERROR_ALREADY_INITIALIZED;
    HANDLE mapping = EnvironmentHandle(L"WSGM_UWP_BRIDGE_MAPPING");
    requestEvent = EnvironmentHandle(L"WSGM_UWP_BRIDGE_REQUEST");
    responseEvent = EnvironmentHandle(L"WSGM_UWP_BRIDGE_RESPONSE");
    if (!mapping || !requestEvent || !responseEvent) return ERROR_INVALID_HANDLE;
    request = static_cast<Request*>(MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(Request)));
    if (!request || request->version != 1) return ERROR_INVALID_DATA;
    if (MH_Initialize() != MH_OK) return ERROR_DLL_INIT_FAILED;
    // The renderer's own log otherwise describes the wrapper rather than the game, which is
    // misleading while diagnosing. Redirecting it means hooking file creation process-wide, which
    // is a large amount of exposure for a diagnostic, so it is installed only when asked for.
    wchar_t diagnostics[8]{};
    const bool redirectLog = GetEnvironmentVariableW(L"WSGM_BRIDGE_DIAGNOSTICS", diagnostics, 8) > 0;

    HMODULE kernel = GetModuleHandleW(L"kernel32.dll");
    struct Hook { const char* name; void* target; void** original; };
#define HOOK(api, replacement, original) { #api, reinterpret_cast<void*>(replacement), reinterpret_cast<void**>(&original) }
    Hook hooks[] = {
        HOOK(CreateFileMappingA, CreateMappingA, realCreateMappingA), HOOK(CreateFileMappingW, CreateMappingW, realCreateMappingW),
        HOOK(OpenFileMappingA, OpenMappingA, realOpenMappingA), HOOK(OpenFileMappingW, OpenMappingW, realOpenMappingW),
        HOOK(CreateMutexA, NewMutexA, realCreateMutexA), HOOK(CreateMutexW, NewMutexW, realCreateMutexW),
        HOOK(OpenMutexA, ExistingMutexA, realOpenMutexA), HOOK(OpenMutexW, ExistingMutexW, realOpenMutexW),
        HOOK(CreateEventA, NewEventA, realCreateEventA), HOOK(CreateEventW, NewEventW, realCreateEventW),
        HOOK(OpenEventA, ExistingEventA, realOpenEventA), HOOK(OpenEventW, ExistingEventW, realOpenEventW),
        HOOK(CreateFileA, RendererFileA, realCreateFileA), HOOK(CreateFileW, RendererFileW, realCreateFileW),
    };
    const size_t hookCount = redirectLog ? _countof(hooks) : _countof(hooks) - 2;
    for (size_t index = 0; index < hookCount; index++)
    {
        auto& hook = hooks[index];
        auto target = reinterpret_cast<void*>(GetProcAddress(kernel, hook.name));
        if (!target || MH_CreateHook(target, hook.target, hook.original) != MH_OK)
        {
            MH_Uninitialize();
            return ERROR_PROC_NOT_FOUND;
        }
    }
    if (MH_EnableHook(MH_ALL_HOOKS) != MH_OK)
    {
        MH_Uninitialize();
        return ERROR_DLL_INIT_FAILED;
    }

    // Consumed. The three values name a writable view of the request mapping and its two events,
    // so anything else in the game could otherwise read them and ask the desktop broker for a
    // Steam object on its own behalf. The allowlist is what actually constrains that; clearing
    // them removes the invitation.
    SetEnvironmentVariableW(L"WSGM_UWP_BRIDGE_MAPPING", nullptr);
    SetEnvironmentVariableW(L"WSGM_UWP_BRIDGE_REQUEST", nullptr);
    SetEnvironmentVariableW(L"WSGM_UWP_BRIDGE_RESPONSE", nullptr);
    return ERROR_SUCCESS;
}

// How many times the renderer asked for a mutex it wanted to own outright, which this refuses.
// Zero means the case the spike compromised over does not arise on this Steam build.
extern "C" __declspec(dllexport) DWORD WINAPI BridgeInitialOwnerRequested(void*)
{
    return static_cast<DWORD>(initialOwnerRequested);
}
