namespace Daignostic.Core.Capture;

/// <summary>
/// Descriptive information about a capture. Deliberately excludes machine identifiers
/// such as the device name or motherboard so the model can be shared with an AI safely.
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
    string CaptureToolVersion);

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
