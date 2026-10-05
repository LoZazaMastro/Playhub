using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Playhub;

public sealed partial class MainWindow
{
    private void AddOptionalGamingControls(StackPanel page)
    {
        var settings = new StackPanel { Spacing = 18 };
        foreach (var child in page.Children.Skip(1).ToArray())
        {
            page.Children.Remove(child);
            settings.Children.Add(child);
        }
        var card = Card();
        card.Children.Add(IconHeader("\uE7FC", "GamingMode.Optional.Title", "GamingMode.Optional.Description"));
        var status = Body("");
        card.Children.Add(status);
        var install = new Button { Content = T("GamingMode.Optional.Install"), MinWidth = 150 };
        var remove = new Button { Content = T("GamingMode.Optional.Remove"), MinWidth = 150 };
        _localizationKeys.AddOrUpdate(install, "GamingMode.Optional.Install");
        _localizationKeys.AddOrUpdate(remove, "GamingMode.Optional.Remove");
        card.Children.Add(ActionRow(install, remove));
        void Refresh()
        {
            var available = _gamingMode.IsInstalled;
            LocalizeProperty(install, ContentControl.ContentProperty, explicitKey: true);
            LocalizeProperty(remove, ContentControl.ContentProperty, explicitKey: true);
            status.Text = T(available ? "GamingMode.Optional.Installed" : "GamingMode.Optional.NotInstalled");
            install.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
            remove.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            settings.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
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
                status.Text = T(enable ? "GamingMode.Optional.Installing" : "GamingMode.Optional.Removing");
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
