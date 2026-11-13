// Playhub Xbox Session. Distributed under GPL-3.0-only.
// Design reference: SteamBro src/overlay.rs, af09896b66d9e549773248521e78d2c6544855e9.
#include "FocusPolicy.h"
#include "PresentationState.h"
#include "SnapshotPixels.h"
#include "SnapshotBlit.h"
#if defined(PLAYHUB_COMPANION_PUBLIC_CONTEXT_TRIAL)
#include "PublicSteamContext.h"
#include "PublicSteamContextPolicy.h"
#endif
#include <windows.h>
#include <windowsx.h>
#include <d3d11.h>
#include <dwmapi.h>
#include <propsys.h>
#include <shellapi.h>
#include <wrl/client.h>
#include <cstdint>
#include <cwchar>
#include <new>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;
using Report = void (WINAPI*)(uint32_t, int32_t);
using TakeGuideRequest = int32_t (WINAPI*)();
using OverlayFlag = bool (__cdecl*)();
constexpr wchar_t ClassName[] = L"PlayhubSteamCompanion";
constexpr int HotkeyId = 4101;
static thread_local wchar_t ExpectedFrameAumid[513]{};

class FrameAumidScope
{
public:
    explicit FrameAumidScope(const wchar_t* aumid)
    {
        wcscpy_s(previous, ExpectedFrameAumid);
        if (!ValidFrameAumid(aumid)) return;
        if (!aumid) { ExpectedFrameAumid[0] = 0; valid = true; return; }
        wcscpy_s(ExpectedFrameAumid, aumid);
        valid = true;
    }
    ~FrameAumidScope() { wcscpy_s(ExpectedFrameAumid, previous); }
    explicit operator bool() const { return valid; }
private:
    wchar_t previous[513]{};
    bool valid = false;
};
#ifdef PLAYHUB_COMPANION_FULLSCREEN_SNAPSHOT
constexpr bool FullscreenSnapshotTrial = true;
#else
constexpr bool FullscreenSnapshotTrial = false;
#endif

static bool SameGame(HANDLE game, DWORD pid, uint64_t birth)
{
    FILETIME created{}, exited{}, kernel{}, user{};
    return GetProcessId(game) == pid && WaitForSingleObject(game, 0) == WAIT_TIMEOUT
        && GetProcessTimes(game, &created, &exited, &kernel, &user)
        && ((uint64_t{created.dwHighDateTime} << 32) | created.dwLowDateTime) == birth;
}

static bool HasClass(HWND window, const wchar_t* expected)
{
    wchar_t name[96]{};
    return GetClassNameW(window, name, _countof(name)) != 0 && wcscmp(name, expected) == 0;
}

static bool FrameIdentity(HWND window, DWORD owner, DWORD thread)
{
    // Direct Win32 sessions pass no AUMID and cannot claim a hosted Store frame.
    if (!ExpectedFrameAumid[0]) return false;
    HANDLE host = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, owner);
    if (!host) return false;
    wchar_t image[MAX_PATH]{}, system[MAX_PATH]{};
    DWORD size = _countof(image);
    const UINT systemSize = GetSystemDirectoryW(system, _countof(system));
    const bool systemHost = systemSize && systemSize < _countof(system)
        && QueryFullProcessImageNameW(host, 0, image, &size)
        && _wcsicmp(image, (std::wstring(system) + L"\\ApplicationFrameHost.exe").c_str()) == 0;
    CloseHandle(host);
    if (!systemHost) return false;
    ComPtr<IPropertyStore> store;
    if (FAILED(SHGetPropertyStoreForWindow(window, IID_PPV_ARGS(store.GetAddressOf())))) return false;
    constexpr PROPERTYKEY appId{{0x9F4C2855, 0x9F79, 0x4B39, {0xA8, 0xD0, 0xE1, 0xD4, 0x2D, 0xE1, 0xD5, 0xF3}}, 5};
    PROPVARIANT value{};
    DWORD confirmedOwner = 0;
    bool match = SUCCEEDED(store->GetValue(appId, &value)) && value.vt == VT_LPWSTR && value.pwszVal
        && _wcsicmp(value.pwszVal, ExpectedFrameAumid) == 0
        && GetWindowThreadProcessId(window, &confirmedOwner) == thread && confirmedOwner == owner;
    PropVariantClear(&value);
    return match;
}

struct FrameCache
{
    HWND window{};
    DWORD owner{}, thread{};
    ULONGLONG until{};
    bool matched{};
};

static bool GameWindow(HWND window, DWORD pid, FrameCache* cache = nullptr)
{
    if (!window || !IsWindowVisible(window) || IsIconic(window) || GetAncestor(window, GA_ROOT) != window)
        return false;
    DWORD cloak = 0, owner = 0;
    if (SUCCEEDED(DwmGetWindowAttribute(window, DWMWA_CLOAKED, &cloak, sizeof(cloak))) && cloak) return false;
    const DWORD thread = GetWindowThreadProcessId(window, &owner);
    if (!thread) return false;
    if (owner == pid) return true;
    if (!ExpectedFrameAumid[0]) return false;
    if (!HasClass(window, L"ApplicationFrameWindow")) return false;

    // A hosted UWP frame is valid only when its single visible CoreWindow is the verified game.
    int matches = 0;
    for (HWND child = FindWindowExW(window, nullptr, L"Windows.UI.Core.CoreWindow", nullptr);
        child; child = FindWindowExW(window, child, L"Windows.UI.Core.CoreWindow", nullptr))
    {
        if (!IsWindowVisible(child)) continue;
        DWORD childOwner = 0;
        GetWindowThreadProcessId(child, &childOwner);
        if (childOwner != pid || ++matches != 1) return false;
    }
    if (matches == 1) return true;
    const auto now = GetTickCount64();
    if (cache && cache->window == window && cache->owner == owner && cache->thread == thread && now < cache->until)
        return cache->matched;
    const bool matched = FrameIdentity(window, owner, thread);
    if (cache) *cache = {window, owner, thread, now + 250, matched};
    return matched;
}

class ComApartment
{
public:
    const HRESULT result = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    ~ComApartment() { if (SUCCEEDED(result)) CoUninitialize(); }
};

struct ValveOverlay
{
    OverlayFlag needsFrame{}, mouse{}, keyboard{}, controller{};

    bool Open() const { return mouse() || keyboard() || controller(); }

    static ValveOverlay Find(HANDLE game, HANDLE owner, const wchar_t* exactRenderer)
    {
        HANDLE processes[]{game, owner};
        const auto end = GetTickCount64() + 3000;
        do
        {
            HMODULE module = GetModuleHandleW(L"GameOverlayRenderer64.dll");
            if (module)
            {
                wchar_t path[32768]{};
                const DWORD length = GetModuleFileNameW(module, path, _countof(path));
                if (length == 0 || length >= _countof(path) || _wcsicmp(path, exactRenderer) != 0) return {};
                return {
                    reinterpret_cast<OverlayFlag>(GetProcAddress(module, "BOverlayNeedsPresent")),
                    reinterpret_cast<OverlayFlag>(GetProcAddress(module, "SteamOverlayIsUsingMouse")),
                    reinterpret_cast<OverlayFlag>(GetProcAddress(module, "SteamOverlayIsUsingKeyboard")),
                    reinterpret_cast<OverlayFlag>(GetProcAddress(module, "SteamOverlayIsUsingGamepad"))
                };
            }
        } while (GetTickCount64() < end && WaitForMultipleObjects(_countof(processes), processes, FALSE, 50) == WAIT_TIMEOUT);
        return {};
    }

    explicit operator bool() const { return needsFrame && mouse && keyboard && controller; }
};

class Surface
{
public:
    HWND window{};
    bool shown{};

    ~Surface()
    {
        if (diagnostics) presentation.Hide(diagnostics);
        if (window) DestroyWindow(window);
    }

