using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace Playhub;

public sealed partial class MainWindow
{
    private void AnimatePluginDestination(FrameworkElement destination, Rect source, int version)
    {
        // Compatibility for the parent-owned MorphPluginCard call site.
        // Navigation is immediate; no visual transition is started here.
    }
}
