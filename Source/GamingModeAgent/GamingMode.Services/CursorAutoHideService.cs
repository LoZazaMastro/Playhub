using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GamingMode.Services;

public sealed class CursorAutoHideService : IDisposable
{
	private struct Point
	{
		public int X;

		public int Y;
	}

	private const int SpiSetCursors = 87;

	private static readonly int[] SystemCursorIds = new int[18]
	{
		32512, 32513, 32514, 32515, 32516, 32640, 32641, 32642, 32643, 32644,
		32645, 32646, 32648, 32649, 32650, 32651, 32671, 32672
	};

	private readonly object _sync = new object();

	private readonly FileLogger _logger;

	private CancellationTokenSource? _cancellation;

	private Task? _worker;

	private bool _hidden;

	private int _hideAfterMs = 2200;

	public bool Running { get; private set; }

	public bool CursorHidden
	{
		get
		{
			lock (_sync)
			{
				return _hidden;
			}
		}
	}

	public int HideAfterMs
	{
		get
		{
			lock (_sync)
			{
				return _hideAfterMs;
			}
		}
	}

	public CursorAutoHideService(FileLogger logger)
	{
		_logger = logger;
	}

	public void Start(int hideAfterMs)
	{
		lock (_sync)
		{
			_hideAfterMs = Math.Clamp(hideAfterMs, 500, 10000);
			if (!Running)
			{
				_cancellation = new CancellationTokenSource();
				Running = true;
				_worker = Task.Run(() => RunAsync(_cancellation.Token));
			}
		}
	}

	public void Stop()
	{
		CancellationTokenSource cancellation;
		Task worker;
		lock (_sync)
		{
			cancellation = _cancellation;
			worker = _worker;
			_cancellation = null;
			_worker = null;
			Running = false;
		}
		try
		{
			cancellation?.Cancel();
			worker?.Wait(650);
		}
		catch
		{
		}
		finally
		{
			cancellation?.Dispose();
			RestoreCursor();
		}
	}

	public void RestoreCursor()
	{
		lock (_sync)
		{
			RestoreSystemCursors();
			_hidden = false;
		}
	}

	public void Dispose()
	{
		Stop();
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			GetCursorPos(out var lastPosition);
			DateTimeOffset lastMovedAt = DateTimeOffset.UtcNow;
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(120, cancellationToken);
				if (!GetCursorPos(out var position))
				{
					continue;
				}
				if (position.X != lastPosition.X || position.Y != lastPosition.Y)
				{
					lastPosition = position;
					lastMovedAt = DateTimeOffset.UtcNow;
					if (CursorHidden)
					{
						RestoreCursor();
					}
				}
				else if (!CursorHidden && DateTimeOffset.UtcNow - lastMovedAt >= TimeSpan.FromMilliseconds(HideAfterMs))
				{
					HideCursor();
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			_logger.Error("Mouse cursor auto-hide crashed.", exception);
			RestoreCursor();
		}
	}

	private void HideCursor()
	{
		lock (_sync)
		{
			if (_hidden)
			{
				return;
			}
			int[] systemCursorIds = SystemCursorIds;
			foreach (int id in systemCursorIds)
			{
				nint num = CreateBlankCursor();
				if (num != IntPtr.Zero)
				{
					SetSystemCursor(num, id);
				}
			}
			_hidden = true;
		}
	}

	private static nint CreateBlankCursor()
	{
		byte[] andPlane = new byte[1] { 255 };
		byte[] xorPlane = new byte[1];
		return CreateCursor(IntPtr.Zero, 0, 0, 1, 1, andPlane, xorPlane);
	}

	private static void RestoreSystemCursors()
	{
		SystemParametersInfo(87, 0, IntPtr.Zero, 0);
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool GetCursorPos(out Point position);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint CreateCursor(nint instance, int xHotSpot, int yHotSpot, int width, int height, byte[] andPlane, byte[] xorPlane);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetSystemCursor(nint cursor, int id);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SystemParametersInfo(int action, int param, nint value, int flags);
}
