using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daignostic.Core.Ai;
using Daignostic.Core.Mcp;
using Daignostic.Core.Settings;
using Daignostic.Services;
using Microsoft.UI.Xaml.Controls;

namespace Daignostic.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel()
    {
        var s = AppServices.Settings;
        CapturesFolder = s.CapturesFolder;
        UseMcp = s.UseCapFrameXMcp;
        McpUrl = s.CapFrameXMcpUrl;
        AiEnabled = s.AiEnabled;
        AiEndpoint = s.AiEndpoint;
        AiModel = s.AiModel;
        SaveApiKey = s.SaveApiKey;
        ShareBackgroundNames = s.ShareBackgroundAppNames;
        HasSavedKey = AppServices.HasStoredKey;
        NewApiKey = "";
        Message = "";
        McpStatus = "";
        KeyStoredInfo = "";
    }

    public ObservableCollection<string> Models { get; } = [];

    [ObservableProperty]
    public partial string CapturesFolder { get; set; }

    [ObservableProperty]
    public partial bool UseMcp { get; set; }

    [ObservableProperty]
    public partial string McpUrl { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMcpStatus))]
    public partial string McpStatus { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity McpSeverity { get; set; }

    [ObservableProperty]
    public partial bool AiEnabled { get; set; }

    [ObservableProperty]
    public partial string AiEndpoint { get; set; }

    [ObservableProperty]
    public partial string AiModel { get; set; }

    /// <summary>The PasswordBox pushes into this; it is cleared after saving so the key does not linger in memory.</summary>
    [ObservableProperty]
    public partial string NewApiKey { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyStatus))]
    public partial bool SaveApiKey { get; set; }

    [ObservableProperty]
    public partial bool ShareBackgroundNames { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyStatus))]
    public partial bool HasSavedKey { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeyInfo))]
    public partial string KeyStoredInfo { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string Message { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity MessageSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsTesting { get; set; }

    public bool HasMessage => Message.Length > 0;

    public bool HasModels => Models.Count > 0;

    public bool HasMcpStatus => McpStatus.Length > 0;

    public bool HasKeyInfo => KeyStoredInfo.Length > 0;

    public string KeyStatus => HasSavedKey
        ? "A key is saved on this PC, encrypted for your Windows account."
        : SaveApiKey
            ? "No API key saved. Local gateways without authentication work without one."
            : "The key will be kept in memory for this session only and never written to disk.";

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = await Pickers.PickFolderAsync();
        if (folder is not null)
        {
            CapturesFolder = folder;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (AiEnabled)
        {
            if (!Uri.TryCreate(AiEndpoint.Trim(), UriKind.Absolute, out var ai) || (ai.Scheme != Uri.UriSchemeHttp && ai.Scheme != Uri.UriSchemeHttps))
            {
                Show("The AI endpoint must be an http or https address, for example http://127.0.0.1:8480.", InfoBarSeverity.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(AiModel))
            {
                Show("Enter a model name, or press List models to pick one from your endpoint.", InfoBarSeverity.Error);
                return;
            }
        }

        if (UseMcp && !Uri.TryCreate(McpUrl, UriKind.Absolute, out _))
        {
            Show("The CapFrameX MCP address is not valid.", InfoBarSeverity.Error);
            return;
        }

        AppServices.Save(new AppSettings
        {
            CapturesFolder = CapturesFolder.Trim(),
            UseCapFrameXMcp = UseMcp,
            CapFrameXMcpUrl = McpUrl.Trim(),
            AiEnabled = AiEnabled,
            AiEndpoint = AiEndpoint.Trim().TrimEnd('/'),
            AiModel = AiModel.Trim(),
            SaveApiKey = SaveApiKey,
            ShareBackgroundAppNames = ShareBackgroundNames,
        });

        if (!string.IsNullOrWhiteSpace(NewApiKey))
        {
            if (SaveApiKey)
            {
                AppServices.Secrets.Set(AppServices.AiKeyName, NewApiKey.Trim());
                AppServices.SessionApiKey = null;
            }
            else
            {
                AppServices.SessionApiKey = NewApiKey.Trim();
                AppServices.Secrets.Delete(AppServices.AiKeyName);
            }

            NewApiKey = "";
        }
        else if (!SaveApiKey)
        {
            // Switching to session-only: forget any key that was stored on disk.
            AppServices.Secrets.Delete(AppServices.AiKeyName);
        }

        HasSavedKey = AppServices.HasStoredKey;
        Show("Settings saved.", InfoBarSeverity.Success);
    }

    [RelayCommand]
    private void ClearKey()
    {
        AppServices.Secrets.Delete(AppServices.AiKeyName);
        AppServices.SessionApiKey = null;
        HasSavedKey = false;
        KeyStoredInfo = "";
        Show("The API key was deleted from this PC.", InfoBarSeverity.Success);
    }

    /// <summary>Lets the user see for themselves that the stored key is encrypted, not readable text.</summary>
    [RelayCommand]
    private void ShowStoredKey()
    {
        var path = AppServices.KeyFilePath;
        if (!File.Exists(path))
        {
            KeyStoredInfo = "Nothing is stored on disk.";
            return;
        }

        var bytes = File.ReadAllBytes(path);
        var head = string.Join(" ", bytes.Take(24).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        KeyStoredInfo = $"File: {path}\nSize: {bytes.Length} bytes of encrypted data.\nFirst bytes: {head} ...\nThis is a Windows DPAPI blob. It is not your key in readable form, and it cannot be decrypted on another Windows account or another PC.";
    }

    [RelayCommand]
    private async Task OpenCapFrameXFolderAsync()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CapFrameX");
        var folder = Directory.Exists(Path.Combine(root, "Configuration")) ? Path.Combine(root, "Configuration") : root;
        if (!Directory.Exists(folder))
        {
            Show("CapFrameX's settings folder was not found. If you use the portable version, open the Configuration folder next to CapFrameX.exe.", InfoBarSeverity.Informational);
            return;
        }

        await Windows.System.Launcher.LaunchFolderPathAsync(folder);
    }

    [RelayCommand]
    private async Task TestMcpAsync()
    {
        IsTesting = true;
        try
        {
            if (!Uri.TryCreate(McpUrl, UriKind.Absolute, out var url))
            {
                McpSeverity = InfoBarSeverity.Error;
                McpStatus = "That is not a valid address. The default is http://localhost:1337/mcp.";
                return;
            }

            var diagnosis = await new CapFrameXMcpClient(AppServices.Http, url).DiagnoseAsync(CancellationToken.None);
            McpSeverity = diagnosis.Status == Core.Mcp.McpStatus.Connected ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            McpStatus = diagnosis.Message;
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task ListModelsAsync()
    {
        IsTesting = true;
        try
        {
            var key = string.IsNullOrWhiteSpace(NewApiKey) ? AppServices.GetApiKey() : NewApiKey.Trim();
            var client = new OpenAiCompatibleClient(AppServices.Http, new AiOptions(AiEndpoint.Trim(), AiModel.Trim()), () => key);
            var models = await client.ListModelsAsync(CancellationToken.None);
            Models.Clear();
            foreach (var m in models)
            {
                Models.Add(m);
            }

            OnPropertyChanged(nameof(HasModels));
            Show(models.Count == 0 ? "The endpoint answered but lists no models. Type the model name yourself." : $"{models.Count} model(s) found. Pick one from the list.", models.Count == 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        }
        catch (AiException ex)
        {
            Show(ex.Message + " You can still type a model name yourself.", InfoBarSeverity.Warning);
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task TestAiAsync()
    {
        IsTesting = true;
        try
        {
            var key = string.IsNullOrWhiteSpace(NewApiKey) ? AppServices.GetApiKey() : NewApiKey.Trim();
            var client = new OpenAiCompatibleClient(AppServices.Http, new AiOptions(AiEndpoint.Trim(), AiModel.Trim()), () => key);
            var reply = await client.CompleteAsync("Reply with the single word: ok", "ping", CancellationToken.None);
            Show("The AI endpoint answered: " + (reply.Length > 60 ? reply[..60] + "..." : reply), InfoBarSeverity.Success);
        }
        catch (AiException ex)
        {
            Show(ex.Message, InfoBarSeverity.Warning);
        }
        finally
        {
            IsTesting = false;
        }
    }

    private void Show(string message, InfoBarSeverity severity)
    {
        Message = message;
        MessageSeverity = severity;
    }
}
