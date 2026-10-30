using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace Daignostic.Core.SystemInfo;

/// <summary>
/// The real, read-only <see cref="ISystemProbe"/>. It opens registry keys for reading, enumerates files, and asks
/// the Windows storage stack two questions (does the drive seek, and how is it connected). It never writes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsSystemProbe : ISystemProbe
{
    public int? RegistryDword(string hive, string subKey, string name)
    {
        try
        {
            using var key = Open(hive, subKey);
            return key?.GetValue(name) is int value ? value : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public string? RegistryString(string hive, string subKey, string name)
    {
        try
        {
            using var key = Open(hive, subKey);
            return key?.GetValue(name)?.ToString();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public DirectoryTotals? DirectorySize(string path, int maxFiles, TimeSpan budget)
    {
        if (!Directory.Exists(path))
        {
            return null;
        }

        var clock = Stopwatch.StartNew();
        long bytes = 0;
        var files = 0;
        var truncated = false;
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                bytes += file.Length;
                files++;
                if (files >= maxFiles || clock.Elapsed > budget)
                {
                    truncated = true;
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return new DirectoryTotals(bytes, files, truncated);
    }

    public StorageInfo? Storage(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return null;
            }

            var (media, bus, model) = QueryDevice(root[0]);
            const double Gb = 1024.0 * 1024 * 1024;
            return new StorageInfo(root, media, bus, model, drive.TotalSize / Gb, drive.AvailableFreeSpace / Gb, drive.DriveFormat);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public DisplayMode? PrimaryDisplay()
    {
        var mode = new byte[220];
        BitConverter.GetBytes((ushort)220).CopyTo(mode, 68);
        if (!EnumDisplaySettingsW(null, -1, mode))
        {
            return null;
        }

        var width = BitConverter.ToInt32(mode, 172);
        var height = BitConverter.ToInt32(mode, 176);
        var hz = BitConverter.ToInt32(mode, 184);
        return width > 0 && height > 0 ? new DisplayMode(width, height, hz) : null;
    }

    public int? MonitorCount()
    {
        var count = GetSystemMetrics(80);
        return count > 0 ? count : null;
    }

    public string? ReadText(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IEnumerable<string> EnumerateFiles(string directory, string pattern)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, pattern).ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    private static RegistryKey? Open(string hive, string subKey) => hive.ToUpperInvariant() switch
    {
        "HKCU" => Registry.CurrentUser.OpenSubKey(subKey, writable: false),
        "HKLM" => Registry.LocalMachine.OpenSubKey(subKey, writable: false),
        _ => null,
    };

    /// <summary>Returns (SSD or HDD, connection such as NVMe or SATA, model) for the volume letter.</summary>
    private static (string Media, string Bus, string Model) QueryDevice(char letter)
    {
        var handle = CreateFileW($@"\\.\{char.ToUpperInvariant(letter)}:", 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return ("Unknown", "Unknown", "");
        }

        try
        {
            var media = "Unknown";
            var seek = Query(handle, propertyId: 7, 64);
            if (seek is { Length: >= 9 })
            {
                media = seek[8] != 0 ? "HDD" : "SSD";
            }

            var bus = "Unknown";
            var model = "";
            var device = Query(handle, propertyId: 0, 1024);
            if (device is { Length: >= 32 })
            {
                bus = BitConverter.ToInt32(device, 28) switch
                {
                    17 => "NVMe",
                    11 => "SATA",
                    3 => "ATA",
                    7 => "USB",
                    8 => "RAID",
                    10 => "SAS",
                    _ => "Other",
                };
                var productOffset = BitConverter.ToInt32(device, 16);
                if (productOffset > 0 && productOffset < device.Length)
                {
                    var end = Array.IndexOf(device, (byte)0, productOffset);
                    model = Encoding.ASCII.GetString(device, productOffset, (end < 0 ? device.Length : end) - productOffset).Trim();
                }
            }

            return (media, bus, model);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static byte[]? Query(IntPtr handle, int propertyId, int outSize)
    {
        var input = new byte[12];
        BitConverter.GetBytes(propertyId).CopyTo(input, 0);
        var output = new byte[outSize];
        return DeviceIoControl(handle, 0x002D1400, input, input.Length, output, output.Length, out _, IntPtr.Zero) ? output : null;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(IntPtr handle, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplaySettingsW(string? deviceName, int modeNumber, byte[] devMode);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);
}
