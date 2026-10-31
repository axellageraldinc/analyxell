using Analyxell.Core.Analysis;
using Analyxell.Core.Capture;
using Analyxell.Core.SystemInfo;

namespace Analyxell.Core.Tests;

public class HardwareProfileTests
{
    [Theory]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", 7000, true, false, false, 5000)]
    [InlineData("AMD Ryzen 9 7950X3D 16-Core Processor", 7000, true, true, false, 5700)]
    [InlineData("AMD Ryzen 5 5600X 6-Core Processor", 5000, false, false, false, 4600)]
    [InlineData("AMD Ryzen 9 9950X 16-Core Processor", 9000, false, true, false, 5700)]
    [InlineData("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", 7000, false, false, true, null)]
    public void Ryzen_models_are_read_from_the_name(string name, int series, bool x3d, bool dual, bool mobile, int? boostMhz)
    {
        var cpu = CpuProfile.Parse(name);
        Assert.True(cpu.IsRyzen);
        Assert.Equal(series, cpu.Series);
        Assert.Equal(x3d, cpu.X3D);
        Assert.Equal(dual, cpu.DualCcd);
        Assert.Equal(mobile, cpu.Mobile);
        Assert.Equal(boostMhz, cpu.RatedBoostMhz is { } b ? (int)b : null);
    }

    [Fact]
    public void An_unknown_cpu_name_is_not_treated_as_a_ryzen()
    {
        Assert.False(CpuProfile.Parse("Intel(R) Core(TM) i7-14700K").IsRyzen);
        Assert.False(CpuProfile.Parse("").IsRyzen);
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4080 SUPER", 40, true, false)]
    [InlineData("NVIDIA GeForce RTX 3060", 30, false, false)]
    [InlineData("NVIDIA GeForce RTX 5090", 50, false, true)]
    [InlineData("NVIDIA GeForce RTX 4080 Laptop GPU", 40, false, false)]
    public void Nvidia_cards_are_read_from_the_name(string name, int series, bool gddr6x, bool multiFrameGeneration)
    {
        var gpu = GpuProfile.Parse(name);
        Assert.Equal(series, gpu.Series);
        Assert.Equal(gddr6x, gpu.Gddr6x);
        Assert.Equal(multiFrameGeneration, gpu.SupportsMultiFrameGeneration);
        Assert.True(gpu.SupportsDlss);
    }

    [Fact]
    public void A_gtx_card_cannot_use_dlss()
    {
        var gpu = GpuProfile.Parse("NVIDIA GeForce GTX 1660 SUPER");
        Assert.True(gpu.IsNvidia);
        Assert.False(gpu.SupportsDlss);
        Assert.False(gpu.SupportsDlssFrameGeneration);
    }
}

public class HardwareRuleTests
{
    private static CaptureRun Run(double[] frames, IEnumerable<SensorSeries>? sensors = null, double tearing = 0, double sync = 1)
    {
        var grid = TestData.SensorGrid(TestData.Times(frames)[^1] + 1);
        return new CaptureRun
        {
            TimeSec = TestData.Times(frames),
            FrameMs = frames,
            AllowsTearing = frames.Select(_ => tearing).ToArray(),
            SyncInterval = frames.Select(_ => sync).ToArray(),
            SensorTimeSec = grid,
            Sensors = sensors?.ToList() ?? [],
        };
    }

    private static CaptureRun WithSensors(double[] frames, Func<double[], IEnumerable<SensorSeries>> sensors, double tearing = 0, double sync = 1)
    {
        var grid = TestData.SensorGrid(TestData.Times(frames)[^1] + 1);
        return Run(frames, sensors(grid), tearing, sync);
    }

    private static AnalysisResult Analyze(CaptureRun run, string cpu = "Test CPU", string gpu = "Test GPU", params (string Key, string Value)[] system)
    {
        var file = TestData.File(run);
        file = new CaptureFile { Info = file.Info with { Cpu = cpu, Gpu = gpu }, Runs = file.Runs };
        return CaptureAnalyzer.Analyze(file, system: new SystemContext(system.Select(s => new SystemSetting(s.Key, s.Key, s.Value, SettingSource.Auto)).ToList()));
    }

