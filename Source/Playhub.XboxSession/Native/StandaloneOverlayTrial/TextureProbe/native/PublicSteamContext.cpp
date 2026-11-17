// Playhub Xbox Session. Distributed under GPL-3.0-only.
#include "PublicSteamContext.h"
#include "PublicSteamContextPolicy.h"
#include <bcrypt.h>
#include <array>
#include <string>
#include <vector>
#include <cwchar>

// Exact public SDK 1.65 prefixes, pinned df2baabf574a738ef1ea90a7e89339107fc0a279.
// See PublicSteamContext.md. No private method or numeric vtable dispatch is used.
namespace
{
struct SteamIPAddress;
class SteamFriends;
class SteamUser;
class SteamGameServer;
enum class AccountType : int32_t;
enum class Universe : int32_t;
class SteamUtils
{
public:
    virtual uint32_t GetSecondsSinceAppActive() = 0;
    virtual uint32_t GetSecondsSinceComputerActive() = 0;
    virtual Universe GetConnectedUniverse() = 0;
    virtual uint32_t GetServerRealTime() = 0;
    virtual const char* GetIPCountry() = 0;
    virtual bool GetImageSize(int, uint32_t*, uint32_t*) = 0;
    virtual bool GetImageRGBA(int, uint8_t*, int) = 0;
    virtual uint8_t GetCurrentBatteryPower() = 0;
    virtual uint32_t GetAppID() = 0;
};
class SteamClient
{
public:
    virtual int32_t CreateSteamPipe() = 0;
    virtual bool BReleaseSteamPipe(int32_t) = 0;
    virtual int32_t ConnectToGlobalUser(int32_t) = 0;
    virtual int32_t CreateLocalUser(int32_t*, AccountType) = 0;
    virtual void ReleaseUser(int32_t, int32_t) = 0;
    virtual SteamUser* GetISteamUser(int32_t, int32_t, const char*) = 0;
    virtual SteamGameServer* GetISteamGameServer(int32_t, int32_t, const char*) = 0;
    virtual void SetLocalIPBinding(const SteamIPAddress&, uint16_t) = 0;
    virtual SteamFriends* GetISteamFriends(int32_t, int32_t, const char*) = 0;
    virtual SteamUtils* GetISteamUtils(int32_t, const char*) = 0;
};
using Factory = void* (__cdecl*)(const char*, int*);
static_assert(sizeof(int32_t) == 4 && sizeof(uint32_t) == 4 && sizeof(void*) == 8);

struct File
{
    HANDLE handle = INVALID_HANDLE_VALUE;
    ~File() { if (handle != INVALID_HANDLE_VALUE) CloseHandle(handle); }
};
bool Readable(const void* pointer, size_t length)
{
    MEMORY_BASIC_INFORMATION page{};
    if (!pointer || !VirtualQuery(pointer, &page, sizeof(page)) || page.State != MEM_COMMIT
        || (page.Protect & (PAGE_GUARD | PAGE_NOACCESS))) return false;
    auto start = reinterpret_cast<uintptr_t>(pointer), limit = reinterpret_cast<uintptr_t>(page.BaseAddress) + page.RegionSize;
    return length <= limit - start;
}
bool PublicObject(const void* object, unsigned methods, HMODULE module)
{
    if (!Readable(object, sizeof(void*))) return false;
    auto table = *static_cast<void* const* const*>(object);
    if (!Readable(table, methods * sizeof(void*))) return false;
    MEMORY_BASIC_INFORMATION page{};
    if (!VirtualQuery(table, &page, sizeof(page)) || page.AllocationBase != module) return false;
    for (unsigned i = 0; i < methods; ++i)
    {
        if (!VirtualQuery(table[i], &page, sizeof(page)) || page.AllocationBase != module || page.State != MEM_COMMIT
            || !(page.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY))) return false;
    }
    return true;
}
bool SamePath(std::wstring left, std::wstring right)
{
    if (left.compare(0, 4, L"\\\\?\\") == 0) left.erase(0, 4);
    if (right.compare(0, 4, L"\\\\?\\") == 0) right.erase(0, 4);
    return _wcsicmp(left.c_str(), right.c_str()) == 0;
}
bool HashMatches(HANDLE file, const char* expected)
{
    if (!PlayhubPublicSteam::ValidDigest(expected)) return false;
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(file, &size) || size.QuadPart < 4096 || size.QuadPart > 128 * 1024 * 1024) return false;
    BCRYPT_ALG_HANDLE algorithm{}; BCRYPT_HASH_HANDLE hash{};
    bool valid = false;
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0) return false;
    if (BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0) >= 0)
    {
        std::array<unsigned char, 64 * 1024> buffer{};
        DWORD read{}; bool ok = true;
        for (;;)
        {
            if (!ReadFile(file, buffer.data(), static_cast<DWORD>(buffer.size()), &read, nullptr)) { ok = false; break; }
            if (!read) break;
            if (BCryptHashData(hash, buffer.data(), read, 0) < 0) { ok = false; break; }
        }
        std::array<unsigned char, 32> bytes{};
        if (ok && BCryptFinishHash(hash, bytes.data(), static_cast<ULONG>(bytes.size()), 0) >= 0)
        {
            constexpr char digits[] = "0123456789abcdef";
            std::array<char, 65> text{};
            for (unsigned i = 0; i < bytes.size(); ++i) { text[i * 2] = digits[bytes[i] >> 4]; text[i * 2 + 1] = digits[bytes[i] & 15]; }
            valid = _stricmp(text.data(), expected) == 0;
        }
        BCryptDestroyHash(hash);
    }
    BCryptCloseAlgorithmProvider(algorithm, 0);
    return valid;
}
bool SamePeHeader(HANDLE file, HMODULE module)
{
    LARGE_INTEGER at{}; DWORD read{};
    IMAGE_DOS_HEADER diskDos{};
    if (!SetFilePointerEx(file, at, nullptr, FILE_BEGIN) || !ReadFile(file, &diskDos, sizeof(diskDos), &read, nullptr)
        || read != sizeof(diskDos) || diskDos.e_magic != IMAGE_DOS_SIGNATURE || diskDos.e_lfanew < sizeof(diskDos)
        || diskDos.e_lfanew > 1024 * 1024 || !Readable(module, sizeof(IMAGE_DOS_HEADER))) return false;
    auto memoryDos = reinterpret_cast<const IMAGE_DOS_HEADER*>(module);
    if (memoryDos->e_magic != diskDos.e_magic || memoryDos->e_lfanew != diskDos.e_lfanew) return false;
    at.QuadPart = diskDos.e_lfanew;
    IMAGE_NT_HEADERS64 disk{};
    if (!SetFilePointerEx(file, at, nullptr, FILE_BEGIN) || !ReadFile(file, &disk, sizeof(disk), &read, nullptr)
        || read != sizeof(disk) || disk.Signature != IMAGE_NT_SIGNATURE || disk.FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64
        || disk.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC) return false;
    auto memory = reinterpret_cast<const IMAGE_NT_HEADERS64*>(reinterpret_cast<const unsigned char*>(module) + diskDos.e_lfanew);
    return Readable(memory, sizeof(*memory)) && memory->Signature == disk.Signature
        && memory->FileHeader.Machine == disk.FileHeader.Machine && memory->FileHeader.TimeDateStamp == disk.FileHeader.TimeDateStamp
        && memory->FileHeader.NumberOfSections == disk.FileHeader.NumberOfSections
        && memory->OptionalHeader.Magic == disk.OptionalHeader.Magic && memory->OptionalHeader.SizeOfImage == disk.OptionalHeader.SizeOfImage
        && memory->OptionalHeader.SizeOfHeaders == disk.OptionalHeader.SizeOfHeaders;
}
}

