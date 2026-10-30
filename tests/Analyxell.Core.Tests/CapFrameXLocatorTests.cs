using System.Net;
using System.Text;
using Analyxell.Core.Ai;
using Analyxell.Core.SystemInfo;

namespace Analyxell.Core.Tests;

public class CapFrameXLocatorTests
{
    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    [Fact]
    public void Nothing_is_reported_as_not_found_with_the_official_download_address()
    {
        var found = CapFrameXLocator.Find(new FakeProbe(), null);
        Assert.False(found.Found);
        Assert.Null(found.ExePath);
        Assert.Equal("https://github.com/CXWorld/CapFrameX/releases", CapFrameXInstall.DownloadUrl);
    }

    [Fact]
    public void The_users_chosen_path_wins_which_is_how_the_portable_version_is_found()
    {
        var probe = new FakeProbe();
        probe.ExistingFiles.Add(@"D:\Tools\CapFrameX\CapFrameX.exe");
        probe.ExistingFiles.Add(Path.Combine(ProgramFiles, "CapFrameX", "CapFrameX.exe"));
        var found = CapFrameXLocator.Find(probe, @"D:\Tools\CapFrameX\CapFrameX.exe");
        Assert.Equal(@"D:\Tools\CapFrameX\CapFrameX.exe", found.ExePath);
    }

    [Fact]
    public void A_wrong_user_path_falls_back_to_the_usual_folders()
    {
        var probe = new FakeProbe();
        var installed = Path.Combine(ProgramFiles, "CapFrameX", "CapFrameX.exe");
        probe.ExistingFiles.Add(installed);
        var found = CapFrameXLocator.Find(probe, @"Z:\gone\CapFrameX.exe");
        Assert.True(found.Found);
        Assert.Equal(installed, found.ExePath);
    }

    [Fact]
    public void The_windows_uninstall_list_is_used_when_it_is_in_an_unusual_place()
    {
        var probe = new FakeProbe();
        const string Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        probe.SubKeys[$"HKLM|{Key}"] = ["SomethingElse", "{GUID-1}"];
        probe.Strings[$@"HKLM|{Key}\SomethingElse|DisplayName"] = "Another App";
        probe.Strings[$@"HKLM|{Key}\{{GUID-1}}|DisplayName"] = "CapFrameX";
        probe.Strings[$@"HKLM|{Key}\{{GUID-1}}|InstallLocation"] = @"E:\Apps\CapFrameX";
        probe.ExistingFiles.Add(@"E:\Apps\CapFrameX\CapFrameX.exe");

        var found = CapFrameXLocator.Find(probe, null);
        Assert.Equal(@"E:\Apps\CapFrameX\CapFrameX.exe", found.ExePath);
        Assert.Contains("installed apps", found.Source);
    }

    [Fact]
    public void An_uninstall_entry_whose_exe_is_missing_does_not_count_as_installed()
    {
        var probe = new FakeProbe();
        const string Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        probe.SubKeys[$"HKLM|{Key}"] = ["{GUID-1}"];
        probe.Strings[$@"HKLM|{Key}\{{GUID-1}}|DisplayName"] = "CapFrameX";
        probe.Strings[$@"HKLM|{Key}\{{GUID-1}}|InstallLocation"] = @"E:\Gone";
        Assert.False(CapFrameXLocator.Find(probe, null).Found);
    }

    [Fact]
    public void Running_state_is_reported_even_when_the_exe_cannot_be_found()
    {
        var found = CapFrameXLocator.Find(new FakeProbe { Running = true }, null);
        Assert.False(found.Found);
        Assert.True(found.Running);
    }
}

public class EndpointAddressTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8480", "models", "http://127.0.0.1:8480/v1/models")]
    [InlineData("http://127.0.0.1:8480/", "models", "http://127.0.0.1:8480/v1/models")]
    [InlineData("http://127.0.0.1:8480/v1", "models", "http://127.0.0.1:8480/v1/models")]
    [InlineData("http://127.0.0.1:8480/v1/", "chat/completions", "http://127.0.0.1:8480/v1/chat/completions")]
    [InlineData("https://api.example.com/V1", "chat/completions", "https://api.example.com/V1/chat/completions")]
    [InlineData("  https://api.example.com  ", "models", "https://api.example.com/v1/models")]
    public void The_v1_prefix_is_never_doubled_or_missing(string endpoint, string resource, string expected) =>
        Assert.Equal(expected, OpenAiCompatibleClient.BuildUri(endpoint, resource).ToString());

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://host/v1")]
    [InlineData("")]
    public void Invalid_addresses_are_rejected_with_a_friendly_error(string endpoint) =>
        Assert.Throws<AiException>(() => OpenAiCompatibleClient.BuildUri(endpoint, "models"));

    [Fact]
    public async Task A_405_or_404_explains_the_address_and_model_name()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed) { Content = new StringContent("no", Encoding.UTF8) }));
        var client = new OpenAiCompatibleClient(new HttpClient(handler), new AiOptions("http://127.0.0.1:8480/v1", "m"), () => null);
        var ex = await Assert.ThrowsAsync<AiException>(() => client.ListModelsAsync(CancellationToken.None));
        Assert.Contains("405", ex.Message);
        Assert.Contains("with or without /v1", ex.Message);
    }

    [Fact]
    public async Task An_address_ending_in_v1_reaches_the_right_path()
    {
        Uri? seen = null;
        var handler = new StubHandler(req =>
        {
            seen = req.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":[{"id":"a"}]}""", Encoding.UTF8, "application/json") });
        });
        var client = new OpenAiCompatibleClient(new HttpClient(handler), new AiOptions("http://127.0.0.1:8480/v1", "m"), () => null);
        await client.ListModelsAsync(CancellationToken.None);
        Assert.Equal("/v1/models", seen!.AbsolutePath);
    }
}
