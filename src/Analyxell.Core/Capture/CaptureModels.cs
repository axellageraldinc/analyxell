namespace Analyxell.Core.Capture;

/// <summary>
/// Descriptive information about a capture. The device (computer) name is deliberately not read at all.
/// <see cref="Motherboard"/> is shown to the user but is never included in AI messages or reports.
/// </summary>
public sealed record CaptureInfo(
    string Game,
    string ProcessName,
    string Cpu,
    string Gpu,
    string GpuDriver,
    string Resolution,
    string GraphicsApi,
    string PresentationMode,
    string Os,
    string SystemRam,
    string RecordedAtUtc,
    string CaptureToolVersion,
    string Motherboard = "",
    string ResizableBar = "",
    string GameMode = "",
    string Hags = "",
    string DisplayLayer = "",
    string Id = "");

/// <summary>One logged hardware sensor sampled on the sensor time grid.</summary>
public sealed record SensorSeries(string Id, string Name, string Type, double[] Values);

/// <summary>Frame-level and sensor-level data of a single capture run.</summary>
public sealed class CaptureRun
{
    public required double[] TimeSec { get; init; }
    public required double[] FrameMs { get; init; }
    public double[] DisplayMs { get; init; } = [];
    public double[] GpuActiveMs { get; init; } = [];
    public double[] CpuActiveMs { get; init; } = [];
    public bool[] Dropped { get; init; } = [];

    /// <summary>What the capture tool says each frame is ("Application" for the game's own frames). Empty when not recorded.</summary>
    public string[] FrameTypes { get; init; } = [];

    /// <summary>1 when the frame was presented with tearing allowed (typically VSync off). One value per frame.</summary>
    public double[] AllowsTearing { get; init; } = [];

    /// <summary>The present sync interval per frame; 0 means not synchronised to the display refresh.</summary>
    public double[] SyncInterval { get; init; } = [];

    /// <summary>Estimated end-to-end input latency per frame in ms, when the capture includes it.</summary>
    public double[] PcLatencyMs { get; init; } = [];

    /// <summary>Animation error per frame in ms (how far presentation timing deviates from smooth motion).</summary>
    public double[] AnimationErrorMs { get; init; } = [];

    public double[] SensorTimeSec { get; init; } = [];
    public IReadOnlyList<SensorSeries> Sensors { get; init; } = [];

    public double DurationSec => TimeSec.Length == 0 ? 0 : TimeSec[^1];

    public SensorSeries? FindSensor(string name, string type) =>
        Sensors.FirstOrDefault(s =>
            s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
            s.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<SensorSeries> FindSensors(string name, string type) =>
        Sensors.Where(s =>
            s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
            s.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
}

public sealed class CaptureFile
{
    public required CaptureInfo Info { get; init; }
    public required IReadOnlyList<CaptureRun> Runs { get; init; }

    /// <summary>Local path the capture was loaded from. Never sent to the AI.</summary>
    public string SourcePath { get; init; } = "";

    public string DisplayName =>
        string.IsNullOrEmpty(SourcePath) ? Info.Game : Path.GetFileNameWithoutExtension(SourcePath);
}
