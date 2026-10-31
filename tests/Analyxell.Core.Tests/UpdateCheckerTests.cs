using Analyxell.Core.Updates;

namespace Analyxell.Core.Tests;

public class UpdateCheckerTests
{
    private const string Release = """
        {"tag_name":"v1.4.0","html_url":"https://github.com/o/r/releases/tag/v1.4.0","body":"notes",
         "assets":[{"name":"analyxell-1.4.0-win-x64.msix","browser_download_url":"https://x/a.msix"},
                   {"name":"analyxell-1.4.0-portable-win-x64.zip","browser_download_url":"https://x/p.zip"},
                   {"name":"analyxell-1.4.0-SHA256SUMS.txt","browser_download_url":"https://x/s.txt"}]}
        """;

    [Fact]
    public void Picks_the_file_matching_the_install_kind()
    {
        Assert.Equal("https://x/a.msix", UpdateChecker.Parse(Release, InstallKind.Installer)!.DownloadUrl);
        Assert.Equal("https://x/p.zip", UpdateChecker.Parse(Release, InstallKind.Portable)!.DownloadUrl);
    }

    [Fact]
    public void Falls_back_to_the_release_page_without_assets()
    {
        var info = UpdateChecker.Parse("""{"tag_name":"1.4.0","html_url":"https://p"}""", InstallKind.Installer)!;
        Assert.Equal("https://p", info.DownloadUrl);
    }

    [Fact]
    public void Returns_null_without_a_usable_tag() =>
        Assert.Null(UpdateChecker.Parse("""{"tag_name":"nightly"}""", InstallKind.Portable));

    [Theory]
    [InlineData("1.4.0", "1.3.0.0", true)]
    [InlineData("1.3.0", "1.3.0.0", false)]
    [InlineData("1.2.9", "1.3.0.0", false)]
    [InlineData("v1.3.1-beta", "1.3.0.0", true)]
    public void Compares_versions(string tag, string current, bool newer)
    {
        Assert.True(UpdateChecker.TryParseVersion(tag, out var latest));
        Assert.Equal(newer, UpdateChecker.IsNewer(latest, Version.Parse(current)));
    }
}
