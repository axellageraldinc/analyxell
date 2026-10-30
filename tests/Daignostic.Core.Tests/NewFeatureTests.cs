using System.Net;
using System.Text;
using Daignostic.Core.Ai;
using Daignostic.Core.Analysis;
using Daignostic.Core.Background;
using Daignostic.Core.Capture;
using Daignostic.Core.Config;
using Daignostic.Core.Mcp;
using Daignostic.Core.Settings;
using Daignostic.Core.SystemInfo;

namespace Daignostic.Core.Tests;

internal sealed class FakeProbe : ISystemProbe
{
    public Dictionary<string, int> Dwords { get; } = [];
    public Dictionary<string, string> Strings { get; } = [];
    public Dictionary<string, DirectoryTotals> Dirs { get; } = [];
    public Dictionary<string, string> Files { get; } = [];
    public StorageInfo? StorageResult { get; set; }
    public DisplayMode? Display { get; set; }

    public int? RegistryDword(string hive, string subKey, string name) => Dwords.TryGetValue($"{hive}|{subKey}|{name}", out var v) ? v : null;

    public string? RegistryString(string hive, string subKey, string name) => Strings.TryGetValue($"{hive}|{subKey}|{name}", out var v) ? v : null;

    public DirectoryTotals? DirectorySize(string path, int maxFiles, TimeSpan budget) => Dirs.TryGetValue(path, out var v) ? v : null;

    public StorageInfo? Storage(string path) => StorageResult;

    public DisplayMode? PrimaryDisplay() => Display;

    public int? MonitorCount() => 2;

    public string? ReadText(string path) => Files.TryGetValue(path, out var v) ? v : null;

    public IEnumerable<string> EnumerateFiles(string directory, string pattern) =>
        Files.Keys.Where(k => k.StartsWith(directory, StringComparison.OrdinalIgnoreCase) &&
            System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(k), "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$"));

    public bool DirectoryExists(string path) => Dirs.ContainsKey(path) || Files.Keys.Any(k => k.StartsWith(path, StringComparison.OrdinalIgnoreCase));

    public HashSet<string> ExistingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string[]> SubKeys { get; } = [];

    public bool Running { get; set; }

    public bool FileExists(string path) => ExistingFiles.Contains(path) || Files.ContainsKey(path);

    public IEnumerable<string> RegistrySubKeyNames(string hive, string subKey) => SubKeys.TryGetValue($"{hive}|{subKey}", out var v) ? v : [];

    public bool IsProcessRunning(string processName) => Running;
}

public class ExtraSignalTests
{
    private static CaptureRun Run(double[] frames, double[] grid, double[]? refresh = null, double tearing = 1, double sync = 0, double latency = 30) => new()
    {
        TimeSec = TestData.Times(frames),
        FrameMs = frames,
        AllowsTearing = frames.Select(_ => tearing).ToArray(),
        SyncInterval = frames.Select(_ => sync).ToArray(),
        PcLatencyMs = frames.Select(_ => latency).ToArray(),
        AnimationErrorMs = frames.Select(_ => 1.0).ToArray(),
        SensorTimeSec = grid,
        Sensors = refresh is null ? [] : [TestData.Sensor("/gpu/0/frequency/0", "Monitor Refresh Rate", "Frequency", refresh)],
    };

    [Fact]
    public void Variable_refresh_is_inferred_from_a_refresh_rate_that_moves()
    {
        var frames = TestData.Steady(1000, 10);
        var grid = TestData.SensorGrid(10);
        var moving = grid.Select((_, i) => i % 2 == 0 ? 60.0 : 144.0).ToArray();
        Assert.True(ExtraSignals.Compute(Run(frames, grid, moving)).VrrEvidence);
        Assert.False(ExtraSignals.Compute(Run(frames, grid, TestData.Const(grid, 144))).VrrEvidence);
    }

