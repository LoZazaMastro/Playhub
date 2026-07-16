using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GamingMode;

public sealed class ActionPill : Border
{
	private readonly TextBlock _label;

	private bool _enabled = true;

	public bool IsActionEnabled
	{
		get
		{
			return _enabled;
		}
		set
		{
			_enabled = value;
			base.Opacity = (value ? 1.0 : 0.45);
			base.Cursor = (value ? Cursors.Hand : Cursors.Arrow);
		}
	}

	public event EventHandler? Click;

	public ActionPill(string text, Brush background, Brush foreground)
	{
		base.Background = background;
		base.CornerRadius = new CornerRadius(28.0);
		base.BorderBrush = AppBrushes.Ink;
		base.BorderThickness = new Thickness(1.4);
		base.Cursor = Cursors.Hand;
		base.Padding = new Thickness(22.0, 0.0, 22.0, 0.0);
		_label = new TextBlock
		{
			Text = text,
			Foreground = foreground,
			FontSize = 19.0,
			FontWeight = FontWeights.Bold,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			TextAlignment = TextAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		Child = _label;
		base.MouseLeftButtonUp += delegate
		{
			if (IsActionEnabled)
			{
				this.Click?.Invoke(this, EventArgs.Empty);
			}
		};
		base.MouseEnter += delegate
		{
			if (IsActionEnabled)
			{
				base.Opacity = 0.88;
			}
		};
		base.MouseLeave += delegate
		{
			base.Opacity = (IsActionEnabled ? 1.0 : 0.45);
		};
	}
}
