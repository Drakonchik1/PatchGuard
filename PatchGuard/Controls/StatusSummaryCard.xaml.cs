using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PatchGuard.Controls;

public partial class StatusSummaryCard : UserControl
{
    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(StatusSummaryCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatusSummaryCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SupportingTextProperty =
        DependencyProperty.Register(nameof(SupportingText), typeof(string), typeof(StatusSummaryCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusBrushProperty =
        DependencyProperty.Register(nameof(StatusBrush), typeof(Brush), typeof(StatusSummaryCard), new PropertyMetadata(Brushes.White));

    public StatusSummaryCard() => InitializeComponent();

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string SupportingText
    {
        get => (string)GetValue(SupportingTextProperty);
        set => SetValue(SupportingTextProperty, value);
    }

    public Brush StatusBrush
    {
        get => (Brush)GetValue(StatusBrushProperty);
        set => SetValue(StatusBrushProperty, value);
    }
}
