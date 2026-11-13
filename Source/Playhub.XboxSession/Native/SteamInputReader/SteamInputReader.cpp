// Playhub Xbox Session. Distributed under GPL-3.0-only.
#include <windows.h>
extern "C"
{
#include <hidsdi.h>
}
#include <xinput.h>
#include "HidControllerReport.h"

#include <algorithm>
#include <array>
#include <bit>
#include <cstddef>
#include <cstdio>
#include <cstdint>
#include <limits>
#include <string_view>
#include <utility>
#include <vector>

namespace
{
    constexpr std::wstring_view ValvePipe = LR"(\\.\pipe\HID#VID_045E&PID_028E&IG_00#)";
    constexpr wchar_t WindowClass[] = L"PlayhubSteamInputReader";
    constexpr UINT Failure = static_cast<UINT>(-1);

    bool Number(std::wstring_view text, unsigned radix, uint64_t& result)
    {
        if (text.empty() || text.size() > 20) return false;
        result = 0;
        for (auto ch : text)
        {
            unsigned digit = ch >= L'0' && ch <= L'9' ? ch - L'0'
                : ch >= L'a' && ch <= L'f' ? ch - L'a' + 10
                : ch >= L'A' && ch <= L'F' ? ch - L'A' + 10 : 16;
            if (digit >= radix || result > (std::numeric_limits<uint64_t>::max() - digit) / radix) return false;
            result = result * radix + digit;
        }
        return true;
    }

    struct Source
    {
        DWORD slot;
        USHORT product;
        uint64_t handle;
    };

    std::optional<std::array<uint64_t, 5>> PipeValues(std::wstring_view path)
    {
        if (path.size() <= ValvePipe.size() || path.size() > 512 ||
            CompareStringOrdinal(path.data(), static_cast<int>(ValvePipe.size()),
                ValvePipe.data(), static_cast<int>(ValvePipe.size()), TRUE) != CSTR_EQUAL) return {};
        path.remove_prefix(ValvePipe.size());
        std::array<uint64_t, 5> values{};
        constexpr std::array<wchar_t, 4> separators{L'&', L'&', L'#', L'#'};
        for (size_t index = 0; index < separators.size(); ++index)
        {
            auto separator = path.find(separators[index]);
            if (separator == std::wstring_view::npos || !Number(path.substr(0, separator), index < 3 ? 16 : 10, values[index]))
                return {};
            path.remove_prefix(separator + 1);
        }
        if (!Number(path, 10, values[4])) return {};
        return values;
    }

    bool PipeMatches(std::wstring_view path, Source source, DWORD ownPid)
    {
        auto values = PipeValues(path);
        return values && (*values)[0] == 0x28DE && (*values)[1] == source.product &&
            source.handle != 0 && (*values)[2] == source.handle && (*values)[3] == source.slot && (*values)[4] == ownPid;
    }

    bool OutputAliasesSource(const std::array<uint64_t, 5>& values, Source source, DWORD ownPid)
    {
        // A separately enumerated output (including a stale Steam record) is
        // not input to this reader. Reject it only if it claims the exact
        // physical input's slot or handle within this reader's Steam context.
        return values[0] == 0x045E && values[1] == 0x7EB0 && values[4] == ownPid
            && (values[2] == source.handle || values[3] == source.slot);
    }

    HANDLE ExactRawDevice(Source source, DWORD ownPid, bool& shadow)
    {
        shadow = false;
        UINT count = 0;
        if (GetRawInputDeviceList(nullptr, &count, sizeof(RAWINPUTDEVICELIST)) != 0 || count == 0 || count > 512)
            return nullptr;
        std::vector<RAWINPUTDEVICELIST> devices(count);
        UINT read = GetRawInputDeviceList(devices.data(), &count, sizeof(RAWINPUTDEVICELIST));
        if (read == Failure || read > devices.size()) return nullptr;
        HANDLE result = nullptr;
        unsigned claims = 0;
        for (UINT index = 0; index < read; ++index)
        {
            auto device = devices[index];
            if (device.dwType != RIM_TYPEHID) continue;
            UINT chars = 0;
            if (GetRawInputDeviceInfoW(device.hDevice, RIDI_DEVICENAME, nullptr, &chars) == Failure ||
                chars < 2 || chars > 512) continue;
            std::vector<wchar_t> name(chars);
            UINT returned = GetRawInputDeviceInfoW(device.hDevice, RIDI_DEVICENAME, name.data(), &chars);
            if (returned == Failure || returned > name.size() || name.back() != L'\0') continue;
            auto values = PipeValues(name.data());
            if (values && OutputAliasesSource(*values, source, ownPid))
                shadow = true;
            if (!PipeMatches(name.data(), source, ownPid)) continue;
            if (++claims != 1) { shadow = true; return nullptr; }
            RID_DEVICE_INFO info{};
            info.cbSize = sizeof(info);
            UINT bytes = sizeof(info);
            if (GetRawInputDeviceInfoW(device.hDevice, RIDI_DEVICEINFO, &info, &bytes) == Failure ||
                info.dwType != RIM_TYPEHID || info.hid.dwVendorId != 0x28DE || info.hid.dwProductId != 0x11FF)
            {
                shadow = true;
                return nullptr;
            }
            result = device.hDevice;
        }
        return result;
    }

