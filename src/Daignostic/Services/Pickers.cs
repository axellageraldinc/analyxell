using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Daignostic.Services;

/// <summary>
/// File and folder pickers. The save picker is the only way the app writes a file outside its own settings:
/// the user chooses the exact location, which is the explicit confirmation the read-only rule requires.
/// </summary>
internal static class Pickers
{
    public static async Task<string?> PickCaptureAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public static async Task<string?> PickAnyFileAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    public static async Task<string?> PickReportPathAsync(string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add("Markdown report", [".md"]);
        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        return (await picker.PickSaveFileAsync())?.Path;
    }
}