    // Tearing

    [Fact]
    public void Frames_landing_on_the_refresh_show_that_vsync_is_on_even_though_the_game_asked_for_none()
    {
        var run = Run(TestData.Steady(3000, 1000.0 / 144), tearing: 1, sync: 0);
        var r = Analyze(run, system: ("display_current_hz", "144"));
        Assert.Contains(r.Findings, f => f.Id == "VSYNC_DETECTED");
        Assert.DoesNotContain(r.Findings, f => f.Id.StartsWith("TEARING_", StringComparison.Ordinal));
    }

    [Fact]
    public void Sync_off_with_no_refresh_data_and_no_answer_is_not_called_tearing()
    {
        var r = Analyze(Run(TestData.Steady(3000, 5), tearing: 1, sync: 0));
        Assert.Contains(r.Findings, f => f.Id == "TEARING_UNKNOWN" && f.Severity == Severity.Info);
        Assert.DoesNotContain(r.Findings, f => f.Id is "TEARING_POSSIBLE" or "VSYNC_DETECTED");
    }

    [Fact]
    public void Frames_that_do_not_sit_on_the_refresh_are_not_mistaken_for_vsync()
    {
        var run = Run(TestData.Steady(3000, 11), tearing: 1, sync: 0);
        var r = Analyze(run, system: ("display_current_hz", "144"));
        Assert.DoesNotContain(r.Findings, f => f.Id == "VSYNC_DETECTED");
    }

    // Model-aware heat

    [Fact]
    public void A_ryzen_7000_at_95_degrees_is_by_design_but_an_older_ryzen_is_hot()
    {
        CaptureRun Hot() => WithSensors(TestData.Steady(3000, 8), g => [TestData.Sensor("c", "CPU Package (Tctl/Tdie)", "Temperature", TestData.Const(g, 95))]);
        Assert.DoesNotContain(Analyze(Hot(), cpu: "AMD Ryzen 7 7800X3D 8-Core Processor").Findings, f => f.Id == "CPU_HOT");
        Assert.Contains(Analyze(Hot(), cpu: "AMD Ryzen 5 5600X 6-Core Processor").Findings, f => f.Id == "CPU_HOT");
    }

    [Fact]
    public void Gddr6x_memory_is_only_hot_near_its_own_limit()
    {
        CaptureRun Mem(double c) => WithSensors(TestData.Steady(3000, 8), g => [TestData.Sensor("m", "GPU Memory Junction", "Temperature", TestData.Const(g, c))]);
        Assert.DoesNotContain(Analyze(Mem(100), gpu: "NVIDIA GeForce RTX 4080").Findings, f => f.Id == "VRAM_HOT");
        Assert.Contains(Analyze(Mem(106), gpu: "NVIDIA GeForce RTX 4080").Findings, f => f.Id == "VRAM_HOT");
        Assert.Contains(Analyze(Mem(97), gpu: "NVIDIA GeForce RTX 3060").Findings, f => f.Id == "VRAM_HOT");
    }

    [Fact]
    public void An_nvidia_card_in_the_high_eighties_is_not_called_thermal_throttling()
    {
        var run = WithSensors(TestData.Steady(3000, 12), g =>
        [
            TestData.Sensor("t", "GPU Core", "Temperature", TestData.Const(g, 87)),
            TestData.Sensor("l", "GPU Core", "Load", TestData.Const(g, 99)),
        ]);
        var r = Analyze(run, gpu: "NVIDIA GeForce RTX 3080");
        Assert.NotEqual(BottleneckVerdict.ThermalThrottling, r.Bottleneck.Verdict);
        Assert.DoesNotContain(r.Findings, f => f.Id == "GPU_HOT");
    }

