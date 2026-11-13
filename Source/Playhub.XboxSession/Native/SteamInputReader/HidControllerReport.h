// Playhub Xbox Session. Distributed under GPL-3.0-only.
#pragma once
#include <array>
#include <cstdint>
#include <limits>
#include <optional>
#include <span>

namespace SteamInputReport
{
    struct Value
    {
        uint16_t index{};
        uint16_t bits{};
        int32_t minimum{};
        int32_t maximum{};
        bool hasNull{};

        bool Valid() const
        {
            return bits > 0 && bits <= 16 && minimum < maximum &&
                int64_t(maximum) - minimum <= 65535;
        }
    };

    struct Layout
    {
        // DataIndex follows the HID Button usages 1–10 in Microsoft's Xbox
        // RawInput descriptor. The guide and other usages are never forwarded.
        std::array<uint16_t, 10> buttons{};
        std::array<Value, 4> sticks{};
        Value hat{}, leftTrigger{}, rightTrigger{};
        bool combinedTriggers = false;
        bool valveUnsigned16 = false;
        uint8_t reportId{};
    };

    struct Data
    {
        uint16_t index;
        uint32_t raw;
    };

    struct Frame
    {
        uint16_t buttons{};
        uint8_t leftTrigger{}, rightTrigger{};
        int16_t leftX{}, leftY{}, rightX{}, rightY{};

        bool operator==(const Frame&) const = default;
    };

    inline bool Number(Value capability, uint32_t raw, int32_t& result)
    {
        if (!capability.Valid()) return false;
        uint32_t mask = (1u << capability.bits) - 1;
        if (raw > mask) return false;
        uint32_t sign = 1u << (capability.bits - 1);
        result = capability.minimum < 0 && (raw & sign)
            ? static_cast<int32_t>(raw) - static_cast<int32_t>(mask + 1) : static_cast<int32_t>(raw);
        return true;
    }

    inline std::optional<uint32_t> Find(std::span<const Data> data, uint16_t index)
    {
        std::optional<uint32_t> result;
        for (auto item : data)
        {
            if (item.index != index) continue;
            if (result) return {};
            result = item.raw;
        }
        return result;
    }

    inline bool Read(Value capability, std::span<const Data> data, int32_t& result, bool valveUnsigned16 = false)
    {
        auto raw = Find(data, capability.index);
        if (raw && valveUnsigned16 && capability.bits == 16 && capability.minimum == 0 &&
            capability.maximum == 65535 && (*raw & 0xFFFF8000u) == 0xFFFF8000u)
            *raw &= 0xFFFFu;
        return raw && Number(capability, *raw, result) &&
            result >= capability.minimum && result <= capability.maximum;
    }

    inline int16_t Stick(int32_t number, Value capability, bool invert, bool valveUnsigned16)
    {
        int64_t range = int64_t(capability.maximum) - capability.minimum;
        int64_t scaled = ((int64_t(number) - capability.minimum) * 65535 + range / 2) / range - 32768;
        if (valveUnsigned16 && scaled >= -2 && scaled <= 2) return 0;
        if (invert) scaled = scaled == -32768 ? 32767 : -scaled;
        return static_cast<int16_t>(scaled);
    }

    inline uint8_t Trigger(int32_t number, int32_t minimum, int32_t maximum)
    {
        int32_t span = maximum - minimum;
        return static_cast<uint8_t>(((number - minimum) * 255 + span / 2) / span);
    }

    inline std::optional<Frame> Decode(const Layout& layout, std::span<const Data> data)
    {
        for (size_t left = 0; left < data.size(); ++left)
            for (size_t right = left + 1; right < data.size(); ++right)
                if (data[left].index == data[right].index) return {};
        constexpr std::array<uint16_t, 10> xboxButtons{
            0x1000, 0x2000, 0x4000, 0x8000, 0x0100, 0x0200, 0x0020, 0x0010, 0x0040, 0x0080
        };
        constexpr std::array<uint16_t, 8> xboxHat{1, 9, 8, 10, 2, 6, 4, 5};
        Frame result{};
        for (size_t button = 0; button < xboxButtons.size(); ++button)
        {
            if (Find(data, layout.buttons[button]).value_or(0) != 0) result.buttons |= xboxButtons[button];
        }

        int32_t leftX = 0, leftY = 0, rightX = 0, rightY = 0, hat = 0, trigger = 0;
        bool valveUnsigned16 = layout.valveUnsigned16;
        if (!Read(layout.sticks[0], data, leftX, valveUnsigned16) ||
            !Read(layout.sticks[1], data, leftY, valveUnsigned16) ||
            !Read(layout.sticks[2], data, rightX, valveUnsigned16) ||
            !Read(layout.sticks[3], data, rightY, valveUnsigned16) ||
            !Read(layout.leftTrigger, data, trigger, valveUnsigned16)) return {};

        auto rawHat = Find(data, layout.hat.index);
        if (rawHat && !Number(layout.hat, *rawHat, hat)) return {};
        if (rawHat && hat >= layout.hat.minimum && hat <= layout.hat.maximum)
        {
            int index = hat - layout.hat.minimum;
            if (index >= static_cast<int>(xboxHat.size())) return {};
            result.buttons |= xboxHat[static_cast<size_t>(index)];
        }
        else if (!layout.hat.hasNull && layout.hat.minimum != 1) return {};

        result.leftX = Stick(leftX, layout.sticks[0], false, valveUnsigned16);
        result.leftY = Stick(leftY, layout.sticks[1], true, valveUnsigned16);
        result.rightX = Stick(rightX, layout.sticks[2], false, valveUnsigned16);
        result.rightY = Stick(rightY, layout.sticks[3], true, valveUnsigned16);

        if (layout.combinedTriggers)
        {
            const auto z = layout.leftTrigger;
            int32_t centre = z.minimum + (z.maximum - z.minimum + 1) / 2;
            if (trigger > centre) result.leftTrigger = Trigger(trigger, centre, z.maximum);
            if (trigger < centre) result.rightTrigger = Trigger(centre - trigger, 0, centre - z.minimum);
        }
        else
        {
            int32_t right = 0;
            if (!Read(layout.rightTrigger, data, right)) return {};
            result.leftTrigger = Trigger(trigger, layout.leftTrigger.minimum, layout.leftTrigger.maximum);
            result.rightTrigger = Trigger(right, layout.rightTrigger.minimum, layout.rightTrigger.maximum);
        }
        return result;
    }
}
