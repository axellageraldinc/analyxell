using Analyxell.Core.Analysis;
using Microsoft.UI.Xaml.Media;

namespace Analyxell.Services;

/// <summary>
/// One colour per kind of marked moment, shared by the chart and the switches under it. None of them is used by a
/// data line (blue, yellow, green, grey, white), and every kind is also drawn with its own shape.
/// </summary>
internal static class EventStyles
{
    public static Windows.UI.Color Color(EventKind kind) => kind switch
    {
        EventKind.Freezes => Windows.UI.Color.FromArgb(255, 255, 77, 77),
        EventKind.Stutters => Windows.UI.Color.FromArgb(255, 255, 70, 200),
        EventKind.DroppedFrames => Windows.UI.Color.FromArgb(255, 165, 107, 255),
        EventKind.FpsDrops => Windows.UI.Color.FromArgb(255, 255, 138, 31),
        _ => Windows.UI.Color.FromArgb(255, 25, 211, 230),
    };

    public static Brush Brush(EventKind kind) => new SolidColorBrush(Color(kind));
}
