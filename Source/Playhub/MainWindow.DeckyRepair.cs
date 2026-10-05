using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.System;

namespace Playhub;

public sealed partial class MainWindow
{
    private bool _deckyRepairOpen;

    private async Task ShowDeckyRepairDialogAsync(string? failure = null)
    {
        if (_deckyRepairOpen) return;
        _deckyRepairOpen = true;
        try
        {
            if (Content.XamlRoot is null && Content is FrameworkElement root)
            {
                var loaded = new TaskCompletionSource();
                RoutedEventHandler? ready = null;
                ready = (_, _) => { root.Loaded -= ready; loaded.TrySetResult(); };
                root.Loaded += ready;
                if (root.XamlRoot is null) await loaded.Task;
                else root.Loaded -= ready;
            }
            var services = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "homebrew", "services");
            var loader = Path.Combine(services, File.Exists(Path.Combine(services, "PluginLoader_noconsole.exe"))
                ? "PluginLoader_noconsole.exe" : "PluginLoader.exe");
            var body = new StackPanel { Spacing = 18, Width = 680 };
            var mascot = new Image
            {
                Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Extra", "decky-repair-mascot.png"))),
                Height = 150, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mascot, T("Mascotte Playhub"));
            body.Children.Add(mascot);
            body.Children.Add(new TextBlock
            {
                Text = T("Rimettiamo Decky in funzione"), FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch
            });
            body.Children.Add(new TextBlock
            {
                Text = T("Se l'installazione o l'avvio di Decky si interrompono, controlla la protezione di Windows e poi reinstalla il loader."),
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 16, Opacity = .8
            });
            if (!string.IsNullOrWhiteSpace(failure))
            {
                body.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Error,
                    Title = T("Operazione non completata"), Message = TranslateMessage(failure) });
            }

            var protection = Card();
            protection.Children.Add(IconHeader(((char)0xE83D).ToString(), "Controlla Windows Defender",
                "Windows Defender potrebbe aver bloccato o messo in quarantena PluginLoader. Apri Sicurezza di Windows e controlla la Cronologia della protezione."));
            protection.Children.Add(Body("Se riconosci il loader ufficiale di Decky e vuoi autorizzarlo, vai in Protezione da virus e minacce > Gestisci impostazioni > Esclusioni > Aggiungi o rimuovi esclusioni. Puoi scegliere il file PluginLoader oppure la sua cartella services."));
            protection.Children.Add(new TextBlock { Text = loader, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true, Opacity = .7 });
            protection.Children.Add(ActionRow(Button("Apri Sicurezza di Windows", async () =>
                await Launcher.LaunchUriAsync(new Uri("windowsdefender:")))));
            body.Children.Add(protection);

            var repair = Card();
            repair.Children.Add(IconHeader(((char)0xE777).ToString(), "Reinstalla Decky",
                "Scarica nuovamente il loader ufficiale e sostituisci i file di Decky. I plugin restano nella loro cartella."));
            var status = new InfoBar { IsOpen = false, IsClosable = false };
            var progress = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed };
            var reinstall = new Button { Content = T("Reinstalla Decky"), Style = StyleResource("PlayhubPrimaryButtonStyle") };
            repair.Children.Add(reinstall); repair.Children.Add(progress); repair.Children.Add(status);
            body.Children.Add(repair);
            var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(320, Math.Min(700, Content.XamlRoot.Size.Height - 170)) };
            var frame = new Grid { RowSpacing = 24 };
            frame.RowDefinitions.Add(new RowDefinition());
            frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            frame.Children.Add(scroll);
            var done = new Button { Content = T("Fatto"), Width = 180, HorizontalAlignment = HorizontalAlignment.Center,
                Style = StyleResource("PlayhubPrimaryButtonStyle") };
            Grid.SetRow(done, 1); frame.Children.Add(done);
            var dialog = new ContentDialog { Content = frame, XamlRoot = Content.XamlRoot };
            dialog.Resources["ContentDialogMaxWidth"] = 760d;
            done.Click += (_, _) => dialog.Hide();
            dialog.Opened += (_, _) =>
            {
                done.Focus(FocusState.Programmatic);
                scroll.ChangeView(null, 0, null, disableAnimation: true);
            };
            reinstall.Click += async (_, _) =>
            {
                reinstall.IsEnabled = false; dialog.IsEnabled = false;
                progress.Visibility = Visibility.Visible; status.IsOpen = false;
                try
                {
                    var result = await _deckyInstaller.InstallLatestAsync();
                    await RefreshDeckyStateAsync();
                    status.Severity = InfoBarSeverity.Success; status.Message = TranslateMessage(result);
                }
                catch (Exception error)
                {
                    status.Severity = InfoBarSeverity.Error; status.Message = TranslateMessage(error.Message);
                }
                finally
                {
                    dialog.IsEnabled = true; reinstall.IsEnabled = true; progress.Visibility = Visibility.Collapsed; status.IsOpen = true;
                }
            };
            await dialog.ShowAsync();
        }
        finally { _deckyRepairOpen = false; }
    }
}
