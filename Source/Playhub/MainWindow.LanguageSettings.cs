using Microsoft.UI.Xaml.Controls;
using Playhub.Services;
using System;
using System.Threading.Tasks;

namespace Playhub;

public sealed partial class MainWindow
{
    private bool _languageChangeInProgress;

    private async Task ChangeLanguageAsync()
    {
        if (_loadingSettings || _languageChangeInProgress) return;
        var selectedLanguage = GetComboKey(_languageCombo);
        // Clearing/repopulating the combo is not a request to switch to English.
        if (string.IsNullOrWhiteSpace(selectedLanguage)) return;
        selectedLanguage = LocalizationService.NormalizeLanguageKey(selectedLanguage);
        if (string.Equals(LocalizationService.NormalizeLanguageKey(_settings.Language),
                selectedLanguage, StringComparison.OrdinalIgnoreCase)) return;

        var previousLanguage = _settings.Language;
        var wasEnabled = _languageCombo.IsEnabled;
        var saved = false;
        var restartPending = false;
        _languageChangeInProgress = true;
        _languageCombo.IsEnabled = false;
        try
        {
            _settings.Language = selectedLanguage;
            await SaveSettingsSilentlyAsync();
            saved = true;
            restartPending = RestartPlayhub();
        }
        catch (Exception ex)
        {
            if (!saved)
            {
                _settings.Language = previousLanguage;
                SelectComboKey(_languageCombo, previousLanguage);
            }
            SetStatus(FriendlyError(ex), InfoBarSeverity.Error);
        }
        finally
        {
            if (!restartPending)
            {
                _languageCombo.IsEnabled = wasEnabled;
                _languageChangeInProgress = false;
            }
        }
    }
}