    [Fact]
    public void Tearing_flags_latency_and_frames_above_refresh_are_measured()
    {
        var frames = TestData.Steady(1000, 5); // 200 fps
        var grid = TestData.SensorGrid(5);
        var x = ExtraSignals.Compute(Run(frames, grid, TestData.Const(grid, 144), tearing: 1, sync: 0, latency: 45));
        Assert.Equal(100, x.TearingAllowedPct);
        Assert.Equal(100, x.SyncZeroPct);
        Assert.Equal(45, x.LatencyAvgMs);
        Assert.Equal(100, x.FramesAboveRefreshPct);
    }

    [Fact]
    public void Missing_columns_give_null_not_errors()
    {
        var x = ExtraSignals.Compute(TestData.Run(TestData.Steady(100, 10)));
        Assert.Null(x.RefreshMaxHz);
        Assert.Null(x.LatencyAvgMs);
        Assert.Null(x.TearingAllowedPct);
    }
}

public class NewRuleTests
{
    private static CaptureRun Frames(double ms, double[]? refresh, double tearing = 1, double sync = 0, double latency = 30, IEnumerable<SensorSeries>? more = null)
    {
        var frames = TestData.Steady(3000, ms);
        var grid = TestData.SensorGrid(Math.Ceiling(frames.Sum() / 1000.0) + 1);
        var sensors = new List<SensorSeries>(more ?? []);
        if (refresh is not null)
        {
            sensors.Add(TestData.Sensor("r", "Monitor Refresh Rate", "Frequency", grid.Select((_, i) => refresh[i % refresh.Length]).ToArray()));
        }

        return new CaptureRun
        {
            TimeSec = TestData.Times(frames),
            FrameMs = frames,
            AllowsTearing = frames.Select(_ => tearing).ToArray(),
            SyncInterval = frames.Select(_ => sync).ToArray(),
            PcLatencyMs = frames.Select(_ => latency).ToArray(),
            SensorTimeSec = grid,
            Sensors = sensors,
        };
    }

    private static AnalysisResult Analyze(CaptureRun run, SystemContext? system = null, BackgroundScanResult? background = null, bool share = false) =>
        CaptureAnalyzer.Analyze(TestData.File(run), 0, null, system, background, share);

    private static SystemContext Ctx(params (string Key, string Value, SettingSource Source)[] items) =>
        new(items.Select(i => new SystemSetting(i.Key, i.Key, i.Value, i.Source)).ToList());

    [Fact]
    public void Sync_off_without_variable_refresh_is_reported_as_tearing_possible()
    {
        var r = Analyze(Frames(5, [144]));
        var f = Assert.Single(r.Findings, x => x.Id == "TEARING_POSSIBLE");
        Assert.NotEmpty(f.Suggestions);
    }

    [Fact]
    public void Sync_off_with_variable_refresh_inside_range_is_not_a_problem()
    {
        var r = Analyze(Frames(10, [60, 144]));
        Assert.Contains(r.Findings, f => f.Id == "TEARING_UNLIKELY");
        Assert.DoesNotContain(r.Findings, f => f.Id is "TEARING_POSSIBLE" or "TEARING_ABOVE_RANGE");
    }

    [Fact]
    public void Frame_rate_above_the_refresh_range_suggests_a_cap()
    {
        var r = Analyze(Frames(5, [60, 144]));
        var f = Assert.Single(r.Findings, x => x.Id == "TEARING_ABOVE_RANGE");
        Assert.Contains(f.Suggestions, s => s.Contains("frame cap"));
    }

    [Fact]
    public void User_saying_gsync_is_on_counts_as_variable_refresh()
    {
        var r = Analyze(Frames(10, [144]), Ctx(("setup.display_sync", "G-SYNC", SettingSource.User)));
        Assert.Contains(r.Findings, f => f.Id == "TEARING_UNLIKELY");
    }

