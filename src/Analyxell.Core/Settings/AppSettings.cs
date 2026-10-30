using System.Text.Json;
using Analyxell.Core.Mcp;

namespace Analyxell.Core.Settings;

/// <summary>User preferences. Contains no secrets: the AI key lives in <see cref="ISecretStore"/>.</summary>
public sealed class AppSettings
{
    public string CapturesFolder { get; set; } = "";

    /// <summary>AI inside the app is optional. Off until the user turns it on and configures an endpoint.</summary>
    public bool AiEnabled { get; set; }

    public string AiEndpoint { get; set; } = "";

    /// <summary>The model name exactly as the endpoint expects it, for example "gpt-5" or "claude-sonnet-5-5".</summary>
    public string AiModel { get; set; } = "";

    /// <summary>When false, the API key is kept in memory for this session only and never written to disk.</summary>
    public bool SaveApiKey { get; set; } = true;

    /// <summary>Include the names of background programs in AI messages. Off by default; categories are sent instead.</summary>
    public bool ShareBackgroundAppNames { get; set; }

    /// <summary>Where the user told us CapFrameX.exe is (needed for the portable version).</summary>
    public string CapFrameXPath { get; set; } = "";

    public bool UseCapFrameXMcp { get; set; } = true;

    public string CapFrameXMcpUrl { get; set; } = CapFrameXMcpClient.DefaultEndpoint.ToString();

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public bool AiConfigured =>
        AiEnabled &&
        AiModel.Trim().Length > 0 &&
        Uri.TryCreate(AiEndpoint.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

public sealed class SettingsStore(string filePath)
{
    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Analyxell", "settings.json");

    public string FilePath { get; } = filePath;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize(File.ReadAllText(FilePath), CoreJsonContext.Default.AppSettings);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable settings file must never stop the app: fall back to defaults.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, CoreJsonContext.Default.AppSettings));
    }
}
