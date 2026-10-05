using System.Runtime.InteropServices;

namespace GamingMode.Services;

internal readonly record struct PhysicalWindowRect(int Left, int Top, int Right, int Bottom)
{
	public bool IsValid => Right > Left && Bottom > Top;
	public bool Matches(PhysicalWindowRect other) => IsValid && other.IsValid
		&& Math.Abs((long)Left - other.Left) <= 1 && Math.Abs((long)Top - other.Top) <= 1
		&& Math.Abs((long)Right - other.Right) <= 1 && Math.Abs((long)Bottom - other.Bottom) <= 1;
}

internal readonly record struct FullscreenWindowBounds(PhysicalWindowRect Window, PhysicalWindowRect Monitor);

internal static class FullscreenWindowGeometry
{
	internal static bool IsOverlayHostProcess(string? processName)
		=> System.IO.Path.GetFileNameWithoutExtension(processName ?? "")
			.Equals("GlosSITarget", StringComparison.OrdinalIgnoreCase);

	// Read and write under the same thread-local physical coordinate context.
	// An existing fullscreen swapchain must not receive style or resize messages.
	internal static bool Apply(Func<FullscreenWindowBounds?> readBounds,
		Action<FullscreenWindowBounds> resize, Func<IDisposable?>? enterPhysicalCoordinates = null)
	{
		using IDisposable? scope = (enterPhysicalCoordinates ?? PhysicalDpiScope.Enter)();
		if (scope == null) return false;
		FullscreenWindowBounds? bounds = readBounds();
		if (bounds == null || !bounds.Value.Window.IsValid || !bounds.Value.Monitor.IsValid
			|| bounds.Value.Window.Matches(bounds.Value.Monitor)) return false;
		resize(bounds.Value);
		return true;
	}
}

internal sealed class PhysicalDpiScope : IDisposable
{
	private nint _previous;
	private PhysicalDpiScope(nint previous) => _previous = previous;

	internal static IDisposable? Enter()
	{
		nint previous = SetThreadDpiAwarenessContext(-4); // PER_MONITOR_AWARE_V2
		if (previous == 0) previous = SetThreadDpiAwarenessContext(-3); // PER_MONITOR_AWARE
		return previous == 0 ? null : new PhysicalDpiScope(previous);
	}

	public void Dispose()
	{
		if (_previous == 0) return;
		SetThreadDpiAwarenessContext(_previous);
		_previous = 0;
	}

	[DllImport("user32.dll")]
	private static extern nint SetThreadDpiAwarenessContext(nint dpiContext);
}
