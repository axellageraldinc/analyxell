using System.Globalization;
using Daignostic.Core.Analysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Daignostic.Controls;

/// <summary>
/// Line chart drawn with plain XAML shapes. It renders any <see cref="ChartSpec"/>. Long captures are reduced to one
/// min/max pair per pixel column, so a 10 minute capture with 45,000 frames stays responsive. For frame-time style
/// charts, spikes above the visible range are clipped and marked, because one 800 ms freeze would otherwise flatten
/// the rest of the graph.
/// </summary>
public sealed partial class FrameTimeChart : UserControl
{
    private const double Left = 52, Right = 12, Top = 10, Bottom = 24;

    // Theme resource keys; the UI picks colours that follow light and dark mode.
    private static readonly string[] Palette =
    [
        "AccentFillColorDefaultBrush",
        "SystemFillColorCautionBrush",
        "SystemFillColorSuccessBrush",
        "TextFillColorTertiaryBrush",
        "SystemFillColorAttentionBrush",
        "TextFillColorPrimaryBrush",
    ];

    private ChartSpec? _spec;
    private IReadOnlyList<EventLayer> _layers = [];
    private double _yMin;
    private double _yMax = 40;
    private double _tMax = 1;
    private Rect _plot;

    public FrameTimeChart() => InitializeComponent();

    public void SetSpec(ChartSpec? spec, IReadOnlyList<EventLayer> layers)
    {
        _spec = spec;
        _layers = layers;
        Redraw();
    }

    /// <summary>Marks freezes only, for views that have no other kind of moment to show.</summary>
    public void SetSpec(ChartSpec? spec, IReadOnlyList<double> freezeTimes) =>
        SetSpec(spec, [ChartEventBuilder.Freezes(freezeTimes)]);

    public void Clear() => SetSpec(null, Array.Empty<EventLayer>());

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        Plot.Children.Clear();
        Tip.Visibility = Visibility.Collapsed;
        var w = Root.ActualWidth;
        var h = Root.ActualHeight;
        if (w < 120 || h < 80)
        {
            return;
        }

        _plot = new Rect(Left, Top, w - Left - Right, h - Top - Bottom);
        if (_spec is null || _spec.Lines.Count == 0 || _spec.Lines.All(s => s.Times.Length == 0))
        {
            AddText(_spec is null ? "No data" : "This capture has no data for this view", _plot.X + 12, _plot.Y + _plot.Height / 2 - 8, "TextFillColorSecondaryBrush");
            return;
        }

        _tMax = Math.Max(1, _spec.Lines.Max(s => s.Times.Length == 0 ? 0 : s.Times[^1]));
        ChooseRange();

        DrawGrid();
        DrawBands();
        foreach (var line in _spec.Lines)
        {
            DrawLine(line);
        }

