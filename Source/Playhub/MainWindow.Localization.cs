using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playhub;

public sealed partial class MainWindow
{
    private sealed class LocalizedProperty
    {
        public string Source = "";
        public string Rendered = "";
        public string? ExplicitSource;
        public bool Updating;
    }

    // Native XAML controls can outlive their managed wrappers. Keep source text on
    // the control itself so a later wrapper never treats a translation as its key.
    private sealed class LocalizationStorage : DependencyObject
    {
        public static readonly DependencyProperty Properties = DependencyProperty.RegisterAttached(
            "PlayhubLocalizedProperties", typeof(object), typeof(LocalizationStorage), new PropertyMetadata(null));
        public static readonly DependencyProperty Source = DependencyProperty.RegisterAttached(
            "PlayhubLocalizationSource", typeof(string), typeof(LocalizationStorage), new PropertyMetadata(null));
        public static readonly DependencyProperty RunState = DependencyProperty.RegisterAttached(
            "PlayhubLocalizedRun", typeof(object), typeof(LocalizationStorage), new PropertyMetadata(null));
        public static readonly DependencyProperty Parent = DependencyProperty.RegisterAttached(
            "PlayhubLocalizationParent", typeof(object), typeof(LocalizationStorage), new PropertyMetadata(null));
    }

    private sealed class NativeLocalizationKeys
    {
        public void AddOrUpdate(DependencyObject owner, string source) => owner.SetValue(LocalizationStorage.Source, source);
        public bool TryGetValue(DependencyObject owner, out string source)
        {
            source = owner.GetValue(LocalizationStorage.Source) as string ?? "";
            return owner.ReadLocalValue(LocalizationStorage.Source) != DependencyProperty.UnsetValue;
        }
    }

    private static bool IsLocalizationProtected(DependencyObject owner)
    {
        for (var current = owner; current is not null;)
        {
            if (current is FrameworkElement { Tag: "noloc" }) return true;
            var parent = current is FrameworkElement element
                ? element.Parent ?? VisualTreeHelper.GetParent(element) : null;
            if (parent is null && current.GetValue(LocalizationStorage.Parent) is WeakReference<DependencyObject> logicalParent)
                logicalParent.TryGetTarget(out parent);
            current = parent;
        }
        return false;
    }

    private void LocalizeProperty(DependencyObject owner, DependencyProperty property, bool explicitKey = false,
        bool message = false)
    {
        if (IsLocalizationProtected(owner)) return;
        if (owner.GetValue(property) is not string current) return;
        if (owner.GetValue(LocalizationStorage.Properties) is not Dictionary<DependencyProperty, LocalizedProperty> properties)
        {
            properties = new();
            owner.SetValue(LocalizationStorage.Properties, properties);
        }
        var sourceKey = explicitKey && _localizationKeys.TryGetValue(owner, out var key) ? key : null;
        if (!properties.TryGetValue(property, out var state))
        {
            state = new LocalizedProperty { Source = sourceKey ?? current, ExplicitSource = sourceKey };
            properties.Add(property, state);
            owner.RegisterPropertyChangedCallback(property, (target, changed) =>
                LocalizeProperty(target, changed, explicitKey, message));
        }
        else if (state.Updating) return;
        else if (sourceKey != state.ExplicitSource && sourceKey is not null)
        {
            state.Source = sourceKey;
            state.ExplicitSource = sourceKey;
        }
        else if (current != state.Rendered && current != state.Source)
        {
            // A new status/label supplied by application code replaces the previous source.
            state.Source = current;
            if (explicitKey) _localizationKeys.AddOrUpdate(owner, current);
            state.ExplicitSource = explicitKey ? current : null;
        }

        state.Rendered = message ? TranslateMessage(state.Source) : T(state.Source);
        if (current == state.Rendered) return;
        state.Updating = true;
        try { owner.SetValue(property, state.Rendered); }
        finally { state.Updating = false; }
    }

    private TextBlock LocalizedText(TextBlock block, string source)
    {
        _localizationKeys.AddOrUpdate(block, source);
        LocalizeProperty(block, TextBlock.TextProperty, explicitKey: true);
        return block;
    }

    private void SetLocalizedToolTip(FrameworkElement owner, string source)
    {
        ToolTipService.SetToolTip(owner, source);
        LocalizeProperty(owner, ToolTipService.ToolTipProperty);
    }

    private void LocalizeElement(DependencyObject element)
        => LocalizeElement(element, new HashSet<DependencyObject>());

