// Playhub Xbox Session. Distributed under GPL-3.0-only.
#pragma once
#include <cstdint>
#include <vector>

// GDI leaves alpha undefined. A successfully copied black frame is legitimate
// loading/fade content; capture validity comes from the copy and identity checks.
inline bool PrepareSnapshotPixels(std::vector<uint32_t>& pixels, bool* hasColor = nullptr)
{
    bool anyVisiblePixel = false;
    for (auto& pixel : pixels)
    {
        anyVisiblePixel |= (pixel & 0x00ffffffu) != 0;
        pixel |= 0xff000000u;
    }
    if (hasColor) *hasColor = anyVisiblePixel;
    return !pixels.empty();
}

enum class SnapshotCaptureState : int32_t
{
    Copied = 0, Black = 1, InvalidSurface = 2, SourceChanged = 3,
    CopyFailed = 4, FlushFailed = 5, InvalidPixels = 6
};

constexpr SnapshotCaptureState ClassifySnapshotCapture(bool validSurface, bool exactSource,
    bool copied, bool flushed)
{
    return !validSurface ? SnapshotCaptureState::InvalidSurface :
        !exactSource ? SnapshotCaptureState::SourceChanged :
        !copied ? SnapshotCaptureState::CopyFailed :
        !flushed ? SnapshotCaptureState::FlushFailed : SnapshotCaptureState::Copied;
}

constexpr bool BoundedSnapshotSize(long width, long height)
{
    return width >= 2 && height >= 2 && width <= 8192 && height <= 8192
        && static_cast<uint64_t>(width) * static_cast<uint64_t>(height) <= 33554432;
}

constexpr bool SnapshotPixelCountMatches(long width, long height, uint64_t count)
{
    return BoundedSnapshotSize(width, height)
        && static_cast<uint64_t>(width) * static_cast<uint64_t>(height) == count;
}

struct SnapshotViewport { long left, top, width, height; };

constexpr bool SelectSnapshotViewport(SnapshotViewport captured, SnapshotViewport monitor, bool filledMonitor,
    bool companionForeground, bool sameMonitor, SnapshotViewport& selected)
{
    if (filledMonitor && companionForeground)
    {
        if (!sameMonitor || !BoundedSnapshotSize(monitor.width, monitor.height)) return false;
        selected = monitor;
    }
    else selected = captured;
    return BoundedSnapshotSize(selected.width, selected.height);
}
