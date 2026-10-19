using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Playhub;

internal static class GameCardBodyPresentation
{
    /// <summary>Preserves natural content sizes and anchors actions in the shared row slot.</summary>
    internal static Grid AnchorFooter(StackPanel body,FrameworkElement footer)
    {
        body.Children.Remove(footer);
        var children=body.Children.ToArray();body.Children.Clear();
        var grid=new Grid{HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch};
        foreach(var child in children)
        {
            int row=grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            // Keep the original control and its margins; only preserve the body's
            // inter-item spacing. The empty star row adds no second artificial gap.
            var slot=new Border{Child=child,Margin=new Thickness(0,0,0,body.Spacing),HorizontalAlignment=HorizontalAlignment.Stretch};
            Grid.SetRow(slot,row);grid.Children.Add(slot);
        }
        grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        Grid.SetRow(footer,grid.RowDefinitions.Count-1);grid.Children.Add(footer);
        return grid;
    }
}
