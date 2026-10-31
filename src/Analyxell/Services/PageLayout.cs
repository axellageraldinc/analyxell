using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Analyxell.Services;

/// <summary>
/// Keeps a page's content as wide as the window allows, up to a comfortable maximum, and centred beyond that.
/// A MaxWidth on a stretched panel inside a ScrollViewer is measured before the window's final size is known
/// and can leave the content wider than the window, so the width is set from the viewport instead.
/// </summary>
public static class PageLayout
{
    public static void Constrain(ScrollViewer scroller, FrameworkElement content, double maxWidth)
    {
        content.HorizontalAlignment = HorizontalAlignment.Center;
        scroller.SizeChanged += (_, e) => Apply(e.NewSize.Width);
        Apply(scroller.ActualWidth);

        void Apply(double viewport)
        {
            if (viewport > 0)
            {
                content.Width = Math.Min(viewport, maxWidth);
            }
        }
    }
}