    [Fact]
    public void Low_fps_and_latency_and_pacing_rules_fire()
    {
        var r = Analyze(Frames(25, null, latency: 80));
        Assert.Contains(r.Findings, f => f.Id == "LOW_FPS");
        Assert.Contains(r.Findings, f => f.Id == "HIGH_LATENCY");
        Assert.DoesNotContain(Analyze(Frames(8, null, latency: 20)).Findings, f => f.Id is "LOW_FPS" or "HIGH_LATENCY");
    }

    [Fact]
    public void Hot_components_are_flagged_with_cooling_advice()
    {
        var grid = TestData.SensorGrid(30);
        var run = Frames(8, null, more:
        [
            TestData.Sensor("g", "GPU Core", "Temperature", TestData.Const(grid, 91)),
            TestData.Sensor("c", "CPU Package (Tctl/Tdie)", "Temperature", TestData.Const(grid, 96)),
        ]);
        var r = Analyze(run);
        Assert.Contains(r.Findings, f => f.Id == "GPU_HOT" && f.Severity == Severity.High);
        Assert.Contains(r.Findings, f => f.Id == "CPU_HOT" && f.Severity == Severity.High);
    }

    [Fact]
    public void Memory_pressure_is_flagged()
    {
        var grid = TestData.SensorGrid(30);
        var r = Analyze(Frames(8, null, more: [TestData.Sensor("m", "RAM Usage", "Load", TestData.Const(grid, 95))]));
        Assert.Contains(r.Findings, f => f.Id == "RAM_PRESSURE");
    }

    [Fact]
    public void Game_on_a_hard_disk_and_a_full_drive_are_flagged_from_system_facts()
    {
        var system = Ctx(("game_drive.media", "HDD", SettingSource.Auto), ("game_drive.free_pct", "6", SettingSource.Auto));
        var r = Analyze(Frames(8, null), system);
        var hdd = Assert.Single(r.Findings, f => f.Id == "GAME_ON_HDD");
        Assert.Contains(hdd.Suggestions, s => s.Contains("SSD"));
        Assert.Contains(r.Findings, f => f.Id == "DRIVE_NEARLY_FULL");
        Assert.Equal("HDD", r.Facts["system.game_drive.media"]);
    }

    [Fact]
    public void Small_shader_cache_with_freezes_and_display_below_max_are_flagged()
    {
        var system = Ctx(
            ("shader_cache_mb", "12", SettingSource.Auto),
            ("display_current_hz", "60", SettingSource.Auto),
            ("setup.monitor_max_hz", "144", SettingSource.User),
            ("power_plan", "Power saver", SettingSource.Auto));
        var r = CaptureAnalyzer.Analyze(TestData.File(TestData.RunWithFreezes(95, 10)), 0, null, system);
        Assert.Contains(r.Findings, f => f.Id == "SHADER_CACHE_SMALL");
        Assert.Contains(r.Findings, f => f.Id == "DISPLAY_BELOW_MAX");
        Assert.Contains(r.Findings, f => f.Id == "POWER_PLAN_SAVER");
    }

    [Fact]
    public void Every_finding_above_info_offers_something_to_try_and_cites_real_facts()
    {
        var grid = TestData.SensorGrid(200);
        var system = Ctx(("game_drive.media", "HDD", SettingSource.Auto), ("shader_cache_mb", "5", SettingSource.Auto));
        var run = TestData.RunWithFreezes(95, 95);
        var hot = new CaptureRun
        {
            TimeSec = run.TimeSec,
            FrameMs = run.FrameMs,
            SensorTimeSec = run.SensorTimeSec,
            Sensors = [.. run.Sensors, TestData.Sensor("g", "GPU Core", "Temperature", TestData.Const(run.SensorTimeSec, 92))],
        };
        var r = CaptureAnalyzer.Analyze(TestData.File(hot), 0, null, system);
        Assert.NotEmpty(r.Findings);
        Assert.All(r.Findings.Where(f => f.Severity >= Severity.Low), f => Assert.NotEmpty(f.Suggestions));
        Assert.All(r.Findings, f => Assert.All(f.Evidence, k => Assert.True(r.Facts.ContainsKey(k), $"{f.Id} cites missing '{k}'")));
        _ = grid;
    }

