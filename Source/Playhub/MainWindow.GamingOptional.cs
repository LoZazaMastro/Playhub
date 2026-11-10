using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Playhub;

public sealed partial class MainWindow
{
    private void AddOptionalGamingControls(StackPanel page, FrameworkElement startupModes)
    {
        var settings = new StackPanel { Spacing = 18 };
        foreach (var child in page.Children.Skip(1).ToArray())
        {
            page.Children.Remove(child);
            settings.Children.Add(child);
        }
        var mascotHeader = (FrameworkElement)page.Children[0];
        page.Children.RemoveAt(0);
        var header = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = new GridLength(0) } },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            },
            Children = { mascotHeader, startupModes }
        };
        startupModes.VerticalAlignment = VerticalAlignment.Center;
        header.SizeChanged += (_, args) => ArrangeHeader(args.NewSize.Width);
        page.Children.Add(header);

        var card = Card();
        var title = SectionTitle("Gaming Mode");
        title.FontSize = 16;
        var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var statusChip = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 8, 3),
            Background = ResourceBrush("SubtleFillColorSecondaryBrush", Windows.UI.Color.FromArgb(24, 255, 255, 255)),
            Child = status
        };
        var heading = ImportActionRow(title, statusChip);
        var copy = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { heading, Body("GamingMode.Optional.Description") }
        };
        var icon = new FontIcon
        {
            Glyph = "\uE7FC",
            FontSize = 20,
            Foreground = ResourceBrush("AccentFillColorDefaultBrush", ParseColor(_settings.AccentColor)),
            Margin = new Thickness(0, 2, 12, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        var install = ActionButton("GamingMode.Optional.Install", primary: true);
        var remove = ActionButton("GamingMode.Optional.Remove", primary: false);
        var actions = new Grid
        {
            MaxWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { install, remove }
        };
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            },
            Children = { icon, copy, actions }
        };
        Grid.SetColumn(copy, 1);
        SetActionLayout(compact: true);
        row.SizeChanged += (_, args) => SetActionLayout(compact: args.NewSize.Width < 540);
        card.Children.Add(row);

        Button ActionButton(string key, bool primary)
        {
            var button = new Button
            {
                Content = LocalizedText(new TextBlock { Text = key, TextWrapping = TextWrapping.Wrap }, key),
                Style = StyleResource(primary ? "PlayhubPrimaryButtonStyle" : "PlayhubSecondaryButtonStyle"),
                MinWidth = 0
            };
            RegisterButton(button, primary);
            return button;
        }

        void SetActionLayout(bool compact)
        {
            Grid.SetColumn(actions, compact ? 1 : 2);
            Grid.SetRow(actions, compact ? 1 : 0);
            actions.Margin = compact ? new Thickness(0, 10, 0, 0) : new Thickness(16, 0, 0, 0);
        }

        void SetInstallStatus(string key)
        {
            _localizationKeys.AddOrUpdate(status, key);
            LocalizeProperty(status, TextBlock.TextProperty, explicitKey: true);
        }

        void ArrangeHeader(double width)
        {
            var installed = startupModes.Visibility == Visibility.Visible;
            var sideBySide = installed && width >= 960;
            header.ColumnSpacing = sideBySide ? 24 : 0;
            header.RowSpacing = installed && !sideBySide ? 14 : 0;
            header.ColumnDefinitions[1].Width = sideBySide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(startupModes, sideBySide ? 1 : 0);
            Grid.SetRow(startupModes, sideBySide ? 0 : 1);
        }

        void Refresh()
        {
            var available = _gamingMode.IsInstalled;
            SetInstallStatus(available ? "GamingMode.Optional.Installed" : "GamingMode.Optional.NotInstalled");
            install.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
            remove.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            settings.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            startupModes.Visibility = settings.Visibility;
            ArrangeHeader(header.ActualWidth);
        }
        async System.Threading.Tasks.Task Apply(bool enable)
        {
            if (!enable)
            {
                var confirm = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = T("GamingMode.Optional.Remove"),
                    Content = Body(T("GamingMode.Optional.ConfirmRemove")),
                    PrimaryButtonText = T("GamingMode.Optional.Remove"),
                    CloseButtonText = T("Annulla"),
                    DefaultButton = ContentDialogButton.Close
                };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            }
            install.IsEnabled = remove.IsEnabled = false;
            try
            {
                SetInstallStatus(enable ? "GamingMode.Optional.Installing" : "GamingMode.Optional.Removing");
                var result = enable
                    ? await _gamingMode.InstallAsync(_settings.DeckyPluginsPath)
                    : await _gamingMode.UninstallAsync(_settings.DeckyPluginsPath);
                SetStatus(T(result.Message), result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
                await RefreshGamingModeAsync();
            }
            catch (Exception error) { SetStatus(error.Message, InfoBarSeverity.Error); }
            finally { install.IsEnabled = remove.IsEnabled = true; Refresh(); }
        }
        install.Click += async (_, _) => await Apply(true);
        remove.Click += async (_, _) => await Apply(false);
        page.Children.Add(card.Root);
        page.Children.Add(settings);
        page.Loaded += (_, _) => Refresh();
        Refresh();
    }
}
