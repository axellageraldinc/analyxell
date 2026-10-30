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
    Latency,
}

/// <summary>One line on a chart. <see cref="Palette"/> is an index the UI maps to theme colours.</summary>
public sealed record ChartLine(string Name, double[] Times, double[] Values, int Palette, double Thickness = 1.4, double Opacity = 1.0);

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
        foreach (var view in new[] { ChartView.Load, ChartView.Temperatures, ChartView.Clocks, ChartView.Memory, ChartView.Power, ChartView.Latency })
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
                var (t, v) = Smoothing.BinAverage(run.TimeSec, run.FrameMs);
                return Spec(view, "Frame time", "ms", "Lower is smoother. White line: 1 s average.",
                    [new("Frame time", run.TimeSec, run.FrameMs, 0, 1.2, 0.6), new("1 s average", t, v, 5, 2.0)],
                    [new(1000.0 / 60, "60 fps"), new(1000.0 / 30, "30 fps")], null, zeroBased: true, clip: true);
            }

            case ChartView.Fps:
            {
                var fps = run.FrameMs.Select(ms => ms > 0 && double.IsFinite(ms) ? 1000.0 / ms : double.NaN).ToArray();
                var (t, v) = Smoothing.BinAverage(run.TimeSec, fps);
                return Spec(view, "FPS", "fps", "Higher is better. Dips are slow frames. White line: 1 s average.",
                    [new("FPS", run.TimeSec, fps, 0, 1.2, 0.6), new("1 s average", t, v, 5, 2.0)],
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
