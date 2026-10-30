using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;

namespace Analyxell.ViewModels;

/// <summary>A message shown directly under the one field (or button) it is about, never at the top of the page.</summary>
public sealed partial class FieldMessage : ObservableObject
{
    public FieldMessage()
    {
        Text = "";
        Severity = InfoBarSeverity.Error;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    public partial string Text { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity Severity { get; set; }

    public bool IsOpen => Text.Length > 0;

    public void Error(string text) => Show(text, InfoBarSeverity.Error);

    public void Warning(string text) => Show(text, InfoBarSeverity.Warning);

    public void Success(string text) => Show(text, InfoBarSeverity.Success);

    public void Info(string text) => Show(text, InfoBarSeverity.Informational);

    public void Clear() => Text = "";

    private void Show(string text, InfoBarSeverity severity)
    {
        Severity = severity;
        Text = text;
    }
}
