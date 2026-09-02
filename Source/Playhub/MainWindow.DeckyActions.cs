using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Playhub;

public sealed partial class MainWindow
{
    private readonly List<Button> _deckyOperationButtons = new();
    private bool _deckyOperationRunning;

    private Button DeckyOperationButton(string label, Func<Task> operation, bool primary = false)
    {
        Button? button = null;
        button = Button(label, () => RunDeckyOperationAsync(button!, operation), primary);
        _localizationKeys.AddOrUpdate(button, label);
        _deckyOperationButtons.Add(button);
        return button;
    }

    private async Task RunDeckyOperationAsync(Button button, Func<Task> operation)
    {
        if (_deckyOperationRunning) return;
        using var context = BeginNotificationContext("decky");
        var enabled = _deckyOperationButtons.Select(item => (Button: item, item.IsEnabled)).ToArray();
        var content = button.Content;
        var width = button.Width;
        var height = button.Height;
        var buildsEnabled = _deckyBuildCombo.IsEnabled;
        var spinner = new ProgressRing { Width = 18, Height = 18, IsActive = true };
        AutomationProperties.SetName(spinner, T("Operazione in corso"));
        _deckyOperationRunning = true;
        try
        {
            if (button.ActualWidth > 0) button.Width = button.ActualWidth;
            if (button.ActualHeight > 0) button.Height = button.ActualHeight;
            foreach (var item in _deckyOperationButtons) item.IsEnabled = false;
            _deckyBuildCombo.IsEnabled = false;
            button.Content = spinner;
            await operation();
        }
        finally
        {
            spinner.IsActive = false;
            _deckyOperationRunning = false;
            button.Content = _localizationKeys.TryGetValue(button, out var label) ? T(label) : content;
            if (!ReferenceEquals(button, _installButton) && _localizationKeys.TryGetValue(_installButton, out var installLabel))
                _installButton.Content = T(installLabel);
            button.Width = width;
            button.Height = height;
            foreach (var item in enabled) item.Button.IsEnabled = item.IsEnabled;
            _deckyBuildCombo.IsEnabled = buildsEnabled;
        }
    }
}