    HRESULT Create(OverlayFlag usingMouse, Report report)
    {
        diagnostics = report;
        overlayUsingMouse = usingMouse;
        HINSTANCE instance = GetModuleHandleW(nullptr);
        WNDCLASSEXW type{sizeof(type)};
        type.lpfnWndProc = WindowProc;
        type.hInstance = instance;
        type.hCursor = nullptr;
        type.lpszClassName = ClassName;
        if (!RegisterClassExW(&type) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
            return HRESULT_FROM_WIN32(GetLastError());
        window = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_TOPMOST, ClassName, L"Playhub Steam Overlay",
            WS_POPUP, 0, 0, 1, 1, nullptr, nullptr, instance, this);
        if (!window) return HRESULT_FROM_WIN32(GetLastError());
        MARGINS margins{-1, -1, -1, -1};
        HRESULT result = DwmExtendFrameIntoClientArea(window, &margins);
        if (FAILED(result)) return result;

        DXGI_SWAP_CHAIN_DESC configuration{};
        configuration.BufferDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        configuration.SampleDesc.Count = 1;
        configuration.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        configuration.BufferCount = 1;
        configuration.OutputWindow = window;
        configuration.Windowed = TRUE;
        configuration.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;
        result = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr, 0, D3D11_SDK_VERSION, &configuration, swap.GetAddressOf(), device.GetAddressOf(),
            nullptr, context.GetAddressOf());
        return FAILED(result) ? result : Draw();
    }

    HRESULT Draw()
    {
        const HRESULT result = DrawFrame();
        presentation.Observe(static_cast<int32_t>(result), shown, diagnostics);
        return result;
    }

    HRESULT DrawFrame()
    {
        if (!view)
        {
            ComPtr<ID3D11Texture2D> buffer;
            HRESULT result = swap->GetBuffer(0, IID_PPV_ARGS(buffer.GetAddressOf()));
            if (FAILED(result)) return result;
            result = device->CreateRenderTargetView(buffer.Get(), nullptr, view.GetAddressOf());
            if (FAILED(result)) return result;
        }
        ID3D11RenderTargetView* target = view.Get();
        context->OMSetRenderTargets(0, nullptr, nullptr);
        if (snapshot)
        {
            ComPtr<ID3D11Texture2D> buffer;
            HRESULT result = swap->GetBuffer(0, IID_PPV_ARGS(buffer.GetAddressOf()));
            if (FAILED(result)) return result;
            D3D11_TEXTURE2D_DESC source{}, destination{};
            snapshot->GetDesc(&source);
            buffer->GetDesc(&destination);
            if (source.Format != destination.Format) return E_INVALIDARG;
            if (source.Width == destination.Width && source.Height == destination.Height)
                context->CopyResource(buffer.Get(), snapshot.Get());
            else
            {
                if (!snapshotView)
                {
                    result = device->CreateShaderResourceView(snapshot.Get(), nullptr, snapshotView.GetAddressOf());
                    if (FAILED(result)) return result;
                }
                result = snapshotBlit.Draw(device.Get(), context.Get(), target, snapshotView.Get(), destination.Width, destination.Height);
                if (FAILED(result)) return result;
            }
        }
        context->OMSetRenderTargets(1, &target, nullptr);
        if (!snapshot)
        {
            const float clear[4]{};
            context->ClearRenderTargetView(target, clear);
        }
        return swap->Present(1, 0);
    }

    HRESULT PrepareSnapshot(HANDLE game, DWORD pid, uint64_t birth, HWND source, Report report)
    {
        DropSnapshot();
        if (!FullscreenSnapshotTrial) return S_OK;
        if (shown || IsWindowVisible(window)) return E_ACCESSDENIED;
        RECT client{};
        POINT origin{};
        DWORD owner = 0;
        const DWORD thread = GetWindowThreadProcessId(source, &owner);
        wchar_t name[96]{};
        WINDOWPLACEMENT placement{sizeof(placement)};
        HMONITOR monitor = MonitorFromWindow(source, MONITOR_DEFAULTTONULL);
        MONITORINFOEXW monitorInfo{};
        monitorInfo.cbSize = sizeof(monitorInfo);
        if (!SameGame(game, pid, birth) || GetForegroundWindow() != source || !GameWindow(source, pid)
            || !thread || !GetClassNameW(source, name, _countof(name)) || !GetClientRect(source, &client)
            || !ClientToScreen(source, &origin) || !GetWindowPlacement(source, &placement)
            || !BoundedSnapshotSize(client.right, client.bottom)
            || !monitor || !GetMonitorInfoW(monitor, &monitorInfo)) return E_ACCESSDENIED;
        const LONG virtualLeft = GetSystemMetrics(SM_XVIRTUALSCREEN), virtualTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        if (origin.x < virtualLeft || origin.y < virtualTop
            || static_cast<int64_t>(origin.x) + client.right > static_cast<int64_t>(virtualLeft) + GetSystemMetrics(SM_CXVIRTUALSCREEN)
            || static_cast<int64_t>(origin.y) + client.bottom > static_cast<int64_t>(virtualTop) + GetSystemMetrics(SM_CYVIRTUALSCREEN)) return E_INVALIDARG;

        // One SDR-display frame, captured before the helper becomes visible.
        // This neither changes the game's HDR/display configuration nor reads
        // a background/foreign window. No frame is written to disk.
        HDC desktop = GetDC(nullptr);
        HDC memory = desktop ? CreateCompatibleDC(desktop) : nullptr;
        BITMAPINFO info{};
        info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = client.right;
        info.bmiHeader.biHeight = -client.bottom;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = BI_RGB;
        void* data = nullptr;
        HBITMAP bitmap = memory ? CreateDIBSection(memory, &info, DIB_RGB_COLORS, &data, nullptr, 0) : nullptr;
        HGDIOBJ previous = bitmap ? SelectObject(memory, bitmap) : nullptr;
        const bool selected = previous && previous != HGDI_ERROR;
        const bool validSurface = desktop && memory && bitmap && data && selected;
        const bool exactSource = validSurface && SameGame(game, pid, birth)
            && GameWindow(source, pid) && GetForegroundWindow() == source;
        bool copied = false, flushed = false;
        DWORD copyError = ERROR_SUCCESS;
        if (exactSource)
        {
            SetLastError(ERROR_SUCCESS);
            copied = BitBlt(memory, 0, 0, client.right, client.bottom, desktop, origin.x, origin.y, SRCCOPY) != FALSE;
            if (!copied) copyError = GetLastError();
            else flushed = GdiFlush() != FALSE;
        }
        const SnapshotCaptureState capture = ClassifySnapshotCapture(validSurface, exactSource, copied, flushed);
        std::vector<uint32_t> pixels;
        bool allocationFailed = false;
        if (capture == SnapshotCaptureState::Copied)
        {
            auto* first = static_cast<uint32_t*>(data);
            try { pixels.assign(first, first + static_cast<size_t>(client.right) * static_cast<size_t>(client.bottom)); }
            catch (const std::bad_alloc&) { allocationFailed = true; }
        }
        if (selected) SelectObject(memory, previous);
        if (bitmap) DeleteObject(bitmap);
        if (memory) DeleteDC(memory);
        if (desktop) ReleaseDC(nullptr, desktop);
        if (allocationFailed) return E_OUTOFMEMORY;
        if (capture != SnapshotCaptureState::Copied)
        {
            report(22, static_cast<int32_t>(capture));
            if (capture == SnapshotCaptureState::SourceChanged) return E_ACCESSDENIED;
            if (capture == SnapshotCaptureState::CopyFailed)
            {
                report(23, static_cast<int32_t>(copyError));
                return HRESULT_FROM_WIN32(copyError ? copyError : ERROR_INVALID_DATA);
            }
            // GdiFlush reports failure of a batch, without a documented last-error contract.
            return capture == SnapshotCaptureState::FlushFailed ? E_FAIL : HRESULT_FROM_WIN32(ERROR_INVALID_HANDLE);
        }
        bool hasColor = false;
        if (!SnapshotPixelCountMatches(client.right, client.bottom, pixels.size())
            || !PrepareSnapshotPixels(pixels, &hasColor))
        {
            report(22, static_cast<int32_t>(SnapshotCaptureState::InvalidPixels));
            return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        }

        DWORD currentOwner = 0;
        RECT currentClient{};
        POINT currentOrigin{};
        MONITORINFOEXW currentMonitor{};
        currentMonitor.cbSize = sizeof(currentMonitor);
        if (!SameGame(game, pid, birth) || GetForegroundWindow() != source || !GameWindow(source, pid)
            || GetWindowThreadProcessId(source, &currentOwner) != thread || currentOwner != owner || !HasClass(source, name)
            || !GetClientRect(source, &currentClient) || !ClientToScreen(source, &currentOrigin)
            || !EqualRect(&client, &currentClient) || origin.x != currentOrigin.x || origin.y != currentOrigin.y
            || !GetMonitorInfoW(monitor, &currentMonitor) || _wcsicmp(currentMonitor.szDevice, monitorInfo.szDevice) != 0
            || !EqualRect(&currentMonitor.rcMonitor, &monitorInfo.rcMonitor))
        {
            report(22, static_cast<int32_t>(SnapshotCaptureState::SourceChanged));
            return E_ACCESSDENIED;
        }
        report(22, static_cast<int32_t>(hasColor ? SnapshotCaptureState::Copied : SnapshotCaptureState::Black));
        D3D11_TEXTURE2D_DESC description{};
        description.Width = client.right;
        description.Height = client.bottom;
        description.MipLevels = description.ArraySize = 1;
        description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        description.SampleDesc.Count = 1;
        description.Usage = D3D11_USAGE_IMMUTABLE;
        description.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        D3D11_SUBRESOURCE_DATA initial{pixels.data(), static_cast<UINT>(client.right * sizeof(uint32_t)), 0};
        HRESULT result = device->CreateTexture2D(&description, &initial, snapshot.GetAddressOf());
        if (FAILED(result)) return result;
        snapshotWindow = source;
        snapshotOwner = owner;
        snapshotThread = thread;
        wcscpy_s(snapshotClass, name);
        snapshotClient = client;
        snapshotOrigin = origin;
        snapshotPlacement = placement.rcNormalPosition;
        snapshotMonitor = monitor;
        wcscpy_s(snapshotMonitorDevice, monitorInfo.szDevice);
        const RECT& display = monitorInfo.rcMonitor;
        snapshotFilledMonitor = origin.x <= display.left + 2 && origin.y <= display.top + 2
            && static_cast<int64_t>(origin.x) + client.right >= display.right - 2
            && static_cast<int64_t>(origin.y) + client.bottom >= display.bottom - 2;
        // Compile the small scaler while still hidden, before a possible FSE
        // mode switch restores a larger desktop monitor.
        if (snapshotFilledMonitor)
        {
            result = snapshotBlit.Initialize(device.Get());
            if (FAILED(result)) { DropSnapshot(); return result; }
        }
        return S_OK;
    }

    bool SnapshotMatches(HWND source, DWORD pid) const
    {
        if (!FullscreenSnapshotTrial || !snapshot || source != snapshotWindow || !IsWindow(source)
            || GetAncestor(source, GA_ROOT) != source || !HasClass(source, snapshotClass)) return false;
        DWORD owner = 0;
        if (GetWindowThreadProcessId(source, &owner) != snapshotThread || owner != snapshotOwner) return false;
        if (owner != pid && (!HasClass(source, L"ApplicationFrameWindow") || !FrameIdentity(source, owner, snapshotThread))) return false;
        if (IsIconic(source))
        {
            WINDOWPLACEMENT placement{sizeof(placement)};
            return GetWindowPlacement(source, &placement) && EqualRect(&placement.rcNormalPosition, &snapshotPlacement);
        }
        RECT client{};
        POINT origin{};
        return GameWindow(source, pid) && GetClientRect(source, &client) && ClientToScreen(source, &origin)
            && EqualRect(&client, &snapshotClient) && origin.x == snapshotOrigin.x && origin.y == snapshotOrigin.y;
    }

    bool HasSnapshot() const { return snapshot.Get() != nullptr; }
    bool RestoreQueued() const { return restoreQueue.Queued(); }
    void MarkRestoreQueued() { restoreQueue.Mark(); }

    void DropSnapshot()
    {
        snapshotView.Reset();
        snapshotBlit.Reset();
        snapshot.Reset();
        snapshotWindow = nullptr;
        snapshotOwner = snapshotThread = 0;
        snapshotClass[0] = 0;
        snapshotMonitor = nullptr;
        snapshotMonitorDevice[0] = 0;
        snapshotFilledMonitor = false;
        restoreQueue.Reset();
    }

    HRESULT Align(HWND game, bool reveal = true)
    {
        RECT client{};
        POINT origin{};
        if (snapshot && game == snapshotWindow && IsIconic(game))
        {
            client = snapshotClient;
            origin = snapshotOrigin;
        }
        else if (!GetClientRect(game, &client) || !ClientToScreen(game, &origin)) return E_FAIL;
        if (snapshot && game == snapshotWindow)
        {
            MONITORINFOEXW monitor{};
            const bool sameMonitor = ReadSnapshotMonitor(monitor);
            SnapshotViewport selected{};
            if (!SelectSnapshotViewport({origin.x, origin.y, client.right, client.bottom},
                {monitor.rcMonitor.left, monitor.rcMonitor.top, monitor.rcMonitor.right - monitor.rcMonitor.left,
                    monitor.rcMonitor.bottom - monitor.rcMonitor.top}, snapshotFilledMonitor,
                GetForegroundWindow() == window, sameMonitor, selected)) return E_ACCESSDENIED;
            origin = {selected.left, selected.top};
            client = {0, 0, selected.width, selected.height};
        }
        if (client.right < 2 || client.bottom < 2) return E_INVALIDARG;
        if (client.right != width || client.bottom != height)
        {
            context->OMSetRenderTargets(0, nullptr, nullptr);
            view.Reset();
            HRESULT result = swap->ResizeBuffers(0, client.right, client.bottom, DXGI_FORMAT_UNKNOWN, 0);
            if (FAILED(result)) return result;
            width = client.right;
            height = client.bottom;
        }
        if (!shown || origin.x != left || origin.y != top || client.right != placedWidth || client.bottom != placedHeight)
        {
            UINT flags = SWP_NOACTIVATE | (reveal ? SWP_SHOWWINDOW : 0);
            if (!SetWindowPos(window, HWND_TOPMOST, origin.x, origin.y, width, height, flags))
                return HRESULT_FROM_WIN32(GetLastError());
            left = origin.x;
            top = origin.y;
            placedWidth = width;
            placedHeight = height;
            shown = reveal;
        }
        return S_OK;
    }

    void Hide()
    {
        if (shown) ShowWindow(window, SW_HIDE);
        shown = false;
        presentation.Hide(diagnostics);
        DropSnapshot();
    }

