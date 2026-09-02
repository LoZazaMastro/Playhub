using Microsoft.UI.Xaml.Controls;
using Playhub.Models;

namespace Playhub;

// No WinUI window is constructed; only the production language handler is linked.
public sealed partial class MainWindow
{
    private readonly PlayhubSettings _settings = new();
    private readonly FakeCombo _languageCombo = new();
    private bool _loadingSettings;
    public Func<Task> Save { get; set; } = () => Task.CompletedTask;
    public Func<bool> Restart { get; set; } = () => false;
    public int Saves { get; private set; }
    public int Restarts { get; private set; }
    public int Errors { get; private set; }
    public string Language { get => _settings.Language; set => _settings.Language = value; }
    public string? Selection => _languageCombo.Key;
    public bool Enabled => _languageCombo.IsEnabled;
    public bool Busy => _languageChangeInProgress;
    public bool Loading { set => _loadingSettings = value; }

    public Task SelectAsync(string? language)
    {
        _languageCombo.Key = language;
        return ChangeLanguageAsync();
    }

    private async Task SaveSettingsSilentlyAsync() { Saves++; await Save(); }
    private bool RestartPlayhub() { Restarts++; return Restart(); }
    private void SetStatus(string message, InfoBarSeverity severity) { Errors++; }
    private string FriendlyError(Exception ex) => ex.Message;
    private static string? GetComboKey(FakeCombo combo) => combo.Key;
    private void SelectComboKey(FakeCombo combo, string language)
    {
        combo.Key = language;
        // Reproduce the SelectionChanged raised by rollback/repopulation.
        var reentry = ChangeLanguageAsync();
        if (!reentry.IsCompletedSuccessfully) throw new Exception("Rollback reentered the save");
    }
    private sealed class FakeCombo
    {
        public string? Key { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
}
