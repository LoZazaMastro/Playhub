using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GamingMode.Models;
using GamingMode.Services;

namespace GamingMode;

public sealed class MainWindow : Window
{
	private static readonly Brush Yellow = Brush("#fcba03");

	private static readonly Brush Ink = Brush("#111111");

	private static readonly Brush Paper = Brush("#f7f7f3");

	private static readonly Brush Muted = Brush("#3f3f3f");

	private readonly AgentClient _client = new AgentClient();

	private readonly TextBlock _messageLabel = Text("", 14.0, FontWeights.Normal, Muted);

	private readonly ModeSegment _defaultMode = new ModeSegment(L.T("desktop.mode"), L.T("gaming.mode"));

	private readonly LogoDropdown _logoSelector = new LogoDropdown();

	private readonly Image _logoPreview = new Image();

	private readonly DispatcherTimer _timer = new DispatcherTimer();

	private IReadOnlyList<LogoChoice> _logoChoices = Array.Empty<LogoChoice>();

	private bool _updating;

	public MainWindow()
	{
		base.Title = L.T("app.title");
		base.Width = 900.0;
		base.Height = 660.0;
		base.MinWidth = base.Width;
		base.MaxWidth = base.Width;
		base.MinHeight = base.Height;
		base.MaxHeight = base.Height;
		base.ResizeMode = ResizeMode.NoResize;
		base.WindowStyle = WindowStyle.None;
		base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		base.Background = Brushes.Transparent;
		base.AllowsTransparency = true;
		base.UseLayoutRounding = true;
		base.SnapsToDevicePixels = true;
		base.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
		base.Content = CreateLayout();
		base.Loaded += async delegate
		{
			await InitializeAsync();
		};
		_timer.Interval = TimeSpan.FromSeconds(3.0);
		_timer.Tick += async delegate
		{
			await RefreshStatusAsync();
		};
	}

	private UIElement CreateLayout()
	{
		Border border = new Border();
		border.Background = Yellow;
		border.BorderBrush = Ink;
		border.BorderThickness = new Thickness(2.0);
		border.CornerRadius = new CornerRadius(26.0);
		border.Padding = new Thickness(38.0);
		border.MouseLeftButtonDown += DragWindow;
		Grid grid = new Grid
		{
			RowDefinitions = 
			{
				new RowDefinition
				{
					Height = GridLength.Auto
				},
				new RowDefinition
				{
					Height = GridLength.Auto
				},
				new RowDefinition
				{
					Height = GridLength.Auto
				},
				new RowDefinition
				{
					Height = GridLength.Auto
				},
				new RowDefinition
				{
					Height = GridLength.Auto
				}
			}
		};
		border.Child = grid;
		UIElement element = CreateHeader();
		grid.Children.Add(element);
		Grid.SetRow(element, 0);
		UIElement element2 = CreateModeActions();
		grid.Children.Add(element2);
		Grid.SetRow(element2, 1);
		UIElement element3 = CreateDefaultSelector();
		grid.Children.Add(element3);
		Grid.SetRow(element3, 2);
		UIElement element4 = CreateSplashLogoSelector();
		grid.Children.Add(element4);
		Grid.SetRow(element4, 3);
		UIElement element5 = CreateFooter();
		grid.Children.Add(element5);
		Grid.SetRow(element5, 4);
		return border;
	}

	private UIElement CreateHeader()
	{
		Grid grid = new Grid();
		grid.Margin = new Thickness(0.0, 0.0, 0.0, 22.0);
		grid.MouseLeftButtonDown += DragWindow;
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		TextBlock textBlock = Text(L.T("app.title"), 58.0, FontWeights.Black, Ink);
		textBlock.LineHeight = 62.0;
		textBlock.Margin = new Thickness(0.0, 18.0, 0.0, 0.0);
		grid.Children.Add(textBlock);
		Grid.SetColumn(textBlock, 0);
		Grid.SetRowSpan(textBlock, 2);
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		ChromeButton chromeButton = new ChromeButton("-");
		chromeButton.Click += delegate
		{
			base.WindowState = WindowState.Minimized;
		};
		ChromeButton chromeButton2 = new ChromeButton("X");
		chromeButton2.Click += delegate
		{
			Close();
		};
		stackPanel.Children.Add(chromeButton);
		stackPanel.Children.Add(chromeButton2);
		grid.Children.Add(stackPanel);
		Grid.SetColumn(stackPanel, 1);
		Border element = new Border
		{
			Width = 204.0,
			Height = 58.0,
			Margin = new Thickness(0.0, 18.0, 0.0, 0.0),
			CornerRadius = new CornerRadius(29.0),
			Background = Ink,
			Padding = new Thickness(24.0, 14.0, 24.0, 14.0),
			Child = new Image
			{
				Source = LoadImage("base-logo.png"),
				Stretch = Stretch.Uniform
			}
		};
		grid.Children.Add(element);
		Grid.SetColumn(element, 1);
		Grid.SetRow(element, 1);
		return grid;
	}