private:
    struct MonitorSearch { const wchar_t* device; MONITORINFOEXW information{}; HMONITOR monitor{}; unsigned matches{}; };

    static BOOL CALLBACK FindSnapshotMonitor(HMONITOR monitor, HDC, LPRECT, LPARAM parameter)
    {
        auto& search = *reinterpret_cast<MonitorSearch*>(parameter);
        MONITORINFOEXW information{};
        information.cbSize = sizeof(information);
        if (GetMonitorInfoW(monitor, &information) && _wcsicmp(information.szDevice, search.device) == 0)
        {
            ++search.matches;
            search.information = information;
            search.monitor = monitor;
        }
        return search.matches <= 1;
    }

    bool ReadSnapshotMonitor(MONITORINFOEXW& information) const
    {
        information.cbSize = sizeof(information);
        if (snapshotMonitor && GetMonitorInfoW(snapshotMonitor, &information)
            && _wcsicmp(information.szDevice, snapshotMonitorDevice) == 0) return true;
        MonitorSearch search{snapshotMonitorDevice};
        return EnumDisplayMonitors(nullptr, nullptr, FindSnapshotMonitor, reinterpret_cast<LPARAM>(&search))
            && search.matches == 1 && (information = search.information, true);
    }

    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam)
    {
        auto* self = reinterpret_cast<Surface*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (message == WM_NCCREATE)
        {
            self = static_cast<Surface*>(reinterpret_cast<CREATESTRUCTW*>(lparam)->lpCreateParams);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        }
        if (message == WM_SETCURSOR && LOWORD(lparam) == HTCLIENT && self
            && (!self->overlayUsingMouse || !self->overlayUsingMouse()))
        {
            SetCursor(nullptr);
            return TRUE;
        }
        return DefWindowProcW(window, message, wparam, lparam);
    }

    ComPtr<IDXGISwapChain> swap;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<ID3D11RenderTargetView> view;
    ComPtr<ID3D11Texture2D> snapshot;
    ComPtr<ID3D11ShaderResourceView> snapshotView;
    SnapshotBlit snapshotBlit;
    OverlayFlag overlayUsingMouse{};
    Report diagnostics{};
    PresentationState presentation;
    HWND snapshotWindow{};
    DWORD snapshotOwner{}, snapshotThread{};
    wchar_t snapshotClass[96]{};
    RECT snapshotClient{}, snapshotPlacement{};
    POINT snapshotOrigin{};
    HMONITOR snapshotMonitor{};
    wchar_t snapshotMonitorDevice[CCHDEVICENAME]{};
    bool snapshotFilledMonitor{};
    SnapshotRestoreQueue restoreQueue;
    LONG width = 1, height = 1, left = 0, top = 0, placedWidth = 0, placedHeight = 0;
};

