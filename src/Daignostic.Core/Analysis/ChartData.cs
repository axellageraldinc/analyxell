using Daignostic.Core.Capture;

namespace Daignostic.Core.Analysis;

public enum ChartView
{
    FrameTime,
    Fps,
    Load,
    Temperatures,
    Clocks,
    Memory,
    Power,
    Disk,
    Latency,
}

/// <summary>One line on a chart. <see cref="Palette"/> is an index the UI maps to theme colours.</summary>
public sealed record ChartLine(string Name, double[] Times, double[] Values, int Palette, double Thickness = 1.4, double Opacity = 1.0, bool Legend = true);

public sealed record ChartReference(double Value, string Label);

public sealed record ChartSpec(
    ChartView View,
    string Label,
    string Unit,
    string Caption,
    IReadOnlyList<ChartLine> Lines,
    IReadOnlyList<ChartReference> References,
    double? FixedMax,
    bool ZeroBased,
    bool ClipSpikes);

/// <summary>Builds the lines for each chart view from a capture, offering only the views the capture has data for.</summary>
public static class ChartDataBuilder
{
    public static IReadOnlyList<ChartView> Available(CaptureRun run)
    {
        var views = new List<ChartView> { ChartView.FrameTime, ChartView.Fps };
        foreach (var view in new[] { ChartView.Load, ChartView.Temperatures, ChartView.Clocks, ChartView.Memory, ChartView.Power, ChartView.Disk, ChartView.Latency })
        {
            if (Build(run, view).Lines.Count > 0)
            {
                views.Add(view);
            }
        }

        return views;
    }

    public static ChartSpec Build(CaptureRun run, ChartView view)
    {
        switch (view)
        {
            case ChartView.FrameTime:
            {
                var series = FrameGenerationSignal.SeriesFor(run);
                var (t, v) = Smoothing.BinAverage(series.Times, series.Ms);
                return Spec(view, "Frame time", "ms", "Lower is smoother. White line: 1 s average." + (series.FromDisplay ? " Shows the frames the screen displays (generated ones included)." : ""),
                    [new("Frame time", series.Times, series.Ms, 0, 1.2, 0.6), new("1 s average", t, v, 5, 2.0)],
                    [new(1000.0 / 60, "60 fps"), new(1000.0 / 30, "30 fps")], null, zeroBased: true, clip: true);
            }

            case ChartView.Fps:
            {
                var series = FrameGenerationSignal.SeriesFor(run);
                var fps = series.Ms.Select(ms => ms > 0 && double.IsFinite(ms) ? 1000.0 / ms : double.NaN).ToArray();
                var (t, v) = Smoothing.BinAverage(series.Times, fps);
                return Spec(view, "FPS", "fps", "Higher is better. Dips are slow frames. White line: 1 s average." + (series.FromDisplay ? " Shows the frames the screen displays (generated ones included)." : ""),
                    [new("FPS", series.Times, fps, 0, 1.2, 0.6), new("1 s average", t, v, 5, 2.0)],
                    [new(60, "60"), new(30, "30")], null, zeroBased: true, clip: true);
            }

            case ChartView.Load:
                return Spec(view, "GPU and CPU load", "%", "How busy the hardware was. A GPU near 100% is the limit; a busy CPU thread with a quiet GPU is a CPU limit.",
                    Sensors(run, ("GPU Core", "Load", "GPU load", 0), ("CPU Total", "Load", "CPU total", 2), ("CPU Max", "Load", "Busiest CPU thread", 1)),
                    [], 100, zeroBased: true, clip: false);

            case ChartView.Temperatures:
                return Spec(view, "Temperatures", "°C", "Heat. Sustained values near your hardware's limit make it slow down.",
                    Sensors(run, ("GPU Core", "Temperature", "GPU", 0), ("GPU Hot Spot", "Temperature", "GPU hot spot", 1), ("CPU Package (Tctl/Tdie)", "Temperature", "CPU", 2), ("GPU Memory Junction", "Temperature", "Video memory", 4)),
                    [], null, zeroBased: false, clip: false);

            case ChartView.Clocks:
                return Spec(view, "Clock speeds", "MHz", "Dips under load can mean power, heat or driver limits.",
                    Sensors(run, ("GPU Core", "Clock", "GPU core", 0), ("CPU Max", "Clock", "CPU fastest core", 2)),
                    [], null, zeroBased: false, clip: false);

            case ChartView.Memory:
                return Spec(view, "Memory", "GB", "Video memory used is the line to watch against your card's total.",
                    Sensors(run, ("GPU Memory Dedicated", "Data", "Video memory used", 0), ("RAM Used", "Data", "System memory used", 2), ("RAM Game Used", "Data", "Used by the game", 1)),
                    [], null, zeroBased: true, clip: false);

            case ChartView.Power:
                return Spec(view, "Power draw", "W", "Power drawn by the GPU and CPU package.",
                    Sensors(run, ("GPU Power", "Power", "GPU", 0), ("CPU Package", "Power", "CPU", 2)),
                    [], null, zeroBased: true, clip: false);

            case ChartView.Disk:
            {
                // One line per drive the monitoring software saw. Freezes that line up with a spike here point at storage.
                var lines = new List<ChartLine>();
                var palette = 0;
                foreach (var drive in run.FindSensors("Drive Total Activity", "Load"))
                {
                    if (drive.Values.Length == run.SensorTimeSec.Length && drive.Values.Any(double.IsFinite))
                    {
                        lines.Add(new ChartLine(DriveLabel(drive.Id), run.SensorTimeSec, drive.Values, palette++ % 5));
                    }
                }

                return Spec(view, "Drive activity", "%",
                    "How busy each drive was. A drive near 100% when the game freezes points at storage. 'NVMe' is an NVMe SSD; 'SATA or other' is a hard disk or SATA SSD. The numbers are the monitoring software's order, not Windows disk numbers.",
                    lines, [], 100, zeroBased: true, clip: false);
            }

            case ChartView.Latency:
            {
                var lat = run.PcLatencyMs;
                if (lat.Length != run.TimeSec.Length || lat.All(v => !double.IsFinite(v) || v <= 0))
                {
                    return Spec(view, "Latency", "ms", "", [], [], null, zeroBased: true, clip: true);
                }

                var (t, v) = Smoothing.BinAverage(run.TimeSec, lat);
                return Spec(view, "Latency", "ms", "Estimated time from input to the result on screen. Lower is more responsive.",
                    [new("Latency", run.TimeSec, lat, 0, 1.2, 0.5), new("1 s average", t, v, 5, 2.0)], [], null, zeroBased: true, clip: true);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(view));
        }
    }

