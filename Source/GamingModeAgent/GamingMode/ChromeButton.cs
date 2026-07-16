using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GamingMode;

public sealed class ChromeButton : Border
{
	public event EventHandler? Click;

	public ChromeButton(string text)
	{
		base.Width = 42.0;
		base.Height = 42.0;
		base.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
		base.CornerRadius = new CornerRadius(21.0);
		base.Background = AppBrushes.Ink;
		base.Cursor = Cursors.Hand;
		Child = new TextBlock
		{
			Text = text,
			Foreground = AppBrushes.Paper,
			FontSize = 17.0,
			FontWeight = FontWeights.Bold,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		base.MouseLeftButtonUp += delegate
		{
			this.Click?.Invoke(this, EventArgs.Empty);
		};
		base.MouseEnter += delegate
		{
			base.Opacity = 0.85;
		};
		base.MouseLeave += delegate
		{
			base.Opacity = 1.0;
		};
	}
}