static INPUT KeyboardInput(WORD key, bool release)
{
    INPUT input{};
    input.type = INPUT_KEYBOARD;
    input.ki.wVk = key;
    input.ki.dwFlags = release ? KEYEVENTF_KEYUP : 0;
    return input;
}

static bool OwnForeground(HWND window)
{
    DWORD owner = 0;
    return window && GetForegroundWindow() == window &&
        GetWindowThreadProcessId(window, &owner) != 0 && owner == GetCurrentProcessId();
}

static unsigned ForegroundForHideLog(HWND window, HWND companion, HWND game)
{
    if (!window) return 0;
    if (window == game) return 1;
    if (window == companion) return 2;
    if (HasClass(window, L"SDL_app")) return 3;
    if (HasClass(window, L"ApplicationFrameWindow")) return 4;
    if (HasClass(window, L"Chrome_WidgetWin_1") || HasClass(window, L"CEFBrowserWindow")) return 5;
    return 6;
}

static bool CanAttemptCompanion(HWND window)
{
    RECT client{};
    POINT origin{};
    MONITORINFO monitor{sizeof(monitor)};
    if (!GetClientRect(window, &client) || !ClientToScreen(window, &origin)
        || !GetMonitorInfoW(MonitorFromWindow(window, MONITOR_DEFAULTTONULL), &monitor)) return false;
    SetLastError(ERROR_SUCCESS);
    const LONG_PTR style = GetWindowLongPtrW(window, GWL_EXSTYLE);
    if (!style && GetLastError() != ERROR_SUCCESS) return false;
    const RECT& screen = monitor.rcMonitor;
    const bool fillsMonitor = origin.x <= screen.left + 2 && origin.y <= screen.top + 2
        && origin.x + client.right >= screen.right - 2 && origin.y + client.bottom >= screen.bottom - 2;
    return client.right > 0 && client.bottom > 0
        && CanLayerCompanion(fillsMonitor, (style & WS_EX_TOPMOST) != 0, FullscreenSnapshotTrial);
}

static HRESULT PrepareRequestedSurface(Surface& surface, HANDLE game, DWORD pid, uint64_t birth, HWND source, Report report)
{
    HRESULT result = surface.PrepareSnapshot(game, pid, birth, source, report);
    if (FullscreenSnapshotTrial) report(17, result);
    if (FAILED(result)) return result;
    if (!FullscreenSnapshotTrial) return surface.Align(source);
    result = surface.Align(source, false);
    if (SUCCEEDED(result)) result = surface.Draw();
    if (FAILED(result) || !SameGame(game, pid, birth) || GetForegroundWindow() != source
        || !GameWindow(source, pid) || !surface.SnapshotMatches(source, pid))
    {
        surface.DropSnapshot();
        return FAILED(result) ? result : E_ACCESSDENIED;
    }
    return surface.Align(source);
}

static bool RetainedSnapshotSource(const Surface& surface, HANDLE game, DWORD pid, uint64_t birth, HWND source)
{
    return KeepSnapshotSource(SameGame(game, pid, birth), surface.SnapshotMatches(source, pid), surface.HasSnapshot(),
        OwnForeground(surface.window) ? Foreground::Companion : Foreground::Other, IsIconic(source) != FALSE);
}

static bool RestoreSnapshotGame(Surface& surface, HANDLE game, DWORD pid, uint64_t birth, HWND source)
{
    if (!OwnForeground(surface.window) || !SameGame(game, pid, birth)
        || (!GameWindow(source, pid) && !surface.SnapshotMatches(source, pid))) return false;
    if (IsIconic(source))
    {
        // Queue the restore on the exact retained game's own UI thread. A hung
        // game must not block this helper or keep an input queue attached.
        if (!surface.SnapshotMatches(source, pid) || !SameGame(game, pid, birth) || !OwnForeground(surface.window)) return false;
        if (!surface.RestoreQueued())
        {
            if (!ShowWindowAsync(source, SW_SHOWNOACTIVATE)) return false;
            surface.MarkRestoreQueued();
        }
    }
    if (GetForegroundWindow() == surface.window && SameGame(game, pid, birth)
        && (GameWindow(source, pid) || surface.SnapshotMatches(source, pid))) SetForegroundWindow(source);
    return GetForegroundWindow() == source && SameGame(game, pid, birth) && GameWindow(source, pid);
}

class ActivationButton
{
public:
    ~ActivationButton() { if (window) DestroyWindow(window); }

    bool Show(HWND source)
    {
        if (!window)
        {
            window = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW, L"BUTTON", L"Open Steam Overlay",
                WS_POPUP | BS_PUSHBUTTON, 0, 0, 1, 1, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
            if (!window) return false;
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(this));
            originalProc = reinterpret_cast<WNDPROC>(SetWindowLongPtrW(window, GWLP_WNDPROC,
                reinterpret_cast<LONG_PTR>(&WindowProc)));
            if (!originalProc)
            {
                DestroyWindow(window);
                window = nullptr;
                return false;
            }
            SendMessageW(window, WM_SETFONT, reinterpret_cast<WPARAM>(GetStockObject(DEFAULT_GUI_FONT)), FALSE);
        }
        RECT client{};
        POINT origin{};
        if (!GetClientRect(source, &client) || !ClientToScreen(source, &origin)) return false;
        UINT dpi = GetDpiForWindow(source);
        if (!dpi) dpi = USER_DEFAULT_SCREEN_DPI;
        const int width = MulDiv(176, dpi, USER_DEFAULT_SCREEN_DPI);
        const int height = MulDiv(36, dpi, USER_DEFAULT_SCREEN_DPI);
        const int inset = MulDiv(12, dpi, USER_DEFAULT_SCREEN_DPI);
        if (client.right < width + 2 * inset || client.bottom < height + 2 * inset) return false;
        if (!SetWindowPos(window, HWND_TOPMOST, origin.x + client.right - width - inset,
            origin.y + client.bottom - height - inset, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW))
            return false;
        game = source;
        deadline = GetTickCount64() + 7000;
        pressed = clicked = false;
        return true;
    }

    bool Visible() const { return game != nullptr; }
    bool OwnsFocus() const { return OwnForeground(window); }
    bool Expired() const { return GetTickCount64() >= deadline; }
    HWND Source() const { return game; }
    bool Clicked() const { return clicked; }

    void Hide()
    {
        if (Visible()) ShowWindow(window, SW_HIDE);
        game = nullptr;
        pressed = clicked = false;
    }

private:
    static bool MouseInput()
    {
        INPUT_MESSAGE_SOURCE source{};
        return GetCurrentInputMessageSource(&source) && source.deviceType == IMDT_MOUSE
            && (source.originId == IMO_HARDWARE || source.originId == IMO_INJECTED);
    }

    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam)
    {
        auto& self = *reinterpret_cast<ActivationButton*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (message == WM_MOUSEACTIVATE)
        {
            if (self.Visible() && !self.Expired() && LOWORD(lparam) == HTCLIENT
                && HIWORD(lparam) == WM_LBUTTONDOWN && GetForegroundWindow() == self.game)
                return MA_ACTIVATE;
            return MA_NOACTIVATEANDEAT;
        }
        if (message == WM_LBUTTONDOWN)
            self.pressed = self.Visible() && !self.Expired() && self.OwnsFocus() && MouseInput();
        if (message == WM_LBUTTONUP)
        {
            RECT client{};
            POINT point{GET_X_LPARAM(lparam), GET_Y_LPARAM(lparam)};
            self.clicked = self.pressed && !self.Expired() && self.OwnsFocus() && MouseInput()
                && GetClientRect(window, &client) && PtInRect(&client, point);
            self.pressed = false;
        }
        if (message == WM_CAPTURECHANGED) self.pressed = false;
        return CallWindowProcW(self.originalProc, window, message, wparam, lparam);
    }

    HWND window{}, game{};
    WNDPROC originalProc{};
    ULONGLONG deadline{};
    bool pressed{}, clicked{};
};

enum class FocusTransfer { Refused, Acquired, DetachFailed };

static bool ConfirmCompanionFocus(HWND source, HWND destination)
{
    for (int check = 0; check < 4; ++check)
    {
        HWND foreground = GetForegroundWindow();
        if (foreground == destination) return OwnForeground(destination);
        if (foreground != source) return false;
        if (check < 3) Sleep(8);
    }
    return false;
}