    /// <summary>Views both runs have data for, so two runs can be overlaid on the same chart.</summary>
    public static IReadOnlyList<ChartView> AvailableForComparison(CaptureRun a, CaptureRun b) =>
        Enum.GetValues<ChartView>().Where(v => BuildComparison(a, b, v, "A", "B").Lines.Count > 0).ToList();

    /// <summary>
    /// One metric per view, drawn for run A (amber) and run B (blue) so they are easy to tell apart. Time-based views
    /// show a faint raw line and a bold one-second average per run; sensor views show the metric named in the caption.
    /// </summary>
    public static ChartSpec BuildComparison(CaptureRun a, CaptureRun b, ChartView view, string nameA, string nameB)
    {
        const int PaletteA = 1;
        const int PaletteB = 0;
        var labelA = $"A: {nameA}";
        var labelB = $"B: {nameB}";

        switch (view)
        {
            case ChartView.FrameTime:
            case ChartView.Fps:
            case ChartView.Latency:
            {
                var sa = FrameGenerationSignal.SeriesFor(a);
                var sb = FrameGenerationSignal.SeriesFor(b);
                double[] Values(CaptureRun r, FrameGenerationSignal.FrameSeries s) => view switch
                {
                    ChartView.Fps => s.Ms.Select(ms => ms > 0 && double.IsFinite(ms) ? 1000.0 / ms : double.NaN).ToArray(),
                    ChartView.Latency => r.PcLatencyMs,
                    _ => s.Ms,
                };

                var va = Values(a, sa);
                var vb = Values(b, sb);
                double[] TimesOf(CaptureRun r, FrameGenerationSignal.FrameSeries s) => view == ChartView.Latency ? r.TimeSec : s.Times;
                var timesA = TimesOf(a, sa);
                var timesB = TimesOf(b, sb);
                if (view == ChartView.Latency && (va.Length != a.TimeSec.Length || vb.Length != b.TimeSec.Length || va.All(v => !double.IsFinite(v) || v <= 0) || vb.All(v => !double.IsFinite(v) || v <= 0)))
                {
                    return Spec(view, "Latency", "ms", "", [], [], null, true, true);
                }

                var (ta, avgA) = Smoothing.BinAverage(timesA, va);
                var (tb, avgB) = Smoothing.BinAverage(timesB, vb);
                var label = view == ChartView.Fps ? "FPS" : view == ChartView.Latency ? "Latency" : "Frame time";
                var unit = view == ChartView.Fps ? "fps" : "ms";
                var refs = view == ChartView.FrameTime ? [new ChartReference(1000.0 / 60, "60 fps"), new ChartReference(1000.0 / 30, "30 fps")]
                    : view == ChartView.Fps ? [new ChartReference(60, "60"), new ChartReference(30, "30")]
                    : Array.Empty<ChartReference>();
                return Spec(view, label, unit, $"Bold lines are one-second averages; faint lines are every frame. {(view == ChartView.Fps ? "Higher" : "Lower")} is better.",
                    [
                        new(labelA + " (all frames)", timesA, va, PaletteA, 1.0, 0.3, false),
                        new(labelB + " (all frames)", timesB, vb, PaletteB, 1.0, 0.3, false),
                        new(labelA, ta, avgA, PaletteA, 2.6),
                        new(labelB, tb, avgB, PaletteB, 2.6),
                    ],
                    refs, null, zeroBased: true, clip: true);
            }

            default:
            {
                var (name, type, label, unit, fixedMax, zero) = view switch
                {
                    ChartView.Load => ("GPU Core", "Load", "GPU load", "%", (double?)100, true),
                    ChartView.Temperatures => ("GPU Core", "Temperature", "GPU temperature", "°C", null, false),
                    ChartView.Clocks => ("GPU Core", "Clock", "GPU core clock", "MHz", null, false),
                    ChartView.Memory => ("GPU Memory Dedicated", "Data", "Video memory used", "GB", null, true),
                    ChartView.Power => ("GPU Power", "Power", "GPU power draw", "W", null, true),
                    _ => ("", "", "", "", null, true),
                };

                var lines = new List<ChartLine>();
                void Add(CaptureRun r, string text, int palette)
                {
                    var s = r.FindSensor(name, type);
                    if (s is not null && s.Values.Length == r.SensorTimeSec.Length && s.Values.Any(double.IsFinite))
                    {
                        lines.Add(new ChartLine(text, r.SensorTimeSec, s.Values, palette, 2.2));
                    }
                }

                Add(a, labelA, PaletteA);
                Add(b, labelB, PaletteB);
                return Spec(view, label, unit, $"{label} for both runs.", lines.Count == 2 ? lines : [], [], fixedMax, zero, false);
            }
        }
    }