    constexpr bool ConfirmedRemoval(bool notified, bool inventoryKnown, bool originalDeviceListed)
    { return notified && inventoryKnown && !originalDeviceListed; }

    bool RemovedFromRawInventory(bool notified, HANDLE originalDevice)
    {
        if (!notified || !originalDevice) return false;
        UINT count = 0;
        if (GetRawInputDeviceList(nullptr, &count, sizeof(RAWINPUTDEVICELIST)) != 0 || count > 512) return false;
        if (!count) return ConfirmedRemoval(notified, true, false);
        std::vector<RAWINPUTDEVICELIST> devices(count);
        UINT read = GetRawInputDeviceList(devices.data(), &count, sizeof(RAWINPUTDEVICELIST));
        if (read == Failure || read > devices.size()) return false;
        return ConfirmedRemoval(notified, true, std::any_of(devices.begin(), devices.begin() + read,
            [originalDevice](const auto& device) { return device.hDevice == originalDevice; }));
    }

    struct WindowScope
    {
        HANDLE parent;
        HWND game;
        DWORD thread;
        DWORD owner;

        bool Active() const
        {
            DWORD now = 0;
            return WaitForSingleObject(parent, 0) == WAIT_TIMEOUT && GetForegroundWindow() == game &&
                GetWindowThreadProcessId(game, &now) == thread && now == owner && GetForegroundWindow() == game;
        }
    };

    bool RendererMatches(std::wstring_view expected)
    {
        HMODULE renderer = GetModuleHandleW(L"GameOverlayRenderer64.dll");
        if (!renderer) return false;
        std::array<wchar_t, 1024> path{};
        DWORD length = GetModuleFileNameW(renderer, path.data(), static_cast<DWORD>(path.size()));
        return length != 0 && length < path.size() &&
            CompareStringOrdinal(path.data(), static_cast<int>(length),
                expected.data(), static_cast<int>(expected.size()), TRUE) == CSTR_EQUAL;
    }

    struct ExtendedCaps
    {
        XINPUT_CAPABILITIES base;
        USHORT vendor, product, version, reserved;
        DWORD steamSlot;
    };
    static_assert(sizeof(ExtendedCaps) == 32);
    using ReadCaps = DWORD(WINAPI*)(DWORD, DWORD, DWORD, ExtendedCaps*);

    struct DescriptorAudit
    {
        std::array<HIDP_BUTTON_CAPS, 4> buttons{};
        std::array<HIDP_VALUE_CAPS, 12> values{};
        USHORT buttonCount = 0, valueCount = 0;
        NTSTATUS buttonStatus = 0, valueStatus = 0;
        const char* result = "unread";

        void Inspect(PHIDP_PREPARSED_DATA descriptor, const HIDP_CAPS& caps)
        {
            if (caps.NumberInputButtonCaps > 0 && caps.NumberInputButtonCaps <= buttons.size())
            {
                buttonCount = caps.NumberInputButtonCaps;
                buttonStatus = HidP_GetButtonCaps(HidP_Input, buttons.data(), &buttonCount, descriptor);
                if (buttonStatus != HIDP_STATUS_SUCCESS || buttonCount > buttons.size()) buttonCount = 0;
            }
            if (caps.NumberInputValueCaps > 0 && caps.NumberInputValueCaps <= values.size())
            {
                valueCount = caps.NumberInputValueCaps;
                valueStatus = HidP_GetValueCaps(HidP_Input, values.data(), &valueCount, descriptor);
                if (valueStatus != HIDP_STATUS_SUCCESS || valueCount > values.size()) valueCount = 0;
            }
        }