    [Fact]
    public void Next_steps_are_ordered_by_severity_without_repeats()
    {
        var r = Analyze(Frames(25, null, latency: 80), Ctx(("game_drive.media", "HDD", SettingSource.Auto)));
        Assert.NotEmpty(r.NextSteps);
        Assert.True(r.NextSteps.Count <= 6);
        Assert.Equal(r.NextSteps.Select(s => s.Text).Distinct().Count(), r.NextSteps.Count);
        Assert.Equal(Enumerable.Range(1, r.NextSteps.Count), r.NextSteps.Select(s => s.Order));
        Assert.True(r.NextSteps.Zip(r.NextSteps.Skip(1)).All(p => p.First.Severity >= p.Second.Severity));
    }
}

public class BackgroundTests
{
    private static ProcessSnapshot Snap(DateTimeOffset at, params ProcessReading[] p) => new(at, p);

    private static ProcessReading P(int pid, string name, double cpuSec, ulong io = 0, long ws = 100_000_000) =>
        new(pid, name, TimeSpan.FromSeconds(cpuSec), io, 0, ws);

    private static BackgroundScanResult Scan(params (int Pid, string Name, double CpuSecBefore, double CpuSecAfter, ulong IoBefore, ulong IoAfter)[] ps)
    {
        var t0 = DateTimeOffset.UtcNow;
        var a = Snap(t0, ps.Select(p => P(p.Pid, p.Name, p.CpuSecBefore, p.IoBefore)).ToArray());
        var b = Snap(t0.AddSeconds(10), ps.Select(p => P(p.Pid, p.Name, p.CpuSecAfter, p.IoAfter)).ToArray());
        return BackgroundScanner.Compute(a, b, 8, "game.exe", null);
    }

    [Fact]
    public void Cpu_percent_is_of_the_whole_machine_and_the_game_is_separated()
    {
        var scan = Scan((1, "game", 0, 40, 0, 0), (2, "chrome", 0, 16, 0, 0));
        Assert.Equal(50, scan.GameCpuPct!.Value, 1);
        var chrome = Assert.Single(scan.Others);
        Assert.Equal(20, chrome.CpuPct, 1);
        Assert.Equal("Browser", chrome.Category);
        Assert.Equal(20, scan.OtherCpuPct, 1);
    }

    [Fact]
    public void Disk_throughput_is_measured_per_second()
    {
        var scan = Scan((2, "backup", 0, 0, 0, 300UL * 1024 * 1024));
        Assert.Equal(30, scan.Others.Single().DiskMBps, 1);
    }

    [Fact]
    public void Quiet_system_is_reported_as_quiet_and_names_stay_out_of_ai_messages()
    {
        var quiet = Scan((2, "idle", 0, 0.1, 0, 0));
        var r = CaptureAnalyzer.Analyze(TestData.File(TestData.Run(TestData.Steady(3000, 10))), 0, null, null, quiet);
        Assert.Contains(r.Findings, f => f.Id == "BACKGROUND_QUIET");
    }