    [Fact]
    public void A_gpu_at_its_power_limit_under_full_load_is_gpu_bound_not_a_power_problem()
    {
        var run = WithSensors(TestData.Steady(3000, 12), g =>
        [
            TestData.Sensor("l", "GPU Core", "Load", TestData.Const(g, 99)),
            TestData.Sensor("p", "GPU Power Limit", "Factor", TestData.Const(g, 1)),
        ]);
        var r = Analyze(run, gpu: "NVIDIA GeForce RTX 4070");
        Assert.Equal(BottleneckVerdict.GpuBound, r.Bottleneck.Verdict);
        Assert.DoesNotContain(r.Findings, f => f.Id == "POWER_LIMIT");
    }

    // Hitch rhythm

    private static double[] FreezeEvery30Seconds(int count)
    {
        var parts = new List<double[]>();
        for (var i = 0; i < count; i++)
        {
            parts.Add(TestData.Steady(2990, 10));
            parts.Add([300]);
        }

        return TestData.Concat([.. parts]);
    }

    [Fact]
    public void Hitches_at_an_even_interval_are_reported_with_the_ftpm_hint_on_ryzen()
    {
        var r = Analyze(Run(FreezeEvery30Seconds(8)), cpu: "AMD Ryzen 7 7700X 8-Core Processor");
        var f = Assert.Single(r.Findings, x => x.Id == "PERIODIC_HITCHES");
        Assert.Contains(f.Suggestions, s => s.Contains("fTPM", StringComparison.Ordinal));
        Assert.True(r.Facts.ContainsKey("hitch.period_sec"));
    }

    [Fact]
    public void Randomly_spaced_hitches_are_not_called_periodic()
    {
        var frames = TestData.Concat(
            TestData.Steady(900, 10), [300], TestData.Steady(4000, 10), [300], TestData.Steady(500, 10), [300],
            TestData.Steady(7000, 10), [300], TestData.Steady(1200, 10), [300], TestData.Steady(2500, 10), [300], TestData.Steady(600, 10));
        Assert.DoesNotContain(Analyze(Run(frames)).Findings, f => f.Id == "PERIODIC_HITCHES");
    }

    // Ryzen scheduling and boost

    private static IEnumerable<SensorSeries> CoreLoads(double[] grid, int cores, double first, double second) =>
        Enumerable.Range(1, cores).Select(i => TestData.Sensor($"c{i}", $"Core #{i} Thread #1", "Load", TestData.Const(grid, i <= cores / 2 ? first : second)));

    [Fact]
    public void An_x3d_game_that_sits_on_the_chiplet_without_cache_is_flagged()
    {
        var run = WithSensors(FreezeEvery30Seconds(2), g => CoreLoads(g, 16, 8, 60));
        var r = Analyze(run, cpu: "AMD Ryzen 9 7950X3D 16-Core Processor", system: ("amd_vcache_driver", "Not found"));
        var f = Assert.Single(r.Findings, x => x.Id == "VCACHE_WRONG_CHIPLET");
        Assert.Contains(f.Suggestions, s => s.Contains("was not found", StringComparison.Ordinal));
    }

    [Fact]
    public void An_x3d_game_on_the_cache_chiplet_is_fine()
    {
        var run = WithSensors(FreezeEvery30Seconds(2), g => CoreLoads(g, 16, 60, 8));
        Assert.DoesNotContain(Analyze(run, cpu: "AMD Ryzen 9 7950X3D 16-Core Processor").Findings, f => f.Id == "VCACHE_WRONG_CHIPLET");
    }

    [Fact]
    public void A_cpu_limited_ryzen_that_never_reaches_its_rated_boost_is_reported()
    {
        var run = WithSensors(TestData.Steady(3000, 20), g =>
        [
            TestData.Sensor("a", "GPU Core", "Load", TestData.Const(g, 40)),
            TestData.Sensor("b", "CPU Max", "Load", TestData.Const(g, 96)),
            TestData.Sensor("c", "CPU Max", "Clock", TestData.Const(g, 4200)),
        ]);
        var r = Analyze(run, cpu: "AMD Ryzen 7 7700X 8-Core Processor", system: ("amd_chipset_driver", "Not found"));
        var f = Assert.Single(r.Findings, x => x.Id == "CPU_BELOW_BOOST");
        Assert.Contains(f.Suggestions, s => s.Contains("chipset driver", StringComparison.Ordinal));
    }