        void Print(const HIDP_CAPS& caps, bool ready, bool combined, DWORD reader, Source source, bool verified) const
        {
            std::printf("{\"stage\":\"control-layout\",\"ready\":%s,\"combinedTriggers\":%s,"
                "\"readerPid\":%lu,\"sourceSlot\":%lu,\"sourceProduct\":%u,\"exactSteamSource\":%s,"
                "\"result\":\"%s\",\"reportBytes\":%u,\"dataIndices\":%u,\"buttonCaps\":%u,\"valueCaps\":%u,"
                "\"buttonStatus\":\"%08lX\",\"valueStatus\":\"%08lX\",\"buttons\":[",
                ready ? "true" : "false", combined ? "true" : "false", reader, source.slot, source.product,
                verified ? "true" : "false", result,
                caps.InputReportByteLength, caps.NumberInputDataIndices, caps.NumberInputButtonCaps,
                caps.NumberInputValueCaps, static_cast<unsigned long>(buttonStatus), static_cast<unsigned long>(valueStatus));
            // Each button entry is page, report, usage range, data range, alias.
            for (USHORT index = 0; index < buttonCount; ++index)
            {
                const auto& item = buttons[index];
                std::printf("%s[%u,%u,%u,%u,%u,%u,%u]", index ? "," : "", item.UsagePage, item.ReportID,
                    item.IsRange ? item.Range.UsageMin : item.NotRange.Usage,
                    item.IsRange ? item.Range.UsageMax : item.NotRange.Usage,
                    item.IsRange ? item.Range.DataIndexMin : item.NotRange.DataIndex,
                    item.IsRange ? item.Range.DataIndexMax : item.NotRange.DataIndex, item.IsAlias);
            }
            std::fputs("],\"values\":[", stdout);
            // Each value entry is page, usage range, data range, report, bits,
            // logical range, absolute, alias, and null support.
            for (USHORT index = 0; index < valueCount; ++index)
            {
                const auto& item = values[index];
                std::printf("%s[%u,%u,%u,%u,%u,%u,%u,%ld,%ld,%u,%u,%u]", index ? "," : "",
                    item.UsagePage, item.IsRange ? item.Range.UsageMin : item.NotRange.Usage,
                    item.IsRange ? item.Range.UsageMax : item.NotRange.Usage,
                    item.IsRange ? item.Range.DataIndexMin : item.NotRange.DataIndex,
                    item.IsRange ? item.Range.DataIndexMax : item.NotRange.DataIndex, item.ReportID, item.BitSize,
                    item.LogicalMin, item.LogicalMax, item.IsAbsolute, item.IsAlias, item.HasNull);
            }
            std::puts("]}");
            std::fflush(stdout);
        }
    };

    bool IsObservedValveDescriptor(const DescriptorAudit& audit, const HIDP_CAPS& caps)
    {
        const auto& button = audit.buttons[0];
        if (caps.InputReportByteLength != 14 || caps.NumberInputDataIndices != 16 || audit.valueCount != 6 ||
            button.ReportID != 0 || button.Range.UsageMax != 10 ||
            button.Range.DataIndexMin != 5 || button.Range.DataIndexMax != 14) return false;

        constexpr std::array<USHORT, 5> axisUsages{0x31, 0x30, 0x34, 0x33, 0x32};
        unsigned seen = 0;
        for (USHORT index = 0; index < audit.valueCount; ++index)
        {
            const auto& value = audit.values[index];
            USHORT usage = value.IsRange ? value.Range.UsageMin : value.NotRange.Usage;
            USHORT data = value.IsRange ? value.Range.DataIndexMin : value.NotRange.DataIndex;
            if (value.UsagePage != 1 || value.ReportID != 0 || !value.IsAbsolute || value.IsAlias ||
                (value.IsRange && (value.Range.UsageMax != usage || value.Range.DataIndexMax != data))) return false;
            if (data < axisUsages.size() && usage == axisUsages[data] && value.BitSize == 16 &&
                value.LogicalMin == 0 && value.LogicalMax == -1 && !value.HasNull)
                seen |= 1u << data;
            else if (data == 15 && usage == 0x39 && value.BitSize == 4 &&
                value.LogicalMin == 1 && value.LogicalMax == 8 && value.HasNull)
                seen |= 1u << axisUsages.size();
            else return false;
        }
        return seen == 0x3F;
    }

