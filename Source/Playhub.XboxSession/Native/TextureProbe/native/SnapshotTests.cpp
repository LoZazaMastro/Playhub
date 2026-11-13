// Playhub Xbox Session. Distributed under GPL-3.0-only.
#include "SnapshotPixels.h"
#include <windows.h>
#include <algorithm>

int main()
{
    if (!BoundedSnapshotSize(3840, 2160) || !BoundedSnapshotSize(7680, 4320)
        || BoundedSnapshotSize(1, 2160) || BoundedSnapshotSize(-3840, 2160)
        || BoundedSnapshotSize(8192, 8192) || BoundedSnapshotSize(2147483647, 2147483647)) return 1;
    if (!SnapshotPixelCountMatches(4, 4, 16) || SnapshotPixelCountMatches(4, 4, 15)
        || SnapshotPixelCountMatches(-4, 4, 16) || SnapshotPixelCountMatches(1, 4, 4)
        || SnapshotPixelCountMatches(8192, 8192, 67108864)) return 11;
    if (ClassifySnapshotCapture(true, true, true, true) != SnapshotCaptureState::Copied
        || ClassifySnapshotCapture(false, true, true, true) != SnapshotCaptureState::InvalidSurface
        || ClassifySnapshotCapture(true, false, true, true) != SnapshotCaptureState::SourceChanged
        || ClassifySnapshotCapture(true, true, false, true) != SnapshotCaptureState::CopyFailed
        || ClassifySnapshotCapture(true, true, true, false) != SnapshotCaptureState::FlushFailed) return 12;
    SnapshotViewport captured{1920, 0, 1920, 1080}, desktop{3840, -200, 3840, 2160}, selected{};
    if (!SelectSnapshotViewport(captured, desktop, true, true, true, selected)
        || selected.left != 3840 || selected.top != -200 || selected.width != 3840 || selected.height != 2160) return 7;
    if (!SelectSnapshotViewport(captured, desktop, true, false, true, selected)
        || selected.left != captured.left || selected.width != captured.width) return 8;
    if (!SelectSnapshotViewport(captured, desktop, false, true, true, selected)
        || selected.left != captured.left || selected.height != captured.height) return 9;
    if (SelectSnapshotViewport(captured, desktop, true, true, false, selected)
        || SelectSnapshotViewport(captured, {0, 0, 0, 2160}, true, true, true, selected)) return 10;
    std::vector<uint32_t> black(16, 0);
    bool blackHasColor = true;
    if (!PrepareSnapshotPixels(black, &blackHasColor) || blackHasColor
        || !std::all_of(black.begin(), black.end(), [](uint32_t p) { return p == 0xff000000; })) return 2;
    std::vector<uint32_t> empty;
    if (PrepareSnapshotPixels(empty)) return 13;
    std::vector<uint32_t> color{0x00112233, 0xaa102030};
    bool hasColor = false;
    if (!PrepareSnapshotPixels(color, &hasColor) || !hasColor || color[0] != 0xff112233 || color[1] != 0xff102030) return 3;

    // Exercise the actual GDI top-down BGRA path without creating a window,
    // changing foreground focus, or reading any desktop pixels.
    HDC source = CreateCompatibleDC(nullptr), destination = CreateCompatibleDC(nullptr);
    BITMAPINFO info{};
    info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    info.bmiHeader.biWidth = 4;
    info.bmiHeader.biHeight = -3;
    info.bmiHeader.biPlanes = 1;
    info.bmiHeader.biBitCount = 32;
    info.bmiHeader.biCompression = BI_RGB;
    uint32_t *sourcePixels = nullptr, *destinationPixels = nullptr;
    HBITMAP sourceBitmap = CreateDIBSection(source, &info, DIB_RGB_COLORS, reinterpret_cast<void**>(&sourcePixels), nullptr, 0);
    HBITMAP destinationBitmap = CreateDIBSection(destination, &info, DIB_RGB_COLORS, reinterpret_cast<void**>(&destinationPixels), nullptr, 0);
    if (!source || !destination || !sourceBitmap || !destinationBitmap || !sourcePixels || !destinationPixels) return 4;
    HGDIOBJ oldSource = SelectObject(source, sourceBitmap), oldDestination = SelectObject(destination, destinationBitmap);
    for (int pixel = 0; pixel < 12; pixel++) sourcePixels[pixel] = 0x00100000u + static_cast<uint32_t>(pixel);
    bool copied = BitBlt(destination, 0, 0, 4, 3, source, 0, 0, SRCCOPY) && GdiFlush();
    std::vector<uint32_t> result(destinationPixels, destinationPixels + 12);
    bool normalized = PrepareSnapshotPixels(result);
    // A real, successful black DIB transfer is valid loading/fade content too.
    std::fill(sourcePixels, sourcePixels + 12, 0u);
    bool copiedBlack = BitBlt(destination, 0, 0, 4, 3, source, 0, 0, SRCCOPY) && GdiFlush();
    std::vector<uint32_t> blackResult(destinationPixels, destinationPixels + 12);
    bool copiedBlackHasColor = true;
    bool normalizedBlack = PrepareSnapshotPixels(blackResult, &copiedBlackHasColor);
    SelectObject(source, oldSource);
    SelectObject(destination, oldDestination);
    DeleteObject(sourceBitmap);
    DeleteObject(destinationBitmap);
    DeleteDC(source);
    DeleteDC(destination);
    if (!copied || !normalized) return 5;
    for (int pixel = 0; pixel < 12; pixel++) if (result[pixel] != 0xff100000u + static_cast<uint32_t>(pixel)) return 6;
    if (!copiedBlack || !normalizedBlack || copiedBlackHasColor
        || !std::all_of(blackResult.begin(), blackResult.end(), [](uint32_t p) { return p == 0xff000000; })) return 14;
    return 0;
}