    [Fact]
    public void A_ryzen_boosting_past_its_rating_is_noticed_as_tuned()
    {
        var run = WithSensors(TestData.Steady(3000, 8), g => [TestData.Sensor("c", "CPU Max", "Clock", TestData.Const(g, 5800))]);
        Assert.Contains(Analyze(run, cpu: "AMD Ryzen 7 7700X 8-Core Processor").Findings, f => f.Id == "CPU_ABOVE_RATED");
    }

    [Fact]
    public void Missing_amd_chipset_driver_is_reported_only_for_ryzen()
    {
        var run = Run(TestData.Steady(3000, 8));
        Assert.Contains(Analyze(run, cpu: "AMD Ryzen 5 5600X 6-Core Processor", system: ("amd_chipset_driver", "Not found")).Findings, f => f.Id == "AMD_CHIPSET_MISSING");
        Assert.DoesNotContain(Analyze(run, cpu: "Intel(R) Core(TM) i7-14700K", system: ("amd_chipset_driver", "Not found")).Findings, f => f.Id == "AMD_CHIPSET_MISSING");
    }

    // Tuning and stability

    [Fact]
    public void Windows_hardware_errors_and_driver_resets_point_at_unstable_tuning()
    {
        var r = Analyze(Run(TestData.Steady(3000, 8)), cpu: "AMD Ryzen 7 7700X 8-Core Processor", gpu: "NVIDIA GeForce RTX 4070",
            system: [("events.whea_errors", "4"), ("events.display_resets", "2")]);
        var whea = Assert.Single(r.Findings, f => f.Id == "HARDWARE_ERRORS_LOGGED");
        Assert.Equal(Severity.Medium, whea.Severity);
        Assert.Contains(whea.Suggestions, s => s.Contains("Curve Optimizer", StringComparison.Ordinal));
        Assert.Contains(r.Findings, f => f.Id == "DISPLAY_DRIVER_RESETS");
    }

    [Fact]
    public void Clean_event_logs_raise_nothing()
    {
        var r = Analyze(Run(TestData.Steady(3000, 8)), system: [("events.whea_errors", "0"), ("events.display_resets", "0")]);
        Assert.DoesNotContain(r.Findings, f => f.Id is "HARDWARE_ERRORS_LOGGED" or "DISPLAY_DRIVER_RESETS");
    }

    [Fact]
    public void A_gpu_overclock_and_a_lowered_power_limit_are_noticed()
    {
        var r = Analyze(Run(TestData.Steady(3000, 8)), gpu: "NVIDIA GeForce RTX 4070",
            system: [("gpu_tuning.core_offset_mhz", "150"), ("gpu_tuning.memory_offset_mhz", "800"), ("gpu_tuning.power_limit_w", "160"), ("gpu_tuning.power_default_w", "200")]);
        Assert.Contains(r.Findings, f => f.Id == "GPU_OVERCLOCKED");
        Assert.Contains(r.Findings, f => f.Id == "GPU_POWER_LIMIT_LOWERED");
    }

    [Fact]
    public void A_stock_gpu_raises_no_tuning_findings()
    {
        var r = Analyze(Run(TestData.Steady(3000, 8)), gpu: "NVIDIA GeForce RTX 4070",
            system: [("gpu_tuning.core_offset_mhz", "0"), ("gpu_tuning.memory_offset_mhz", "0"), ("gpu_tuning.power_limit_w", "200"), ("gpu_tuning.power_default_w", "200")]);
        Assert.DoesNotContain(r.Findings, f => f.Id is "GPU_OVERCLOCKED" or "GPU_POWER_LIMIT_LOWERED" or "GPU_CLOCKS_LOWERED");
    }