    std::optional<SteamInputReport::Layout> Describe(DescriptorAudit& audit, const HIDP_CAPS& caps)
    {
        auto reject = [&](const char* reason) -> std::optional<SteamInputReport::Layout>
        {
            audit.result = reason;
            return {};
        };
        if (caps.NumberInputButtonCaps != 1 || caps.NumberInputValueCaps < 6 || caps.NumberInputValueCaps > 7)
            return reject("cap-count");
        if (audit.buttonCount != 1 || audit.valueCount != caps.NumberInputValueCaps)
            return reject("cap-query");
        const auto& button = audit.buttons[0];
        if (button.UsagePage != 9 || !button.IsRange || button.IsAlias ||
            button.Range.UsageMin != 1 || button.Range.UsageMax < 10 || button.Range.UsageMax > 11 ||
            button.Range.DataIndexMax - button.Range.DataIndexMin != button.Range.UsageMax - button.Range.UsageMin)
            return reject("button-shape");

        SteamInputReport::Layout layout{};
        layout.reportId = button.ReportID;
        layout.valveUnsigned16 = IsObservedValveDescriptor(audit, caps);
        std::vector<USHORT> indices;
        for (size_t index = 0; index < layout.buttons.size(); ++index)
        {
            layout.buttons[index] = static_cast<USHORT>(button.Range.DataIndexMin + index);
            indices.push_back(layout.buttons[index]);
        }
        for (USHORT index = 0; index < audit.valueCount; ++index)
        {
            const auto& value = audit.values[index];
            if (value.UsagePage != 1 || value.IsAlias || !value.IsAbsolute || value.ReportID != layout.reportId ||
                (value.IsRange && (value.Range.UsageMin != value.Range.UsageMax ||
                                  value.Range.DataIndexMin != value.Range.DataIndexMax))) return reject("value-shape");
            USHORT usage = value.IsRange ? value.Range.UsageMin : value.NotRange.Usage;
            USHORT data = value.IsRange ? value.Range.DataIndexMin : value.NotRange.DataIndex;
            // The Steam-created Xbox descriptor returns the unsigned 16-bit
            // maximum sign-extended in HIDP_VALUE_CAPS.LogicalMax.
            LONG maximum = layout.valveUnsigned16 && usage != 0x39 ? 65535 : value.LogicalMax;
            SteamInputReport::Value mapped{data, value.BitSize, value.LogicalMin, maximum, value.HasNull != 0};
            if (!mapped.Valid()) return reject("value-range");
            if (std::find(indices.begin(), indices.end(), data) != indices.end()) return reject("data-overlap");
            indices.push_back(data);
            switch (usage)
            {
                case 0x30: layout.sticks[0] = mapped; break;
                case 0x31: layout.sticks[1] = mapped; break;
                case 0x33: layout.sticks[2] = mapped; break;
                case 0x34: layout.sticks[3] = mapped; break;
                case 0x39: layout.hat = mapped; break;
                case 0x32: layout.leftTrigger = mapped; break;
                case 0x35: layout.rightTrigger = mapped; break;
                default: return reject("value-usage");
            }
        }
        if (!std::all_of(layout.sticks.begin(), layout.sticks.end(), [](auto axis) { return axis.Valid(); }) ||
            !layout.hat.Valid() || layout.hat.maximum - layout.hat.minimum != 7 || !layout.leftTrigger.Valid())
            return reject("required-controls");
        layout.combinedTriggers = !layout.rightTrigger.Valid();
        if (layout.combinedTriggers && layout.leftTrigger.maximum - layout.leftTrigger.minimum < 2)
            return reject("trigger-range");
        audit.result = layout.valveUnsigned16 ? "supported-valve-unsigned16" : "supported";
        return layout;
    }

    struct Sample
    {
        WindowScope scope;
        HANDLE device;
        std::vector<BYTE> descriptor{};
        HIDP_CAPS caps{};
        std::optional<SteamInputReport::Layout> layout{};
        std::optional<SteamInputReport::Frame> prior{};
        unsigned rawReports = 0;
        unsigned validReports = 0;
        unsigned controlReports = 0;
        unsigned changes = 0;
        bool removed = false;
        bool streaming = false;

        PHIDP_PREPARSED_DATA Data() { return reinterpret_cast<PHIDP_PREPARSED_DATA>(descriptor.data()); }

        bool OpenDescriptor()
        {
            UINT bytes = 0;
            if (GetRawInputDeviceInfoW(device, RIDI_PREPARSEDDATA, nullptr, &bytes) == Failure || bytes == 0 || bytes > 16384)
                return false;
            descriptor.resize(bytes);
            return GetRawInputDeviceInfoW(device, RIDI_PREPARSEDDATA, descriptor.data(), &bytes) != Failure &&
                bytes <= descriptor.size() && HidP_GetCaps(Data(), &caps) == HIDP_STATUS_SUCCESS &&
                caps.UsagePage == 1 && (caps.Usage == 4 || caps.Usage == 5) && caps.InputReportByteLength > 0 &&
                caps.InputReportByteLength <= 256 && caps.NumberInputDataIndices > 0 && caps.NumberInputDataIndices <= 128;
        }

