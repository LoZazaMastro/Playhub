using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Playhub;

public sealed partial class MainWindow
{
    private const double PluginSearchClosedWidth = StoreSearchController.ClosedWidth;
    private const double PluginSearchMaximumWidth = StoreSearchController.MaximumWidth;
    private StoreSearchController? _pluginSearchController;

    // Existing review hooks read the shared controller's actual state.
    private bool _pluginSearchExpanded => _pluginSearchController?.Expanded == true;
    private Grid? _pluginSearchHost => _pluginSearchController?.Host;
    private Storyboard? _pluginSearchMorph => _pluginSearchController?.Morph;
    private ScaleTransform? _pluginSearchShellScale => _pluginSearchController?.ShellScale;
    private TranslateTransform? _pluginSearchLeadingOffset => _pluginSearchController?.LeadingOffset;

    private Grid BuildCollapsiblePluginSearch(Button showAllButton)
    {
        var width = _pluginSearchController?.ExpandedWidth ?? PluginSearchMaximumWidth;
        _pluginSearchController?.Dispose();
        _pluginSearchController = new StoreSearchController(this, _pluginSearchBox, showAllButton,
            () => T("Cerca plugin e funzioni"), width);
        return _pluginSearchController.Host;
    }

    private void SetPluginSearchExpanded(bool expanded, bool animate = true) =>
        _pluginSearchController?.SetExpanded(expanded, animate);

    private void UpdatePluginSearchWidth(double availableWidth) =>
        _pluginSearchController?.Resize(availableWidth);

    private void FinishPluginSearchMorph() => _pluginSearchController?.Finish();

    private void CollapseEmptyPluginSearchOutside(DependencyObject? source) =>
        _pluginSearchController?.CollapseEmptyOutside(source);

    private static void AddPluginSearchAnimation(Storyboard storyboard, DependencyObject target,
        string property, double from, double to) =>
        StoreSearchController.AddAnimation(storyboard, target, property, from, to);
}
