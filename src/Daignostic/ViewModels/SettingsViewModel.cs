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
        AiEndpoint = s.AiEndpoint;
        AiPreset = s.AiPreset;
        HasSavedKey = AppServices.Secrets.Get(AppServices.AiKeyName) is not null;
        NewApiKey = "";
        Message = "";
    }

    [ObservableProperty]
    public partial string CapturesFolder { get; set; }

    [ObservableProperty]
    public partial bool UseMcp { get; set; }

    [ObservableProperty]
    public partial string McpUrl { get; set; }

    [ObservableProperty]
    public partial string AiEndpoint { get; set; }

    [ObservableProperty]
    public partial string AiPreset { get; set; }

    /// <summary>Bound one-way from a PasswordBox event; cleared after saving so the key never lingers in memory longer than needed.</summary>
    [ObservableProperty]
    public partial string NewApiKey { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyStatus))]
    public partial bool HasSavedKey { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string Message { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity MessageSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsTesting { get; set; }

    public bool HasMessage => Message.Length > 0;

    public string KeyStatus => HasSavedKey
        ? "An API key is saved, encrypted for your Windows account."
        : "No API key saved. Local gateways without authentication work without one.";

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
        if (!Uri.TryCreate(AiEndpoint, UriKind.Absolute, out var ai) || (ai.Scheme != Uri.UriSchemeHttp && ai.Scheme != Uri.UriSchemeHttps))
        {
            Show("The AI endpoint must be an http or https URL.", InfoBarSeverity.Error);
            return;
        }

        if (UseMcp && !Uri.TryCreate(McpUrl, UriKind.Absolute, out _))
        {
            Show("The CapFrameX MCP URL is not valid.", InfoBarSeverity.Error);
            return;
        }

        AppServices.Save(new AppSettings
        {
            CapturesFolder = CapturesFolder.Trim(),
            UseCapFrameXMcp = UseMcp,
            CapFrameXMcpUrl = McpUrl.Trim(),
            AiEndpoint = AiEndpoint.Trim().TrimEnd('/'),
            AiPreset = string.IsNullOrWhiteSpace(AiPreset) ? "text" : AiPreset.Trim(),
        });

        if (!string.IsNullOrWhiteSpace(NewApiKey))
        {
            AppServices.Secrets.Set(AppServices.AiKeyName, NewApiKey.Trim());
            NewApiKey = "";
            HasSavedKey = true;
        }

        Show("Settings saved.", InfoBarSeverity.Success);
    }

    [RelayCommand]
    private void ClearKey()
    {
        AppServices.Secrets.Delete(AppServices.AiKeyName);
        HasSavedKey = false;
        Show("The saved API key was deleted.", InfoBarSeverity.Success);
    }

    [RelayCommand]
    private async Task TestMcpAsync()
    {
        IsTesting = true;
        try
        {
            if (!Uri.TryCreate(McpUrl, UriKind.Absolute, out var url))
            {
                Show("The CapFrameX MCP URL is not valid.", InfoBarSeverity.Error);
                return;
            }

            var ok = await new CapFrameXMcpClient(AppServices.Http, url).PingAsync(CancellationToken.None);
            Show(ok ? "Connected to CapFrameX." : "CapFrameX did not answer. Is it running with MCP enabled (McpEnabled in its settings)?",
                ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
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
            var key = string.IsNullOrWhiteSpace(NewApiKey) ? AppServices.Secrets.Get(AppServices.AiKeyName) : NewApiKey.Trim();
            var client = new OpenAiCompatibleClient(AppServices.Http, new AiOptions(AiEndpoint.Trim(), string.IsNullOrWhiteSpace(AiPreset) ? "text" : AiPreset.Trim()), () => key);
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


