// Playhub Xbox Session. Distributed under GPL-3.0-only.
#pragma once
#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <wrl/client.h>

// The game's pre-switch frame may differ from the desktop's restored monitor
// resolution. Scale only the owned snapshot; never resize the game's swapchain.
class SnapshotBlit
{
public:
    HRESULT Initialize(ID3D11Device* device) { return device ? Prepare(device) : E_INVALIDARG; }

    HRESULT Draw(ID3D11Device* device, ID3D11DeviceContext* context, ID3D11RenderTargetView* destination,
        ID3D11ShaderResourceView* source, UINT width, UINT height)
    {
        if (!device || !context || !destination || !source || !width || !height) return E_INVALIDARG;
        HRESULT result = Prepare(device);
        if (FAILED(result)) return result;
        D3D11_VIEWPORT viewport{0, 0, static_cast<float>(width), static_cast<float>(height), 0, 1};
        context->RSSetViewports(1, &viewport);
        context->RSSetState(raster.Get());
        context->OMSetRenderTargets(1, &destination, nullptr);
        context->OMSetBlendState(nullptr, nullptr, 0xffffffff);
        context->OMSetDepthStencilState(depth.Get(), 0);
        context->IASetInputLayout(nullptr);
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->VSSetShader(vertex.Get(), nullptr, 0);
        context->GSSetShader(nullptr, nullptr, 0);
        context->HSSetShader(nullptr, nullptr, 0);
        context->DSSetShader(nullptr, nullptr, 0);
        context->PSSetShader(pixel.Get(), nullptr, 0);
        context->PSSetShaderResources(0, 1, &source);
        ID3D11SamplerState* sampling = sampler.Get();
        context->PSSetSamplers(0, 1, &sampling);
        context->Draw(3, 0);
        ID3D11ShaderResourceView* released = nullptr;
        context->PSSetShaderResources(0, 1, &released);
        return device->GetDeviceRemovedReason();
    }

    void Reset()
    {
        vertex.Reset(); pixel.Reset(); sampler.Reset(); raster.Reset(); depth.Reset();
    }

private:
    HRESULT Prepare(ID3D11Device* device)
    {
        if (vertex && pixel && sampler && raster && depth) return S_OK;
        Reset();
        struct SystemCompiler
        {
            HMODULE module = LoadLibraryExW(L"d3dcompiler_47.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
            ~SystemCompiler() { if (module) FreeLibrary(module); }
        } compiler;
        if (!compiler.module) return HRESULT_FROM_WIN32(GetLastError());
        auto compile = reinterpret_cast<decltype(&D3DCompile)>(GetProcAddress(compiler.module, "D3DCompile"));
        if (!compile) return HRESULT_FROM_WIN32(ERROR_PROC_NOT_FOUND);
        constexpr char shader[] =
            "struct Out { float4 position:SV_Position; float2 uv:TEXCOORD0; };"
            "Out VS(uint id:SV_VertexID) { Out o; float2 uv=float2((id<<1)&2,id&2);"
            "o.position=float4(uv*float2(2,-2)+float2(-1,1),0,1);o.uv=uv;return o;}"
            "Texture2D<float4> image:register(t0);SamplerState linearSampler:register(s0);"
            "float4 PS(Out i):SV_Target {return float4(image.Sample(linearSampler,i.uv).rgb,1);}";
        Microsoft::WRL::ComPtr<ID3DBlob> code, errors;
        HRESULT result = compile(shader, sizeof(shader) - 1, nullptr, nullptr, nullptr, "VS", "vs_4_0",
            D3DCOMPILE_ENABLE_STRICTNESS | D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, code.GetAddressOf(), errors.GetAddressOf());
        if (FAILED(result)) return result;
        result = device->CreateVertexShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, vertex.GetAddressOf());
        if (FAILED(result)) return result;
        code.Reset(); errors.Reset();
        result = compile(shader, sizeof(shader) - 1, nullptr, nullptr, nullptr, "PS", "ps_4_0",
            D3DCOMPILE_ENABLE_STRICTNESS | D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, code.GetAddressOf(), errors.GetAddressOf());
        if (FAILED(result)) return result;
        result = device->CreatePixelShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, pixel.GetAddressOf());
        if (FAILED(result)) return result;
        D3D11_SAMPLER_DESC sampling{};
        sampling.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        sampling.AddressU = sampling.AddressV = sampling.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampling.MaxLOD = D3D11_FLOAT32_MAX;
        result = device->CreateSamplerState(&sampling, sampler.GetAddressOf());
        if (FAILED(result)) return result;
        D3D11_RASTERIZER_DESC rasterization{};
        rasterization.FillMode = D3D11_FILL_SOLID;
        rasterization.CullMode = D3D11_CULL_NONE;
        rasterization.DepthClipEnable = TRUE;
        result = device->CreateRasterizerState(&rasterization, raster.GetAddressOf());
        if (FAILED(result)) return result;
        D3D11_DEPTH_STENCIL_DESC depthSettings{};
        return device->CreateDepthStencilState(&depthSettings, depth.GetAddressOf());
    }

    Microsoft::WRL::ComPtr<ID3D11VertexShader> vertex;
    Microsoft::WRL::ComPtr<ID3D11PixelShader> pixel;
    Microsoft::WRL::ComPtr<ID3D11SamplerState> sampler;
    Microsoft::WRL::ComPtr<ID3D11RasterizerState> raster;
    Microsoft::WRL::ComPtr<ID3D11DepthStencilState> depth;
};
