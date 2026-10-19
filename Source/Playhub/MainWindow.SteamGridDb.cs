using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Playhub.Services;

namespace Playhub;

public sealed partial class MainWindow
{
    private Task<bool>? _steamGridDbPrompt;
    private readonly SemaphoreSlim _steamGridDbSaveGate = new(1, 1);
    private int _steamGridDbConnectionRequest;
#if PLAYHUB_UI_REVIEW
    private Func<SteamGridDbConnection>? _reviewSteamGridDbConnectionFactory;
#endif

    private UIElement BuildSteamGridDbConnectionCard(bool importPage = false)
    {
        var card = Card();
        card.Children.Add(IconHeader("\uE91B", "SteamGridDB", T("SteamGridDb.ConnectionHelp")));
        var key = new PasswordBox { Password = _settings.SteamGridDbApiKey ?? "", PlaceholderText = T("SteamGridDb.ApiKey"),
            PasswordRevealMode = PasswordRevealMode.Hidden, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (importPage) _xboxSteamGridDbKeyBox = key;
        var feedback = new InfoBar { IsOpen = false, IsClosable = false };
        Button? connect = null;
        connect = Button("SteamGridDb.Connect", async () =>
        {
            connect!.IsEnabled = false;
            try
            {
                var status = await CheckAndSaveSteamGridDbAsync(key.Password);
                feedback.Message = T(status == SteamGridDbConnectionStatus.Connected ? "SteamGridDb.Connected" :
                    status == SteamGridDbConnectionStatus.InvalidKey ? "SteamGridDb.InvalidKey" : "SteamGridDb.Unavailable");
                feedback.Severity = status == SteamGridDbConnectionStatus.Connected ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
                feedback.IsOpen = true;
            }
            catch (Exception error) { feedback.Message = FriendlyError(error); feedback.Severity = InfoBarSeverity.Error; feedback.IsOpen = true; }
            finally { connect.IsEnabled = true; }
        });
        var actions = new Playhub.Importing.AdaptiveImportToolbar();
        actions.Children.Add(connect);
        actions.Children.Add(Button("SteamGridDb.GetKey", async () =>
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.steamgriddb.com/profile/preferences/api"))));
        card.Children.Add(key); card.Children.Add(actions); card.Children.Add(feedback);
        return card;
    }

    private async Task<SteamGridDbConnectionStatus> CheckAndSaveSteamGridDbAsync(string key, CancellationToken token = default)
    {
        var request = ++_steamGridDbConnectionRequest;
#if PLAYHUB_UI_REVIEW
        using var connection = _reviewSteamGridDbConnectionFactory?.Invoke() ?? new SteamGridDbConnection();
#else
        using var connection = new SteamGridDbConnection();
#endif
        var result = await connection.CheckAsync(key, token);
        if (result != SteamGridDbConnectionStatus.Connected) return result;
        token.ThrowIfCancellationRequested();
        if (request != _steamGridDbConnectionRequest) return SteamGridDbConnectionStatus.Unavailable;
        await _steamGridDbSaveGate.WaitAsync(token);
        try
        {
            if (request != _steamGridDbConnectionRequest) return SteamGridDbConnectionStatus.Unavailable;
            var previous = _settings.SteamGridDbApiKey;
            try
            {
                _settings.SteamGridDbApiKey = key.Trim();
                await _settingsService.SaveAsync();
            }
            catch { _settings.SteamGridDbApiKey = previous; throw; }
            return result;
        }
        finally { _steamGridDbSaveGate.Release(); }
    }

    private async Task<bool> EnsureSteamGridDbConnectionAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested) return false;
        if (!string.IsNullOrWhiteSpace(_settings.SteamGridDbApiKey)) return true;
        var pending = _steamGridDbPrompt ??= PromptSteamGridDbConnectionAsync(token);
        try { return await pending; }
        finally { if (ReferenceEquals(_steamGridDbPrompt, pending)) _steamGridDbPrompt = null; }
    }
    private async Task<bool> PromptSteamGridDbConnectionAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var key = new PasswordBox { PlaceholderText = T("SteamGridDb.ApiKey"), PasswordRevealMode = PasswordRevealMode.Hidden };
        var feedback = new InfoBar { IsOpen = false, IsClosable = false };
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(new TextBlock { Text = T("SteamGridDb.ConnectionHelp"), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(key);
        content.Children.Add(Button("SteamGridDb.GetKey", async () => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.steamgriddb.com/profile/preferences/api"))));
        content.Children.Add(feedback);
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "SteamGridDB", Content = content,
            PrimaryButtonText = T("SteamGridDb.Connect"), CloseButtonText = T("Annulla") };
        using var cancellation = token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        if (token.IsCancellationRequested) return false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                var status = await CheckAndSaveSteamGridDbAsync(key.Password, token);
                args.Cancel = status != SteamGridDbConnectionStatus.Connected;
                if (args.Cancel)
                {
                    feedback.Message = T(status == SteamGridDbConnectionStatus.InvalidKey ? "SteamGridDb.InvalidKey" : "SteamGridDb.Unavailable");
                    feedback.Severity = InfoBarSeverity.Warning; feedback.IsOpen = true;
                }
            }
            catch (OperationCanceledException) { args.Cancel = true; }
            catch (Exception error) { args.Cancel = true; feedback.Message = FriendlyError(error); feedback.IsOpen = true; }
            finally { deferral.Complete(); }
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary && !token.IsCancellationRequested;
    }
}
