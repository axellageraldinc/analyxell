using Daignostic.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Daignostic.Controls;

public sealed partial class CapFrameXCard : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(CapFrameXViewModel),
            typeof(CapFrameXCard),
            new PropertyMetadata(null, (d, _) => ((CapFrameXCard)d).Bindings?.Update()));

    public CapFrameXCard() => InitializeComponent();

    public CapFrameXViewModel ViewModel
    {
        get => (CapFrameXViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => ViewModel.Refresh();
}