    /// <summary>"/nvme/1/load/33" becomes "NVMe drive 1"; "/hdd/0/load/33" becomes "SATA or other drive 0".</summary>
    internal static string DriveLabel(string sensorId)
    {
        var parts = sensorId.Trim('/').Split('/');
        var kind = parts.Length > 0 && parts[0].Equals("nvme", StringComparison.OrdinalIgnoreCase) ? "NVMe" : "SATA or other";
        return parts.Length > 1 ? $"{kind} drive {parts[1]}" : $"{kind} drive";
    }

    private static ChartSpec Spec(ChartView view, string label, string unit, string caption, IReadOnlyList<ChartLine> lines,
        IReadOnlyList<ChartReference> refs, double? fixedMax, bool zeroBased, bool clip) =>
        new(view, label, unit, caption, lines, refs, fixedMax, zeroBased, clip);

    private static List<ChartLine> Sensors(CaptureRun run, params (string Name, string Type, string Label, int Palette)[] wanted)
    {
        var lines = new List<ChartLine>();
        foreach (var (name, type, label, palette) in wanted)
        {
            var sensor = run.FindSensor(name, type);
            if (sensor is not null && sensor.Values.Length == run.SensorTimeSec.Length && sensor.Values.Any(double.IsFinite))
            {
                lines.Add(new ChartLine(label, run.SensorTimeSec, sensor.Values, palette));
            }
        }

        return lines;
    }
}
