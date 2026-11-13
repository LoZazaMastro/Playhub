using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GamingMode.Services;

namespace GamingMode.Services
{
    internal enum FrameMeasurementStatus { Ready, Busy, TargetUnavailable, GameNotForeground, PermissionDenied, ProviderUnavailable, NoData, Canceled, TimedOut, Failed, CleanupFailed }
}

internal static class Program
{
    private static int _passed;
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Case(string name, Action test) { test(); _passed++; Console.WriteLine("PASS " + name); }
    private static LiveFrameTelemetry Sample(long sequence, long tick, int pid = 1, long birth = 2) =>
        new(FrameMeasurementStatus.Ready, sequence, tick, PresentedFps: 120, MeanFrameTimeMs: 8.333,
            ProcessId: pid, ProcessBirth: birth);

    [STAThread]
    private static void Main()
    {
        Case("physical placements stay inside negative-origin monitors at 100–300% DPI", () => {
            foreach (double dpi in new[] { 96d, 120, 144, 192, 288 })
            foreach (HudPosition position in Enum.GetValues<HudPosition>())
            foreach (int detail in new[] { 1, 2, 3, 4 }) {
                var bounds = new LivePerformanceHudBounds(-2560, -1440, 2560, 1440);
                var placement = LivePerformanceHudGeometry.Place(bounds, new(true, detail, position), dpi);
                Assert(placement.Left >= bounds.Left && placement.Top >= bounds.Top
                    && placement.Left + placement.Width <= bounds.Left + bounds.Width
                    && placement.Top + placement.Height <= bounds.Top + bounds.Height, "Placement overflow");
            }
        });
        Case("settings normalization and center placement are deterministic", () => {
            var settings = new LivePerformanceHudSettings(true, 90, (HudPosition)999, MarginPixels: -30).Normalized();
            Assert(settings.Detail == 4 && settings.Position == HudPosition.TopLeft && settings.MarginPixels == 0, "Invalid settings survived");
            var placement = LivePerformanceHudGeometry.Place(new(0, 0, 1920, 1080), new(true, 1, HudPosition.Center), 96);
            Assert(placement.Left == 885 && placement.Top == 515, "Center is not centered");
        });
        Case("only new measured sequences add points; unavailable values clear rather than become zero", () => {
            var history = new LivePerformanceHudHistory(); history.Update(Sample(1, 1000), 1000);
            history.Update(Sample(1, 1001), 1001); Assert(history.Count == 1, "Duplicate interpolated");
            history.Update(new(FrameMeasurementStatus.Busy, 1, 1050), 1050); Assert(history.Count == 1, "Busy changed measurement");
            history.Update(new(FrameMeasurementStatus.NoData, 1, 1100), 1100); Assert(history.Count == 0 && history.Latest is null, "NoData retained FPS");
        });
        Case("graph storage stays bounded and another process starts a new sequence", () => {
            var history = new LivePerformanceHudHistory();
            for (int i = 0; i < 5000; i++) history.Update(Sample(i + 1, 1000 + i), 1000 + i);
            Assert(history.Count <= LivePerformanceHudHistory.Capacity, "Unbounded history");
            history.Update(Sample(1, 6001, 10, 20), 6001);
            Assert(history.Count == 1 && history.Latest?.ProcessId == 10, "Old PID poisoned new sequence");
        });
        Case("stale and invalid samples never display an old value", () => {
            var history = new LivePerformanceHudHistory(); history.Update(Sample(1, 1000), 1000);
            Assert(!history.Expire(1499) && history.Expire(1500) && history.Latest is null, "Stale boundary");
            history.Update(Sample(2, 2000) with { PresentedFps = double.NaN }, 2000); Assert(history.Latest is null, "NaN passed");
            history.Update(Sample(3, 3000), 3600); Assert(history.Latest is null, "Old queued sample passed");
            history.Update(Sample(4, 4000), 4000); history.Update(Sample(5, 3900), 4000);
            Assert(history.Latest?.Sequence == 4, "Backward timestamp entered graph");
        });
        Case("four renderer levels draw with bounded graphs and measured draw timing", () => {
            var surface = new LivePerformanceHudSurface();
            var usage = new LivePerformanceHudUsage(24, 62, 12L << 30, 32L << 30, 3L << 30, 8L << 30);
            foreach (bool opaque in new[] { true, false })
            for (int detail = 1; detail <= 4; detail++) {
                long now = Environment.TickCount64;
                for (int i = 0; i < 100; i++) surface.SetData(new(true, detail, OpaqueBackground: opaque), Sample(i + 1, now - 99 + i), usage, now);
                surface.Measure(new(surface.Width, surface.Height)); surface.Arrange(new(0, 0, surface.Width, surface.Height));
                var bitmap = new RenderTargetBitmap((int)surface.Width, (int)surface.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                if (Environment.GetEnvironmentVariable("PLAYHUB_HUD_PREVIEW_DIR") is { Length: > 0 } preview) {
                    Directory.CreateDirectory(preview);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(preview, $"hud-{detail}-{(opaque ? "opaque" : "transparent")}.png")); encoder.Save(stream);
                }
                byte[] pixels = new byte[(int)surface.Width * (int)surface.Height * 4]; bitmap.CopyPixels(pixels, (int)surface.Width * 4, 0);
                Assert(pixels.Any(value => value != 0), "Empty renderer");
                int blank = (((int)surface.Height - 4) * (int)surface.Width + (int)surface.Width / 2) * 4 + 3;
                Assert(opaque ? pixels[blank] == 255 : pixels[blank] == 0, "Background mode did not apply");
            }
            Assert(surface.BufferedPoints <= LivePerformanceHudHistory.Capacity, "Renderer history unbounded");
            Assert(surface.Draws > 0 && double.IsFinite(surface.MaximumDrawMicroseconds), "No render measurement");
            Console.WriteLine($"RENDER draws={surface.Draws} average_us={surface.AverageDrawMicroseconds:0.00} max_us={surface.MaximumDrawMicroseconds:0.00}");
        });
        Case("native HWND is click-through, non-activating and never becomes foreground", () => {
            nint before = LivePerformanceHud.Native.GetForegroundWindow();
            Type type = typeof(LivePerformanceHud).GetNestedType("HudWindow", BindingFlags.NonPublic)!;
            var window = (Window)Activator.CreateInstance(type, BindingFlags.NonPublic | BindingFlags.Instance, null, [new Action(() => { })], null)!;
            try {
                nint hwnd = new WindowInteropHelper(window).EnsureHandle();
                long styles = LivePerformanceHud.Native.GetWindowLongPtr(hwnd, -20).ToInt64();
                long required = LivePerformanceHud.Native.ExTransparent | LivePerformanceHud.Native.ExNoActivate | LivePerformanceHud.Native.ExToolWindow;
                Assert((styles & required) == required && !window.ShowActivated && !window.IsHitTestVisible, "Input interception");
                Assert(SendMessage(hwnd, 0x84, 0, 0) == -1, "Hit test not transparent");
                Assert(SendMessage(hwnd, 0x21, 0, 0) == 3, "Mouse can activate HUD");
                Assert(LivePerformanceHud.Native.GetForegroundWindow() == before && !window.IsVisible, "Hidden smoke changed foreground");
            } finally { window.Close(); }
        });
        Case("a five-second measured graph renders repeatedly with bounded storage", () => {
            var surface = new LivePerformanceHudSurface();
            long now = Environment.TickCount64;
            for (int i = 0; i <= 100; i++) {
                long tick = now - 5000 + i * 50; double milliseconds = 8.3 + i % 10;
                surface.SetData(new(true, 4), Sample(i + 1, tick) with { MeanFrameTimeMs = milliseconds, PresentedFps = 1000 / milliseconds }, null, tick);
            }
            surface.Measure(new(surface.Width, surface.Height)); surface.Arrange(new(0, 0, surface.Width, surface.Height));
            var bitmap = new RenderTargetBitmap((int)surface.Width, (int)surface.Height, 96, 96, PixelFormats.Pbgra32);
            // Hold the fixture measurement clock fixed; expiry has its own
            // boundary test and bitmap readback can exceed 500ms on slow GPUs.
            for (int frame = 0; frame < 64; frame++) { surface.Tick(now); surface.UpdateLayout(); bitmap.Render(surface); }
            Assert(surface.BufferedPoints <= 101 && surface.Draws >= 60, "Full history did not render or exceeded time bound");
            Console.WriteLine($"GRAPH draws={surface.Draws} points={surface.BufferedPoints} average_us={surface.AverageDrawMicroseconds:0.00} max_us={surface.MaximumDrawMicroseconds:0.00}");
        });
        Case("off accepts no publications and creates no dispatcher thread", () => {
            using var hud = new LivePerformanceHud();
            for (int i = 0; i < 100; i++) hud.Publish(Sample(i + 1, Environment.TickCount64), null, new(0, 0, 1920, 1080));
            Assert(hud.Diagnostics.Published == 0 && typeof(LivePerformanceHud).GetField("_thread", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hud) is null, "HUD off still runs");
        });
        Case("rapid telemetry is coalesced; a foreign target cannot show a HUD and disposal stops its dispatcher", () => {
            nint foreground = LivePerformanceHud.Native.GetForegroundWindow();
            var hud = new LivePerformanceHud();
            hud.Configure(new(true, 4));
            for (int i = 0; i < 10000; i++) hud.Publish(Sample(i + 1, Environment.TickCount64, -1, 1), null, new(0, 0, 1920, 1080));
            Assert(hud.Diagnostics.Published == 10000 && hud.Diagnostics.Coalesced > 0, "Updates were not coalesced");
            Thread thread = (Thread)typeof(LivePerformanceHud).GetField("_thread", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hud)!;
            hud.Configure(new(false));
            hud.Publish(Sample(10001, Environment.TickCount64), null, new(0, 0, 1920, 1080));
            Assert(hud.Diagnostics.Published == 10000, "Off still publishes");
            hud.Dispose(); Assert(thread.Join(TimeSpan.FromSeconds(3)), "Dispatcher survived disposal");
            Assert(!hud.Diagnostics.Visible && LivePerformanceHud.Native.GetForegroundWindow() == foreground, "Fixture changed visible state");
        });
        Case("foreground identity verifies both game and optional host process births without showing a window", () => {
            nint foreground = LivePerformanceHud.Native.GetForegroundWindow();
            LivePerformanceHud.Native.GetWindowThreadProcessId(foreground, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            long birth = process.StartTime.ToUniversalTime().ToFileTimeUtc();
            using var hud = new LivePerformanceHud();
            var check = typeof(LivePerformanceHud).GetMethod("CheckForeground", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert((bool)check.Invoke(hud, [(int)pid, birth])!, "Exact foreground rejected");
            Assert(!(bool)check.Invoke(hud, [(int)pid, birth + 1])!, "Reused PID accepted");
            using var own = Process.GetCurrentProcess();
            long ownBirth = own.StartTime.ToUniversalTime().ToFileTimeUtc();
            var proof = typeof(LivePerformanceHud).GetField("_foregroundProof", BindingFlags.Instance | BindingFlags.NonPublic)!;
            proof.SetValue(hud, new LivePerformanceHudForegroundProof(foreground, (int)pid, birth));
            Assert((bool)check.Invoke(hud, [own.Id, ownBirth])!, "Verified host rejected");
            proof.SetValue(hud, new LivePerformanceHudForegroundProof(foreground + 1, (int)pid, birth));
            Assert(!(bool)check.Invoke(hud, [own.Id, ownBirth])!, "Different hosted HWND accepted");
            Assert(LivePerformanceHud.Native.GetForegroundWindow() == foreground && !hud.Diagnostics.Visible, "Native identity check showed UI");
        });
        Console.WriteLine($"Passed {_passed} renderer checks. No game or visible HUD was opened; exclusive fullscreen remains unverified.");
    }

    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);
}
