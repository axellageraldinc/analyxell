using System.Net;
using System.Text;
using Daignostic.Core.Ai;
using Daignostic.Core.Analysis;
using Daignostic.Core.Capture;
using Daignostic.Core.Config;
using Daignostic.Core.Mcp;
using Daignostic.Core.Settings;

namespace Daignostic.Core.Tests;

public class CaptureReaderTests
{
    private const string Json = """
        {"Hash":"x","Info":{"GameName":"Test Game","ProcessName":"game.exe","Processor":"CPU","GPU":"GPU",
         "GPUDriverVersion":"1.0","DeviceName":"SECRET-PC-NAME","Motherboard":"SECRET-BOARD","ResolutionInfo":"1920x1080",
         "ApiInfo":"DX12","OS":"OS","SystemRam":"16GB","CreationDate":"2026-01-01T00:00:00Z","AppVersion":"1.9"},
         "Runs":[{"CaptureData":{"TimeInSeconds":[0.01,0.02,0.03],"MsBetweenPresents":[10,10,10],
         "MsBetweenDisplayChange":[10,10,10],"GpuActive":[9,9,9],"Dropped":[false,true,false]},
         "SensorData2":{"MeasureTime":{"Name":"MeasureTime","Type":"Time","Values":[0,0.25]},
         "/gpu-nvidia/0/load/0":{"Name":"GPU Core","Type":"Load","Values":[50,60]}}}]}
        """;

    [Fact]
    public void Reads_frames_sensors_and_info_with_a_bom()
    {
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(Json)).ToArray();
        var capture = CaptureReader.Load(new MemoryStream(bytes), "x.json");

