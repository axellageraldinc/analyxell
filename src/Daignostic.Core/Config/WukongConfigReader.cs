using System.Globalization;
using System.Text.RegularExpressions;
using Daignostic.Core.Capture;

namespace Daignostic.Core.Config;

/// <summary>
/// Reads Black Myth: Wukong's GameUserSettings.ini. Only values whose meaning is certain are labelled;
/// everything else is passed through as a raw id and flagged so nobody mistakes it for a preset name.
/// </summary>
public sealed partial class WukongConfigReader : IGameConfigReader
{
    private readonly string? _pathOverride;

    public WukongConfigReader(string? pathOverride = null) => _pathOverride = pathOverride;

    public string Name => "Black Myth: Wukong";

    public bool Matches(CaptureInfo info) =>
        info.ProcessName.Equals("b1-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase) ||
        info.Game.Contains("Wukong", StringComparison.OrdinalIgnoreCase);

    public GameSettings? TryRead()
    {
        var path = _pathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "b1", "Saved", "Config", "Windows", "GameUserSettings.ini");

        if (!File.Exists(path))
        {
            return null;
        }

        string[] lines;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            lines = reader.ReadToEnd().Split('\n', StringSplitOptions.TrimEntries);
        }
        catch (IOException)
        {
            return null;
        }

        var ini = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ui = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            ini[key] = value;
            if (key == "UISettingData")
            {
                foreach (Match m in UiPairRegex().Matches(value))
                {
                    ui[m.Groups[1].Value] = m.Groups[2].Value;
                }
            }
        }

        var items = new List<GameSetting>();
        void Add(string key, string label, string? value, bool raw = false)
        {
            if (value is not null)
            {
                items.Add(new GameSetting(key, label, value, raw));
            }
        }

        string? U(string key) => ui.TryGetValue(key, out var v) ? v : null;
        string? OnOff(string key) => U(key) switch { "1" => "On", "0" => "Off", _ => null };

        if (ini.TryGetValue("ResolutionSizeX", out var rx) && ini.TryGetValue("ResolutionSizeY", out var ry))
        {
            Add("resolution", "Resolution", $"{rx}x{ry}");
        }

        Add("ray_tracing", "Ray tracing", OnOff("Rtx"));
        Add("ray_tracing_level", "Ray tracing level (raw id)", U("RtxLevel"), raw: true);
        Add("dlss", "DLSS", OnOff("Dlss"));
        Add("render_scale", "Render scale (raw id)", U("ImageQuality"), raw: true);
        Add("frame_generation", "Frame generation", U("InsertFrame") is { } fg ? (fg == "0" ? "Off" : $"On (raw id {fg})") : null);
        Add("low_latency", "Low latency (Reflex)", OnOff("LowLatencyMode"));
        Add("vsync", "VSync", OnOff("Vsync"));
        Add("frame_cap", "Frame cap (raw id)", U("LockFrameRate"), raw: true);
        Add("display_mode", "Fullscreen mode (raw id)", ini.GetValueOrDefault("FullscreenMode"), raw: true);
        Add("quality_preset", "Quality preset (raw id)", U("QualityLevel"), raw: true);

        foreach (var (key, label) in new[]
                 {
                     ("ViewDistance", "View distance"), ("AntiAliasing", "Anti-aliasing"),
                     ("PostProcessing", "Post-processing"), ("ShadowQuality", "Shadows"),
                     ("TextureQuality", "Textures"), ("FxQuality", "Effects"),
                     ("MaterialQuality", "Materials"), ("VegetationQuality", "Vegetation"),
                     ("GlobalIllumination", "Global illumination"), ("ReflectionQuality", "Reflections"),
                 })
        {
            Add($"quality.{key.ToLowerInvariant()}", $"{label} (raw id)", U(key), raw: true);
        }

        return new GameSettings(
            Name,
            Path.GetFileName(path),
            File.GetLastWriteTimeUtc(path).ToUniversalTime(),
            items);
    }

    [GeneratedRegex("""\("(\w+)",\s*"(-?\d+(?:\.\d+)?)"\)""", RegexOptions.CultureInvariant)]
    private static partial Regex UiPairRegex();

    internal static string Format(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
