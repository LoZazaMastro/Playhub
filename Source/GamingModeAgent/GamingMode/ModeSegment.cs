using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GamingMode;

public sealed class ModeSegment : Border
{
	private readonly TextBlock _leftText;

	private readonly TextBlock _rightText;

	private readonly Border _left;

	private readonly Border _right;

	private string _selectedMode = "Desktop";

	public string SelectedMode
	{
		get
		{
			return _selectedMode;
		}
		set
		{
			if (!_selectedMode.Equals(value, StringComparison.OrdinalIgnoreCase))
			{
				_selectedMode = value;
				Update();
				this.SelectedModeChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	public event EventHandler? SelectedModeChanged;

	public ModeSegment(string left, string right)
	{
		base.BorderBrush = AppBrushes.Paper;
		base.BorderThickness = new Thickness(2.0);
		base.CornerRadius = new CornerRadius(25.0);
		base.Padding = new Thickness(4.0);
		base.Background = Brushes.Transparent;
		base.Cursor = Cursors.Hand;
		Grid grid = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition(),
				new ColumnDefinition()
			}
		};
		Child = grid;
		_left = SegmentCell();
		_right = SegmentCell();
		_leftText = SegmentText(left);
		_rightText = SegmentText(right);
		_left.Child = _leftText;
		_right.Child = _rightText;
		grid.Children.Add(_left);
		grid.Children.Add(_right);
		Grid.SetColumn(_right, 1);
		_left.MouseLeftButtonUp += delegate
		{
			SelectedMode = "Desktop";
		};
		_right.MouseLeftButtonUp += delegate
		{
			SelectedMode = "Gaming";
		};
		Update();
	}

	private void Update()
	{
		bool flag = _selectedMode.Equals("Desktop", StringComparison.OrdinalIgnoreCase);
		_left.Background = (flag ? AppBrushes.Yellow : Brushes.Transparent);
		_right.Background = (flag ? Brushes.Transparent : AppBrushes.Yellow);
		_leftText.Foreground = (flag ? AppBrushes.Ink : AppBrushes.Paper);
		_rightText.Foreground = (flag ? AppBrushes.Paper : AppBrushes.Ink);
	}

	private static Border SegmentCell()
	{
		return new Border
		{
			CornerRadius = new CornerRadius(21.0),
			Margin = new Thickness(0.0)
		};
	}

	private static TextBlock SegmentText(string text)
	{
		return new TextBlock
		{
			Text = text,
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			TextAlignment = TextAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
	}
}