    [Fact]
    public void Heavy_background_programs_produce_local_only_findings_whose_names_never_reach_the_ai()
    {
        var scan = Scan((2, "chrome", 0, 40, 0, 400UL * 1024 * 1024), (3, "obs64", 0, 12, 0, 0));
        var r = CaptureAnalyzer.Analyze(TestData.File(TestData.Run(TestData.Steady(3000, 10))), 0, null, null, scan, shareBackgroundNames: false);

        Assert.Contains(r.Findings, f => f.Id == "BACKGROUND_CPU" && f.LocalOnly && f.Detail.Contains("chrome"));
        Assert.Contains(r.Findings, f => f.Id == "BACKGROUND_DISK");
        Assert.Contains(r.Findings, f => f.Id == "BACKGROUND_KNOWN_APPS");
        Assert.All(r.Findings.Where(f => f.Id.StartsWith("BACKGROUND_", StringComparison.Ordinal) && f.Id != "BACKGROUND_QUIET"), f => Assert.True(f.LocalOnly));

        var message = AiPrompts.BuildSingle(r, null);
        Assert.DoesNotContain("chrome", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("obs64", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background.other_cpu_pct", message);
        Assert.Contains("Browser", message);
        Assert.DoesNotContain("BACKGROUND_CPU", message);

        var shared = CaptureAnalyzer.Analyze(TestData.File(TestData.Run(TestData.Steady(3000, 10))), 0, null, null, scan, shareBackgroundNames: true);
        Assert.Contains("chrome", AiPrompts.BuildSingle(shared, null));
    }

    [Theory]
    [InlineData("discord", "Overlay")]
    [InlineData("OBS64.exe", "Recorder")]
    [InlineData("msmpeng", "Security scan")]
    [InlineData("OneDrive", "Cloud sync")]
    public void Known_programs_are_recognised(string name, string category) =>
        Assert.Equal(category, KnownApps.Find(name)?.Category);

    [Fact]
    public void Unknown_programs_are_not_labelled() => Assert.Null(KnownApps.Find("my-own-tool"));
}

public class SystemSettingsTests
{
    [Fact]
    public void Reader_collects_windows_toggles_drive_and_shader_cache_read_only()
    {
        var probe = new FakeProbe();
        probe.Dwords["HKCU|Software\\Microsoft\\GameBar|AutoGameModeEnabled"] = 0;
        probe.Dwords["HKLM|SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers|HwSchMode"] = 2;
        probe.Strings["HKLM|SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes|ActivePowerScheme"] = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        probe.Strings["HKCU|Software\\Microsoft\\DirectX\\UserGpuPreferences|DirectXUserGlobalSettings"] = "VRROptimizeEnable=1;";
        probe.Display = new DisplayMode(3440, 1440, 144);
        probe.StorageResult = new StorageInfo("D:\\", "SSD", "SATA", "Test SSD", 476, 190, "NTFS");
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NVIDIA", "DXCache");
        probe.Dirs[cache] = new DirectoryTotals(300L * 1024 * 1024, 120, false);

        var ctx = SystemSettingsReader.Read(probe, new Dictionary<string, string> { ["display_sync"] = "G-SYNC", ["frame_cap"] = "141", ["vsync"] = "Unknown" }, @"D:\Games\Test", "NVIDIA GeForce RTX 5070");

        Assert.Equal("Off", ctx.Get("game_mode"));
        Assert.Equal("On", ctx.Get("hags"));
        Assert.Equal("High performance", ctx.Get("power_plan"));
        Assert.Equal("On", ctx.Get("windowed_vrr"));
        Assert.Equal("3440x1440 at 144 Hz", ctx.Get("display_mode"));
        Assert.Equal("SSD", ctx.Get("game_drive.media"));
        Assert.Equal("40", ctx.Get("game_drive.free_pct"));
        Assert.Equal("300", ctx.Get("shader_cache_mb"));
        Assert.Equal("G-SYNC", ctx.Get("setup.display_sync"));
        Assert.Equal("141", ctx.Get("setup.frame_cap"));
        Assert.Null(ctx.Get("setup.vsync"));
        Assert.All(ctx.Items.Where(i => i.Key.StartsWith("setup.", StringComparison.Ordinal)), i => Assert.Equal(SettingSource.User, i.Source));
        Assert.All(ctx.Items.Where(i => !i.Key.StartsWith("setup.", StringComparison.Ordinal)), i => Assert.Equal(SettingSource.Auto, i.Source));
    }

    [Fact]
    public void Drive_models_and_monitor_names_stay_local_and_user_values_are_labelled()
    {
        var ctx = new SystemContext(
        [
            new SystemSetting("game_drive.model", "Drive", "SecretModel123", SettingSource.Auto),
            new SystemSetting("setup.monitor_model", "Monitor", "Acme 27", SettingSource.User),
            new SystemSetting("setup.frame_cap", "Cap", "141", SettingSource.User),
            new SystemSetting("game_drive.media", "Type", "SSD", SettingSource.Auto),
        ]);
        var r = CaptureAnalyzer.Analyze(TestData.File(TestData.Run(TestData.Steady(3000, 10))), 0, null, ctx);
        Assert.DoesNotContain(r.Facts.Values, v => v.Contains("SecretModel123") || v.Contains("Acme"));
        Assert.Equal("141", r.Facts["setup.frame_cap"]);
        Assert.Equal("SSD", r.Facts["system.game_drive.media"]);
    }

    [Fact]
    public void Steam_install_is_found_from_the_game_manifest_not_by_guessing()
    {
        var probe = new FakeProbe();
        probe.Strings["HKCU|Software\\Valve\\Steam|SteamPath"] = "c:/steam";
        probe.Files[@"c:\steam\steamapps\libraryfolders.vdf"] = "\"libraryfolders\" { \"0\" { \"path\" \"d:\\\\SteamLibrary\" } }";
        probe.Files[@"d:\SteamLibrary\steamapps\appmanifest_2358720.acf"] = "\"AppState\" { \"name\" \"Black Myth: Wukong\" \"installdir\" \"BlackMythWukong\" }";
        probe.Dirs[@"d:\SteamLibrary\steamapps\common\BlackMythWukong"] = new DirectoryTotals(0, 0, false);

        Assert.Equal(@"d:\SteamLibrary\steamapps\common\BlackMythWukong", GameLocator.FindSteamInstall(probe, "Black Myth: Wukong"));
        Assert.Null(GameLocator.FindSteamInstall(probe, "Some Other Game"));
    }

    [Fact]
    public void Specs_show_the_motherboard_but_the_ai_facts_never_do()
    {
        var info = TestData.File(TestData.Run(TestData.Steady(10, 10))).Info with { Motherboard = "ACME-BOARD-9000", ResizableBar = "Enabled", Hags = "Enabled" };
        var file = new CaptureFile { Info = info, Runs = [TestData.RunWithFreezes(95, 10)] };
        var r = CaptureAnalyzer.Analyze(file);
        Assert.Contains(r.Specs.Sections.SelectMany(s => s.Items), i => i.Label == "Motherboard" && i.Value == "ACME-BOARD-9000");
        Assert.Contains("Test GPU", r.Specs.Summary);
        Assert.DoesNotContain(r.Facts.Values, v => v.Contains("ACME-BOARD"));
        Assert.DoesNotContain("ACME-BOARD", AiPrompts.BuildSingle(r, null));
        Assert.Equal("Enabled", r.Facts["pc.resizable_bar"]);
    }
}

public class UserDataTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "daignostic-ud-" + Guid.NewGuid().ToString("N"));

