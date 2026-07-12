using System.Windows;
using System.Windows.Controls;

namespace PatchGuard.Controls;

public partial class JourneyStepIndicator : UserControl
{
    private static readonly string[] Labels = ["Choose scan", "Scan", "Review", "Fix / verify"];

    public static readonly DependencyProperty CurrentStepProperty =
        DependencyProperty.Register(
            nameof(CurrentStep),
            typeof(int),
            typeof(JourneyStepIndicator),
            new PropertyMetadata(1, OnCurrentStepChanged),
            value => value is >= 1 and <= 4);

    public JourneyStepIndicator()
    {
        InitializeComponent();
        RefreshSteps();
    }

    public int CurrentStep
    {
        get => (int)GetValue(CurrentStepProperty);
        set => SetValue(CurrentStepProperty, value);
    }

    private static void OnCurrentStepChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _) =>
        ((JourneyStepIndicator)dependencyObject).RefreshSteps();

    private void RefreshSteps()
    {
        StepItems.ItemsSource = Labels.Select((label, index) =>
        {
            var number = index + 1;
            var state = number < CurrentStep ? "complete" : number == CurrentStep ? "current" : "upcoming";
            return new JourneyStepItem(
                number,
                label,
                number == CurrentStep,
                number < CurrentStep,
                $"Step {number} of 4, {label}, {state}");
        });
    }

    private sealed record JourneyStepItem(
        int Number,
        string Label,
        bool IsCurrent,
        bool IsComplete,
        string AutomationName);
}
