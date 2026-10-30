using System.Text.Json;
using Daignostic.Core.Mcp;

namespace Daignostic.Core.Settings;

/// <summary>User preferences. Contains no secrets: the AI key lives in <see cref="ISecretStore"/>.</summary>
public sealed class AppSettings
{
    public string CapturesFolder { get; set; } = "";

    public string AiEndpoint { get; set; } = "http://127.0.0.1:8480";

    public string AiPreset { get; set; } = "text";

    public bool UseCapFrameXMcp { get; set; } = true;

    public string CapFrameXMcpUrl { get; set; } = CapFrameXMcpClient.DefaultEndpoint.ToString();
}

public sealed class SettingsStore(string filePath)
{
    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Daignostic", "settings.json");

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
