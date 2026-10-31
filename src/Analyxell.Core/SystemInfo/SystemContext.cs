namespace Analyxell.Core.SystemInfo;

public enum SettingSource
{
    /// <summary>Read automatically and read-only from Windows or the file system.</summary>
    Auto,

    /// <summary>Entered by the user. Taken at their word, never verified.</summary>
    User,
}

public sealed record SystemSetting(string Key, string Label, string Value, SettingSource Source);

/// <summary>Everything we know about the PC setup that a capture does not contain.</summary>
public sealed class SystemContext
{
    public static SystemContext Empty { get; } = new([]);

    public SystemContext(IReadOnlyList<SystemSetting> items) => Items = items;

    public IReadOnlyList<SystemSetting> Items { get; }

    public string? Get(string key) =>
        Items.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;

    public bool Is(string key, string expected) =>
        string.Equals(Get(key), expected, StringComparison.OrdinalIgnoreCase);
}

public sealed record StorageInfo(string Root, string MediaType, string BusType, string Model, double TotalGb, double FreeGb, string FileSystem)
{
    public double FreePercent => TotalGb <= 0 ? 0 : FreeGb / TotalGb * 100.0;
}

public sealed record DisplayMode(int Width, int Height, int RefreshHz);

public sealed record DirectoryTotals(long Bytes, int Files, bool Truncated);

/// <summary>
/// Read-only window onto the machine. The real implementation reads the registry and queries the storage stack;
/// tests supply a fake. Nothing here can change the system.
/// </summary>
public interface ISystemProbe
{
    int? RegistryDword(string hive, string subKey, string name);

    string? RegistryString(string hive, string subKey, string name);

    DirectoryTotals? DirectorySize(string path, int maxFiles, TimeSpan budget);

    StorageInfo? Storage(string path);

    DisplayMode? PrimaryDisplay();

    int? MonitorCount();

    string? ReadText(string path);

    IEnumerable<string> EnumerateFiles(string directory, string pattern);

    bool DirectoryExists(string path);

    bool FileExists(string path);

    IEnumerable<string> RegistrySubKeyNames(string hive, string subKey);

    bool IsProcessRunning(string processName);

    /// <summary>Hardware errors and graphics driver resets Windows logged recently, or null when the log cannot be read.</summary>
    HardwareEvents? RecentHardwareEvents(int days) => null;

    /// <summary>The NVIDIA GPU's clock offsets and power limit as set right now, or null without an NVIDIA driver.</summary>
    GpuTuning? NvidiaTuning() => null;

    /// <summary>File names (not paths) found under a folder that match any pattern, searched for at most <paramref name="budget"/>.</summary>
    IReadOnlyList<string> FindFileNames(string directory, IReadOnlyList<string> patterns, TimeSpan budget) => [];
}

/// <summary>How many hardware errors (WHEA) and graphics driver resets (Event 4101) Windows logged in the last <paramref name="Days"/> days.</summary>
public sealed record HardwareEvents(int WheaErrors, int DisplayResets, int Days);

/// <summary>What NVML reports about the GPU's tuning. Offsets are 0 and the power limit equals the default on a stock card.</summary>
public sealed record GpuTuning(string Name, int? CoreOffsetMhz, int? MemoryOffsetMhz, int? PowerLimitW, int? DefaultPowerLimitW, int? MaxCoreClockMhz = null);
