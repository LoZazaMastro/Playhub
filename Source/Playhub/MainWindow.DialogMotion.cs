using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;

namespace Playhub;

public sealed partial class MainWindow
{
    private static IEnumerable<FrameworkElement> DialogVisuals(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var descendant in DialogVisuals(child)) yield return descendant;
        }
    }

    private void ConfigureDialogEntrance(ContentDialog dialog)
    {
        LocalizeElement(dialog);
        dialog.Loaded += (_, _) => LocalizeElement(dialog);
        dialog.Opened += (_, _) => LocalizeElement(dialog);
        // Let ShowAsync create the native template and run its fade/zoom. Creating
        // it early or animating its backing visuals can offset popup hit testing.
    }
}