    public UserDataTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Free_form_text_is_understood_in_common_formats()
    {
        var json = FreeFormSettingsParser.Parse("""{"graphics":{"quality":"high","rt":true},"fov":90,"list":[1,2]}""");
        Assert.Contains(json.Items, i => i.Key == "graphics.quality" && i.Value == "high");
        Assert.Contains(json.Items, i => i.Key == "graphics.rt" && i.Value.Equals("true", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(json.Items, i => i.Key == "list[1]" && i.Value == "2");

        var lines = FreeFormSettingsParser.Parse("# comment\n[Video]\nShadowQuality=High\nDLSS: Quality\n; skip\nvsync = off,\nnonsense line\n");
        Assert.Equal(["ShadowQuality", "DLSS", "vsync"], lines.Items.Select(i => i.Key));
        Assert.Equal("off", lines.Items.Last().Value);
        Assert.Single(lines.Warnings);

        Assert.Empty(FreeFormSettingsParser.Parse("   ").Items);
    }

    [Fact]
    public void Profiles_and_setup_round_trip_and_survive_corruption()
    {
        var store = new GameProfileStore(Path.Combine(_dir, "games"));
        Assert.Null(store.Load("b1-Win64-Shipping.exe"));
        store.Save(new GameProfile { ProcessName = "b1-Win64-Shipping.exe", Game = "G", Origin = "entered by you", Items = [new ProfileItem("RT", "off")] });
        var back = store.Load("b1-Win64-Shipping.exe")!;
        Assert.Equal("off", Assert.Single(back.Items).Value);
        Assert.NotEmpty(back.SavedAtUtc);

        File.WriteAllText(Path.Combine(_dir, "games", "b1-Win64-Shipping.exe.json"), "{ nope");
        Assert.Null(store.Load("b1-Win64-Shipping.exe"));

        var setup = new UserSetupStore(Path.Combine(_dir, "setup.json"));
        setup.Save(new UserSetupData { Values = { ["display_sync"] = "FreeSync" } });
        Assert.Equal("FreeSync", setup.Load().Values["display_sync"]);
    }

    [Fact]
    public void Merged_settings_prefer_the_game_file_and_mark_user_items_raw()
    {
        var fromFile = new GameSettings("G", "f.ini", DateTime.UtcNow, [new GameSetting("ray_tracing", "Ray tracing", "Off", false)]);
        var profile = new GameProfile { Origin = "AI import", Items = [new ProfileItem("ray_tracing", "ON"), new ProfileItem("Shadows", "4")] };

        var merged = GameSettingsMerger.Merge(fromFile, profile, "G")!;
        Assert.Equal("Off", merged.Get("ray_tracing"));
        var extra = merged.Items.Single(i => i.Key == "profile.Shadows");
        Assert.True(extra.IsRaw);
        Assert.Equal("AI import", extra.Source);

        Assert.Null(GameSettingsMerger.Merge(null, null, "G"));
        Assert.Single(GameSettingsMerger.Merge(null, profile, "G")!.Items.Where(i => i.Key == "profile.Shadows"));
    }
}

public class AiExtrasTests
{
    private const string ConfigText = "[Video]\nShadowQuality=3\nRayTracing=1\nAuthToken=abcdef\nAccountEmail=me@example.com\nPath=C:\\Users\\RealPerson\\Documents\\x\nBlob=AAAAAAAAAABBBBBBBBBBCCCCCCCCCCDDDDDDDDDDEEEEEEEEEE\n";

    [Fact]
    public void Config_text_is_redacted_before_it_can_be_sent()
    {
        var r = AiSettingsImporter.Redact(ConfigText);
        Assert.DoesNotContain("AuthToken", r.Text);
        Assert.DoesNotContain("me@example.com", r.Text);
        Assert.DoesNotContain("RealPerson", r.Text);
        Assert.DoesNotContain("AAAAAAAAAA", r.Text);
        Assert.Contains("ShadowQuality=3", r.Text);
        Assert.Contains(@"C:\Users\<user>", r.Text);
        Assert.True(r.LinesRemoved >= 3);
        Assert.Equal(1, r.PathsAnonymised);
    }

    [Fact]
    public void Ai_import_keeps_only_settings_that_literally_appear_in_the_file()
    {
        var reply = """{"settings":[{"key":"ShadowQuality","value":"3"},{"key":"RayTracing","value":"1"},{"key":"MadeUp","value":"9"},{"key":"ShadowQuality","value":"7"}]}""";
        var result = AiSettingsImporter.ParseReply(reply, AiSettingsImporter.Redact(ConfigText).Text);
        Assert.Equal(["ShadowQuality", "RayTracing"], result.Accepted.Select(a => a.Key));
        Assert.Equal(2, result.Dropped);
        Assert.Throws<AiException>(() => AiSettingsImporter.ParseReply("no json", "x"));
    }

    [Fact]
    public async Task Import_sends_only_the_redacted_text()
    {
        string? sent = null;
        var handler = new StubHandler(async req =>
        {
            sent = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"{\"settings\":[{\"key\":\"ShadowQuality\",\"value\":\"3\"}]}"}}]}""", Encoding.UTF8, "application/json"),
            };
        });
        var client = new OpenAiCompatibleClient(new HttpClient(handler), new AiOptions("http://127.0.0.1:1", "m"), () => null);
        var redacted = AiSettingsImporter.Redact(ConfigText);

        var result = await AiSettingsImporter.ImportAsync(client, "G", redacted, CancellationToken.None);

        Assert.Single(result.Accepted);
        Assert.DoesNotContain("RealPerson", sent);
        Assert.DoesNotContain("AuthToken", sent);
    }

    [Fact]
    public void Export_pack_is_self_contained_and_private()
    {
        var r = CaptureAnalyzer.Analyze(TestData.File(TestData.RunWithFreezes(95, 10)));
        var md = AiExportPack.BuildSingle(r, "I turned RT off", new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc));
        Assert.Contains("# daignostic analysis pack: Test Game", md);
        Assert.Contains("## How to use this file", md);
        Assert.Contains("Instructions for the AI", md);
        Assert.Contains("```json", md);
        Assert.Contains("freeze.count", md);
        Assert.Contains("I turned RT off", md);
        Assert.Contains("suggestions", md);
        Assert.Contains("2026-10-30", md);
        Assert.DoesNotContain(@"C:\Users", md);
    }

    [Fact]
    public async Task Models_can_be_listed_and_a_model_name_is_required()
    {
        var handler = new StubHandler(req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"zeta"},{"id":"alpha"},{"id":"alpha"}]}""", Encoding.UTF8, "application/json"),
            RequestMessage = req,
        }));
        var client = new OpenAiCompatibleClient(new HttpClient(handler), new AiOptions("http://127.0.0.1:1", ""), () => "k");
        Assert.Equal(["alpha", "zeta"], await client.ListModelsAsync(CancellationToken.None));
        var ex = await Assert.ThrowsAsync<AiException>(() => client.CompleteAsync("s", "u", CancellationToken.None));
        Assert.Contains("model name", ex.Message);
    }

    [Fact]
    public void Ai_is_optional_and_only_configured_with_a_valid_endpoint_and_model()
    {
        Assert.False(new AppSettings().AiConfigured);
        Assert.False(new AppSettings { AiEnabled = true, AiEndpoint = "http://x", AiModel = "" }.AiConfigured);
        Assert.False(new AppSettings { AiEnabled = true, AiEndpoint = "not a url", AiModel = "m" }.AiConfigured);
        Assert.False(new AppSettings { AiEnabled = false, AiEndpoint = "http://x", AiModel = "m" }.AiConfigured);
        Assert.True(new AppSettings { AiEnabled = true, AiEndpoint = "http://127.0.0.1:8480", AiModel = "m" }.AiConfigured);
    }

    [Theory]
    [InlineData(null, McpStatus.NotRunning)]
    [InlineData("""{"Message": "Error"}""", McpStatus.NotEnabled)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-03-26","content":[{"type":"text","text":"pong"}]}}""", McpStatus.Connected)]
    public async Task Mcp_failures_are_explained_in_plain_terms(string? body, McpStatus expected)
    {
        var handler = new StubHandler(_ => body is null
            ? throw new HttpRequestException("refused")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));
        var diagnosis = await new CapFrameXMcpClient(new HttpClient(handler), CapFrameXMcpClient.DefaultEndpoint).DiagnoseAsync(CancellationToken.None);
        Assert.Equal(expected, diagnosis.Status);
        Assert.NotEmpty(diagnosis.Message);
    }
}
