using Analyxell.Core.Settings;

namespace Analyxell.Core.Mcp;

public sealed record CatalogResult(IReadOnlyList<CaptureListing> Items, string Source, string? Warning);

/// <summary>Finds capture files, first through CapFrameX's MCP server, then by scanning a folder.</summary>
public static class CaptureCatalog
{
    public static IReadOnlyList<CaptureListing> ListFolder(string folder, int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        try
        {
            return new DirectoryInfo(folder)
                .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(limit)
                .Select(f => new CaptureListing(f.FullName, "", "", new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero), 0))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static async Task<CatalogResult> ListAsync(AppSettings settings, HttpClient http, CancellationToken ct)
    {
        string? warning = null;
        if (settings.UseCapFrameXMcp && Uri.TryCreate(settings.CapFrameXMcpUrl, UriKind.Absolute, out var url))
        {
            try
            {
                var items = await new CapFrameXMcpClient(http, url).ListRecordsAsync(100, ct).ConfigureAwait(false);
                return new CatalogResult(items, "CapFrameX (MCP)", null);
            }
            catch (McpException ex)
            {
                warning = ex.Message;
            }
        }

        var folder = ListFolder(settings.CapturesFolder);
        if (folder.Count == 0 && string.IsNullOrWhiteSpace(settings.CapturesFolder))
        {
            warning ??= "Set a captures folder in Settings, or open a capture file.";
        }

        return new CatalogResult(folder, "Captures folder", warning);
    }
}
