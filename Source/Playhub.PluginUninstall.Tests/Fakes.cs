// Headless lifecycle tests only. These controls do not render or bootstrap WinUI.
namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum HorizontalAlignment { Center }
    public enum VerticalAlignment { Center }
    public enum GridUnitType { Star }
    public enum TextWrapping { NoWrap }
    public enum TextTrimming { CharacterEllipsis }
    public enum TextAlignment { Center, Left }
    public record Thickness(double Left, double Top, double Right, double Bottom);
    public record GridLength(double Value, GridUnitType Unit = default)
    {
        public static GridLength Auto => new(0);
    }
    public sealed class DependencyProperty;
    public class DependencyObject
    {
        private readonly List<Action<DependencyObject, DependencyProperty>> _callbacks = new();
        public long RegisterPropertyChangedCallback(DependencyProperty property,
            Action<DependencyObject, DependencyProperty> callback)
        {
            _callbacks.Add(callback);
            return _callbacks.Count;
        }
        protected void Changed(DependencyProperty property)
        {
            foreach (var callback in _callbacks) callback(this, property);
        }
    }
    public class UIElement : DependencyObject
    {
        public Visibility Visibility { get; set; }
        public bool IsHitTestVisible { get; set; }
    }
    public class FrameworkElement : UIElement
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double MinWidth { get; set; }
        public double MinHeight { get; set; }
        public double MaxWidth { get; set; }
        public double MaxHeight { get; set; }
        public VerticalAlignment VerticalAlignment { get; set; }
        public event EventHandler? Loaded;
        public event EventHandler<SizeChangedEventArgs>? SizeChanged;
        public void RaiseLoaded() => Loaded?.Invoke(this, EventArgs.Empty);
        public void RaiseSizeChanged(double width) => SizeChanged?.Invoke(this, new(width));
    }
    public sealed class SizeChangedEventArgs(double width) : EventArgs
    {
        public Size NewSize { get; } = new(width);
    }
    public record Size(double Width);
}

namespace Microsoft.UI.Xaml.Controls
{
    using Microsoft.UI.Xaml;
    public enum InfoBarSeverity { Success, Error, Warning }
    public class Control : FrameworkElement
    {
        public static readonly DependencyProperty IsEnabledProperty = new();
        private bool _enabled = true;
        public bool IsEnabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                Changed(IsEnabledProperty);
            }
        }
        public Thickness Padding { get; set; } = new(0, 0, 0, 0);
        public Thickness BorderThickness { get; set; } = new(0, 0, 0, 0);
        public object? Style { get; set; }
        public object? Foreground { get; set; }
        public object? FontFamily { get; set; }
        public object? FontWeight { get; set; }
        public double FontSize { get; set; }
    }
    public sealed class Button : Control
    {
        public object? Content { get; set; }
        public string? ToolTipText { get; set; }
        public string AutomationName { get; set; } = "";
        public HorizontalAlignment HorizontalContentAlignment { get; set; }
        public VerticalAlignment VerticalContentAlignment { get; set; }
        public event EventHandler? Click;
        public void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);
    }
    public sealed class Grid : FrameworkElement
    {
        public double ColumnSpacing { get; set; }
        public List<UIElement> Children { get; } = new();
        public List<ColumnDefinition> ColumnDefinitions { get; } = new();
        public static void SetColumn(UIElement element, int column) { }
    }
    public sealed class ColumnDefinition
    {
        public GridLength Width { get; set; } = GridLength.Auto;
    }
    public sealed class FontIcon : Control
    {
        public string Glyph { get; set; } = "";
    }
    public sealed class TextBlock : Control
    {
        public string Text { get; set; } = "";
        public TextWrapping TextWrapping { get; set; }
        public TextTrimming TextTrimming { get; set; }
        public TextAlignment TextAlignment { get; set; }
    }
    public sealed class ProgressRing : Control
    {
        public bool IsActive { get; set; }
        public bool IsIndeterminate { get; set; }
        public double Value { get; set; }
    }
    public static class ToolTipService
    {
        public static void SetToolTip(Button button, string text) => button.ToolTipText = text;
        public static object? GetToolTip(Button button) => button.ToolTipText;
    }
}

namespace Microsoft.UI.Xaml.Automation
{
    public static class AutomationProperties
    {
        public static void SetName(Controls.Button button, string text) => button.AutomationName = text;
        public static string GetName(Controls.Button button) => button.AutomationName;
    }
}

namespace Playhub.Services
{
    internal static class Diag
    {
        public static void Step(string message) { }
        public static void Crash(string message, object error) { }
    }

    internal static class AppPaths
    {
        public static string DownloadsRoot => throw new InvalidOperationException("Real file access is forbidden in these tests.");
    }

    internal sealed class FakePluginService
    {
        public Func<string, Task> Remove { get; set; } = _ => throw new InvalidOperationException("No removal fake configured.");
        public Func<Models.DeckyPluginInfo, Task> Install { get; set; } = _ => throw new InvalidOperationException("No installation fake configured.");
        public int RemoveCalls { get; private set; }
        public int InstallCalls { get; private set; }
        public Task UninstallAsync(Models.DeckyPluginInfo plugin) => DeckyPluginService.UninstallAsync(plugin, path =>
        {
            RemoveCalls++;
            return Remove(path);
        });
        public Task InstallOrUpdateAsync(Models.DeckyPluginInfo plugin, string path, IProgress<PluginInstallProgress> progress)
        {
            InstallCalls++;
            return Install(plugin);
        }
    }
}
