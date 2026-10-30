namespace Analyxell.Core.Capture;

/// <summary>Finds a capture file path among the arguments the app was started with.</summary>
public static class LaunchArguments
{
    public static string? FindCapturePath(string? arguments) => FindCapturePaths(arguments).FirstOrDefault();

    /// <summary>All existing .json files named in the arguments, in order (for example a baseline and a later run).</summary>
    public static IReadOnlyList<string> FindCapturePaths(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return [];
        }

        // A single unquoted path that contains spaces is common when a launcher passes the path as-is.
        var whole = arguments.Trim().Trim('"');
        if (IsCapture(whole))
        {
            return [Path.GetFullPath(whole)];
        }

        return Split(arguments)
            .Where(IsCapture)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsCapture(string path) =>
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    /// <summary>Splits on spaces, keeping "quoted paths with spaces" together.</summary>
    internal static IEnumerable<string> Split(string arguments)
    {
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var c in arguments)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}
