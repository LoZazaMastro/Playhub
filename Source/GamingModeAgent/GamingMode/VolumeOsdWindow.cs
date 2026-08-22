using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GamingMode.Services;

namespace GamingMode;

// INDICATORE DEL VOLUME.
//
// L'unica finestra nostra che sopravvive al passaggio dentro il plugin, e c'e'
// un motivo preciso: in Gaming Mode Explorer non e' in esecuzione, e
// l'indicatore del volume di Windows lo disegna Explorer. Senza di lui, alzando
// il volume non si vedrebbe niente.
//
// E' minuscola e innocua: non prende mai il fuoco (ShowActivated = false),
// compare in basso al centro per un secondo e mezzo e sparisce. Non intercetta
// input, non tocca Steam, non entra nell'ordine delle finestre attivabili.
// Nessuno dei problemi della vecchia Dashboard puo' ripresentarsi qui.
internal sealed class VolumeOsdWindow : Window
{
	private readonly TextBlock _value;
	private readonly Border _fill;
	private readonly Grid _track;
	private readonly DispatcherTimer _timer;
	private int _level;
	private Color _accent = Colors.White;
	private bool _allowClose;

	public VolumeOsdWindow()
	{
		WindowStyle = WindowStyle.None;
		ResizeMode = ResizeMode.NoResize;
		ShowInTaskbar = false;
		ShowActivated = false;
		Topmost = true;
		Width = 410;
		Height = 74;
		Background = Brushes.Transparent;
		AllowsTransparency = true;
		Opacity = 0;
		Visibility = Visibility.Hidden;
		WindowStartupLocation = WindowStartupLocation.Manual;
		Focusable = false;

		Grid content = new();
		content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
		content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
		content.Children.Add(new TextBlock
		{
			Text = "",
			FontFamily = new FontFamily("Segoe Fluent Icons"),
			FontSize = 24,
			Foreground = Brushes.White,
			VerticalAlignment = VerticalAlignment.Center
		});

		Grid track = new() { Height = 10, VerticalAlignment = VerticalAlignment.Center };
		track.Children.Add(new Border
		{
			CornerRadius = new CornerRadius(5),
			Background = new SolidColorBrush(Color.FromArgb(58, 255, 255, 255))
		});
		_fill = new Border
		{
			CornerRadius = new CornerRadius(5),
			HorizontalAlignment = HorizontalAlignment.Left,
			Width = 0
		};
		track.Children.Add(_fill);
		track.SizeChanged += (_, _) => ApplyFill();
		_track = track;
		Grid.SetColumn(track, 1);
		content.Children.Add(track);

		_value = new TextBlock
		{
			Foreground = Brushes.White,
			FontSize = 18,
			FontWeight = FontWeights.SemiBold,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(_value, 2);
		content.Children.Add(_value);

		Grid glass = new();
		glass.Children.Add(new Border
		{
			CornerRadius = new CornerRadius(26),
			Background = new LinearGradientBrush(
				Color.FromArgb(216, 32, 36, 42),
				Color.FromArgb(234, 13, 15, 18),
				new Point(0, 0),
				new Point(0.35, 1))
		});
		glass.Children.Add(new Border
		{
			CornerRadius = new CornerRadius(26),
			BorderThickness = new Thickness(1),
			BorderBrush = new LinearGradientBrush(
				Color.FromArgb(130, 255, 255, 255),
				Color.FromArgb(26, 255, 255, 255),
				new Point(0, 0),
				new Point(0, 1)),
			Padding = new Thickness(22, 14, 22, 14),
			Child = content
		});
		Content = glass;

		ApplyAccent(ReadPlayhubAccent());
		Closing += (_, args) => { if (!_allowClose) { args.Cancel = true; Hide(); } };
		_timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1450), DispatcherPriority.Background, (_, _) => HideAnimated(), Dispatcher);
		_timer.Stop();
	}

	public void CloseImmediately()
	{
		_allowClose = true;
		_timer.Stop();
		Close();
	}

	// Il colore si rimette a ogni comparsa: cambiare tema non deve richiedere di
	// ricostruire la finestra.
	public void ApplyAccent(Color accent)
	{
		if (accent == _accent) return;
		_accent = accent;
		_fill.Background = new LinearGradientBrush(
			Color.FromArgb(255, accent.R, accent.G, accent.B),
			Color.FromArgb(220, (byte)Math.Min(255, accent.R + 45), (byte)Math.Min(255, accent.G + 45), (byte)Math.Min(255, accent.B + 45)),
			new Point(0, 0),
			new Point(1, 1));
	}

	public void ShowSnapshot(SystemVolumeSnapshot snapshot)
	{
		if (!snapshot.Available) return;
		_level = snapshot.Level;
		ApplyFill();
		_value.Text = snapshot.Muted ? "Muto" : $"{snapshot.Level}%";
		Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
		Top = SystemParameters.PrimaryScreenHeight - Height - 44;
		Visibility = Visibility.Visible;
		Show();
		BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 1, TimeSpan.FromMilliseconds(90)));
		_timer.Stop();
		_timer.Start();
	}

	private void ApplyFill()
	{
		double width = _track.ActualWidth * Math.Clamp(_level, 0, 100) / 100d;
		if (double.IsNaN(width) || width < 0) width = 0;
		_fill.BeginAnimation(FrameworkElement.WidthProperty,
			new DoubleAnimation(width, TimeSpan.FromMilliseconds(220))
			{
				EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
			});
	}

	private void HideAnimated()
	{
		_timer.Stop();
		DoubleAnimation fade = new(Opacity, 0, TimeSpan.FromMilliseconds(200))
		{
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
		};
		fade.Completed += (_, _) =>
		{
			if (Opacity <= 0.02) Visibility = Visibility.Hidden;
		};
		BeginAnimation(OpacityProperty, fade);
	}

	// Colore accento scelto dall'utente nelle impostazioni di Playhub.
	public static Color ReadPlayhubAccent()
	{
		try
		{
			string path = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				"Playhub", "settings.json");
			if (File.Exists(path))
			{
				using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
				if (document.RootElement.TryGetProperty("AccentColor", out System.Text.Json.JsonElement value)
					&& value.ValueKind == System.Text.Json.JsonValueKind.String)
				{
					object? parsed = ColorConverter.ConvertFromString(value.GetString() ?? "");
					if (parsed is Color color) return color;
				}
			}
		}
		catch
		{
		}
		return Colors.White;
	}
}
