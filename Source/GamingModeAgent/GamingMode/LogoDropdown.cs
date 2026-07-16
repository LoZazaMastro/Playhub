using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace GamingMode;

public sealed class LogoDropdown : Border
{
	private readonly TextBlock _label;

	private readonly Popup _popup;

	private readonly StackPanel _itemsHost;

	private IReadOnlyList<LogoChoice> _items = Array.Empty<LogoChoice>();

	private LogoChoice? _selected;

	public LogoChoice? Selected => _selected;

	public event EventHandler? SelectedChanged;

	public LogoDropdown()
	{
		base.Background = AppBrushes.Paper;
		base.BorderBrush = AppBrushes.Paper;
		base.BorderThickness = new Thickness(2.0);
		base.CornerRadius = new CornerRadius(25.0);
		base.Cursor = Cursors.Hand;
		base.Padding = new Thickness(24.0, 0.0, 16.0, 0.0);
		Grid grid = new Grid
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
		Child = grid;
		_label = new TextBlock
		{
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Foreground = AppBrushes.Ink,
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		grid.Children.Add(_label);
		TextBlock element = new TextBlock
		{
			Text = "v",
			FontSize = 16.0,
			FontWeight = FontWeights.Black,
			Foreground = AppBrushes.Ink,
			Margin = new Thickness(16.0, 0.0, 0.0, 1.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		grid.Children.Add(element);
		Grid.SetColumn(element, 1);
		_itemsHost = new StackPanel();
		_popup = new Popup
		{
			PlacementTarget = this,
			Placement = PlacementMode.Bottom,
			AllowsTransparency = true,
			StaysOpen = false,
			PopupAnimation = PopupAnimation.Fade,
			Child = new Border
			{
				Background = AppBrushes.Paper,
				BorderBrush = AppBrushes.Ink,
				BorderThickness = new Thickness(1.4),
				CornerRadius = new CornerRadius(22.0),
				Padding = new Thickness(5.0),
				Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
				Child = _itemsHost
			}
		};
		base.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs eventArgs)
		{
			eventArgs.Handled = true;
			if (_popup.Child is FrameworkElement frameworkElement)
			{
				frameworkElement.Width = base.ActualWidth;
			}
			_popup.IsOpen = !_popup.IsOpen;
		};
	}

	public void SetItems(IReadOnlyList<LogoChoice> items)
	{
		_items = items;
		RebuildItems();
		if ((object)_selected == null && _items.Count > 0)
		{
			SetSelected(_items[0]);
		}
	}

	public void SetSelected(LogoChoice choice, bool notify = false)
	{
		bool flag = (object)_selected == null || !object.Equals(_selected, choice);
		_selected = choice;
		_label.Text = choice.Name;
		RebuildItems();
		if (notify && flag)
		{
			this.SelectedChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	private void RebuildItems()
	{
		_itemsHost.Children.Clear();
		foreach (LogoChoice choice in _items)
		{
			bool flag = (object)_selected != null && object.Equals(_selected, choice);
			Border item = new Border
			{
				Height = 42.0,
				CornerRadius = new CornerRadius(18.0),
				Padding = new Thickness(18.0, 0.0, 18.0, 0.0),
				Margin = new Thickness(0.0, 1.0, 0.0, 1.0),
				Background = (flag ? AppBrushes.Yellow : AppBrushes.Paper),
				Cursor = Cursors.Hand,
				Child = new TextBlock
				{
					Text = choice.Name,
					FontSize = 15.0,
					FontWeight = FontWeights.Bold,
					Foreground = AppBrushes.Ink,
					VerticalAlignment = VerticalAlignment.Center,
					TextTrimming = TextTrimming.CharacterEllipsis
				}
			};
			item.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs eventArgs)
			{
				eventArgs.Handled = true;
				_popup.IsOpen = false;
				SetSelected(choice, notify: true);
			};
			item.MouseEnter += delegate
			{
				item.Opacity = 0.84;
			};
			item.MouseLeave += delegate
			{
				item.Opacity = 1.0;
			};
			_itemsHost.Children.Add(item);
		}
	}
}
