using Analyxell.Core.Capture;

namespace Analyxell.Core.Tests;

public class LaunchArgumentsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "analyxell args " + Guid.NewGuid().ToString("N"));

    public LaunchArgumentsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Finds_a_quoted_path_with_spaces()
    {
        var file = Path.Combine(_dir, "my capture.json");
        File.WriteAllText(file, "{}");
        Assert.Equal(file, LaunchArguments.FindCapturePath($"--flag \"{file}\""));
    }

    [Fact]
    public void Finds_several_captures_in_order()
    {
        var a = Path.Combine(_dir, "a.json");
        var b = Path.Combine(_dir, "b.json");
        File.WriteAllText(a, "{}");
        File.WriteAllText(b, "{}");
        Assert.Equal([a, b], LaunchArguments.FindCapturePaths($"\"{a}\" \"{b}\" \"{a}\""));
    }

    [Fact]
    public void Finds_an_unquoted_path_that_contains_spaces()
    {
        var file = Path.Combine(_dir, "my capture.json");
        File.WriteAllText(file, "{}");
        Assert.Equal(file, LaunchArguments.FindCapturePath(file));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--verbose notes.txt")]
    [InlineData("missing-file.json")]
    public void Returns_null_when_there_is_no_existing_json_file(string? args) =>
        Assert.Null(LaunchArguments.FindCapturePath(args));
}