        DrawMarkers();
        DrawLegend();
        AutomationProperties.SetName(this, Describe());
    }

    private void ChooseRange()
    {
        var spec = _spec!;
        var all = spec.Lines.SelectMany(l => l.Values).Where(double.IsFinite).OrderBy(v => v).ToArray();
        if (all.Length == 0)
        {
            _yMin = 0;
            _yMax = 1;
            return;
        }

        var min = all[0];
        var max = all[^1];
        double top;
        if (spec.FixedMax is { } fixedMax)
        {
            top = fixedMax;
        }
        else if (spec.ClipSpikes)
        {
            top = all[(int)Math.Min(all.Length - 1, all.Length * 0.999)] * 1.25;
        }
        else
        {
            top = max + (max - (spec.ZeroBased ? 0 : min)) * 0.08;
        }

        _yMin = spec.ZeroBased ? 0 : NiceFloor(min - Math.Max(1, (max - min) * 0.1));
        _yMax = Math.Max(_yMin + 1, NiceCeiling(Math.Max(top, _yMin + 1)));
    }

    private static double NiceCeiling(double value)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var m in new[] { 1.0, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0, 7.5, 10.0 })
        {
            if (value <= m * magnitude)
            {
                return m * magnitude;
            }
        }

        return 10 * magnitude;
    }

    private static double NiceFloor(double value)
    {
        if (value <= 0)
        {
            return 0;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        return Math.Floor(value / magnitude * 2) / 2 * magnitude;
    }

    private double X(double t) => _plot.X + t / _tMax * _plot.Width;

    private double Y(double value) => _plot.Y + _plot.Height - (Math.Clamp(value, _yMin, _yMax) - _yMin) / (_yMax - _yMin) * _plot.Height;

    private void DrawGrid()
    {
        var range = _yMax - _yMin;
        var rawStep = range / 5;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        var step = new[] { 1.0, 2.0, 2.5, 5.0, 10.0 }.Select(m => m * magnitude).FirstOrDefault(s => s >= rawStep, 10 * magnitude);

        for (var v = Math.Ceiling(_yMin / step) * step; v <= _yMax + step * 0.001; v += step)
        {
            AddLine(_plot.X, Y(v), _plot.X + _plot.Width, Y(v), "DividerStrokeColorDefaultBrush", 1, null);
            AddText(Label(v), 4, Y(v) - 8, "TextFillColorSecondaryBrush");
        }

        foreach (var reference in _spec!.References)
        {
            if (reference.Value > _yMin && reference.Value < _yMax)
            {
                AddLine(_plot.X, Y(reference.Value), _plot.X + _plot.Width, Y(reference.Value), "TextFillColorTertiaryBrush", 1, [4, 4]);
                AddText(reference.Label, _plot.X + _plot.Width - 40, Y(reference.Value) - 16, "TextFillColorTertiaryBrush");
            }
        }

        var tStep = _tMax <= 60 ? 10 : _tMax <= 180 ? 30 : _tMax <= 600 ? 60 : _tMax <= 1800 ? 300 : 600;
        for (double t = 0; t <= _tMax; t += tStep)
        {
            AddText(TimeLabel(t), X(t) - 12, _plot.Y + _plot.Height + 4, "TextFillColorSecondaryBrush");
        }
    }

    private void DrawLine(ChartLine s)
    {
        var n = Math.Min(s.Times.Length, s.Values.Length);
        if (n == 0)
        {
            return;
        }

        var buckets = Math.Max(50, (int)_plot.Width);
        var min = new double[buckets];
        var max = new double[buckets];
        var minIdx = new int[buckets];
        var maxIdx = new int[buckets];
        Array.Fill(min, double.MaxValue);
        Array.Fill(max, double.MinValue);

        for (var i = 0; i < n; i++)
        {
            var v = s.Values[i];
            if (!double.IsFinite(v))
            {
                continue;
            }

            var b = Math.Clamp((int)(s.Times[i] / _tMax * (buckets - 1)), 0, buckets - 1);
            if (v < min[b])
            {
                min[b] = v;
                minIdx[b] = i;
            }

            if (v > max[b])
            {
                max[b] = v;
                maxIdx[b] = i;
            }
        }

        var points = new PointCollection();
        for (var b = 0; b < buckets; b++)
        {
            if (min[b] == double.MaxValue)
            {
                continue;
            }

            var (first, second) = minIdx[b] <= maxIdx[b] ? (min[b], max[b]) : (max[b], min[b]);
            var x = X(s.Times[Math.Min(minIdx[b], maxIdx[b])]);
            points.Add(new Point(x, Y(first)));
            if (first != second)
            {
                points.Add(new Point(x, Y(second)));
            }
        }

        Plot.Children.Add(new Polyline
        {
            Points = points,
            Stroke = Brush(Palette[s.Palette % Palette.Length]),
            StrokeThickness = s.Thickness,
            Opacity = s.Opacity,
            StrokeLineJoin = PenLineJoin.Round,
        });
    }

    // Colours per kind of moment; theme resource keys so they follow light and dark mode.
    private static string EventBrush(EventKind kind) => kind switch
    {
        EventKind.Freezes => "SystemFillColorCriticalBrush",
        EventKind.Stutters => "SystemFillColorCautionBrush",
        EventKind.FpsDrops => "SystemFillColorCautionBrush",
        EventKind.DroppedFrames => "TextFillColorSecondaryBrush",
        _ => "SystemFillColorAttentionBrush",
    };

    /// <summary>Stretches of the run (FPS drops, tearing possible) are shaded behind the lines.</summary>
    private void DrawBands()
    {
        foreach (var layer in _layers.Where(l => l.IsSpan))
        {
            foreach (var e in layer.Events)
            {
                var x1 = X(Math.Min(e.StartSec, _tMax));
                var x2 = Math.Max(x1 + 2, X(Math.Min(e.EndSec, _tMax)));
                var band = new Rectangle { Width = x2 - x1, Height = _plot.Height, Fill = Brush(EventBrush(layer.Kind)), Opacity = layer.Kind == EventKind.FpsDrops ? 0.18 : 0.10 };
                Plot.Children.Add(band.At(x1, _plot.Y));
            }
        }
    }

    /// <summary>Single moments: freezes get a full-height line and a flag; stutters and dropped frames get short ticks at the bottom.</summary>
    private void DrawMarkers()
    {
        foreach (var layer in _layers.Where(l => !l.IsSpan))
        {
            var key = EventBrush(layer.Kind);
            foreach (var e in layer.Events)
            {
                var x = X(e.StartSec);
                if (layer.Kind == EventKind.Freezes)
                {
                    AddLine(x, _plot.Y, x, _plot.Y + _plot.Height, key, 1, [2, 3]);
                    Plot.Children.Add(new Polygon
                    {
                        Points = [new Point(x - 4, _plot.Y), new Point(x + 4, _plot.Y), new Point(x, _plot.Y + 7)],
                        Fill = Brush(key),
                    });
                }
                else
                {
                    var tall = layer.Kind == EventKind.Stutters ? 12 : 7;
                    AddLine(x, _plot.Y + _plot.Height - tall, x, _plot.Y + _plot.Height, key, 1.5, null);
                }
            }
        }
    }

    private void DrawLegend()
    {
        if (_spec!.Lines.Count(l => l.Legend) < 2)
        {
            return;
        }

        var x = _plot.X + 10;
        foreach (var line in _spec.Lines.Where(l => l.Legend))
        {
            Plot.Children.Add(new Rectangle { Width = 10, Height = 3, Fill = Brush(Palette[line.Palette % Palette.Length]) }.At(x, _plot.Y + 4));
            AddText(line.Name, x + 14, _plot.Y - 2, "TextFillColorSecondaryBrush");
            x += 24 + line.Name.Length * 6.2;
        }

    }

    private void Plot_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_spec is null || _spec.Lines.Count == 0 || _plot.Width <= 0)
        {
            return;
        }

        var pos = e.GetCurrentPoint(Plot).Position;
        if (pos.X < _plot.X || pos.X > _plot.X + _plot.Width)
        {
            Tip.Visibility = Visibility.Collapsed;
            return;
        }

        var t = (pos.X - _plot.X) / _plot.Width * _tMax;
        var lines = new List<string> { TimeLabel(t) };
        foreach (var s in _spec.Lines.Where(l => l.Legend))
        {
            var i = NearestIndex(s.Times, t);
            if (i < 0 || i >= s.Values.Length)
            {
                continue;
            }

            var v = s.Values[i];
            var text = _spec.View == ChartView.FrameTime && s.Name == "Frame time"
                ? $"{s.Name}: {Format(v)} ms ({Format(1000 / Math.Max(v, 0.01))} fps)"
                : $"{s.Name}: {Format(v)} {_spec.Unit}";
            lines.Add(text);
        }

        TipText.Text = string.Join("\n", lines);
        Tip.Margin = new Thickness(Math.Min(pos.X + 12, Math.Max(0, Root.ActualWidth - 210)), Math.Max(0, pos.Y - 40), 0, 0);
        Tip.Visibility = Visibility.Visible;
    }

    private void Plot_PointerExited(object sender, PointerRoutedEventArgs e) => Tip.Visibility = Visibility.Collapsed;

    private static int NearestIndex(double[] times, double t)
    {
        if (times.Length == 0)
        {
            return -1;
        }

        int lo = 0, hi = times.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (times[mid] < t)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private string Describe()
    {
        var first = _spec!.Lines[0];
        var worst = first.Values.Where(double.IsFinite).DefaultIfEmpty(0).Max();
        return $"{_spec.Label} chart, {_spec.Lines.Count} line(s) over {TimeLabel(_tMax)}, highest value {Format(worst)} {_spec.Unit}, {string.Join(", ", _layers.Where(l => l.Count > 0).Select(l => $"{l.Count} {l.Label.ToLowerInvariant()}"))} marked.";
    }

    private static string Label(double v) => Math.Abs(v) >= 1000 ? $"{v / 1000:0.#}k" : v.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Format(double v) => v.ToString(Math.Abs(v) >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture);

    private static string TimeLabel(double t) => $"{(int)t / 60}:{(int)t % 60:00}";

    private static Brush Brush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var b) && b is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    private void AddLine(double x1, double y1, double x2, double y2, string brushKey, double thickness, double[]? dashes)
    {
        var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Brush(brushKey), StrokeThickness = thickness };
        if (dashes is not null)
        {
            line.StrokeDashArray = [.. dashes];
        }

        Plot.Children.Add(line);
    }

    private void AddText(string text, double x, double y, string brushKey)
    {
        var tb = new TextBlock { Text = text, FontSize = 11, Foreground = Brush(brushKey) };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        Plot.Children.Add(tb);
    }
}

internal static class ShapeExtensions
{
    public static T At<T>(this T element, double left, double top)
        where T : UIElement
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        return element;
    }
}