        Assert.Equal("Test Game", capture.Info.Game);
        var run = Assert.Single(capture.Runs);
        Assert.Equal([10, 10, 10], run.FrameMs);
        Assert.Equal([false, true, false], run.Dropped);
        Assert.Equal([0, 0.25], run.SensorTimeSec);
        Assert.Equal("GPU Core", Assert.Single(run.Sensors).Name);
    }

    [Fact]
    public void Machine_identifiers_never_reach_the_ai_message()
    {
        var capture = CaptureReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(Json)), @"C:\Users\someone\cap.json");
        var message = AiPrompts.BuildSingle(CaptureAnalyzer.Analyze(capture), "note");
        Assert.DoesNotContain("SECRET-PC-NAME", message);
        Assert.DoesNotContain("SECRET-BOARD", message);
        Assert.DoesNotContain("someone", message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"Runs":[]}""")]
    [InlineData("""{"Runs":[{"CaptureData":{}}]}""")]
    public void Rejects_files_that_are_not_captures(string json) =>
        Assert.Throws<InvalidDataException>(() => CaptureReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(json))));

    /// <summary>Optional check against a real capture; set DAIGNOSTIC_SAMPLE_CAPTURE to run it locally.</summary>
    [Fact]
    public void Real_capture_parses_and_gives_plausible_numbers_when_available()
    {
        var path = Environment.GetEnvironmentVariable("DAIGNOSTIC_SAMPLE_CAPTURE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        var result = CaptureAnalyzer.Analyze(CaptureReader.Load(path));
        Assert.InRange(result.Metrics.AverageFps, 1, 1000);
        Assert.True(result.Metrics.Low1AverageFps <= result.Metrics.AverageFps);
        Assert.NotEmpty(result.Facts);
    }
}

public class AiTests
{
    private static readonly HashSet<string> Keys = ["fps.p1", "finding:FREEZES"];

    [Fact]
    public void Parses_fenced_json_and_keeps_cited_items()
    {
        const string reply = """
            Sure!
            ```json
            {"summary":"Freezes.","inconclusive":false,"findings":[
              {"title":"A","explanation":"x","evidence":["fps.p1","made.up"],"actions":["Turn off ray tracing"],"confidence":"medium"}]}
            ```
            """;
        var a = AiResponseParser.Parse(reply, Keys);
        var item = Assert.Single(a.Items);
        Assert.Equal(["fps.p1"], item.Evidence);
        Assert.Equal(0, a.DroppedUncited);
        Assert.False(a.Inconclusive);
    }

    [Fact]
    public void Drops_claims_without_valid_evidence_and_flags_inconclusive()
    {
        const string reply = """{"summary":"s","findings":[{"title":"A","evidence":[]},{"title":"B","evidence":["invented.key"]}]}""";
        var a = AiResponseParser.Parse(reply, Keys);
        Assert.Empty(a.Items);
        Assert.Equal(2, a.DroppedUncited);
        Assert.True(a.Inconclusive);
    }

    [Theory]
    [InlineData("no json here")]
    [InlineData("{not valid json}")]
    public void Garbage_replies_raise_a_clear_error(string reply) =>
        Assert.Throws<AiException>(() => AiResponseParser.Parse(reply, Keys));

    [Fact]
    public void System_prompt_forbids_risky_advice_and_requires_citations()
    {
        Assert.Contains("registry", AiPrompts.System);
        Assert.Contains("cite evidence", AiPrompts.System);
        Assert.Contains("inconclusive", AiPrompts.System);
    }

    [Fact]
    public void User_message_has_no_note_field_unless_one_is_given()
    {
        var r = CaptureAnalyzer.Analyze(TestData.File(TestData.Run(TestData.Steady(100, 10))));
        Assert.DoesNotContain("user_note", AiPrompts.BuildSingle(r, null));
        Assert.Contains("user_note", AiPrompts.BuildSingle(r, "I turned RT off"));
    }

    [Fact]
    public async Task Client_posts_to_chat_completions_with_bearer_key_and_preset()
    {
        HttpRequestMessage? seen = null;
        string? body = null;
        var handler = new StubHandler(async req =>
        {
            seen = req;
            body = await req.Content!.ReadAsStringAsync();
            return Reply("""{"choices":[{"message":{"content":"{\"summary\":\"ok\"}"}}]}""");
        });
        var client = new OpenAiCompatibleClient(new HttpClient(handler), new AiOptions("http://127.0.0.1:8480/", "balanced"), () => "sk-test-key");

        var text = await client.CompleteAsync("sys", "user", CancellationToken.None);

        Assert.Equal("""{"summary":"ok"}""", text);
        Assert.Equal("http://127.0.0.1:8480/v1/chat/completions", seen!.RequestUri!.ToString());
        Assert.Equal("Bearer", seen.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test-key", seen.Headers.Authorization.Parameter);
        Assert.Contains("\"model\":\"balanced\"", body);
        Assert.DoesNotContain("tools", body);
    }

    [Fact]
    public async Task Errors_never_leak_the_api_key()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("denied sk-test-key") }));
        var client = new OpenAiCompatibleClient(new HttpClient(handler), new AiOptions(), () => "sk-test-key");

        var ex = await Assert.ThrowsAsync<AiException>(() => client.CompleteAsync("s", "u", CancellationToken.None));
        Assert.DoesNotContain("sk-test-key", ex.Message);
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public async Task Unreachable_endpoint_gives_a_friendly_error()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("refused"));
        var client = new OpenAiCompatibleClient(new HttpClient(handler), new AiOptions(), () => null);
        var ex = await Assert.ThrowsAsync<AiException>(() => client.CompleteAsync("s", "u", CancellationToken.None));
        Assert.Contains("Is it running", ex.Message);
    }

    [Fact]
    public async Task Analyst_round_trip_uses_only_valid_evidence()
    {
        var r = CaptureAnalyzer.Analyze(TestData.File(TestData.RunWithFreezes(95, 10)));
        var handler = new StubHandler(_ => Task.FromResult(Reply(
            """{"choices":[{"message":{"content":"{\"summary\":\"s\",\"findings\":[{\"title\":\"t\",\"explanation\":\"e\",\"evidence\":[\"finding:FREEZES\",\"freeze.count\"],\"actions\":[],\"confidence\":\"high\"}]}"}}]}""")));
        var analyst = new AiAnalyst(new OpenAiCompatibleClient(new HttpClient(handler), new AiOptions(), () => null));

        var analysis = await analyst.AnalyzeAsync(r, null, CancellationToken.None);
        Assert.Equal(["finding:FREEZES", "freeze.count"], Assert.Single(analysis.Items).Evidence);
    }

    private static HttpResponseMessage Reply(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public class McpTests
{
    [Theory]
    [InlineData("cfx_start_capture")]
    [InlineData("cfx_stop_capture")]
    [InlineData("cfx_set_config")]
    [InlineData("cfx_set_logged_sensors")]
    [InlineData("cfx_set_overlay_entry")]
    [InlineData("anything_else")]
    public async Task Write_and_unknown_tools_are_blocked_before_any_request(string tool)
    {
        var called = false;
        var handler = new StubHandler(_ =>
        {
            called = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var client = new CapFrameXMcpClient(new HttpClient(handler), CapFrameXMcpClient.DefaultEndpoint);

        await Assert.ThrowsAsync<McpToolNotAllowedException>(() => client.CallToolAsync(tool, "{}", CancellationToken.None));
        Assert.False(called);
    }

    [Fact]
    public void Allow_list_contains_only_read_style_tools() =>
        Assert.All(McpToolPolicy.AllowedTools, t => Assert.DoesNotMatch("start|stop|set_|toggle|delete|write", t));

    [Fact]
    public void Parses_the_record_listing()
    {
        const string json = """[{"id":"D:\\Logs\\a.json","game":"G","processName":"g.exe","recordedAt":"2026-10-30T06:43:09Z","durationSec":600}]""";
        var item = Assert.Single(CapFrameXMcpClient.ParseListings(json));
        Assert.Equal("a.json", item.FileName);
        Assert.Equal(600, item.DurationSec);
        Assert.NotNull(item.RecordedAt);
    }

    [Fact]
    public async Task Lists_records_through_initialize_then_tools_call()
    {
        var methods = new List<string>();
        var handler = new StubHandler(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            if (body.Contains("\"method\":\"initialize\""))
            {
                methods.Add("initialize");
                var r = Json("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-03-26"}}""");
                r.Headers.Add("Mcp-Session-Id", "abc");
                return r;
            }

            if (body.Contains("notifications/initialized"))
            {
                methods.Add("initialized");
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            methods.Add("call:" + string.Join(",", req.Headers.GetValues("Mcp-Session-Id")));
            return Json("""{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"[{\"id\":\"C:\\\\x\\\\a.json\",\"game\":\"G\"}]"}]}}""");
        });

        var list = await new CapFrameXMcpClient(new HttpClient(handler), CapFrameXMcpClient.DefaultEndpoint)
            .ListRecordsAsync(5, CancellationToken.None);

        Assert.Equal("G", Assert.Single(list).Game);
        Assert.Equal(["initialize", "initialized", "call:abc"], methods);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public class ConfigAndSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "daignostic-tests-" + Guid.NewGuid().ToString("N"));

    public ConfigAndSettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Wukong_reader_maps_certain_values_flags_raw_ones_and_never_writes()
    {
        var path = Path.Combine(_dir, "GameUserSettings.ini");
        File.WriteAllText(path, string.Join('\n',
            """UISettingData=(("Rtx", "1"),("RtxLevel", "1"),("Dlss", "1"),("InsertFrame", "0"),("LowLatencyMode", "1"),("Vsync", "0"),("QualityLevel", "6"),("ShadowQuality", "4"))""",
            "ResolutionSizeX=3440",
            "ResolutionSizeY=1440"));
        var before = (File.ReadAllText(path), File.GetLastWriteTimeUtc(path));

        var s = new WukongConfigReader(path).TryRead()!;

        Assert.True(s.IsOn("ray_tracing"));
        Assert.True(s.IsOn("dlss"));
        Assert.False(s.IsOn("vsync"));
        Assert.Equal("Off", s.Get("frame_generation"));
        Assert.Equal("3440x1440", s.Get("resolution"));
        Assert.True(s.Items.Single(i => i.Key == "quality_preset").IsRaw);
        Assert.Equal("6", s.Get("quality_preset"));
        Assert.Equal(before, (File.ReadAllText(path), File.GetLastWriteTimeUtc(path)));
    }

    [Fact]
    public void Wukong_reader_returns_null_when_the_file_is_missing() =>
        Assert.Null(new WukongConfigReader(Path.Combine(_dir, "nope.ini")).TryRead());

    [Fact]
    public void Wukong_reader_matches_by_process_or_name()
    {
        var reader = new WukongConfigReader();
        var file = TestData.File(TestData.Run(TestData.Steady(10, 10)), "Black Myth: Wukong", "b1-Win64-Shipping.exe");
        Assert.True(reader.Matches(file.Info));
        Assert.False(reader.Matches(TestData.File(TestData.Run(TestData.Steady(10, 10))).Info));
    }

    [Fact]
    public void Settings_round_trip_and_survive_corruption()
    {
        var store = new SettingsStore(Path.Combine(_dir, "s", "settings.json"));
        Assert.Equal("text", store.Load().AiPreset);

        store.Save(new AppSettings { AiPreset = "balanced", CapturesFolder = @"D:\Caps" });
        var loaded = store.Load();
        Assert.Equal("balanced", loaded.AiPreset);
        Assert.Equal(@"D:\Caps", loaded.CapturesFolder);

        File.WriteAllText(store.FilePath, "{ corrupt");
        Assert.Equal("text", store.Load().AiPreset);
    }

    [Fact]
    public void Api_key_is_encrypted_at_rest_and_never_in_the_settings_file()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var secrets = new DpapiSecretStore(Path.Combine(_dir, "secrets"));
        secrets.Set("aikey", "sk-super-secret");
        Assert.Equal("sk-super-secret", secrets.Get("aikey"));
        Assert.DoesNotContain("sk-super-secret", File.ReadAllText(Path.Combine(_dir, "secrets", "aikey.bin"), Encoding.Latin1));

        var settings = new SettingsStore(Path.Combine(_dir, "settings.json"));
        settings.Save(new AppSettings());
        Assert.DoesNotContain("sk-", File.ReadAllText(settings.FilePath));

        secrets.Delete("aikey");
        Assert.Null(secrets.Get("aikey"));
    }
}

internal sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        respond(request);
}