static FocusTransfer FocusFromGame(HANDLE game, DWORD pid, uint64_t birth, HWND source, HWND destination, Report report,
    bool guideRequested = false)
{
    const DWORD ownThread = GetCurrentThreadId();
    DWORD owner = 0;
    if (GetWindowThreadProcessId(destination, &owner) != ownThread || owner != GetCurrentProcessId()
        || GetForegroundWindow() != source || !SameGame(game, pid, birth) || !GameWindow(source, pid)
        || GetForegroundWindow() != source)
    {
        report(8, 0x10000 | ERROR_ACCESS_DENIED);
        return FocusTransfer::Refused;
    }
    if (SetForegroundWindow(destination) && ConfirmCompanionFocus(source, destination))
    {
        report(8, 0);
        return FocusTransfer::Acquired;
    }

    // A UWP game's foreground HWND belongs to its separately attested shell
    // host. Retain that exact owner rather than treating every host as the game
    // process, or attaching to an arbitrary current foreground thread.
    const DWORD sourceThread = GetWindowThreadProcessId(source, &owner);
    struct HostProcess
    {
        HANDLE process{};
        ~HostProcess() { if (process) CloseHandle(process); }
    } host;
    uint64_t hostBirth = 0;
    const bool direct = owner == pid;
    bool hosted = false;
    if (!direct && sourceThread && owner && ExpectedFrameAumid[0]
        && HasClass(source, L"ApplicationFrameWindow") && FrameIdentity(source, owner, sourceThread))
    {
        host.process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, owner);
        FILETIME created{}, exited{}, kernel{}, user{};
        if (host.process && GetProcessTimes(host.process, &created, &exited, &kernel, &user))
        {
            hostBirth = (uint64_t{created.dwHighDateTime} << 32) | created.dwLowDateTime;
            hosted = hostBirth && SameGame(host.process, owner, hostBirth);
        }
    }
    if (!direct && !hosted)
    {
        report(8, 0x50000 | ERROR_ACCESS_DENIED);
        return FocusTransfer::Refused;
    }
    const HDESK ownDesktop = GetThreadDesktop(ownThread);
    const HDESK sourceDesktop = sourceThread ? GetThreadDesktop(sourceThread) : nullptr;
    auto Current = [&]()
    {
        DWORD sourceOwner = 0, destinationOwner = 0, ownSession = 0, sourceSession = 0;
        const bool stableSource = GetWindowThreadProcessId(source, &sourceOwner) == sourceThread && sourceOwner == owner;
        const bool directOwner = direct && owner == pid;
        const bool frameOwner = hosted && SameGame(host.process, owner, hostBirth)
            && HasClass(source, L"ApplicationFrameWindow") && FrameIdentity(source, owner, sourceThread);
        return CanAttachFocusQueue({SameGame(game, pid, birth),
            ProcessIdToSessionId(GetCurrentProcessId(), &ownSession) && ProcessIdToSessionId(owner, &sourceSession)
                && ownSession == sourceSession,
            stableSource, GetForegroundWindow() == source && GameWindow(source, pid) && GetForegroundWindow() == source,
            GetWindowThreadProcessId(destination, &destinationOwner) == ownThread && destinationOwner == GetCurrentProcessId(),
            ownDesktop && sourceDesktop && ownDesktop == sourceDesktop
                && GetThreadDesktop(ownThread) == ownDesktop && GetThreadDesktop(sourceThread) == sourceDesktop,
            sourceThread && sourceThread != ownThread, directOwner, frameOwner});
    };
    if (!Current())
    {
        report(8, 0x60000 | ERROR_ACCESS_DENIED);
        return FocusTransfer::Refused;
    }
    InputQueueAttachment attachment([&](bool attach)
    { return AttachThreadInput(ownThread, sourceThread, attach ? TRUE : FALSE) != FALSE; });
    if (!attachment.Attach())
    {
        const DWORD error = GetLastError();
        report(8, 0x20000 | (error & 0xffff));
        if (TryGuideSelfActivation(guideRequested, error == ERROR_ACCESS_DENIED, Current,
            [&]() { SwitchToThisWindow(destination, FALSE); },
            [&]()
            {
                DWORD destinationOwner = 0;
                return SameGame(game, pid, birth)
                    && GetWindowThreadProcessId(destination, &destinationOwner) == ownThread
                    && destinationOwner == GetCurrentProcessId()
                    && ConfirmCompanionFocus(source, destination);
            }))
        {
            report(8, 2);
            return FocusTransfer::Acquired;
        }
        if (guideRequested && error == ERROR_ACCESS_DENIED)
            report(8, 0x70000 | ERROR_ACCESS_DENIED);
        return FocusTransfer::Refused;
    }
    const bool currentAfterAttach = Current();
    bool focusInvoked = false, focusReturned = false;
    if (currentAfterAttach)
    {
        focusInvoked = true;
        focusReturned = SetForegroundWindow(destination) != FALSE;
    }
    auto foregroundClass = [&]()
    {
        const HWND foreground = GetForegroundWindow();
        DWORD foregroundOwner = 0;
        const DWORD foregroundThread = foreground ? GetWindowThreadProcessId(foreground, &foregroundOwner) : 0;
        const bool stable = GetForegroundWindow() == foreground;
        return ClassifyAttachedFocusForeground(foreground != nullptr,
            stable && foreground == source && foregroundThread == sourceThread && foregroundOwner == owner,
            stable && foreground == destination && foregroundThread == ownThread && foregroundOwner == GetCurrentProcessId());
    };
    const auto beforeDetach = foregroundClass();
    if (!attachment.Detach())
    {
        report(8, 0x40000 | (GetLastError() & 0xffff));
        return FocusTransfer::DetachFailed;
    }
    const auto afterDetach = foregroundClass();
    // Emit only after successful detach. No managed logging while queues are attached.
    report(29, static_cast<int32_t>(PackAttachedFocusDiagnostic(currentAfterAttach, focusInvoked, focusReturned,
        beforeDetach, afterDetach)));
    if (ConfirmCompanionFocus(source, destination))
    {
        report(8, 1);
        return FocusTransfer::Acquired;
    }
    report(8, 0x30000 | ERROR_ACCESS_DENIED);
    return FocusTransfer::Refused;
}

class SteamMainWindow
{
public:
    SteamMainWindow(const wchar_t* client, const wchar_t* browser) : clientPath(client), browserPath(browser) { }
    ~SteamMainWindow() { Reset(); }

    bool Capture()
    {
        Reset();
        return (clientPath || browserPath) && EnumWindows(Collect, reinterpret_cast<LPARAM>(this)) && window;
    }

    bool IsCurrent(HWND candidate) const
    {
        DWORD currentPid = 0;
        return candidate && candidate == window && IsMainWindow(candidate)
            && GetWindowThreadProcessId(candidate, &currentPid) == thread && currentPid == pid
            && SameGame(process, pid, birth);
    }

    HWND Window() const { return window; }

private:
    static bool IsMainWindow(HWND candidate)
    {
        DWORD cloaked = 0;
        return IsWindowVisible(candidate) && !IsIconic(candidate) && GetAncestor(candidate, GA_ROOT) == candidate
            && !GetWindow(candidate, GW_OWNER) && HasClass(candidate, L"SDL_app")
            && !(GetWindowLongPtrW(candidate, GWL_EXSTYLE) & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE))
            && SUCCEEDED(DwmGetWindowAttribute(candidate, DWMWA_CLOAKED, &cloaked, sizeof(cloaked))) && !cloaked;
    }

    static BOOL CALLBACK Collect(HWND candidate, LPARAM context)
    {
        if (!IsMainWindow(candidate)) return TRUE;
        auto& self = *reinterpret_cast<SteamMainWindow*>(context);
        DWORD owner = 0, ownSession = 0, candidateSession = 0;
        const DWORD ownerThread = GetWindowThreadProcessId(candidate, &owner);
        if (!ownerThread || !ProcessIdToSessionId(owner, &candidateSession)
            || !ProcessIdToSessionId(GetCurrentProcessId(), &ownSession) || candidateSession != ownSession) return TRUE;
        HANDLE retained = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, owner);
        if (!retained) return TRUE;
        wchar_t image[32768]{};
        DWORD length = _countof(image);
        FILETIME created{}, exited{}, kernel{}, user{};
        if (!QueryFullProcessImageNameW(retained, 0, image, &length)
            || !((self.clientPath && _wcsicmp(image, self.clientPath) == 0)
                || (self.browserPath && _wcsicmp(image, self.browserPath) == 0))
            || !GetProcessTimes(retained, &created, &exited, &kernel, &user))
        {
            CloseHandle(retained);
            return TRUE;
        }
        if (self.window)
        {
            CloseHandle(retained);
            self.Reset();
            return FALSE;
        }
        self.window = candidate;
        self.pid = owner;
        self.thread = ownerThread;
        self.process = retained;
        self.birth = (uint64_t{created.dwHighDateTime} << 32) | created.dwLowDateTime;
        return TRUE;
    }

    void Reset()
    {
        if (process) CloseHandle(process);
        process = nullptr;
        window = nullptr;
    }

    const wchar_t* clientPath;
    const wchar_t* browserPath;
    HWND window{};
    HANDLE process{};
    DWORD pid{}, thread{};
    uint64_t birth{};
};