    [Fact]
    public void Full_clocks_at_low_power_look_like_an_undervolt()
    {
        var run = WithSensors(TestData.Steady(3000, 12), g =>
        [
            TestData.Sensor("l", "GPU Core", "Load", TestData.Const(g, 99)),
            TestData.Sensor("c", "GPU Core", "Clock", TestData.Const(g, 2800)),
            TestData.Sensor("p", "GPU Power", "Power", TestData.Const(g, 150)),
        ]);
        var r = Analyze(run, gpu: "NVIDIA GeForce RTX 4070", system: [("gpu_tuning.power_limit_w", "200"), ("gpu_tuning.power_default_w", "200")]);
        Assert.Contains(r.Findings, f => f.Id == "UNDERVOLT_LIKELY" && f.Severity == Severity.Info);
    }

    [Fact]
    public void A_game_that_ships_dlss_on_a_gpu_limited_rtx_card_gets_an_upscaler_hint_but_a_gtx_card_does_not()
    {
        var run = WithSensors(TestData.Steady(3000, 25), g => [TestData.Sensor("l", "GPU Core", "Load", TestData.Const(g, 99))]);
        Assert.Contains(Analyze(run, gpu: "NVIDIA GeForce RTX 3070", system: ("game_upscalers", "DLSS, FSR")).Findings, f => f.Id == "UPSCALER_AVAILABLE");
        Assert.DoesNotContain(Analyze(run, gpu: "NVIDIA GeForce GTX 1660", system: ("game_upscalers", "DLSS, FSR")).Findings, f => f.Id == "UPSCALER_AVAILABLE");
    }

    [Fact]
    public void Memory_far_above_ddr5_6400_on_ryzen_7000_is_noted()
    {
        var file = TestData.File(Run(TestData.Steady(3000, 8)));
        file = new CaptureFile { Info = file.Info with { Cpu = "AMD Ryzen 7 7800X3D 8-Core Processor", SystemRam = "32GB (2x16GB) 7600MT/s" }, Runs = file.Runs };
        Assert.Contains(CaptureAnalyzer.Analyze(file).Findings, f => f.Id == "RAM_ABOVE_SWEET_SPOT");
    }
}

public class AmdSystemProbeTests
{
    [Fact]
    public void The_amd_chipset_and_vcache_drivers_are_looked_for_on_ryzen_only()
    {
        var probe = new FakeProbe();
        probe.SubKeys["HKLM|SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall"] = ["{abc}"];
        probe.Strings["HKLM|SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{abc}|DisplayName"] = "Some Other Program";
        probe.SubKeys["HKLM|SYSTEM\\CurrentControlSet\\Services"] = ["Dnscache", "amd3dvcacheSvc"];

        var ryzen = SystemSettingsReader.Read(probe, new Dictionary<string, string>(), null, "", "AMD Ryzen 9 7950X3D 16-Core Processor");
        Assert.Equal("Not found", ryzen.Get("amd_chipset_driver"));
        Assert.Equal("Installed", ryzen.Get("amd_vcache_driver"));

        var intel = SystemSettingsReader.Read(probe, new Dictionary<string, string>(), null, "", "Intel(R) Core(TM) i7-14700K");
        Assert.Null(intel.Get("amd_chipset_driver"));
    }
}

public class GpuClockOvershootTests
{
    [Fact]
    public void A_core_clock_past_the_cards_stock_maximum_is_noticed_even_without_offsets()
    {
        var frames = TestData.Steady(3000, 12);
        var grid = TestData.SensorGrid(TestData.Times(frames)[^1] + 1);
        var run = new CaptureRun
        {
            TimeSec = TestData.Times(frames),
            FrameMs = frames,
            SensorTimeSec = grid,
            Sensors = [TestData.Sensor("c", "GPU Core", "Clock", TestData.Const(grid, 3200))],
        };
        var file = TestData.File(run);
        file = new CaptureFile { Info = file.Info with { Gpu = "NVIDIA GeForce RTX 4070" }, Runs = file.Runs };
        var r = CaptureAnalyzer.Analyze(file, system: new SystemContext([new SystemSetting("gpu_tuning.max_clock_mhz", "max", "3105", SettingSource.Auto)]));
        Assert.Contains(r.Findings, f => f.Id == "GPU_OVERCLOCKED");
    }
}
