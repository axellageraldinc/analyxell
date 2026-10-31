using Analyxell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Analyxell.Services;

/// <summary>Colours for the edge of a card, picked from the theme so they follow light and dark mode.</summary>
public static class LevelBrushes
{
    public static Brush Edge(MetricLevel level) => Theme(level switch
    {
        MetricLevel.Good => "SystemFillColorSuccessBrush",
        MetricLevel.Warning => "SystemFillColorCautionBrush",
        MetricLevel.Bad => "SystemFillColorCriticalBrush",
        MetricLevel.Focus => "AccentFillColorDefaultBrush",
        _ => "ControlStrongStrokeColorDefaultBrush",
    });

    /// <summary>Colour for the small label that names the level; plain secondary text when there is nothing to flag.</summary>
    public static Brush Label(MetricLevel level) => level == MetricLevel.Neutral ? Theme("TextFillColorSecondaryBrush") : Edge(level);

    private static Brush Theme(string key) =>
        Application.Current.Resources.TryGetValue(key, out var b) && b is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);
}