enum class GuideRecovery { None, WaitingForEvent, Recovered };

class GuideForegroundRecovery
{
public:
    GuideForegroundRecovery(HWND companion, const wchar_t* client, const wchar_t* browser)
        : main(client, browser), companionWindow(companion)
    {
        if (current || (!client && !browser)) return;
        current = this;
        hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, nullptr, ForegroundEvent,
            0, 0, WINEVENT_OUTOFCONTEXT);
        if (!hook) current = nullptr;
    }

    ~GuideForegroundRecovery()
    {
        if (!hook) return;
        current = nullptr;
        UnhookWinEvent(hook);
    }

    void Begin(HWND source)
    {
        Cancel();
        if (!hook || !source || !main.Capture() || GetForegroundWindow() != source) return;
        sourceGame = source;
        armedAt = GetTickCount();
        policy.Arm(GetTickCount64());
    }

    void CompanionAcquired() { policy.ForegroundChanged(GuideFocusTarget::Companion); }
    void Cancel() { policy.Cancel(); waitUntil = 0; }

    void ObserveOpen(bool open)
    {
        const auto now = GetTickCount64();
        if (!policy.WithinGuide(now) || (policy.OpenObserved() && (!open || !policy.InRecoveryWindow(now))))
        {
            Cancel();
            return;
        }
        if (!open || policy.OpenObserved() || !OwnForeground(companionWindow)) return;
        LASTINPUTINFO info{sizeof(info)};
        if (!GetLastInputInfo(&info)) { Cancel(); return; }
        if (policy.OverlayOpened(now)) lastInput = info.dwTime;
    }

    GuideRecovery Restore(HANDLE game, DWORD pid, uint64_t birth, HWND source, HWND active,
        const ValveOverlay& valve, const Surface& surface, Report report)
    {
        const auto now = GetTickCount64();
        if (!policy.InRecoveryWindow(now) || !main.IsCurrent(active) || !valve.Open()
            || source != sourceGame || !SameGame(game, pid, birth)
            || !(GameWindow(source, pid) && CanAttemptCompanion(source) || surface.SnapshotMatches(source, pid))
            || !UnchangedInput() || GetForegroundWindow() != active)
        {
            Cancel();
            return GuideRecovery::None;
        }
        if (!policy.SteamHopObserved())
        {
            // Out-of-context WinEvents are queued. Leave at most one short
            // window for the ordered exact foreground event before giving up.
            if (!waitUntil) waitUntil = now + 24;
            if (now < waitUntil) return GuideRecovery::WaitingForEvent;
            Cancel();
            return GuideRecovery::None;
        }
        if (!policy.Take(GetTickCount64(), main.IsCurrent(active) && GetForegroundWindow() == active,
            UnchangedInput(), valve.Open())) return GuideRecovery::None;
        if (GetForegroundWindow() != active) return GuideRecovery::None;
        const bool restored = SetForegroundWindow(companionWindow) && ConfirmCompanionFocus(active, companionWindow);
        report(16, restored ? ERROR_SUCCESS : ERROR_ACCESS_DENIED);
        return restored ? GuideRecovery::Recovered : GuideRecovery::None;
    }

private:
    static void CALLBACK ForegroundEvent(HWINEVENTHOOK, DWORD, HWND window, LONG, LONG, DWORD, DWORD occurred)
    {
        auto* self = current;
        if (!self || !self->policy.Armed() || static_cast<int32_t>(occurred - self->armedAt) < 0) return;
        GuideFocusTarget target = window == self->companionWindow ? GuideFocusTarget::Companion :
            window == self->sourceGame ? GuideFocusTarget::Game :
            window == self->main.Window() ? GuideFocusTarget::SteamMain : GuideFocusTarget::Other;
        self->policy.ForegroundChanged(target);
    }

    bool UnchangedInput() const
    {
        for (int key : {VK_MENU, VK_LWIN, VK_RWIN, VK_CONTROL, VK_SHIFT, VK_TAB,
            VK_LBUTTON, VK_RBUTTON, VK_MBUTTON, VK_XBUTTON1, VK_XBUTTON2})
            if (GetAsyncKeyState(key) & 0x8000) return false;
        LASTINPUTINFO info{sizeof(info)};
        return GetLastInputInfo(&info) && info.dwTime == lastInput;
    }

    static thread_local GuideForegroundRecovery* current;
    SteamMainWindow main;
    HWND companionWindow, sourceGame{};
    HWINEVENTHOOK hook{};
    GuideFocusRecoveryPolicy policy;
    DWORD armedAt{}, lastInput{};
    ULONGLONG waitUntil{};
};

thread_local GuideForegroundRecovery* GuideForegroundRecovery::current = nullptr;

class Replay
{
public:
    bool Pending() const { return deadline != 0; }

    bool Start()
    {
        if (failedRelease) return false;
        // A cold Valve browser installs its window hook during these presents.
        // Prepare only after the user's request; idle games keep their focus.
        frames = prepared ? 4 : 10;
        sent = false;
        // The first Valve browser initializes after the first visible presents.
        // Retain the exact user-requested surface while that bounded startup
        // finishes; subsequent replays use the shorter warm deadline.
        deadline = GetTickCount64() + (prepared ? 2000 : 4000);
        return true;
    }

    DWORD Cancel()
    {
        deadline = 0;
        return Release();
    }

    DWORD AfterDraw(HANDLE game, HWND ownedWindow, HWND originalGame, DWORD pid, uint64_t birth, bool retainedSnapshot)
    {
        if (!Pending()) return ERROR_SUCCESS;
        const auto now = GetTickCount64();
        if (keyUp && now >= keyUp)
        {
            const DWORD released = Release();
            if (released != ERROR_SUCCESS) { deadline = 0; return released; }
        }
        if (now >= deadline)
        {
            const DWORD released = Cancel();
            return released == ERROR_SUCCESS ? WAIT_TIMEOUT : released;
        }
        if (frames > 0) --frames;
        if (!sent && frames == 0)
        {
            // Don't release a modifier that was actually pressed by the user.
            if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) || (GetAsyncKeyState(VK_TAB) & 0x8000))
                return ERROR_SUCCESS;
            if (!SameGame(game, pid, birth) || (!GameWindow(originalGame, pid) && !retainedSnapshot) || !OwnForeground(ownedWindow))
            {
                Cancel();
                return ERROR_ACCESS_DENIED;
            }
            INPUT chord[]{KeyboardInput(VK_LSHIFT, false), KeyboardInput(VK_TAB, false)};
            const UINT accepted = SendInput(_countof(chord), chord, sizeof(INPUT));
            shiftDown = accepted >= 1;
            tabDown = accepted == 2;
            sent = true;
            prepared = accepted == 2;
            keyUp = now + 500;
            if (accepted != 2)
            {
                Cancel();
                return ERROR_ACCESS_DENIED;
            }
        }
        return ERROR_SUCCESS;
    }

private:
    DWORD Release()
    {
        if (failedRelease) return ERROR_SUCCESS;
        // Release exactly the events successfully sent, even on close or lost focus.
        for (int attempt = 0; attempt < 2 && (tabDown || shiftDown); ++attempt)
        {
            INPUT events[2]{};
            UINT count = 0;
            if (tabDown) events[count++] = KeyboardInput(VK_TAB, true);
            if (shiftDown) events[count++] = KeyboardInput(VK_LSHIFT, true);
            UINT accepted = SendInput(count, events, sizeof(INPUT));
            if (tabDown && accepted) { tabDown = false; --accepted; }
            if (shiftDown && accepted) shiftDown = false;
        }
        keyUp = 0;
        failedRelease = tabDown || shiftDown;
        return failedRelease ? ERROR_ACCESS_DENIED : ERROR_SUCCESS;
    }

    int frames = 0;
    bool sent = false, prepared = false, shiftDown = false, tabDown = false, failedRelease = false;
    ULONGLONG deadline = 0, keyUp = 0;
};