struct PublicSteamContext::State
{
    File file;
    HMODULE module{};
    SteamClient* client{};
    SteamUtils* utils{};
    PublicSteamContext::Current current{};
    void* state{};
    Report report{};
    PlayhubPublicSteam::ContextLease<State> lease{*this};
    bool Current() { return current && current(state); }
    int32_t CreatePipe() { return client->CreateSteamPipe(); }
    int32_t Connect(int32_t pipe) { return client->ConnectToGlobalUser(pipe); }
    bool Friends(int32_t user, int32_t pipe, const char* version)
    { auto value = client->GetISteamFriends(user, pipe, version); return PublicObject(value, 1, module); }
    bool Utils(int32_t pipe, const char* version)
    { utils = client->GetISteamUtils(pipe, version); return PublicObject(utils, 9, module); }
    uint32_t AppId() { return utils->GetAppID(); }
    void ReleaseUser(int32_t pipe, int32_t user) { client->ReleaseUser(pipe, user); }
    bool ReleasePipe(int32_t pipe) { return client->BReleaseSteamPipe(pipe); }
    ~State()
    {
        lease.Close();
        if (report && lease.flags) report(27, static_cast<int32_t>(lease.released));
        if (module) FreeLibrary(module);
    }
};

PublicSteamContext::PublicSteamContext() : value(new State) { }
PublicSteamContext::~PublicSteamContext() = default;
std::unique_ptr<PublicSteamContext> PublicSteamContext::Read(const wchar_t* exactRenderer, Current current, void* state, Report report)
{
    auto result = std::unique_ptr<PublicSteamContext>(new PublicSteamContext);
    auto& context = *result->value;
    context.current = current; context.state = state; context.report = report;
    uint32_t flags = 0;
    auto refuse = [&](DWORD error) { if (report) { report(24, flags | context.lease.flags); report(26, static_cast<int32_t>(error)); } return std::move(result); };
#if !defined(PLAYHUB_COMPANION_PUBLIC_CONTEXT_TRIAL)
    return refuse(ERROR_NOT_SUPPORTED);
#else
    if (!exactRenderer || !current || !report || !context.Current()) return refuse(ERROR_INVALID_PARAMETER);
    std::wstring renderer(exactRenderer);
    size_t split = renderer.find_last_of(L"\\/");
    if (split == std::wstring::npos || _wcsicmp(renderer.c_str() + split + 1, L"GameOverlayRenderer64.dll")) return refuse(ERROR_BAD_PATHNAME);
    std::wstring path = renderer.substr(0, split + 1) + L"steamclient64.dll";
    context.file.handle = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
    if (context.file.handle == INVALID_HANDLE_VALUE) return refuse(GetLastError());
    BY_HANDLE_FILE_INFORMATION info{};
    wchar_t canonical[32768]{};
    DWORD length = GetFinalPathNameByHandleW(context.file.handle, canonical, _countof(canonical), FILE_NAME_NORMALIZED);
    if (!GetFileInformationByHandle(context.file.handle, &info) || (info.dwFileAttributes & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_DIRECTORY))
        || !length || length >= _countof(canonical) || !SamePath(path, canonical)
        || !HashMatches(context.file.handle, PLAYHUB_PUBLIC_STEAMCLIENT_SHA256)) return refuse(ERROR_INVALID_DATA);
    flags |= PlayhubPublicSteam::FilePinned;
    if (!context.Current()) return refuse(ERROR_CANCELLED);
    context.module = LoadLibraryExW(path.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!context.module) return refuse(GetLastError());
    wchar_t actual[32768]{};
    length = GetModuleFileNameW(context.module, actual, _countof(actual));
    if (!length || length >= _countof(actual) || !SamePath(path, actual) || !SamePeHeader(context.file.handle, context.module)
        || !context.Current()) return refuse(ERROR_INVALID_DATA);
    flags |= PlayhubPublicSteam::ModuleVerified;
    auto factory = reinterpret_cast<Factory>(GetProcAddress(context.module, "CreateInterface"));
    if (!factory) return refuse(ERROR_PROC_NOT_FOUND);
    int outcome = 1;
    context.client = static_cast<SteamClient*>(factory(PlayhubPublicSteam::ClientVersion, &outcome));
    if (!context.client || outcome || !PublicObject(context.client, 10, context.module) || !context.Current()) return refuse(ERROR_INVALID_FUNCTION);
    flags |= PlayhubPublicSteam::ClientAvailable;
    bool complete = context.lease.Read();
    report(28, static_cast<int32_t>(context.lease.outcome));
    report(24, static_cast<int32_t>(flags | context.lease.flags));
    if (complete) report(25, static_cast<int32_t>(context.lease.appId));
    return result;
#endif
}
