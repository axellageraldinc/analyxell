using Analyxell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Analyxell.Controls;

public sealed partial class AiPanel : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(AiPanelViewModel),
            typeof(AiPanel),
            new PropertyMetadata(null, (d, _) => ((AiPanel)d).Bindings?.Update()));

    public AiPanel() => InitializeComponent();

    private void Check_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            ViewModel.RequestCheck(id);
        }
    }

    public AiPanelViewModel ViewModel
    {
        get => (AiPanelViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}
