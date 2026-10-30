using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Daignostic.Controls;

/// <summary>One line on the chart. <paramref name="BrushKey"/> is a theme resource so colours follow light/dark mode.</summary>
public sealed record ChartSeries(string Name, double[] Times, double[] Values, string BrushKey, double Thickness = 1.4, double Opacity = 1.0);

/// <summary>
/// Frame-time line chart drawn with plain XAML shapes. Long captures are reduced to one min/max pair per pixel
/// column, so a 10 minute capture with 45,000 frames stays responsive. Spikes above the visible range are clipped
/// and marked, because one 800 ms freeze would otherwise flatten the rest of the graph.
/// </summary>
public sealed partial class FrameTimeChart : UserControl
{
    private const double Left = 46, Right = 12, Top = 10, Bottom = 24;

    private IReadOnlyList<ChartSeries> _series = [];
    private IReadOnlyList<double> _markers = [];
    private double _yMax = 40;
    private double _tMax = 1;
    private Rect _plot;

    public FrameTimeChart() => InitializeComponent();

    public void SetData(IReadOnlyList<ChartSeries> series, IReadOnlyList<double> markerTimes)
    {
        _series = series;
        _markers = markerTimes;
        Redraw();
    }

    public void Clear() => SetData([], []);

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
        if (_series.Count == 0 || _series.All(s => s.Times.Length == 0))
        {
            AddText("No data", _plot.X + _plot.Width / 2 - 20, _plot.Y + _plot.Height / 2 - 8, "TextFillColorSecondaryBrush");
            return;
        }

        _tMax = Math.Max(1, _series.Max(s => s.Times.Length == 0 ? 0 : s.Times[^1]));
        _yMax = ChooseYMax();

        DrawGrid();
        foreach (var s in _series)
        {
            DrawSeries(s);
        }

        DrawMarkers();
        AutomationProperties.SetName(this, Describe());
    }

    private double ChooseYMax()
    {
        var all = _series.SelectMany(s => s.Values).Where(double.IsFinite).OrderBy(v => v).ToArray();
        if (all.Length == 0)
        {
            return 40;
        }

        var p999 = all[(int)Math.Min(all.Length - 1, all.Length * 0.999)];
        var target = Math.Max(33.4, p999 * 1.25);
        foreach (var step in new double[] { 20, 25, 40, 50, 100, 200, 400 })
        {
            if (target <= step)
            {
                return step;
            }
        }

        return Math.Ceiling(target / 100) * 100;
    }

    private double X(double t) => _plot.X + t / _tMax * _plot.Width;

    private double Y(double ms) => _plot.Y + _plot.Height - Math.Min(ms, _yMax) / _yMax * _plot.Height;

    private void DrawGrid()
    {
        var step = _yMax <= 20 ? 5 : _yMax <= 50 ? 10 : _yMax <= 100 ? 20 : _yMax <= 200 ? 50 : 100;
        for (double v = 0; v <= _yMax + 0.001; v += step)
        {
            AddLine(_plot.X, Y(v), _plot.X + _plot.Width, Y(v), "DividerStrokeColorDefaultBrush", 1, null);
            AddText($"{v:0}", 4, Y(v) - 8, "TextFillColorSecondaryBrush");
        }

        foreach (var (ms, label) in new[] { (1000.0 / 60, "60 fps"), (1000.0 / 30, "30 fps") })
        {
            if (ms < _yMax)
            {
                AddLine(_plot.X, Y(ms), _plot.X + _plot.Width, Y(ms), "TextFillColorTertiaryBrush", 1, [4, 4]);
                AddText(label, _plot.X + _plot.Width - 40, Y(ms) - 16, "TextFillColorTertiaryBrush");
            }
        }

        var tStep = _tMax <= 60 ? 10 : _tMax <= 180 ? 30 : _tMax <= 600 ? 60 : _tMax <= 1800 ? 300 : 600;
        for (double t = 0; t <= _tMax; t += tStep)
        {
            AddText(TimeLabel(t), X(t) - 12, _plot.Y + _plot.Height + 4, "TextFillColorSecondaryBrush");
        }
    }

    private void DrawSeries(ChartSeries s)
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
            Stroke = Brush(s.BrushKey),
            StrokeThickness = s.Thickness,
            Opacity = s.Opacity,
            StrokeLineJoin = PenLineJoin.Round,
        });
    }

    private void DrawMarkers()
    {
        foreach (var t in _markers)
        {
            AddLine(X(t), _plot.Y, X(t), _plot.Y + _plot.Height, "SystemFillColorCriticalBrush", 1, [2, 3]);
            var tri = new Polygon
            {
                Points = [new Point(X(t) - 4, _plot.Y), new Point(X(t) + 4, _plot.Y), new Point(X(t), _plot.Y + 7)],
                Fill = Brush("SystemFillColorCriticalBrush"),
            };
            Plot.Children.Add(tri);
        }
    }

    private void Plot_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_series.Count == 0 || _plot.Width <= 0)
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
        foreach (var s in _series)
        {
            var i = NearestIndex(s.Times, t);
            if (i >= 0)
            {
                var ms = s.Values[i];
                lines.Add($"{s.Name}: {ms.ToString("0.0", CultureInfo.InvariantCulture)} ms ({(1000 / Math.Max(ms, 0.01)).ToString("0", CultureInfo.InvariantCulture)} fps)");
            }
        }

        TipText.Text = string.Join("\n", lines);
        Tip.Margin = new Thickness(Math.Min(pos.X + 12, Math.Max(0, Root.ActualWidth - 190)), Math.Max(0, pos.Y - 40), 0, 0);
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
        var first = _series[0];
        var worst = first.Values.Where(double.IsFinite).DefaultIfEmpty(0).Max();
        return $"Frame time chart, {_series.Count} series over {TimeLabel(_tMax)}, worst frame {worst:0} milliseconds, {_markers.Count} freeze markers.";
    }

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
