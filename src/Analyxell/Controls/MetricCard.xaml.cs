using Analyxell.Services;
using Analyxell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Analyxell.Controls;

public sealed partial class MetricCard : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(MetricCard), new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(MetricCard), new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(MetricCard), new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(MetricLevel), typeof(MetricCard), new PropertyMetadata(MetricLevel.Neutral, OnChanged));

    public MetricCard()
    {
        InitializeComponent();
        Edge.Background = LevelBrushes.Edge(Level);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    /// <summary>How the number reads at a glance; shown as the colour of the card's left edge.</summary>
    public MetricLevel Level
    {
        get => (MetricLevel)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (MetricCard)d;
        card.Edge.Background = LevelBrushes.Edge(card.Level);
        var state = card.Level switch
        {
            MetricLevel.Good => "good",
            MetricLevel.Warning => "needs a look",
            MetricLevel.Bad => "a problem",
            _ => "",
        };
        AutomationProperties.SetName(card, $"{card.Title}: {card.Value} {card.Caption}{(state.Length > 0 ? ", " + state : "")}");
    }
}