        void Handle(HRAWINPUT input)
        {
            if (!scope.Active() || removed) return;
            alignas(RAWINPUT) std::array<BYTE, 4096> bytes{};
            UINT size = static_cast<UINT>(bytes.size());
            UINT received = GetRawInputData(input, RID_INPUT, bytes.data(), &size, sizeof(RAWINPUTHEADER));
            constexpr size_t offset = offsetof(RAWINPUT, data.hid.bRawData);
            if (received == Failure || received < offset || received > bytes.size()) return;
            auto& raw = *reinterpret_cast<const RAWINPUT*>(bytes.data());
            if (raw.header.dwType != RIM_TYPEHID || raw.header.hDevice != device ||
                raw.data.hid.dwSizeHid != caps.InputReportByteLength || raw.data.hid.dwCount == 0 ||
                raw.data.hid.dwCount > (received - offset) / raw.data.hid.dwSizeHid) return;

            for (DWORD index = 0; index < raw.data.hid.dwCount; ++index)
            {
                ++rawReports;
                std::array<HIDP_DATA, 128> values{};
                ULONG count = caps.NumberInputDataIndices;
                auto report = reinterpret_cast<char*>(bytes.data() + offset + index * raw.data.hid.dwSizeHid);
                if (HidP_GetData(HidP_Input, values.data(), &count, Data(), report, raw.data.hid.dwSizeHid) != HIDP_STATUS_SUCCESS ||
                    count > values.size()) continue;
                ++validReports;
                if (!layout || static_cast<BYTE>(report[0]) != layout->reportId) continue;
                std::array<SteamInputReport::Data, 128> current{};
                for (ULONG value = 0; value < count; ++value)
                    current[value] = {values[value].DataIndex, values[value].RawValue};
                auto frame = SteamInputReport::Decode(*layout, std::span(current.data(), count));
                if (!frame) continue;
                ++controlReports;
                if (prior && frame != prior) ++changes;
                prior = frame;
                if (streaming)
                {
                    std::printf("{\"stage\":\"frame\",\"seq\":%u,\"tick\":%llu,\"buttons\":%u,\"lt\":%u,\"rt\":%u,"
                        "\"lx\":%d,\"ly\":%d,\"rx\":%d,\"ry\":%d}\n", controlReports, GetTickCount64(), frame->buttons,
                        frame->leftTrigger, frame->rightTrigger, frame->leftX, frame->leftY, frame->rightX, frame->rightY);
                    std::fflush(stdout);
                }
            }
        }
    };

