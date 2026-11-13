// Playhub Xbox Session. Distributed under GPL-3.0-only.
#include "SnapshotBlit.h"
#include <cstdint>
#include <vector>

using Microsoft::WRL::ComPtr;

int main()
{
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    HRESULT result = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
        nullptr, 0, D3D11_SDK_VERSION, device.GetAddressOf(), nullptr, context.GetAddressOf());
    if (FAILED(result)) return 1;
    constexpr UINT sourceWidth = 1920, sourceHeight = 1080, width = 3840, height = 2160;
    std::vector<uint32_t> pixels(sourceWidth * sourceHeight);
    for (UINT y = 0; y < sourceHeight; y++) for (UINT x = 0; x < sourceWidth; x++)
        pixels[y * sourceWidth + x] = y < sourceHeight / 2
            ? x < sourceWidth / 2 ? 0xffff0000u : 0xff00ff00u
            : x < sourceWidth / 2 ? 0xff0000ffu : 0xffffff00u;
    D3D11_TEXTURE2D_DESC description{};
    description.Width = sourceWidth;
    description.Height = sourceHeight;
    description.MipLevels = description.ArraySize = 1;
    description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    description.SampleDesc.Count = 1;
    description.Usage = D3D11_USAGE_IMMUTABLE;
    description.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    D3D11_SUBRESOURCE_DATA initial{pixels.data(), sourceWidth * sizeof(uint32_t), 0};
    ComPtr<ID3D11Texture2D> source, destination, readable;
    ComPtr<ID3D11ShaderResourceView> sourceView;
    ComPtr<ID3D11RenderTargetView> destinationView;
    if (FAILED(device->CreateTexture2D(&description, &initial, source.GetAddressOf()))
        || FAILED(device->CreateShaderResourceView(source.Get(), nullptr, sourceView.GetAddressOf()))) return 2;
    description.Width = width;
    description.Height = height;
    description.Usage = D3D11_USAGE_DEFAULT;
    description.BindFlags = D3D11_BIND_RENDER_TARGET;
    if (FAILED(device->CreateTexture2D(&description, nullptr, destination.GetAddressOf()))
        || FAILED(device->CreateRenderTargetView(destination.Get(), nullptr, destinationView.GetAddressOf()))) return 3;
    SnapshotBlit scaler;
    if (FAILED(scaler.Initialize(device.Get())) || FAILED(scaler.Draw(device.Get(), context.Get(), destinationView.Get(), sourceView.Get(), width, height))) return 4;
    // A second draw uses the same shader/sampler cache, with no window or swapchain.
    if (FAILED(scaler.Draw(device.Get(), context.Get(), destinationView.Get(), sourceView.Get(), width, height))) return 5;
    description.Usage = D3D11_USAGE_STAGING;
    description.BindFlags = 0;
    description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    if (FAILED(device->CreateTexture2D(&description, nullptr, readable.GetAddressOf()))) return 6;
    context->OMSetRenderTargets(0, nullptr, nullptr);
    context->CopyResource(readable.Get(), destination.Get());
    D3D11_MAPPED_SUBRESOURCE mapped{};
    if (FAILED(context->Map(readable.Get(), 0, D3D11_MAP_READ, 0, &mapped))) return 7;
    auto at = [&](UINT x, UINT y) { return reinterpret_cast<const uint32_t*>(static_cast<const uint8_t*>(mapped.pData) + y * mapped.RowPitch)[x]; };
    bool correct = at(32, 32) == 0xffff0000u && at(width - 33, 32) == 0xff00ff00u
        && at(32, height - 33) == 0xff0000ffu && at(width - 33, height - 33) == 0xffffff00u
        && (at(0, 0) >> 24) == 0xff && (at(width - 1, height - 1) >> 24) == 0xff;
    context->Unmap(readable.Get(), 0);
    scaler.Reset();
    return correct ? 0 : 8;
}
