using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Playhub.Models;
using Playhub.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Playhub;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, PluginInstallOperation> _pluginInstallOperations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pluginUninstalls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pluginInstallErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pluginUninstallErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<WeakReference<Action<PluginInstallOperation?>>>> _pluginInstallSubscribers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<WeakReference<Action<bool>>>> _pluginUninstallSubscribers =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed class PluginInstallOperation
    {
        public required DeckyPluginInfo Plugin { get; init; }
        public bool Updating { get; init; }
        public bool Installed { get; set; }
        public bool AcceptingProgress { get; set; } = true;
        public PluginInstallProgress Progress { get; set; } = new(PluginInstallPhase.Resolving);
    }

    private Button CreatePluginInstallButton(DeckyPluginInfo plugin, bool compact, bool slim = false)
    {
        var key = PluginStoreKey(plugin);
        var button = new Button
        {
            Height = compact || slim ? 32 : 42,
            MinHeight = compact || slim ? 32 : 42,
            MaxHeight = compact || slim ? 32 : 42,
            MinWidth = compact ? 32 : 42,
            Width = compact ? 32 : slim ? 112 : double.NaN,
            Padding = new Thickness(compact ? 0 : slim ? 10 : 12, 0, compact ? 0 : slim ? 10 : 12, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Style = StyleResource("PlayhubPrimaryButtonStyle")
        };
        RegisterButton(button, primary: true);

        string PhaseLabel(PluginInstallPhase phase) => phase switch
        {
            PluginInstallPhase.Resolving => T("Preparazione"),
            PluginInstallPhase.Downloading => T("Download"),
            PluginInstallPhase.Extracting => T("Estrazione"),
            PluginInstallPhase.Installing => T("Installazione"),
            PluginInstallPhase.Completed => T("Completato"),
            _ => T("Preparazione")
        };

        // Icon tiles stay fixed; text actions fit their label until progress starts.
        var contentWidth = compact ? 16d : slim ? 92d : 140d;
        var content = new Grid { Width = compact || slim ? contentWidth : double.NaN, MaxWidth = contentWidth };
        button.SizeChanged += (_, args) =>
        {
            if (!compact && !slim) return;
            var availableWidth = Math.Max(0, args.NewSize.Width - button.Padding.Left - button.Padding.Right
                - button.BorderThickness.Left - button.BorderThickness.Right);
            var maxWidth = Math.Min(contentWidth, availableWidth);
            if (Math.Abs(content.MaxWidth - maxWidth) > 0.5)
                content.MaxWidth = maxWidth;
        };
        var idle = new Grid { IsHitTestVisible = false, ColumnSpacing = compact ? 0 : 8 };
        var idleIcon = new FontIcon { FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        var idleText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (!compact)
        {
            idle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            idle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(idleText, 1);
            idle.Children.Add(idleText);
        }
        idle.Children.Add(idleIcon);
        var ring = new ProgressRing
        {
            Width = 14,
            Height = 14,
            MinWidth = 0,
            MinHeight = 0,
            IsActive = false,
            IsIndeterminate = true,
            Foreground = button.Foreground,
            VerticalAlignment = VerticalAlignment.Center
        };
        var phaseText = new TextBlock
        {
            FontSize = slim ? 13 : button.FontSize,
            FontFamily = button.FontFamily,
            FontWeight = button.FontWeight,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = compact ? TextAlignment.Center : TextAlignment.Left
        };
        var percentText = new TextBlock
        {
            FontSize = compact ? 12 : button.FontSize,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = Visibility.Collapsed
        };
        var progressContent = new Grid
        {
            ColumnSpacing = 6,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        if (compact)
        {
            progressContent.Children.Add(ring);
        }
        else
        {
            progressContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            progressContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progressContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(phaseText, 1);
            Grid.SetColumn(percentText, 2);
            progressContent.Children.Add(ring);
            progressContent.Children.Add(percentText);
        }
        if (!compact) progressContent.Children.Add(phaseText);
        content.Children.Add(idle);
        content.Children.Add(progressContent);
        button.Content = content;

        void Describe(string label)
        {
            var description = $"{plugin.Name}: {label}";
            ToolTipService.SetToolTip(button, description);
            AutomationProperties.SetName(button, description);
        }

        string? completedLabel = null;
        string? lastProgressDescription = null;
        void Restore()
        {
            if (!compact && !slim) content.Width = double.NaN;
            lastProgressDescription = null;
            var installed = plugin.IsInstalled && !plugin.HasUpdate;
            var label = installed ? completedLabel ?? T("Installato")
                : T(plugin.IsInstalled ? "Aggiorna" : "Installa");
            _pluginInstallErrors.TryGetValue(key, out var error);
            var glyph = ((char)(error is not null ? 0xE7BA : installed ? 0xE73E : plugin.IsInstalled ? 0xE895 : 0xE896)).ToString();
            idleIcon.Glyph = glyph;
            idleText.Text = label;
            idle.Visibility = Visibility.Visible;
            progressContent.Visibility = Visibility.Collapsed;
            ring.IsActive = false;
            Describe(error ?? label);
            button.IsEnabled = !installed && !IsIntegratedGamingModePlugin(plugin) &&
                !_pluginUninstalls.Contains(key) && !_pluginBulkUpdateRunning;
        }

        void Render(PluginInstallOperation? operation)
        {
            if (operation is null)
            {
                Restore();
                return;
            }
            if (operation.Installed)
            {
                plugin.IsInstalled = true;
                plugin.HasUpdate = false;
                plugin.Version = operation.Plugin.Version;
                plugin.InstalledVersion = operation.Plugin.InstalledVersion;
                plugin.InstalledFolder = operation.Plugin.InstalledFolder;
                completedLabel = T(operation.Updating ? "Aggiornato" : "Installato");
                Restore();
                return;
            }

            button.IsEnabled = false;
            if (!compact && !slim) content.Width = contentWidth;
            idle.Visibility = Visibility.Collapsed;
            progressContent.Visibility = Visibility.Visible;
            ring.IsActive = true;
            var progress = operation.Progress;
            ring.IsIndeterminate = !compact || progress.Phase != PluginInstallPhase.Downloading || progress.Percent is null;
            if (compact && progress.Percent is double currentPercent) ring.Value = currentPercent;
            var label = PhaseLabel(progress.Phase);
            var percentage = progress.Phase == PluginInstallPhase.Downloading && progress.Percent is double percent
                ? $"{percent.ToString("0", CultureInfo.CurrentCulture)}%" : "";
            var description = percentage.Length == 0 ? label : $"{label} {percentage}";
            if (lastProgressDescription == description)
                return;
            lastProgressDescription = description;
            phaseText.Text = label;
            percentText.Text = percentage;
            percentText.Visibility = percentage.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            Describe(description);
        }

        // The button owns its renderer; the window only keeps weak references to cached views.
        Action<PluginInstallOperation?> render = Render;
        Action<bool> renderUninstall = removed =>
        {
            if (removed)
            {
                DeckyPluginService.MarkPluginUninstalled(plugin);
                completedLabel = null;
            }
            _pluginInstallOperations.TryGetValue(key, out var operation);
            Render(operation);
        };
        // Management builders may apply their enabled state after button creation.
        button.RegisterPropertyChangedCallback(Control.IsEnabledProperty, (_, _) =>
        {
            if (button.IsEnabled && (IsIntegratedGamingModePlugin(plugin) || _pluginUninstalls.Contains(key)))
                button.IsEnabled = false;
        });
        SubscribePluginInstallProgress(key, render);
        SubscribePluginUninstallState(key, renderUninstall);
        button.Loaded += (_, _) =>
        {
            SubscribePluginInstallProgress(key, render);
            SubscribePluginUninstallState(key, renderUninstall);
        };
        button.Click += (_, _) => { _ = InstallPluginWithProgressAsync(plugin); };
        return button;
    }

    private Button BindPluginUninstallButton(Button button, DeckyPluginInfo plugin, bool compact)
    {
        var key = PluginStoreKey(plugin);
        var icon = new FontIcon { Glyph = ((char)0xE74D).ToString(), FontSize = 16 };
        var ring = new ProgressRing
        {
            Width = 14, Height = 14, MinWidth = 0, MinHeight = 0,
            IsActive = false, IsIndeterminate = true, Foreground = button.Foreground,
            Visibility = Visibility.Collapsed
        };
        var indicator = new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        indicator.Children.Add(icon);
        indicator.Children.Add(ring);
        var content = new Grid { ColumnSpacing = compact ? 0 : 8, IsHitTestVisible = false };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        content.Children.Add(indicator);
        if (!compact)
        {
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock
            {
                Text = T("Disinstalla"), VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(label, 1);
            content.Children.Add(label);
        }
        button.Content = content;

        bool Blocked() => IsIntegratedGamingModePlugin(plugin) || !plugin.IsInstalled || _pluginBulkUpdateRunning ||
            _pluginUninstalls.Contains(key) || _pluginInstallOperations.ContainsKey(key);

        void Render()
        {
            var busy = _pluginUninstalls.Contains(key);
            _pluginUninstallErrors.TryGetValue(key, out var error);
            ring.IsActive = busy;
            ring.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            icon.Glyph = ((char)(error is not null ? 0xE7BA : 0xE74D)).ToString();
            icon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
            button.IsEnabled = !Blocked();
            var description = $"{plugin.Name}: {error ?? T("Disinstalla")}{(busy ? "..." : "")}";
            ToolTipService.SetToolTip(button, description);
            AutomationProperties.SetName(button, description);
        }

        Action<bool> renderUninstall = removed =>
        {
            if (removed) DeckyPluginService.MarkPluginUninstalled(plugin);
            Render();
        };
        Action<PluginInstallOperation?> renderInstall = operation =>
        {
            if (operation?.Installed == true)
            {
                plugin.IsInstalled = true;
                plugin.HasUpdate = false;
                plugin.Version = operation.Plugin.Version;
                plugin.InstalledVersion = operation.Plugin.InstalledVersion;
                plugin.InstalledFolder = operation.Plugin.InstalledFolder;
            }
            Render();
        };
        // Generic async button handlers re-enable in finally; keep the operation guard authoritative.
        button.RegisterPropertyChangedCallback(Control.IsEnabledProperty, (_, _) =>
        {
            if (button.IsEnabled && Blocked()) button.IsEnabled = false;
        });
        SubscribePluginInstallProgress(key, renderInstall);
        SubscribePluginUninstallState(key, renderUninstall);
        button.Loaded += (_, _) =>
        {
            SubscribePluginInstallProgress(key, renderInstall);
            SubscribePluginUninstallState(key, renderUninstall);
        };
        return button;
    }

    private void SubscribePluginUninstallState(string key, Action<bool> render)
    {
        if (!_pluginUninstallSubscribers.TryGetValue(key, out var subscribers))
        {
            subscribers = new List<WeakReference<Action<bool>>>();
            _pluginUninstallSubscribers.Add(key, subscribers);
        }
        subscribers.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, render));
        subscribers.Add(new WeakReference<Action<bool>>(render));
        render(false);
    }

    private void PublishPluginUninstallState(string key, bool removed = false)
    {
        if (!_pluginUninstallSubscribers.TryGetValue(key, out var subscribers)) return;
        foreach (var reference in subscribers.ToArray())
        {
            if (reference.TryGetTarget(out var render)) render(removed);
        }
        subscribers.RemoveAll(reference => !reference.TryGetTarget(out _));
        if (subscribers.Count == 0) _pluginUninstallSubscribers.Remove(key);
    }

    private void SubscribePluginInstallProgress(string key, Action<PluginInstallOperation?> render)
    {
        if (!_pluginInstallSubscribers.TryGetValue(key, out var subscribers))
        {
            subscribers = new List<WeakReference<Action<PluginInstallOperation?>>>();
            _pluginInstallSubscribers.Add(key, subscribers);
        }
        subscribers.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, render));
        subscribers.Add(new WeakReference<Action<PluginInstallOperation?>>(render));
        _pluginInstallOperations.TryGetValue(key, out var operation);
        render(operation);
    }

    private void PublishPluginInstallProgress(string key, PluginInstallOperation? operation)
    {
        if (!_pluginInstallSubscribers.TryGetValue(key, out var subscribers))
            return;
        foreach (var reference in subscribers.ToArray())
        {
            if (reference.TryGetTarget(out var render))
                render(operation);
        }
        subscribers.RemoveAll(reference => !reference.TryGetTarget(out _));
        if (subscribers.Count == 0)
            _pluginInstallSubscribers.Remove(key);
    }

    private async Task<bool> InstallPluginWithProgressAsync(DeckyPluginInfo plugin, bool bulkOperation = false)
    {
        using var context = BeginNotificationContext("plugins");
        var key = PluginStoreKey(plugin);
        if (IsIntegratedGamingModePlugin(plugin) || _pluginInstallOperations.ContainsKey(key) || _pluginUninstalls.Contains(key) ||
            (_pluginBulkUpdateRunning && !bulkOperation)) return false;
        if (plugin.IsInstalled && !plugin.HasUpdate) return true;

        // UI-thread registration happens before the first await, locking every matching button.
        var operation = new PluginInstallOperation { Plugin = plugin, Updating = plugin.IsInstalled };
        _pluginInstallOperations.Add(key, operation);
        _pluginInstallErrors.Remove(key);
        try
        {
            PublishPluginInstallProgress(key, operation);
            var progress = new Progress<PluginInstallProgress>(value =>
            {
                if (!operation.AcceptingProgress || !_pluginInstallOperations.TryGetValue(key, out var current) ||
                    !ReferenceEquals(current, operation))
                    return;
                double? percent = value.Phase == PluginInstallPhase.Downloading &&
                    value.Percent is double number && double.IsFinite(number)
                    ? Math.Round(Math.Clamp(number, 0, 100)) : null;
                var normalized = new PluginInstallProgress(value.Phase, percent);
                if (operation.Progress == normalized)
                    return;
                operation.Progress = normalized;
                PublishPluginInstallProgress(key, operation);
            });
            await _pluginService.InstallOrUpdateAsync(plugin, _settings.DeckyPluginsPath, progress);
            operation.AcceptingProgress = false;
            MarkPluginInstalled(plugin);
            _pluginUninstallErrors.Remove(key);
            operation.Installed = true;
            operation.Progress = new PluginInstallProgress(PluginInstallPhase.Completed);
            PublishPluginInstallProgress(key, operation);
            if (!bulkOperation)
                await CommitPluginInstallStateAsync(plugin);
            return true;
        }
        catch (Exception ex)
        {
            _pluginInstallErrors[key] = T(operation.Updating
                ? "Aggiornamento non riuscito: " : "Installazione del plugin non riuscita: ") + FriendlyError(ex);
            Diag.Crash($"Plugin install: {key}", ex);
            return false;
        }
        finally
        {
            operation.AcceptingProgress = false;
            _pluginInstallOperations.Remove(key);
            PublishPluginInstallProgress(key, operation.Installed ? operation : null);
        }
    }

    private async Task UninstallStorePluginAsync(DeckyPluginInfo plugin)
    {
        using var context = BeginNotificationContext("plugins");
        var key = PluginStoreKey(plugin);
        if (IsIntegratedGamingModePlugin(plugin) || !plugin.IsInstalled || _pluginBulkUpdateRunning ||
            _pluginInstallOperations.ContainsKey(key) || _pluginUninstalls.Contains(key)) return;
        // Register synchronously so every live action is busy before removal yields.
        _pluginUninstalls.Add(key);
        _pluginUninstallErrors.Remove(key);
        try
        {
            PublishPluginUninstallState(key);
            Diag.Step($"Plugin uninstall removal started: {key}");
            await _pluginService.UninstallAsync(plugin);
            _pluginInstallErrors.Remove(key);
            CommitPluginUninstallState(plugin);
            Diag.Step($"Plugin uninstall removal completed: {key}");
        }
        catch (Exception ex)
        {
            _pluginUninstallErrors[key] = T("Rimozione del plugin non riuscita: ") + FriendlyError(ex);
            Diag.Crash($"Plugin uninstall removal: {key}", ex);
            throw;
        }
        finally
        {
            _pluginUninstalls.Remove(key);
            PublishPluginUninstallState(key);
        }
    }

    private void CommitPluginUninstallState(DeckyPluginInfo plugin)
    {
        var key = PluginStoreKey(plugin);
        DeckyPluginService.MarkPluginUninstalled(plugin);
        foreach (var current in _plugins.Where(item =>
            string.Equals(PluginStoreKey(item), key, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            DeckyPluginService.MarkPluginUninstalled(current);
            if (string.Equals(current.CatalogSource, "installed", StringComparison.OrdinalIgnoreCase))
                _plugins.Remove(current);
        }
        PublishPluginUninstallState(key, removed: true);

        // Reuse current catalog metadata; removal needs no catalog/release network refresh.
        InvalidatePluginAllViews();
        InvalidateFeaturedFrames();
        _pluginCardsDirty = true;
        _pluginManagementDirty = true;
        if (string.Equals(_pluginPagePluginKey, key, StringComparison.OrdinalIgnoreCase) &&
            !_plugins.Any(item => string.Equals(PluginStoreKey(item), key, StringComparison.OrdinalIgnoreCase)))
            ClosePluginPage();
        if (_currentPageTag == "plugins")
        {
            // The last local-only plugin may have been removed, leaving an empty collection.
            if (_pluginStoreMode == "manage") RenderPluginManagementIfNeeded();
            else RenderPluginCardsIfNeeded();
        }
        RefreshOpenPluginPage();
    }
}