    LRESULT CALLBACK Procedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam)
    {
        if (message == WM_NCCREATE)
        {
            auto* init = reinterpret_cast<const CREATESTRUCTW*>(lparam);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(init->lpCreateParams));
        }
        auto* sample = reinterpret_cast<Sample*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (sample && message == WM_INPUT) sample->Handle(reinterpret_cast<HRAWINPUT>(lparam));
        if (sample && message == WM_INPUT_DEVICE_CHANGE && wparam == GIDC_REMOVAL &&
            reinterpret_cast<HANDLE>(lparam) == sample->device) sample->removed = true;
        return DefWindowProcW(window, message, wparam, lparam);
    }

    int Refuse(const char* reason, int code, DWORD detail = 0)
    {
        std::printf("{\"stage\":\"native-reader-refused\",\"reason\":\"%s\",\"code\":%d,\"detail\":%lu}\n",
            reason, code, detail);
        std::fflush(stdout);
        return code;
    }

    int Run(const WindowScope& scope, Source source, std::wstring_view renderer, bool streaming)
    {
        DWORD ownPid = GetCurrentProcessId();
        auto wait = GetTickCount64();
        HANDLE device = nullptr;
        bool shadow = false;
        do
        {
            if (!scope.Active()) return Refuse("scope-lost-before-source", 3);
            if (RendererMatches(renderer)) device = ExactRawDevice(source, ownPid, shadow);
            if (!device) Sleep(35);
        } while (!device && GetTickCount64() - wait < 2200);
        if (!device)
        {
            std::printf("{\"stage\":\"native-source-absent\",\"renderer\":%s}\n", RendererMatches(renderer) ? "true" : "false");
            return 4;
        }

        Sample sample{scope, device};
        if (!sample.OpenDescriptor())
        {
            std::puts("{\"stage\":\"raw-descriptor-unavailable\",\"exactPath\":true}");
            return 5;
        }
        DescriptorAudit descriptor{};
        descriptor.Inspect(sample.Data(), sample.caps);
        sample.layout = Describe(descriptor, sample.caps);
        sample.streaming = streaming && sample.layout.has_value();
        if (streaming)
        {
            bool verified = scope.Active() && RendererMatches(renderer) && ExactRawDevice(source, ownPid, shadow) == device && !shadow;
            descriptor.Print(sample.caps, sample.streaming, sample.layout && sample.layout->combinedTriggers, ownPid, source, verified);
            if (!verified) return Refuse("steam-source-changed", 8);
            if (!sample.streaming) return Refuse("control-descriptor-unsupported", 8);
        }

        WNDCLASSW cls{};
        cls.hInstance = GetModuleHandleW(nullptr);
        cls.lpszClassName = WindowClass;
        cls.lpfnWndProc = Procedure;
        if (!RegisterClassW(&cls)) return Refuse("window-class", 6, GetLastError());
        HWND window = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, WindowClass, L"", WS_POPUP,
            0, 0, 1, 1, nullptr, nullptr, cls.hInstance, &sample);
        if (!window) return Refuse("hidden-window", 6, GetLastError());
        RAWINPUTDEVICE raw[]{{1, 4, RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, window},
            {1, 5, RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, window}};
        if (!RegisterRawInputDevices(raw, static_cast<UINT>(std::size(raw)), sizeof(RAWINPUTDEVICE)))
        {
            DWORD error = GetLastError();
            DestroyWindow(window);
            return Refuse("raw-input-registration", 7, error);
        }

        HMODULE xinput = GetModuleHandleW(L"XInput1_4.dll");
        auto readCaps = std::bit_cast<ReadCaps>(xinput ? GetProcAddress(xinput, MAKEINTRESOURCEA(108)) : nullptr);
        ExtendedCaps details{};
        bool xinputVerified = readCaps && readCaps(1, source.slot, 0, &details) == ERROR_SUCCESS &&
            details.vendor == 0x28DE && details.product == 0x11FF && details.steamSlot == source.slot;
        bool current = true;
        unsigned xinputChanges = 0;
        DWORD previous = 0;
        bool hasPrevious = false;
        auto start = GetTickCount64();
        auto lastCheck = start;
        while (scope.Active() && !sample.removed && current &&
            (streaming ? !shadow : GetTickCount64() - start < 4000))
        {
            MSG msg{};
            for (unsigned messages = 0; messages < 128 && PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE); ++messages)
                DispatchMessageW(&msg);
            XINPUT_STATE state{};
            // The static API is sampled for comparison even if its ordinal
            // describes a generic OS pad. Only the exact raw device is read as
            // an attributed source, and this process never produces input.
            if (!streaming && XInputGetState(source.slot, &state) == ERROR_SUCCESS)
            {
                if (hasPrevious && state.dwPacketNumber != previous) ++xinputChanges;
                previous = state.dwPacketNumber;
                hasPrevious = true;
            }
            if (GetTickCount64() - lastCheck >= 250)
            {
                current = ExactRawDevice(source, ownPid, shadow) == device;
                lastCheck = GetTickCount64();
            }
            MsgWaitForMultipleObjects(0, nullptr, FALSE, 20, QS_INPUT);
        }

        for (auto& entry : raw) { entry.dwFlags = RIDEV_REMOVE; entry.hwndTarget = nullptr; }
        RegisterRawInputDevices(raw, static_cast<UINT>(std::size(raw)), sizeof(RAWINPUTDEVICE));
        DestroyWindow(window);
        std::printf("{\"stage\":\"native-source-sample\",\"rawReports\":%u,\"decodedReports\":%u,\"controlReports\":%u,"
            "\"rawStateChanges\":%u,\"xinputVerified\":%s,\"xinputStateChanges\":%u,\"capVid\":%u,\"capPid\":%u,"
            "\"buttonCaps\":%u,\"valueCaps\":%u,\"reportBytes\":%u,\"controlLayout\":%s,\"combinedTriggers\":%s,"
            "\"steamShadow\":%s,\"sourceRemoved\":%s,\"scopeIntact\":%s}\n",
            sample.rawReports, sample.validReports, sample.controlReports, sample.changes, xinputVerified ? "true" : "false",
            xinputChanges, details.vendor, details.product, sample.caps.NumberInputButtonCaps,
            sample.caps.NumberInputValueCaps, sample.caps.InputReportByteLength,
            sample.layout ? "true" : "false", sample.layout && sample.layout->combinedTriggers ? "true" : "false",
            shadow ? "true" : "false", RemovedFromRawInventory(sample.removed, device) ? "true" : "false",
            scope.Active() && current && !sample.removed ? "true" : "false");
        return 0;
    }

    int SelfTest()
    {
        constexpr auto valid = LR"(\\.\pipe\HID#VID_045E&PID_028E&IG_00#28DE&1302&00000000000000AA#2#1234)";
        if (!PipeMatches(valid, Source{2, 0x1302, 0xAA}, 1234)
            || PipeMatches(valid, Source{1, 0x1302, 0xAA}, 1234)
            || PipeMatches(valid, Source{2, 0x1302, 0xAB}, 1234)
            || PipeMatches(valid, Source{2, 0x1304, 0xAA}, 1234)
            || PipeMatches(valid, Source{2, 0x1302, 0xAA}, 1235)) return 1;
        std::puts("PASS exact Steam RawInput path fixture");
        const Source physical{2, 0x1302, 0xAA};
        if (OutputAliasesSource({0x045E, 0x7EB0, 0xBB, 3, 1234}, physical, 1234)
            || !OutputAliasesSource({0x045E, 0x7EB0, 0xAA, 3, 1234}, physical, 1234)
            || !OutputAliasesSource({0x045E, 0x7EB0, 0xBB, 2, 1234}, physical, 1234)
            || OutputAliasesSource({0x045E, 0x7EB0, 0xAA, 2, 1235}, physical, 1234)
            || PipeMatches(LR"(\\.\pipe\HID#VID_045E&PID_028E&IG_00#045E&7EB0&00000000000000BB#3#1234)", physical, 1234)) return 1;
        std::puts("PASS separate relay output cannot become a physical Steam source; aliases are rejected");
        if (!ConfirmedRemoval(true, true, false) || ConfirmedRemoval(true, true, true)
            || ConfirmedRemoval(true, false, false) || ConfirmedRemoval(false, true, false)) return 9;
        std::puts("PASS source removal requires notification for the bound device and confirmed absence, never an unknown inventory");

        SteamInputReport::Layout xbox{};
        for (USHORT index = 0; index < 10; ++index) xbox.buttons[index] = index;
        xbox.sticks = {{{10, 16, 0, 65535}, {11, 16, 0, 65535}, {12, 16, 0, 65535}, {13, 16, 0, 65535}}};
        xbox.hat = {14, 4, 1, 8, true};
        xbox.leftTrigger = {15, 16, 0, 65535};
        xbox.combinedTriggers = true;
        std::array<SteamInputReport::Data, 8> data{{
            {10, 32768}, {11, 32768}, {12, 32768}, {13, 32768}, {14, 0}, {15, 32768}, {100, 1}, {101, 2}
        }};
        auto neutral = SteamInputReport::Decode(xbox, data);
        if (!neutral || *neutral != SteamInputReport::Frame{}) return 2;
        data[6].raw = 997;
        if (SteamInputReport::Decode(xbox, data) != neutral) return 3;
        data[0].raw = 65535;
        data[1].raw = 0;
        data[4].raw = 2;
        data[5].raw = 65535;
        data[6] = {0, 1};
        auto controls = SteamInputReport::Decode(xbox, data);
        if (!controls || controls->buttons != 0x1009 || controls->leftX != 32767 ||
            controls->leftY != 32767 || controls->leftTrigger != 255 || controls->rightTrigger != 0) return 4;
        data[5].raw = 0;
        controls = SteamInputReport::Decode(xbox, data);
        if (!controls || controls->leftTrigger != 0 || controls->rightTrigger != 255) return 5;
        data[7] = {0, 0};
        if (SteamInputReport::Decode(xbox, data)) return 6;
        std::puts("PASS actual controls, metadata exclusion, D-pad diagonals, axes, combined triggers, duplicate rejection");

        DescriptorAudit audit{};
        HIDP_CAPS caps{};
        caps.NumberInputButtonCaps = audit.buttonCount = 1;
        caps.NumberInputValueCaps = audit.valueCount = 6;
        auto& button = audit.buttons[0];
        button.UsagePage = 9;
        button.IsRange = TRUE;
        button.Range.UsageMin = 1;
        button.Range.UsageMax = 10;
        button.Range.DataIndexMax = 9;
        constexpr std::array<USHORT, 6> usages{0x30, 0x31, 0x33, 0x34, 0x39, 0x32};
        for (size_t index = 0; index < usages.size(); ++index)
        {
            auto& value = audit.values[index];
            value.UsagePage = 1;
            value.IsAbsolute = TRUE;
            value.BitSize = 16;
            value.LogicalMax = 65535;
            value.NotRange.Usage = usages[index];
            value.NotRange.DataIndex = static_cast<USHORT>(10 + index);
        }
        audit.values[4].BitSize = 4;
        audit.values[4].LogicalMin = 1;
        audit.values[4].LogicalMax = 8;
        if (!Describe(audit, caps) || std::string_view(audit.result) != "supported") return 7;
        audit.values[0].NotRange.Usage = 0x40;
        if (Describe(audit, caps) || std::string_view(audit.result) != "value-usage") return 8;
        std::puts("PASS descriptor acceptance and precise refusal codes");

        caps.InputReportByteLength = 14;
        caps.NumberInputDataIndices = 16;
        button.Range.DataIndexMin = 5;
        button.Range.DataIndexMax = 14;
        constexpr std::array<USHORT, 5> valveAxes{0x31, 0x30, 0x34, 0x33, 0x32};
        for (USHORT index = 0; index < valveAxes.size(); ++index)
        {
            auto& value = audit.values[index];
            value.BitSize = 16;
            value.LogicalMin = 0;
            value.LogicalMax = -1;
            value.NotRange.Usage = valveAxes[index];
            value.NotRange.DataIndex = index;
        }
        auto& hat = audit.values[5];
        hat.BitSize = 4;
        hat.LogicalMin = 1;
        hat.LogicalMax = 8;
        hat.HasNull = TRUE;
        hat.NotRange.Usage = 0x39;
        hat.NotRange.DataIndex = 15;
        auto valve = Describe(audit, caps);
        if (!valve || !valve->valveUnsigned16 || !valve->combinedTriggers || valve->sticks[0].index != 1 ||
            valve->sticks[0].maximum != 65535 || std::string_view(audit.result) != "supported-valve-unsigned16") return 9;
        std::array<SteamInputReport::Data, 7> valveData{{
            {0, 0x7FFF}, {1, 0xFFFF8000}, {2, 0x8001}, {3, 0x7FFE}, {4, 0x8002}, {5, 0}, {15, 0}
        }};
        if (SteamInputReport::Decode(*valve, valveData) != neutral) return 10;
        valveData[0].raw = 0;
        valveData[1].raw = 0x8003;
        valveData[4].raw = 0xFFFFFFFF;
        valveData[5].raw = 1;
        valveData[6].raw = 2;
        controls = SteamInputReport::Decode(*valve, valveData);
        if (!controls || controls->leftX != 3 || controls->leftY != 32767 || controls->leftTrigger != 255 ||
            controls->rightTrigger != 0 || controls->buttons != 0x1009) return 11;
        valveData[4].raw = 0;
        controls = SteamInputReport::Decode(*valve, valveData);
        if (!controls || controls->leftTrigger != 0 || controls->rightTrigger != 255) return 12;
        valveData[1].raw = 0x12348000;
        if (SteamInputReport::Decode(*valve, valveData)) return 13;
        ++caps.InputReportByteLength;
        if (Describe(audit, caps) || std::string_view(audit.result) != "value-range") return 14;
        int32_t signedValue = 0;
        if (!SteamInputReport::Number({0, 16, -32768, 32767}, 0xFFFF, signedValue) || signedValue != -1) return 15;
        std::puts("PASS observed Valve descriptor, unsigned values, neutral jitter, and descriptor boundaries");
        return 0;
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc == 2 && std::wstring_view(argv[1]) == L"--self-test") return SelfTest();
    if (argc != 12) return Refuse("arguments", 2);
    bool streaming = std::wstring_view(argv[1]) == L"--relay-session";
    if (!streaming && std::wstring_view(argv[1]) != L"--probe") return Refuse("mode", 2);
    uint64_t game = 0, slot = 0, product = 0, handle = 0, parentPid = 0, birth = 0, hwnd = 0, thread = 0, owner = 0;
    if (!Number(argv[2], 10, game) || static_cast<DWORD>(game) != 0x02000000 || (game >> 32) < 0x80000000 ||
        !Number(argv[3], 10, slot) || slot > 3 || !Number(argv[4], 16, product) || (product != 0x1302 && product != 0x1304) ||
        !Number(argv[5], 16, handle) || handle == 0 || !Number(argv[7], 10, parentPid) || parentPid == 0 || parentPid > MAXDWORD ||
        !Number(argv[8], 10, birth) || !Number(argv[9], 10, hwnd) || hwnd == 0 ||
        !Number(argv[10], 10, thread) || thread == 0 || thread > MAXDWORD ||
        !Number(argv[11], 10, owner) || owner == 0 || owner > MAXDWORD) return Refuse("identity-arguments", 2);

    std::array<wchar_t, 32> inherited{};
    uint64_t inheritedGame = 0;
    DWORD length = GetEnvironmentVariableW(L"SteamGameId", inherited.data(), static_cast<DWORD>(inherited.size()));
    if (length == 0 || length >= inherited.size() || !Number(inherited.data(), 10, inheritedGame) || inheritedGame != game)
        return Refuse("steam-game-id", 2);

    HANDLE parent = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, static_cast<DWORD>(parentPid));
    if (!parent) return Refuse("parent-handle", 3, GetLastError());
    FILETIME born{}, ended{}, kernel{}, user{};
    bool verified = GetProcessTimes(parent, &born, &ended, &kernel, &user) &&
        ((static_cast<uint64_t>(born.dwHighDateTime) << 32) | born.dwLowDateTime) == birth;
    int code = verified ? Run(WindowScope{parent, reinterpret_cast<HWND>(static_cast<uintptr_t>(hwnd)),
        static_cast<DWORD>(thread), static_cast<DWORD>(owner)},
        Source{static_cast<DWORD>(slot), static_cast<USHORT>(product), handle}, argv[6], streaming)
        : Refuse("parent-creation", 3);
    CloseHandle(parent);
    return code;
}
