using System;
using System.IO;
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

	public void Show(GamingSplashSettings settings)
	{
		if (!settings.Enabled)
		{
			return;
		}
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
		string logoPath = ResolveLogoPath(settings.LogoPath);
		Thread thread2 = new Thread((ThreadStart)delegate
		{
			try
			{
				Dispatcher currentDispatcher = Dispatcher.CurrentDispatcher;
				Window window = CreateSplashWindow(logoPath);
				lock (_sync)
				{
					_dispatcher = currentDispatcher;
					_window = window;
				}
				window.Show();
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
		ready.Wait(TimeSpan.FromSeconds(3.0));
		_logger.Info("Gaming splash screen shown.");
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
			await (await dispatcher.InvokeAsync(delegate
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

	private static Window CreateSplashWindow(string? logoPath)
	{
		Grid grid = new Grid
		{
			Background = Brushes.Black
		};
		ImageSource imageSource = LoadImage(logoPath);
		if (imageSource != null)
		{
			grid.Children.Add(new Image
			{
				Source = imageSource,
				Stretch = Stretch.Uniform,
				Width = Math.Min(460.0, SystemParameters.VirtualScreenWidth * 0.28),
				Height = 180.0,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			});
		}
		else
		{
			grid.Children.Add(new TextBlock
			{
				Text = "playhub",
				Foreground = Brushes.White,
				FontSize = 56.0,
				FontWeight = FontWeights.Bold,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			});
		}
		return new Window
		{
			WindowStyle = WindowStyle.None,
			ResizeMode = ResizeMode.NoResize,
			ShowInTaskbar = false,
			Topmost = true,
			Background = Brushes.Black,
			Content = grid,
			Left = SystemParameters.VirtualScreenLeft,
			Top = SystemParameters.VirtualScreenTop,
			Width = SystemParameters.VirtualScreenWidth,
			Height = SystemParameters.VirtualScreenHeight,
			WindowStartupLocation = WindowStartupLocation.Manual
		};
	}

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