    private void LocalizeElement(DependencyObject element, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element) || element is FrameworkElement { Tag: "noloc" }) return;
        void Walk(DependencyObject? child)
        {
            if (child is null || visited.Contains(child)) return;
            child.SetValue(LocalizationStorage.Parent, new WeakReference<DependencyObject>(element));
            LocalizeElement(child, visited);
        }
        void Property(DependencyProperty property, bool sourceKey = false, bool message = false)
            => LocalizeProperty(element, property, sourceKey, message);

        if (element is FrameworkElement framework)
        {
            if (ToolTipService.GetToolTip(framework) is string)
                Property(ToolTipService.ToolTipProperty);
            else Walk(ToolTipService.GetToolTip(framework) as DependencyObject);
            Walk(framework.ContextFlyout);
            Walk(FlyoutBase.GetAttachedFlyout(framework));
        }

        // Localize logical content, never input text or native template bindings.
        switch (element)
        {
            case ContentDialog dialog:
                Property(ContentDialog.TitleProperty);
                Property(ContentDialog.PrimaryButtonTextProperty);
                Property(ContentDialog.SecondaryButtonTextProperty);
                Property(ContentDialog.CloseButtonTextProperty);
                Walk(dialog.Title as DependencyObject);
                if (dialog.Content is string) Property(ContentControl.ContentProperty);
                else Walk(dialog.Content as DependencyObject);
                return;
            case TextBlock text:
                if (text.Inlines.Count > 1 || text.Inlines.Any(inline => inline is Span))
                    foreach (var inline in text.Inlines) Walk(inline);
                else Property(TextBlock.TextProperty, sourceKey: true);
                return;
            case RichTextBlock rich:
                foreach (var block in rich.Blocks) Walk(block);
                return;
            case Paragraph paragraph:
                foreach (var inline in paragraph.Inlines) Walk(inline);
                return;
            case Span span:
                foreach (var inline in span.Inlines) Walk(inline);
                return;
            case Run run:
                if (run.GetValue(LocalizationStorage.RunState) is not LocalizedProperty runState)
                {
                    runState = new LocalizedProperty { Source = run.Text, Rendered = run.Text };
                    run.SetValue(LocalizationStorage.RunState, runState);
                }
                if (run.Text != runState.Rendered) runState.Source = run.Text;
                run.Text = runState.Rendered = T(runState.Source);
                return;
            case InfoBar info:
                Property(InfoBar.TitleProperty);
                Property(InfoBar.MessageProperty, sourceKey: true, message: true);
                Walk(info.ActionButton);
                Walk(info.Content as DependencyObject);
                return;
            case ComboBox combo:
                Property(ComboBox.PlaceholderTextProperty);
                Property(ComboBox.HeaderProperty);
                Walk(combo.Header as DependencyObject);
                foreach (var item in combo.Items.OfType<DependencyObject>()) Walk(item);
                return;
            case TextBox box:
                Property(Microsoft.UI.Xaml.Controls.TextBox.PlaceholderTextProperty, sourceKey: true);
                Property(Microsoft.UI.Xaml.Controls.TextBox.HeaderProperty);
                Walk(box.Header as DependencyObject);
                return;
            case NumberBox box:
                Property(NumberBox.HeaderProperty);
                Property(NumberBox.PlaceholderTextProperty);
                return;
            case ToggleSwitch toggle:
                Property(ToggleSwitch.HeaderProperty, sourceKey: true);
                Walk(toggle.Header as DependencyObject);
                ApplyToggleStateText(toggle);
                return;
            case Expander expander:
                Property(Expander.HeaderProperty, sourceKey: true);
                Walk(expander.Header as DependencyObject);
                if (expander.Content is string) Property(ContentControl.ContentProperty);
                else Walk(expander.Content as DependencyObject);
                return;
            case MenuFlyout flyout:
                foreach (var item in flyout.Items) Walk(item);
                return;
            case MenuFlyoutSubItem submenu:
                Property(MenuFlyoutSubItem.TextProperty);
                foreach (var item in submenu.Items) Walk(item);
                return;
            case MenuFlyoutItem:
                Property(MenuFlyoutItem.TextProperty);
                return;
            case MenuFlyoutSeparator:
            case IconElement:
                return;
            case Flyout flyout:
                Walk(flyout.Content);
                return;
            case Popup popup:
                Walk(popup.Child);
                return;
            case AppBarButton appBar:
                Property(AppBarButton.LabelProperty);
                Walk(appBar.Flyout);
                return;
            case Button button:
                Walk(button.Flyout);
                if (button.Content is string) Property(ContentControl.ContentProperty, sourceKey: true);
                else Walk(button.Content as DependencyObject);
                return;
            case ContentControl content:
                if (content.Content is string) Property(ContentControl.ContentProperty, sourceKey: true);
                else Walk(content.Content as DependencyObject);
                return;
            case Border border:
                Walk(border.Child);
                return;
            case Panel panel:
                foreach (var child in panel.Children) Walk(child);
                return;
        }

        if (element is not UIElement) return;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            Walk(VisualTreeHelper.GetChild(element, i));
    }
}
