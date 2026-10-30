using System.Text.Json;

namespace Daignostic.Core.Capture;

/// <summary>
/// Reads CapFrameX capture files (.json). Read-only: the file is never modified.
/// Uses <see cref="JsonDocument"/> so it is safe under trimming and Native AOT.
/// </summary>
public static class CaptureReader
{
    public static CaptureFile Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Load(stream, path);
    }

    public static CaptureFile Load(Stream stream, string sourcePath = "")
    {
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("Runs", out var runsEl) ||
            runsEl.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Not a CapFrameX capture: the 'Runs' array is missing.");
        }

        var info = root.TryGetProperty("Info", out var infoEl) ? ReadInfo(infoEl) : EmptyInfo();
        var runs = new List<CaptureRun>();
        foreach (var runEl in runsEl.EnumerateArray())
        {
            var run = ReadRun(runEl);
            if (run is not null)
            {
                runs.Add(run);
            }
        }

        if (runs.Count == 0)
        {
            throw new InvalidDataException("The capture contains no usable runs.");
        }

        return new CaptureFile { Info = info, Runs = runs, SourcePath = sourcePath };
    }

    private static CaptureInfo EmptyInfo() => new("", "", "", "", "", "", "", "", "", "", "", "");

    private static CaptureInfo ReadInfo(JsonElement el) => new(
        Game: Str(el, "GameName"),
        ProcessName: Str(el, "ProcessName"),
        Cpu: Str(el, "Processor"),
        Gpu: Str(el, "GPU"),
        GpuDriver: Str(el, "GPUDriverVersion"),
        Resolution: Str(el, "ResolutionInfo"),
        GraphicsApi: Str(el, "ApiInfo"),
        PresentationMode: Str(el, "PresentationMode"),
        Os: Str(el, "OS"),
        SystemRam: Str(el, "SystemRam"),
        RecordedAtUtc: Str(el, "CreationDate"),
        CaptureToolVersion: Str(el, "AppVersion"),
        Motherboard: Str(el, "Motherboard"),
        ResizableBar: Str(el, "ResizableBar"),
        GameMode: Str(el, "WinGameMode"),
        Hags: Str(el, "HAGS"),
        Id: Str(el, "Id"),
        DisplayLayer: Str(el, "DisplayLayer"));

    private static CaptureRun? ReadRun(JsonElement runEl)
    {
        if (!runEl.TryGetProperty("CaptureData", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var time = Doubles(data, "TimeInSeconds");
        var frameMs = Doubles(data, "MsBetweenPresents");
        if (time.Length == 0 || time.Length != frameMs.Length)
        {
            return null;
        }

        var sensorTimes = Array.Empty<double>();
        var sensors = new List<SensorSeries>();
        if (runEl.TryGetProperty("SensorData2", out var sd) && sd.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in sd.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var type = Str(prop.Value, "Type");
                var values = Doubles(prop.Value, "Values");
                if (prop.Name == "MeasureTime")
                {
                    sensorTimes = values;
                }
                else if (type != "Time" && values.Length > 0)
                {
                    sensors.Add(new SensorSeries(prop.Name, Str(prop.Value, "Name"), type, values));
                }
            }
        }

        return new CaptureRun
        {
            TimeSec = time,
            FrameMs = frameMs,
            DisplayMs = Doubles(data, "MsBetweenDisplayChange"),
            GpuActiveMs = Doubles(data, "GpuActive"),
            CpuActiveMs = Doubles(data, "CpuActive"),
            Dropped = Bools(data, "Dropped"),
            AllowsTearing = Doubles(data, "AllowsTearing"),
            SyncInterval = Doubles(data, "SyncInterval"),
            PcLatencyMs = Doubles(data, "PcLatency"),
            AnimationErrorMs = Doubles(data, "AnimationError"),
            SensorTimeSec = sensorTimes,
            Sensors = sensors,
        };
    }

    private static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double[] Doubles(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new double[arr.GetArrayLength()];
        var i = 0;
        foreach (var item in arr.EnumerateArray())
        {
            result[i++] = item.ValueKind == JsonValueKind.Number ? item.GetDouble() : double.NaN;
        }

        return result;
    }

    private static bool[] Bools(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new bool[arr.GetArrayLength()];
        var i = 0;
        foreach (var item in arr.EnumerateArray())
        {
            result[i++] = item.ValueKind == JsonValueKind.True;
        }

        return result;
    }
}