	private UIElement CreateModeActions()
	{
		StackPanel obj = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 24.0)
		};
		ActionPill actionPill = new ActionPill(L.T("action.gaming"), Ink, Paper)
		{
			Width = 320.0,
			Height = 58.0,
			Margin = new Thickness(0.0, 0.0, 14.0, 0.0)
		};
		actionPill.Click += async delegate
		{
			await RunActionAsync(_client.SwitchToGamingModeAsync);
		};
		ActionPill actionPill2 = new ActionPill(L.T("action.desktop"), Paper, Ink)
		{
			Width = 320.0,
			Height = 58.0
		};
		actionPill2.Click += async delegate
		{
			await RunActionAsync(_client.SwitchToDesktopModeAsync);
		};
		obj.Children.Add(actionPill);
		obj.Children.Add(actionPill2);
		return obj;
	}

	private UIElement CreateDefaultSelector()
	{
		Border obj = new Border
		{
			Background = Ink,
			CornerRadius = new CornerRadius(32.0),
			Padding = new Thickness(30.0, 24.0, 30.0, 24.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 24.0)
		};
		Grid grid = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition
				{
					Width = new GridLength(240.0)
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			}
		};
		obj.Child = grid;
		TextBlock textBlock = Text(L.T("default.startup"), 20.0, FontWeights.Bold, Paper);
		textBlock.VerticalAlignment = VerticalAlignment.Center;
		grid.Children.Add(textBlock);
		_defaultMode.Width = 360.0;
		_defaultMode.Height = 50.0;
		_defaultMode.SelectedModeChanged += async delegate
		{
			await SetDefaultAsync((_defaultMode.SelectedMode == "Gaming") ? ModeKind.Gaming : ModeKind.Desktop);
		};
		grid.Children.Add(_defaultMode);
		Grid.SetColumn(_defaultMode, 1);
		return obj;
	}

	private UIElement CreateSplashLogoSelector()
	{
		Border obj = new Border
		{
			Background = Ink,
			CornerRadius = new CornerRadius(32.0),
			Padding = new Thickness(30.0, 20.0, 30.0, 20.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 22.0)
		};
		Grid grid = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition
				{
					Width = new GridLength(240.0)
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				},
				new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				}
			}
		};
		obj.Child = grid;
		TextBlock textBlock = Text(L.T("splash.logo"), 20.0, FontWeights.Bold, Paper);
		textBlock.VerticalAlignment = VerticalAlignment.Center;
		grid.Children.Add(textBlock);
		_logoChoices = LoadLogoChoices();
		_logoSelector.Width = 360.0;
		_logoSelector.Height = 50.0;
		_logoSelector.SetItems(_logoChoices);
		_logoSelector.SelectedChanged += async delegate
		{
			if (!_updating && (object)_logoSelector.Selected != null)
			{
				UpdateLogoPreview(_logoSelector.Selected);
				await RunActionAsync(() => _client.SetSplashLogoAsync(_logoSelector.Selected.Path));
			}
		};
		grid.Children.Add(_logoSelector);
		Grid.SetColumn(_logoSelector, 1);
		Border element = new Border
		{
			Width = 150.0,
			Height = 50.0,
			CornerRadius = new CornerRadius(25.0),
			Background = Brushes.Black,
			Padding = new Thickness(20.0, 10.0, 20.0, 10.0),
			HorizontalAlignment = HorizontalAlignment.Right,
			Child = _logoPreview
		};
		grid.Children.Add(element);
		Grid.SetColumn(element, 2);
		LogoChoice logoChoice = _logoChoices.FirstOrDefault();
		if ((object)logoChoice != null)
		{
			_logoSelector.SetSelected(logoChoice);
			UpdateLogoPreview(logoChoice);
		}
		return obj;
	}

	private UIElement CreateFooter()
	{
		Grid obj = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			}
		};
		_messageLabel.Text = L.T("agent.starting");
		_messageLabel.TextWrapping = TextWrapping.Wrap;
		_messageLabel.LineHeight = 20.0;
		_messageLabel.VerticalAlignment = VerticalAlignment.Center;
		obj.Children.Add(_messageLabel);
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		ActionPill actionPill = new ActionPill(L.T("config"), Paper, Ink)
		{
			Width = 140.0,
			Height = 52.0
		};
		actionPill.Click += delegate
		{
			OpenConfigFolder();
		};
		stackPanel.Children.Add(actionPill);
		obj.Children.Add(stackPanel);
		Grid.SetColumn(stackPanel, 1);
		return obj;
	}

	private async Task InitializeAsync()
	{
		_messageLabel.Text = L.T("agent.starting");
		bool flag = await _client.EnsureAgentRunningAsync();
		_messageLabel.Text = (flag ? "" : L.T("agent.unreachable"));
		await RefreshStatusAsync();
		_timer.Start();
	}

	private async Task RunActionAsync(Func<Task<ApiResult?>> action)
	{
		_ = 1;
		try
		{
			ApiResult apiResult = await action();
			_messageLabel.Text = ((apiResult != null && !apiResult.Ok) ? apiResult.Message : "");
			await RefreshStatusAsync();
		}
		catch (Exception exception)
		{
			_messageLabel.Text = FriendlyError(exception);
		}
	}

	private async Task SetDefaultAsync(ModeKind mode)
	{
		if (!_updating)
		{
			await RunActionAsync((mode == ModeKind.Desktop) ? new Func<Task<ApiResult>>(_client.SetDefaultDesktopAsync) : new Func<Task<ApiResult>>(_client.SetDefaultGamingAsync));
		}
	}

	private async Task RefreshStatusAsync()
	{
		try
		{
			ModeStatus modeStatus = await _client.GetStatusAsync();
			if (modeStatus == null)
			{
				_messageLabel.Text = L.T("status.noStatus");
				return;
			}
			_updating = true;
			_defaultMode.SelectedMode = ((modeStatus.DefaultMode == ModeKind.Gaming) ? "Gaming" : "Desktop");
			SetSelectedLogo(modeStatus.SplashLogoPath);
			_updating = false;
		}
		catch (Exception exception)
		{
			_messageLabel.Text = FriendlyError(exception);
		}
		finally
		{
			_updating = false;
		}
	}

	private static void OpenConfigFolder()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode");
		Directory.CreateDirectory(text);
		Process.Start(new ProcessStartInfo
		{
			FileName = text,
			UseShellExecute = true
		});
	}

	private void SetSelectedLogo(string? configuredPath)
	{
		LogoChoice choice = FindLogoChoice(configuredPath);
		_logoSelector.SetSelected(choice);
		UpdateLogoPreview(choice);
	}

	private LogoChoice FindLogoChoice(string? configuredPath)
	{
		if (string.IsNullOrWhiteSpace(configuredPath))
		{
			return _logoChoices.FirstOrDefault() ?? LogoChoice.Playhub;
		}
		string second = Environment.ExpandEnvironmentVariables(configuredPath).Trim().Trim('"');
		foreach (LogoChoice logoChoice in _logoChoices)
		{
			if (logoChoice.Path != null && PathsEqual(logoChoice.Path, second))
			{
				return logoChoice;
			}
		}
		return _logoChoices.FirstOrDefault() ?? LogoChoice.Playhub;
	}

	private void UpdateLogoPreview(LogoChoice choice)
	{
		_logoPreview.Source = ((choice.Path == null) ? LoadImage("base-logo.png") : LoadImageFromPath(choice.Path));
		_logoPreview.Stretch = Stretch.Uniform;
	}

	private static IReadOnlyList<LogoChoice> LoadLogoChoices()
	{
		List<LogoChoice> list = new List<LogoChoice> { LogoChoice.Playhub };
		string path = Path.Combine(AppContext.BaseDirectory, "assets", "logos");
		if (!Directory.Exists(path))
		{
			return list;
		}
		foreach (string item in Directory.EnumerateFiles(path, "*.png").OrderBy(Path.GetFileName))
		{
			list.Add(new LogoChoice(LogoDisplayName(item), item));
		}
		return list;
	}

	private static string LogoDisplayName(string path)
	{
		string text = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
		return text switch
		{
			"asus" => "ASUS", 
			"lenovo" => "Lenovo", 
			"msi" => "MSI", 
			"playstation" => "PlayStation", 
			"rog" => "ROG", 
			"steam-deck" => "Steam Deck", 
			"steamos" => "SteamOS", 
			"xbox" => "Xbox", 
			_ => CultureName(text), 
		};
	}

	private static string CultureName(string name)
	{
		return string.Join(" ", from part in name.Split('-', StringSplitOptions.RemoveEmptyEntries)
			select (part.Length != 0) ? (char.ToUpperInvariant(part[0]) + part.Substring(1, part.Length - 1)) : part);
	}

	private static bool PathsEqual(string first, string second)
	{
		try
		{
			return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
		}
	}

	private static string FriendlyError(Exception exception)
	{
		if (exception is HttpRequestException)
		{
			return L.T("error.unreachable") + " " + exception.Message;
		}
		return L.T("error.unreadable") + " " + exception.Message;
	}

	private void DragWindow(object sender, MouseButtonEventArgs e)
	{
		if (e.ButtonState != MouseButtonState.Pressed || IsInsideInteractive(e.OriginalSource as DependencyObject))
		{
			return;
		}
		try
		{
			e.Handled = true;
			DragMove();
		}
		catch
		{
		}
	}

	private static bool IsInsideInteractive(DependencyObject? current)
	{
		while (current != null)
		{
			if ((current is ChromeButton || current is ActionPill || current is ModeSegment || current is LogoDropdown) ? true : false)
			{
				return true;
			}
			current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
		}
		return false;
	}

	private static TextBlock Text(string text, double size, FontWeight weight, Brush color)
	{
		return new TextBlock
		{
			Text = text,
			FontSize = size,
			FontWeight = weight,
			Foreground = color,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
	}

	private static ImageSource? LoadImage(string fileName)
	{
		return LoadImageFromPath(Path.Combine(AppContext.BaseDirectory, "assets", fileName));
	}

	private static ImageSource? LoadImageFromPath(string path)
	{
		if (!File.Exists(path))
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

	private static SolidColorBrush Brush(string hex)
	{
		return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
	}
}
