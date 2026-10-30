using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Daignostic.Core.Background;

/// <summary>
/// Reads process CPU time, disk I/O counters, memory and (when Windows provides it) per-process GPU use.
/// Strictly read-only: it opens processes for query access only and never signals, suspends or ends anything.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsProcessSampler : IProcessSampler
{
    private List<PerformanceCounter>? _gpuCounters;

    public int LogicalCores => Environment.ProcessorCount;

    public ProcessSnapshot Take()
    {
        var list = new List<ProcessReading>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                using (process)
                {
                    ulong read = 0, write = 0;
                    var handle = OpenProcess(0x1000, false, process.Id);
                    if (handle != IntPtr.Zero)
                    {
                        try
                        {
                            if (GetProcessIoCounters(handle, out var io))
                            {
                                read = io.ReadTransferCount;
                                write = io.WriteTransferCount;
                            }
                        }
                        finally
                        {
                            CloseHandle(handle);
                        }
                    }

                    list.Add(new ProcessReading(process.Id, process.ProcessName, process.TotalProcessorTime, read, write, process.WorkingSet64));
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Protected or exited processes cannot be read; skip them.
            }
        }

        return new ProcessSnapshot(DateTimeOffset.UtcNow, list);
    }

    public bool BeginGpu()
    {
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var counters = new List<PerformanceCounter>();
            foreach (var instance in category.GetInstanceNames())
            {
                var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);
                counter.NextValue();
                counters.Add(counter);
            }

            _gpuCounters = counters;
            return counters.Count > 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            _gpuCounters = null;
            return false;
        }
    }

    public IReadOnlyDictionary<int, double>? EndGpu()
    {
        if (_gpuCounters is null)
        {
            return null;
        }

        var byPid = new Dictionary<int, double>();
        foreach (var counter in _gpuCounters)
        {
            try
            {
                var match = PidInInstance().Match(counter.InstanceName);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var pid))
                {
                    byPid[pid] = byPid.GetValueOrDefault(pid) + counter.NextValue();
                }
            }
            catch (InvalidOperationException)
            {
                // The engine instance disappeared while we were measuring.
            }
            finally
            {
                counter.Dispose();
            }
        }

        _gpuCounters = null;
        return byPid.ToDictionary(kv => kv.Key, kv => Math.Min(100, kv.Value));
    }

    [GeneratedRegex(@"^pid_(\d+)_")]
    private static partial Regex PidInInstance();

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
}
