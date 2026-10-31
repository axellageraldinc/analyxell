using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Analyxell.Core.SystemInfo;

/// <summary>
/// Reads, and only reads, the NVIDIA GPU's clock offsets and power limit through NVML, the library the NVIDIA driver
/// installs. A positive offset is an overclock; a power limit under the default is a power cut. A custom voltage curve
/// (the usual way to undervolt) is not visible here. Every call is optional: an older driver simply reports less.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe class Nvml
{
    public static GpuTuning? Read()
    {
        nint lib = 0;
        var candidates = new[]
        {
            "nvml.dll",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
        };
        foreach (var candidate in candidates)
        {
            try
            {
                if (NativeLibrary.TryLoad(candidate, out lib))
                {
                    break;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or BadImageFormatException)
            {
                lib = 0;
            }
        }

        if (lib == 0)
        {
            return null;
        }

        try
        {
            if (!NativeLibrary.TryGetExport(lib, "nvmlInit_v2", out var initPtr) ||
                !NativeLibrary.TryGetExport(lib, "nvmlDeviceGetHandleByIndex_v2", out var handlePtr) ||
                ((delegate* unmanaged<int>)initPtr)() != 0)
            {
                return null;
            }

            try
            {
                nint device;
                if (((delegate* unmanaged<uint, nint*, int>)handlePtr)(0, &device) != 0)
                {
                    return null;
                }

                var name = "";
                if (NativeLibrary.TryGetExport(lib, "nvmlDeviceGetName", out var namePtr))
                {
                    var buffer = stackalloc byte[96];
                    if (((delegate* unmanaged<nint, byte*, uint, int>)namePtr)(device, buffer, 96) == 0)
                    {
                        var text = new ReadOnlySpan<byte>(buffer, 96);
                        var end = text.IndexOf((byte)0);
                        name = Encoding.ASCII.GetString(text[..(end < 0 ? 96 : end)]);
                    }
                }

                return new GpuTuning(
                    name,
                    Offset(lib, "nvmlDeviceGetGpcClkVfOffset", device),
                    Offset(lib, "nvmlDeviceGetMemClkVfOffset", device),
                    Milliwatts(lib, "nvmlDeviceGetPowerManagementLimit", device),
                    Milliwatts(lib, "nvmlDeviceGetPowerManagementDefaultLimit", device),
                    MaxClock(lib, device));
            }
            finally
            {
                if (NativeLibrary.TryGetExport(lib, "nvmlShutdown", out var shutdown))
                {
                    ((delegate* unmanaged<int>)shutdown)();
                }
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or SEHException or AccessViolationException)
        {
            return null;
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    private static int? Offset(nint lib, string export, nint device)
    {
        if (!NativeLibrary.TryGetExport(lib, export, out var ptr))
        {
            return null;
        }

        int value;
        return ((delegate* unmanaged<nint, int*, int>)ptr)(device, &value) == 0 ? value : null;
    }


    /// <summary>The highest graphics clock the card is built to reach at stock (clock type 0 is graphics).</summary>
    private static int? MaxClock(nint lib, nint device)
    {
        if (!NativeLibrary.TryGetExport(lib, "nvmlDeviceGetMaxClockInfo", out var ptr))
        {
            return null;
        }

        uint value;
        return ((delegate* unmanaged<nint, uint, uint*, int>)ptr)(device, 0, &value) == 0 ? (int)value : null;
    }
    private static int? Milliwatts(nint lib, string export, nint device)
    {
        if (!NativeLibrary.TryGetExport(lib, export, out var ptr))
        {
            return null;
        }

        uint value;
        return ((delegate* unmanaged<nint, uint*, int>)ptr)(device, &value) == 0 ? (int)Math.Round(value / 1000.0) : null;
    }
}