extern "C" __declspec(dllexport) int32_t WINAPI RunSteamCompanion(HANDLE game, HANDLE owner,
    DWORD pid, uint64_t birth, const wchar_t* exactRenderer, Report report, TakeGuideRequest takeGuideRequest,
    const wchar_t* steamClient, const wchar_t* steamBrowser, const wchar_t* expectedFrameAumid)
{
    if (!report || !takeGuideRequest || !exactRenderer || !SameGame(game, pid, birth)) return ERROR_INVALID_PARAMETER;
    FrameAumidScope application(expectedFrameAumid);
    if (!application) return ERROR_INVALID_PARAMETER;
    auto valve = ValveOverlay::Find(game, owner, exactRenderer);
    if (!valve) return ERROR_MOD_NOT_FOUND;
    ComApartment apartment;
    if (FAILED(apartment.result) && apartment.result != RPC_E_CHANGED_MODE) return apartment.result;
    SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
#if defined(PLAYHUB_COMPANION_PUBLIC_CONTEXT_TRIAL)
    struct PublicSessionProof { HANDLE game, owner; DWORD pid; uint64_t birth; } publicProof{game, owner, pid, birth};
    // Declared before Surface, so it still outlives Surface destruction.
    std::unique_ptr<PublicSteamContext> publicContext;
#endif
    Surface surface;
    ActivationButton activation;
    HRESULT result = surface.Create(valve.mouse, report);
    if (FAILED(result)) return result;
#if defined(PLAYHUB_COMPANION_PUBLIC_CONTEXT_TRIAL)
    PlayhubPublicSteam::FirstPresentReadGate publicReadGate;
    if (!publicReadGate.AfterCreationPresent(static_cast<int32_t>(result), [&]()
    {
        publicContext = PublicSteamContext::Read(exactRenderer, [](void* value)
        {
            auto& proof = *static_cast<PublicSessionProof*>(value);
            return SameGame(proof.game, proof.pid, proof.birth) && WaitForSingleObject(proof.owner, 0) == WAIT_TIMEOUT;
        }, &publicProof, report);
    })) report(28, static_cast<int32_t>(PlayhubPublicSteam::ReadOutcome::FirstPresentNotReady));
#endif
    GuideForegroundRecovery guideRecovery(surface.window, steamClient, steamBrowser);
    report(1, 0);

    HANDLE processes[]{game, owner};
    HWND lastGame = nullptr;
    bool hotkey = false, attempted = false, exposed = false, gameForeground = false;
    int lastOverlayScope = 0;
    Replay replay;
    FrameCache frame;
    HWND lastValidFrame = nullptr;
    DWORD lastValidFrameOwner = 0, lastValidFrameThread = 0;
    ULONGLONG lastValidFrameAt = 0;
    HWND warmupGame = nullptr;
    unsigned warmupFrames = 0;
    ULONGLONG warmupUntil = 0;
    ULONGLONG restoreUntil = 0;
    bool retainedIconicReported = false;
#if defined(PLAYHUB_COMPANION_GUIDE_WARMUP) && !defined(PLAYHUB_COMPANION_FULLSCREEN_SNAPSHOT)
    bool triedWarmup = false;
#else
    bool triedWarmup = true;
#endif
    while (SameGame(game, pid, birth) && IsWindow(surface.window))
    {
        DWORD signaled = MsgWaitForMultipleObjectsEx(_countof(processes), processes,
            surface.shown ? 8 : 33, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
        if (signaled == WAIT_OBJECT_0 || signaled == WAIT_OBJECT_0 + 1) break;
        if (signaled == WAIT_FAILED) { result = HRESULT_FROM_WIN32(GetLastError()); break; }
        bool pressed = false;
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE))
        {
            if (message.message == WM_HOTKEY && message.hwnd == surface.window && message.wParam == HotkeyId)
            {
                pressed = true;
                break;
            }
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }

        HWND active = GetForegroundWindow();
        Foreground front = active == surface.window ? Foreground::Companion :
            GameWindow(active, pid, &frame) ? Foreground::Game : Foreground::Other;
        if (front == Foreground::Game) lastGame = active;
        bool overlayOpen = valve.Open();
        const bool guideRequested = takeGuideRequest() && front == Foreground::Game;
        if (guideRequested)
        {
            pressed = true;
            report(13, 0);
        }
        if (activation.Visible())
        {
            HWND source = activation.Source();
            if (activation.Clicked() && activation.OwnsFocus() && SameGame(game, pid, birth) && GameWindow(source, pid))
            {
                HRESULT preparation = surface.Align(source);
                if (SUCCEEDED(preparation) && activation.OwnsFocus()
                    && SetForegroundWindow(surface.window) && OwnForeground(surface.window))
                {
                    lastGame = source;
                    front = Foreground::Companion;
                    if (!replay.Start()) report(5, ERROR_ACCESS_DENIED);
                    else if (CanAttemptCompanion(source)) report(12, 0);
                    report(10, 1);
                }
                else
                {
                    if (FAILED(preparation)) report(7, preparation);
                    report(10, ERROR_ACCESS_DENIED);
                    if (activation.OwnsFocus() && GameWindow(source, pid) && activation.OwnsFocus())
                        SetForegroundWindow(source);
                }
                activation.Hide();
            }
            else if (activation.Expired() || overlayOpen
                || (!activation.OwnsFocus() && active != source) || !GameWindow(source, pid, &frame))
            {
                if (activation.OwnsFocus() && GameWindow(source, pid) && activation.OwnsFocus())
                    SetForegroundWindow(source);
                activation.Hide();
            }
        }
        if (warmupFrames && (front != Foreground::Companion || overlayOpen || !GameWindow(warmupGame, pid, &frame)))
            warmupFrames = 0;
        const bool composited = CanAttemptCompanion(lastGame);
        if (!triedWarmup && !pressed && !overlayOpen && front == Foreground::Game
            && composited && GameWindow(lastGame, pid) && GetForegroundWindow() == lastGame)
        {
            triedWarmup = true;
            // Position and clear the surface while hidden so its first visible buffer is transparent.
            HRESULT preparation = surface.Align(lastGame, false);
            if (SUCCEEDED(preparation)) preparation = surface.Draw();
            if (SUCCEEDED(preparation) && GetForegroundWindow() == lastGame)
                preparation = surface.Align(lastGame);
            FocusTransfer transfer = FocusTransfer::Refused;
            if (SUCCEEDED(preparation))
                transfer = FocusFromGame(game, pid, birth, lastGame, surface.window, report);
            else
                report(7, preparation);
            if (transfer == FocusTransfer::DetachFailed)
            {
                result = HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED);
                break;
            }
            if (transfer == FocusTransfer::Acquired)
            {
                warmupGame = lastGame;
                warmupFrames = 10;
                warmupUntil = GetTickCount64() + 350;
                front = Foreground::Companion;
            }
            else { surface.Hide(); report(6, ERROR_ACCESS_DENIED); }
        }
        const bool allowHotkey = composited && AllowHotkey(front, overlayOpen);
        if (allowHotkey && !attempted)
        {
            hotkey = RegisterHotKey(surface.window, HotkeyId, MOD_SHIFT | MOD_NOREPEAT, VK_TAB) != FALSE;
            attempted = true;
            if (!hotkey) report(5, GetLastError());
        }
        else if (!allowHotkey)
        {
            if (hotkey) UnregisterHotKey(surface.window, HotkeyId);
            hotkey = attempted = false;
        }
        if (AllowOverlayRequest(front, overlayOpen, guideRequested, pressed, composited)
            && GameWindow(lastGame, pid) && GetForegroundWindow() == lastGame)
        {
            if (guideRequested) guideRecovery.Begin(lastGame);
            else guideRecovery.Cancel();
            if (hotkey) UnregisterHotKey(surface.window, HotkeyId);
            hotkey = attempted = false;
            restoreUntil = 0;
            HRESULT preparation = PrepareRequestedSurface(surface, game, pid, birth, lastGame, report);
            FocusTransfer transfer = FocusTransfer::Refused;
            if (SUCCEEDED(preparation))
            {
                transfer = FocusFromGame(game, pid, birth, lastGame, surface.window, report, guideRequested);
            }
            else
                report(7, preparation);
            if (transfer == FocusTransfer::DetachFailed)
            {
                result = HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED);
                break;
            }
            if (transfer == FocusTransfer::Acquired)
            {
                activation.Hide();
                const bool alreadyOpen = valve.Open();
                DWORD released = ERROR_SUCCESS;
                const bool ready = PrepareOverlayActivation(alreadyOpen,
                    [&]()
                    {
                        return OwnForeground(surface.window) && SameGame(game, pid, birth)
                            && ((GameWindow(lastGame, pid) && CanAttemptCompanion(lastGame))
                                || RetainedSnapshotSource(surface, game, pid, birth, lastGame))
                            && OwnForeground(surface.window);
                    },
                    [&]() { released = replay.Cancel(); return released == ERROR_SUCCESS; },
                    [&]() { return replay.Start(); });
                overlayOpen = valve.Open();
                if (!ready)
                {
                    guideRecovery.Cancel();
                    report(5, released != ERROR_SUCCESS ? released : ERROR_ACCESS_DENIED);
                }
                else
                {
                    if (guideRequested) guideRecovery.CompanionAcquired();
                    report(12, 0);
                }
                front = Foreground::Companion;
            }
            else
            {
                guideRecovery.Cancel();
                if (GetForegroundWindow() == lastGame && GameWindow(lastGame, pid)
                    && GetForegroundWindow() == lastGame && CanAttemptCompanion(lastGame))
                {
                    surface.Hide();
                    if (!FullscreenSnapshotTrial && activation.Show(lastGame)) report(10, 0);
                }
                else if (FullscreenSnapshotTrial && OwnForeground(surface.window))
                {
                    RestoreSnapshotGame(surface, game, pid, birth, lastGame);
                    if (GetForegroundWindow() != surface.window) surface.Hide();
                }
            }
        }
        if (overlayOpen || front == Foreground::Other)
        {
            DWORD released = replay.Cancel();
            if (released != ERROR_SUCCESS)
            {
                guideRecovery.Cancel();
                report(5, released);
            }
        }
        guideRecovery.ObserveOpen(overlayOpen);
        if (front == Foreground::Other && overlayOpen)
        {
            const auto recovery = guideRecovery.Restore(game, pid, birth, lastGame, active, valve, surface, report);
            if (recovery == GuideRecovery::WaitingForEvent) continue;
            if (recovery == GuideRecovery::Recovered) front = Foreground::Companion;
        }
        const bool retainedSource = RetainedSnapshotSource(surface, game, pid, birth, lastGame);
        if (retainedSource != retainedIconicReported)
        {
            report(18, retainedSource ? 1 : 0);
            retainedIconicReported = retainedSource;
        }
        const bool wantsSurface = ShowCompanion(front, overlayOpen, replay.Pending() || warmupFrames != 0)
            && (!FullscreenSnapshotTrial || surface.HasSnapshot());
        const bool validFrame = wantsSurface && (GameWindow(lastGame, pid, &frame) || retainedSource);
        bool keepExistingSurface = false;
        if (validFrame)
        {
            lastValidFrame = lastGame;
            lastValidFrameThread = GetWindowThreadProcessId(lastGame, &lastValidFrameOwner);
            lastValidFrameAt = GetTickCount64();
        }
        else if (KeepOwnedOverlayForFrame(front, overlayOpen, surface.shown,
            lastValidFrameThread != 0 && lastValidFrame == lastGame, GetTickCount64() - lastValidFrameAt))
        {
            DWORD currentOwner = 0;
            keepExistingSurface = SameGame(game, pid, birth)
                && GetWindowThreadProcessId(lastGame, &currentOwner) == lastValidFrameThread
                && currentOwner == lastValidFrameOwner && OwnForeground(surface.window);
        }
        int hideDetail = 0;
        if (validFrame)
        {
            result = surface.Align(lastGame);
            if (FAILED(result)) break;
            if (front == Foreground::Game && GetForegroundWindow() == lastGame && GameWindow(lastGame, pid))
                SetForegroundWindow(surface.window);
            result = surface.Draw();
            if (FAILED(result)) break;
            DWORD replayStatus = replay.AfterDraw(game, surface.window, lastGame, pid, birth,
                RetainedSnapshotSource(surface, game, pid, birth, lastGame));
            if (replayStatus != ERROR_SUCCESS) report(5, replayStatus);
            if (warmupFrames && (--warmupFrames == 0 || GetTickCount64() >= warmupUntil))
            {
                warmupFrames = 0;
                bool returned = OwnForeground(surface.window) && GameWindow(warmupGame, pid)
                    && OwnForeground(surface.window) && SetForegroundWindow(warmupGame)
                    && GetForegroundWindow() == warmupGame;
                surface.Hide();
                report(6, returned ? ERROR_SUCCESS : ERROR_ACCESS_DENIED);
            }
        }
        else if (keepExistingSurface && OwnForeground(surface.window))
        {
            // Preserve the last verified geometry during a short hosted-frame
            // transition. Do not align, restore or claim an input scope here.
            result = surface.Draw();
            if (FAILED(result)) break;
        }
        else
        {
            bool holdForRestore = false;
            if (RestoreGame(front, surface.shown, overlayOpen, replay.Pending()) && GetForegroundWindow() == surface.window)
            {
                if (FullscreenSnapshotTrial && surface.HasSnapshot())
                {
                    if (!restoreUntil) restoreUntil = GetTickCount64() + 1500;
                    bool restored = RestoreSnapshotGame(surface, game, pid, birth, lastGame);
                    holdForRestore = !restored && OwnForeground(surface.window) && SameGame(game, pid, birth)
                        && surface.SnapshotMatches(lastGame, pid) && GetTickCount64() < restoreUntil;
                    if (restored || !holdForRestore) report(19, restored ? ERROR_SUCCESS : ERROR_ACCESS_DENIED);
                }
                else if (GameWindow(lastGame, pid)) SetForegroundWindow(lastGame);
            }
            if (holdForRestore)
            {
                // Keep the captured game frame until the exact source really
                // owns foreground again. Alt-Tab cancels this on the next frame.
                result = surface.Draw();
                if (FAILED(result)) break;
            }
            else
            {
                if (overlayOpen && surface.shown && exposed)
                {
                    HWND before = GetForegroundWindow();
                    DWORD beforePid = 0, afterPid = 0;
                    GetWindowThreadProcessId(before, &beforePid);
                    const unsigned beforeClass = ForegroundForHideLog(before, surface.window, lastGame);
                    surface.Hide();
                    HWND after = GetForegroundWindow();
                    GetWindowThreadProcessId(after, &afterPid);
                    const unsigned afterClass = ForegroundForHideLog(after, surface.window, lastGame);
                    hideDetail = beforeClass | (afterClass << 4) | (wantsSurface ? 0x100 : 0);
                    report(14, static_cast<int32_t>(beforePid));
                    report(15, static_cast<int32_t>(afterPid));
                }
                else surface.Hide();
                // BOverlayNeedsPresent is an edge. Query once, then service it immediately.
                if (valve.needsFrame())
                {
                    result = surface.Draw();
                    if (FAILED(result)) break;
                }
            }
        }
        bool visible = overlayOpen && surface.shown;
        if (visible != exposed)
        {
            report(visible ? 2 : overlayOpen ? 4 : 3, hideDetail);
            exposed = visible;
        }
        HWND foreground = GetForegroundWindow();
        bool verifiedGameForeground = foreground == lastGame && GameWindow(foreground, pid, &frame);
        const bool retainedForScope = RetainedSnapshotSource(surface, game, pid, birth, lastGame);
        const bool ownSurfaceForeground = foreground == surface.window && (GameWindow(lastGame, pid, &frame) || retainedForScope);
        int overlayScope = (verifiedGameForeground || ownSurfaceForeground) && (CanAttemptCompanion(lastGame) || retainedForScope)
            ? verifiedGameForeground ? 1 : 2 : 0;
        if (overlayScope != lastOverlayScope)
        {
            report(11, overlayScope);
            lastOverlayScope = overlayScope;
        }
        if (verifiedGameForeground != gameForeground)
        {
            gameForeground = verifiedGameForeground;
            report(9, gameForeground ? 1 : 0);
        }
    }
    DWORD released = replay.Cancel();
    if (released != ERROR_SUCCESS) report(5, released);
    if (hotkey) UnregisterHotKey(surface.window, HotkeyId);
    if (activation.OwnsFocus() && SameGame(game, pid, birth) && GameWindow(activation.Source(), pid)
        && activation.OwnsFocus())
        SetForegroundWindow(activation.Source());
    activation.Hide();
    if (SameGame(game, pid, birth) && GetForegroundWindow() == surface.window)
    {
        if (FullscreenSnapshotTrial && surface.HasSnapshot()) RestoreSnapshotGame(surface, game, pid, birth, lastGame);
        else if (GameWindow(lastGame, pid)) SetForegroundWindow(lastGame);
    }
    if (exposed) report(3, 0);
    if (lastOverlayScope) report(11, 0);
    if (gameForeground) report(9, 0);
    return FAILED(result) ? result : 0;
}
