using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GamingMode.Models;

namespace GamingMode.Services;

public sealed class SplashScreenService : IDisposable
{
	private readonly FileLogger _logger;

	private readonly object _sync = new object();

	private sealed class Surface
	{
		internal readonly CancellationTokenSource Cancellation = new();
		internal readonly long Started = Environment.TickCount64;
		internal Dispatcher? Dispatcher;
		internal Thread? Thread;
		internal Window? Window;
		internal nint Handle, EventHook;
		internal uint NativeThread;
		internal WinEventCallback? Callback;
	}
	private Surface? _surface;
	private Func<bool>? _foregroundGuard;
	private (int Left, int Top, int Width, int Height)? _physicalBounds;
	public bool Running { get { lock (_sync) return _surface is { } surface && !surface.Cancellation.IsCancellationRequested && surface.Thread?.IsAlive == true; } }
	public SplashScreenService(FileLogger logger) => _logger = logger;
	internal void SetForegroundGuard(Func<bool> allowed) { lock (_sync) _foregroundGuard = allowed; }
	internal void SetTargetPhysicalBounds(int left, int top, int width, int height)
	{ if (width > 0 && height > 0) lock (_sync) _physicalBounds = (left, top, width, height); }
	public void Show(GamingSplashSettings settings, int failSafeCloseMs = 150000)
	{
		if (!settings.Enabled) return;
		string? logoPath = ResolveLogoPath(settings.LogoPath);
		ShowCore(() => CreateSplashWindow(logoPath, settings), failSafeCloseMs);
	}
	public void ShowTransition(ModeKind mode, string? language, GamingSplashSettings? settings = null, int failSafeCloseMs = 90000)
	{
		ShowCore(() =>
		{
			Window window = CreateSplashWindow(null);
			window.Left = 0; window.Top = 0;
			window.Width = SystemParameters.PrimaryScreenWidth; window.Height = SystemParameters.PrimaryScreenHeight;
			window.Content = ModeTransitionVisual.Create(mode, language, settings);
			return window;
		}, failSafeCloseMs);
	}
	private void ShowCore(Func<Window> createWindow, int failSafeCloseMs)
	{
		Surface surface = new(); Func<bool>? allowed; (int Left,int Top,int Width,int Height)? bounds;
		lock (_sync)
		{
			if (_surface?.Thread?.IsAlive == true) return;
			_surface = surface; allowed = _foregroundGuard; bounds = _physicalBounds;
		}
		ManualResetEventSlim ready = new(false);
		bool Allowed() { try { return !surface.Cancellation.IsCancellationRequested && (allowed?.Invoke() ?? true); } catch { return false; } }
		surface.Thread = new Thread(() =>
		{
			DispatcherTimer? failSafe = null;
			nint previousDpi = SetThreadDpiAwarenessContext(new nint(-4));
			try
			{
				var dispatcher = Dispatcher.CurrentDispatcher; surface.Dispatcher = dispatcher; surface.NativeThread = GetCurrentThreadId();
				if (!Allowed()) { surface.Cancellation.Cancel(); ready.Set(); return; }
				var window = createWindow(); surface.Window = window;
				window.SourceInitialized += (_, _) =>
				{
					surface.Handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
					if (bounds is { } target) SetWindowPos(surface.Handle, new nint(-1), target.Left, target.Top, target.Width, target.Height, 0x0010);
					if (allowed is not null)
					{
						surface.Callback = (_, _, _, _, _, _, _) => { if (!Allowed()) CancelSurface(surface); };
						surface.EventHook = SetWinEventHook(3, 3, 0, surface.Callback, 0, 0, 0);
						if (surface.EventHook == 0) CancelSurface(surface);
					}
				};
				window.ContentRendered += (_, _) =>
				{
					_logger.Info($"Gaming splash first frame rendered after {Environment.TickCount64 - surface.Started} ms.");
					ready.Set();
					if (!Allowed()) CancelSurface(surface);
				};
				window.Closed += (_, _) =>
				{
					failSafe?.Stop(); surface.Cancellation.Cancel();
					if (surface.EventHook != 0) { UnhookWinEvent(surface.EventHook); surface.EventHook = 0; }
					surface.Handle = 0; ready.Set(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
				};
				if (!Allowed()) { surface.Cancellation.Cancel(); ready.Set(); return; }
				window.Show();
				if (bounds is { } shownBounds && surface.Handle != 0)
					SetWindowPos(surface.Handle, new nint(-1), shownBounds.Left, shownBounds.Top, shownBounds.Width, shownBounds.Height, 0x0010);
				if (!Allowed()) CancelSurface(surface);
				failSafe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Clamp(failSafeCloseMs, 1000, 300000)) };
				failSafe.Tick += (_, _) => { _logger.Info("Gaming splash deadline reached."); CancelSurface(surface); };
				failSafe.Start(); Dispatcher.Run();
			}
			catch (Exception error) { surface.Cancellation.Cancel(); _logger.Error("Gaming splash could not be shown.", error); ready.Set(); }
			finally
			{
				failSafe?.Stop();
				if (surface.EventHook != 0) { UnhookWinEvent(surface.EventHook); surface.EventHook = 0; }
				if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi);
				lock (_sync) if (ReferenceEquals(_surface, surface)) _surface = null;
			}
		}) { IsBackground = true, Name = "Gaming Mode Splash" };
		surface.Thread.SetApartmentState(ApartmentState.STA); surface.Thread.Start();
		if (!ready.Wait(TimeSpan.FromSeconds(3))) _logger.Info("Gaming splash first frame is still pending after 3000 ms.");
	}
	private static void CancelSurface(Surface surface)
	{
		surface.Cancellation.Cancel();
		// Lower only our own no-activate surface immediately, before its Dispatcher
		// can process a close. A foreign foreground window must stay accessible.
		if (surface.Handle != 0 && GetWindowThreadProcessId(surface.Handle, out uint owner) == surface.NativeThread
			&& owner == Environment.ProcessId) SetWindowPos(surface.Handle, new nint(-2), 0, 0, 0, 0, 0x0013);
		var dispatcher = surface.Dispatcher;
		if (dispatcher is null || dispatcher.HasShutdownStarted) return;
		dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
		{
			try { surface.Window?.Close(); } catch (InvalidOperationException) { }
			dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
		}));
	}
	internal void CancelImmediately() { Surface? surface; lock (_sync) surface = _surface; if (surface is not null) CancelSurface(surface); }
	public async Task HideAsync(int minVisibleMs = 0, bool fade = false, int fadeMs = 450)
	{
		Surface? surface; lock (_sync) surface = _surface;
		if (surface?.Dispatcher is not { } dispatcher || surface.Cancellation.IsCancellationRequested) return;
		try
		{
			long remaining = minVisibleMs - (Environment.TickCount64 - surface.Started);
			if (remaining > 0) await Task.Delay((int)Math.Min(remaining,120000), surface.Cancellation.Token).ConfigureAwait(false);
			if (surface.Cancellation.IsCancellationRequested || dispatcher.HasShutdownStarted) return;
			var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			using var registration = surface.Cancellation.Token.Register(() => completion.TrySetResult());
			await dispatcher.InvokeAsync(() =>
			{
				if (surface.Cancellation.IsCancellationRequested) { completion.TrySetResult(); return; }
				if (!fade || surface.Window is not { } window) { CancelSurface(surface); completion.TrySetResult(); return; }
				var animation = new DoubleAnimation { From = window.Opacity, To = 0, Duration = TimeSpan.FromMilliseconds(Math.Clamp(fadeMs,100,3000)), FillBehavior = FillBehavior.HoldEnd };
				animation.Completed += (_, _) => { CancelSurface(surface); completion.TrySetResult(); };
				window.BeginAnimation(UIElement.OpacityProperty, animation);
			}, DispatcherPriority.Send);
			await completion.Task.WaitAsync(TimeSpan.FromMilliseconds(Math.Clamp(fadeMs,100,3000)+1000)).ConfigureAwait(false);
		}
		catch (OperationCanceledException) { }
		catch (Exception error) { _logger.Error("Gaming splash close failed.", error); CancelSurface(surface); }
	}
	public void Dispose()
	{
		Surface? surface; lock (_sync) surface = _surface;
		if (surface is null) return;
		CancelSurface(surface);
		if (surface.Thread is { IsAlive:true } thread && thread != Thread.CurrentThread) thread.Join(1000);
	}
	private delegate void WinEventCallback(nint hook,uint kind,nint hwnd,int objectId,int childId,uint thread,uint tick);
	[DllImport("user32.dll")] private static extern nint SetWinEventHook(uint minimum,uint maximum,nint module,WinEventCallback callback,uint process,uint thread,uint flags);
	[DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
	[DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
	[DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
	[DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd,out uint pid);

	internal static void ShowPreview(string variant, GamingSplashSettings settings, string? language)
	{
		var window = CreateSplashWindow(ResolveLogoPath(settings.LogoPath), settings, interactive: true);
		window.Left = 0;
		window.Top = 0;
		window.Width = SystemParameters.PrimaryScreenWidth;
		window.Height = SystemParameters.PrimaryScreenHeight;
		if (variant != "boot")
			window.Content = ModeTransitionVisual.Create(variant == "desktop" ? ModeKind.Desktop : ModeKind.Gaming, language, settings);
		if (window.Content is UIElement content) content.IsHitTestVisible = true;
		window.PreviewKeyDown += (_, e) =>
		{
			if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; window.Close(); }
		};
		window.PreviewMouseDown += (_, e) => { e.Handled = true; window.Close(); };
		new Application { ShutdownMode = ShutdownMode.OnMainWindowClose }.Run(window);
	}

	private static Window CreateSplashWindow(string? logoPath, GamingSplashSettings? settings = null, bool interactive = false)
	{
		Grid grid = new Grid
		{
			Background = Brushes.Black,
			ClipToBounds = true
		};
		ModeTransitionVisual.AddGlows(grid, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight, settings);
		ImageSource imageSource = LoadImage(logoPath);
		if (imageSource != null)
		{
			grid.Children.Add(new Image
			{
				Source = imageSource,
				Effect = ModeTransitionVisual.TextShadow(),
				Stretch = Stretch.Uniform,
				Width = Math.Min(460.0, SystemParameters.VirtualScreenWidth * 0.28) * Playhub.Shared.SplashLogoScale.ForPath(logoPath),
				Height = 180.0 * Playhub.Shared.SplashLogoScale.ForPath(logoPath),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			});
		}
		else
		{
			grid.Children.Add(new TextBlock
			{
				Text = "playhub",
				Effect = ModeTransitionVisual.TextShadow(),
				Foreground = Brushes.White,
				FontSize = 56.0,
				FontWeight = FontWeights.Bold,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			});
		}
		Window window = new Window
		{
			WindowStyle = WindowStyle.None,
			ResizeMode = ResizeMode.NoResize,
			ShowInTaskbar = false,
			ShowActivated = interactive,
			Topmost = true,
			Background = Brushes.Black,
			Content = grid,
			Left = SystemParameters.VirtualScreenLeft,
			Top = SystemParameters.VirtualScreenTop,
			Width = SystemParameters.VirtualScreenWidth,
			Height = SystemParameters.VirtualScreenHeight,
			WindowStartupLocation = WindowStartupLocation.Manual
		};
		window.SourceInitialized += delegate
		{
			if (interactive) return;
			try
			{
				nint handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
				if (handle != 0)
				{
					nint exStyle = GetWindowLongPtr(handle, -20);
					SetWindowLongPtr(handle, -20, new IntPtr(exStyle.ToInt64() | 0x8000000L | 0x20L | 0x80L));
				}
			}
			catch
			{
			}
		};
		return window;
	}

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int width, int height, uint flags);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

	private static ImageSource? LoadImage(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return null;
		}
		BitmapImage bitmapImage = new BitmapImage();
		bitmapImage.BeginInit();
		bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
		bitmapImage.UriSource = new Uri(path, UriKind.Absolute);
		bitmapImage.EndInit();
		bitmapImage.Freeze();
		return bitmapImage;
	}

	private static string? ResolveLogoPath(string? configuredPath)
	{
		if (!string.IsNullOrWhiteSpace(configuredPath))
		{
			string text = Environment.ExpandEnvironmentVariables(configuredPath).Trim().Trim('"');
			if (File.Exists(text))
			{
				return text;
			}
		}
		string text2 = Path.Combine(AppContext.BaseDirectory, "assets", "base-logo.png");
		if (!File.Exists(text2))
		{
			return null;
		}
		return text2;
	}
}
