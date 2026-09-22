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

	private DateTimeOffset? _shownAt;

	private Dispatcher? _dispatcher;

	private Thread? _thread;

	private Window? _window;

	public bool Running
	{
		get
		{
			lock (_sync)
			{
				return _thread?.IsAlive ?? false;
			}
		}
	}

	public SplashScreenService(FileLogger logger)
	{
		_logger = logger;
	}

	public void Show(GamingSplashSettings settings, int failSafeCloseMs = 150000)
	{
		if (!settings.Enabled)
		{
			return;
		}
		string? logoPath = ResolveLogoPath(settings.LogoPath);
		ShowCore(() => CreateSplashWindow(logoPath, settings), failSafeCloseMs);
	}

	public void ShowTransition(ModeKind mode, string? language, GamingSplashSettings? settings = null)
	{
		ShowCore(() =>
		{
			Window window = CreateSplashWindow(null);
			window.Left = 0;
			window.Top = 0;
			window.Width = SystemParameters.PrimaryScreenWidth;
			window.Height = SystemParameters.PrimaryScreenHeight;
			window.Content = ModeTransitionVisual.Create(mode, language, settings);
			return window;
		}, 90000);
	}

	private void ShowCore(Func<Window> createWindow, int failSafeCloseMs)
	{
		lock (_sync)
		{
			Thread thread = _thread;
			if (thread != null && thread.IsAlive)
			{
				return;
			}
			_shownAt = DateTimeOffset.Now;
		}
		ManualResetEventSlim ready = new ManualResetEventSlim(initialState: false);
		var firstFrameTimer = System.Diagnostics.Stopwatch.StartNew();
		int failSafeMs = Math.Clamp(failSafeCloseMs, 5000, 300000);
		Thread thread2 = new Thread((ThreadStart)delegate
		{
			try
			{
				Dispatcher currentDispatcher = Dispatcher.CurrentDispatcher;
				Window window = createWindow();
				window.ContentRendered += (_, _) =>
					_logger.Info($"Gaming splash first frame rendered after {firstFrameTimer.ElapsedMilliseconds} ms.");
				lock (_sync)
				{
					_dispatcher = currentDispatcher;
					_window = window;
				}
				window.Show();
				DispatcherTimer failSafeTimer = new DispatcherTimer
				{
					Interval = TimeSpan.FromMilliseconds(failSafeMs)
				};
				failSafeTimer.Tick += delegate
				{
					failSafeTimer.Stop();
					try
					{
						_logger.Info("Gaming splash screen fail-safe closed the window.");
						window.Close();
					}
					catch
					{
					}
					currentDispatcher.InvokeShutdown();
				};
				failSafeTimer.Start();
				ready.Set();
				Dispatcher.Run();
			}
			catch (Exception exception)
			{
				_logger.Error("Gaming splash screen could not be shown.", exception);
				ready.Set();
			}
		})
		{
			IsBackground = true,
			Name = "Gaming Mode Splash"
		};
		thread2.SetApartmentState(ApartmentState.STA);
		lock (_sync)
		{
			_thread = thread2;
		}
		thread2.Start();
		if (!ready.Wait(TimeSpan.FromSeconds(3.0)))
			_logger.Info("Gaming splash creation is still pending after 3000 ms.");
	}

	public async Task HideAsync(int minVisibleMs = 0, bool fade = false, int fadeMs = 450)
	{
		Dispatcher dispatcher;
		Thread thread;
		Window window;
		DateTimeOffset? shownAt;
		lock (_sync)
		{
			dispatcher = _dispatcher;
			thread = _thread;
			window = _window;
			shownAt = _shownAt;
			_dispatcher = null;
			_thread = null;
			_window = null;
			_shownAt = null;
		}
		if (dispatcher == null)
		{
			return;
		}
		if (shownAt.HasValue && minVisibleMs > 0)
		{
			int num = minVisibleMs - (int)(DateTimeOffset.Now - shownAt.Value).TotalMilliseconds;
			if (num > 0)
			{
				await Task.Delay(num);
			}
		}
		try
		{
			Task hideTask = (await dispatcher.InvokeAsync(delegate
			{
				TaskCompletionSource completion = new TaskCompletionSource();
				if (!fade || window == null)
				{
					window?.Close();
					dispatcher.InvokeShutdown();
					completion.SetResult();
					return completion.Task;
				}
				DoubleAnimation doubleAnimation = new DoubleAnimation
				{
					From = window.Opacity,
					To = 0.0,
					Duration = TimeSpan.FromMilliseconds(Math.Clamp(fadeMs, 100, 3000)),
					FillBehavior = FillBehavior.Stop
				};
				doubleAnimation.Completed += delegate
				{
					window.Opacity = 0.0;
					window.Close();
					dispatcher.InvokeShutdown();
					completion.SetResult();
				};
				window.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
				return completion.Task;
			}).Task);
			await Task.WhenAny(hideTask, Task.Delay(Math.Clamp(fadeMs, 100, 3000) + 5000));
			if (thread != null && thread.IsAlive)
			{
				thread.Join(TimeSpan.FromSeconds(1.0));
			}
			_logger.Info("Gaming splash screen hidden.");
		}
		catch (Exception exception)
		{
			_logger.Error("Gaming splash screen could not be hidden.", exception);
		}
	}

	public void Dispose()
	{
		HideAsync().GetAwaiter().GetResult();
	}

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
		// Steam may create its fullscreen window after us. Keep the curtain above it
		// without taking keyboard/controller focus from the destination session.
		var foregroundTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
		foregroundTimer.Tick += (_, _) =>
		{
			nint handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
			if (handle != 0) SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
		};
		window.ContentRendered += (_, _) => foregroundTimer.Start();
		window.Closed += (_, _) => foregroundTimer.Stop();
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
