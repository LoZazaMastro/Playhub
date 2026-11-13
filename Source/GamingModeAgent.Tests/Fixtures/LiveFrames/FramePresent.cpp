#include <windows.h>
#include <d3d11.h>
#include <wrl/client.h>
#include <iostream>
#include <string>
using Microsoft::WRL::ComPtr;

int main()
{
    const HINSTANCE instance = GetModuleHandleW(nullptr);
    const wchar_t name[] = L"PlayhubOwnedFrameFixture";
    WNDCLASSW cls{};
    cls.hInstance = instance; cls.lpszClassName = name; cls.lpfnWndProc = DefWindowProcW;
    if (!RegisterClassW(&cls)) return 1;
    // The fixture never shows or activates this window and never captures the desktop.
    HWND window = CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, name, name,
        WS_POPUP | WS_DISABLED, 0, 0, 64, 64, nullptr, nullptr, instance, nullptr);
    if (!window) return 2;
    DXGI_SWAP_CHAIN_DESC desc{};
    desc.BufferDesc.Width = desc.BufferDesc.Height = 64;
    desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1; desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 2; desc.OutputWindow = window; desc.Windowed = TRUE;
    ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> context; ComPtr<IDXGISwapChain> swap;
    HRESULT result = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0,
        nullptr, 0, D3D11_SDK_VERSION, &desc, &swap, &device, nullptr, &context);
    if (FAILED(result)) return 3;
    ComPtr<ID3D11Texture2D> buffer; ComPtr<ID3D11RenderTargetView> view;
    if (FAILED(swap->GetBuffer(0, IID_PPV_ARGS(&buffer))) || FAILED(device->CreateRenderTargetView(buffer.Get(), nullptr, &view))) return 4;
    FILETIME created{}, exited{}, kernel{}, user{};
    GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user);
    std::cout << "READY " << GetCurrentProcessId() << " "
        << ((uint64_t{created.dwHighDateTime} << 32) | created.dwLowDateTime) << std::endl;
    std::string command;
    if (!std::getline(std::cin, command) || command != "go") return 5;
    const ULONGLONG start = GetTickCount64();
    unsigned presents = 0, occluded = 0;
    while (GetTickCount64() - start < 5000)
    {
        float color[4]{presents % 2 ? 0.25f : 0.75f, 0.1f, 0.5f, 1.0f};
        context->ClearRenderTargetView(view.Get(), color);
        result = swap->Present(0, 0);
        if (FAILED(result)) break;
        if (result == DXGI_STATUS_OCCLUDED) ++occluded;
        ++presents;
        Sleep(8);
    }
    std::cout << "RESULT presents=" << presents << " occluded=" << occluded << " elapsed=" << GetTickCount64() - start << std::endl;
    context->ClearState(); context->Flush(); view.Reset(); buffer.Reset(); swap.Reset(); context.Reset(); device.Reset();
    DestroyWindow(window); UnregisterClassW(name, instance);
    return FAILED(result) ? 6 : 0;
}
