using System.Globalization;
using System.Text.RegularExpressions;

namespace Analyxell.Core.Analysis;

/// <summary>
/// What the CPU model name tells us about how it should behave: how hot is normal, how fast it should boost, and
/// whether the game can be split across two chiplets. Read from the name only, so it works for any Ryzen.
/// </summary>
internal sealed partial record CpuProfile(string Name, bool IsRyzen, int Series, bool X3D, bool DualCcd, bool Mobile, double? RatedBoostMhz)
{
    public static CpuProfile Unknown { get; } = new("", false, 0, false, false, false, null);

    /// <summary>Ryzen 7000 and 9000 desktop chips are built to boost until they reach about 95 C, so that is not a fault.</summary>
    public bool RunsHotByDesign => IsRyzen && !Mobile && Series is 7000 or 9000;

    /// <summary>Two compute chiplets where only one has the extra cache: the game has to land on the right one.</summary>
    public bool DualCcdX3D => DualCcd && X3D;

    private static readonly Dictionary<string, double> RatedBoostGhz = new(StringComparer.OrdinalIgnoreCase)
    {
        ["3600"] = 4.2, ["3600X"] = 4.4, ["3700X"] = 4.4, ["3800X"] = 4.5, ["3900X"] = 4.6, ["3950X"] = 4.7,
        ["5500"] = 4.2, ["5600"] = 4.4, ["5600X"] = 4.6, ["5700X"] = 4.6, ["5700X3D"] = 4.1, ["5800X"] = 4.7, ["5800X3D"] = 4.5,
        ["5900X"] = 4.8, ["5950X"] = 4.9,
        ["7500F"] = 5.0, ["7600"] = 5.1, ["7600X"] = 5.3, ["7700"] = 5.3, ["7700X"] = 5.4, ["7800X3D"] = 5.0,
        ["7900"] = 5.4, ["7900X"] = 5.6, ["7900X3D"] = 5.6, ["7950X"] = 5.7, ["7950X3D"] = 5.7,
        ["9600X"] = 5.4, ["9700X"] = 5.5, ["9800X3D"] = 5.2, ["9900X"] = 5.6, ["9900X3D"] = 5.5, ["9950X"] = 5.7, ["9950X3D"] = 5.7,
    };

    public static CpuProfile Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Unknown;
        }

        var m = ModelPattern().Match(name);
        if (!m.Success)
        {
            return new CpuProfile(name, name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase), 0, false, false, false, null);
        }

        var tier = int.Parse(m.Groups["tier"].Value, CultureInfo.InvariantCulture);
        var number = int.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture);
        var suffix = m.Groups["suffix"].Value.ToUpperInvariant();
        var series = number / 1000 * 1000;
        var mobile = suffix.Contains('H') || suffix.Contains('U');
        var x3d = suffix.Contains("X3D", StringComparison.Ordinal);
        var dual = tier == 9 && series >= 3000 && (!mobile || suffix.StartsWith("HX", StringComparison.Ordinal));
        double? boost = !mobile && RatedBoostGhz.TryGetValue(number.ToString(CultureInfo.InvariantCulture) + suffix, out var ghz) ? ghz * 1000 : null;
        return new CpuProfile(name, true, series, x3d, dual, mobile, boost);
    }

    [GeneratedRegex(@"Ryzen\s+(?<tier>[3579])\s+(?<num>\d{4})(?<suffix>[A-Z0-9]*)", RegexOptions.IgnoreCase)]
    private static partial Regex ModelPattern();
}

/// <summary>What the GPU model name tells us: which NVIDIA features it has and how hot its memory may run.</summary>
internal sealed partial record GpuProfile(string Name, bool IsNvidia, bool IsRtx, int Series, bool Gddr6x, bool Laptop)
{
    public static GpuProfile Unknown { get; } = new("", false, false, 0, false, false);

    /// <summary>DLSS super resolution runs on every RTX card; GTX and older cards cannot use it.</summary>
    public bool SupportsDlss => IsRtx;

    public bool SupportsDlssFrameGeneration => IsRtx && Series >= 40;

    public bool SupportsMultiFrameGeneration => IsRtx && Series >= 50;

    private static readonly HashSet<string> Gddr6xModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "3070TI", "3080", "3080TI", "3090", "3090TI", "4070", "4070SUPER", "4070TI", "4070TISUPER", "4080", "4080SUPER", "4090",
    };

    public static GpuProfile Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Unknown;
        }

        var nvidia = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase);
        var laptop = name.Contains("Laptop", StringComparison.OrdinalIgnoreCase) || name.Contains("Max-Q", StringComparison.OrdinalIgnoreCase) || name.Contains("Mobile", StringComparison.OrdinalIgnoreCase);
        var m = RtxPattern().Match(name);
        if (!m.Success)
        {
            return new GpuProfile(name, nvidia, false, 0, false, laptop);
        }

        var number = int.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture);
        var suffix = Regex.Replace(m.Groups["suffix"].Value, @"\s+", "").ToUpperInvariant();
        var key = number.ToString(CultureInfo.InvariantCulture) + suffix;
        return new GpuProfile(name, true, true, number / 100, !laptop && Gddr6xModels.Contains(key), laptop);
    }

    [GeneratedRegex(@"RTX\s+(?:PRO\s+)?(?<num>\d{4})(?<suffix>(?:\s*Ti)?(?:\s*SUPER)?)", RegexOptions.IgnoreCase)]
    private static partial Regex RtxPattern();
}
