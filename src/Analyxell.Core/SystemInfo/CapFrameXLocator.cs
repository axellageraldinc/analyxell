namespace Analyxell.Core.SystemInfo;

public sealed record CapFrameXInstall(bool Found, string? ExePath, string Source, bool Running)
{
    public static string DownloadUrl { get; } = "https://github.com/CXWorld/CapFrameX/releases";

    public static string ProjectUrl { get; } = "https://github.com/CXWorld/CapFrameX";

    public static CapFrameXInstall NotFound(bool running = false) => new(false, null, "", running);
}

/// <summary>
/// Looks for CapFrameX without touching it: a path the user chose, the usual install folders, and the Windows
/// uninstall list. The portable version has no install record, so a user-chosen path is how it is found.
/// </summary>
public static class CapFrameXLocator
{
    public const string ExeName = "CapFrameX.exe";

    public static CapFrameXInstall Find(ISystemProbe probe, string? userPath)
    {
        var running = probe.IsProcessRunning("CapFrameX");

        if (!string.IsNullOrWhiteSpace(userPath) && probe.FileExists(userPath))
        {
            return new CapFrameXInstall(true, userPath, "the location you chose", running);
        }

        var candidates = new List<(string Path, string Source)>();
        void Add(Environment.SpecialFolder folder, string relative, string source)
        {
            var root = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(root))
            {
                candidates.Add((Path.Combine(root, relative, ExeName), source));
            }
        }

        Add(Environment.SpecialFolder.ProgramFiles, "CapFrameX", "Program Files");
        Add(Environment.SpecialFolder.ProgramFilesX86, "CapFrameX", "Program Files (x86)");
        Add(Environment.SpecialFolder.LocalApplicationData, @"Programs\CapFrameX", "your user programs folder");
        Add(Environment.SpecialFolder.LocalApplicationData, "CapFrameX", "your local app folder");

        foreach (var (path, source) in candidates)
        {
            if (probe.FileExists(path))
            {
                return new CapFrameXInstall(true, path, source, running);
            }
        }

        foreach (var (hive, key) in new[]
                 {
                     ("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                     ("HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                     ("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                 })
        {
            foreach (var sub in probe.RegistrySubKeyNames(hive, key))
            {
                var path = $@"{key}\{sub}";
                var name = probe.RegistryString(hive, path, "DisplayName");
                if (name is null || !name.Contains("CapFrameX", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var location = probe.RegistryString(hive, path, "InstallLocation");
                var exe = !string.IsNullOrWhiteSpace(location) ? Path.Combine(location, ExeName) : null;
                if (exe is null)
                {
                    var icon = probe.RegistryString(hive, path, "DisplayIcon")?.Trim('"');
                    exe = icon is not null && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? icon : null;
                }

                if (exe is not null && probe.FileExists(exe))
                {
                    return new CapFrameXInstall(true, exe, "the Windows list of installed apps", running);
                }
            }
        }

        return CapFrameXInstall.NotFound(running);
    }
}
