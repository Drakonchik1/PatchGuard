using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace PatchGuard.Tests;

public sealed partial class UiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData("ScanView.xaml")]
    [InlineData("MonitorView.xaml")]
    [InlineData("FpsView.xaml")]
    [InlineData("OptimizeView.xaml")]
    [InlineData("FindingsView.xaml")]
    [InlineData("GuideView.xaml")]
    public void PrimaryViewsUseSharedPageHeader(string fileName)
    {
        var xaml = ReadView(fileName);

        Assert.Contains("<controls:PageHeader", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HomeView.xaml")]
    [InlineData("DiagnoseView.xaml")]
    [InlineData("ScanView.xaml")]
    [InlineData("MonitorView.xaml")]
    [InlineData("FpsView.xaml")]
    [InlineData("OptimizeView.xaml")]
    [InlineData("FindingsView.xaml")]
    [InlineData("GuideView.xaml")]
    public void PrimaryViewsAvoidFixedMinimumWidthColumns(string fileName)
    {
        var xaml = ReadView(fileName);

        Assert.DoesNotContain("MinWidth=\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ColumnDefinition Width=\"320\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ColumnDefinition Width=\"300\"", xaml, StringComparison.Ordinal);

        var explicitWidths = ExplicitWidthRegex().Matches(xaml)
            .Select(match => int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture));
        Assert.All(explicitWidths, width => Assert.InRange(width, 0, 520));
    }

    [Theory]
    [InlineData("FpsView.xaml")]
    [InlineData("OptimizeView.xaml")]
    [InlineData("FindingsView.xaml")]
    [InlineData("GuideView.xaml")]
    public void PrimaryViewsExposeAutomationNames(string fileName)
    {
        Assert.Contains("AutomationProperties.Name=", ReadView(fileName), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DiagnoseView.xaml")]
    [InlineData("ScanView.xaml")]
    [InlineData("FindingsView.xaml")]
    [InlineData("GuideView.xaml")]
    public void DiagnosticJourneyUsesPersistentAccessibleStepIndicator(string fileName)
    {
        var xaml = ReadView(fileName);

        Assert.Contains("<controls:JourneyStepIndicator", xaml, StringComparison.Ordinal);
        Assert.Contains("CurrentStep=", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void FindingsExposeHonestActionMetadataAndOptionalAdvice()
    {
        var xaml = ReadView("FindingsView.xaml");

        Assert.Contains("Explanation", xaml, StringComparison.Ordinal);
        Assert.Contains("Evidence", xaml, StringComparison.Ordinal);
        Assert.Contains("Recommended fix", xaml, StringComparison.Ordinal);
        Assert.Contains("Action state", xaml, StringComparison.Ordinal);
        Assert.Contains("Admin requirement", xaml, StringComparison.Ordinal);
        Assert.Contains("Verification status", xaml, StringComparison.Ordinal);
        Assert.Contains("Open optional AI guidance", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardRecommendationProvidesMatchingQuickScanAction()
    {
        var homeView = ReadView("HomeView.xaml");

        Assert.Contains("Content=\"Run quick scan\"", homeView, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding StartScanCommand}\"", homeView, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"{Binding Scenarios[3]}\"", homeView, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardQuickAccessUsesViewModelCommands()
    {
        var homeView = ReadView("HomeView.xaml");

        Assert.DoesNotContain("AncestorType=Window", homeView, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding OpenMonitorCommand}\"", homeView, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding OpenGamePerformanceCommand}\"", homeView, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding OpenOptimizeCommand}\"", homeView, StringComparison.Ordinal);
    }

    [Fact]
    public void SidebarDestinationsHaveAutomationNamesAndKeyboardFocusStyle()
    {
        var mainWindow = File.ReadAllText(Path.Combine(RepositoryRoot, "PatchGuard", "MainWindow.xaml"));
        var styles = File.ReadAllText(Path.Combine(RepositoryRoot, "PatchGuard", "Resources", "Styles.xaml"));

        Assert.Equal(7, AutomationNameRegex().Count(mainWindow));
        Assert.Contains("FocusVisualStyle", styles, StringComparison.Ordinal);
        Assert.Contains("KeyboardNavigation.DirectionalNavigation", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryButtonNormalAndHoverColorsMeetWcagAa()
    {
        var styles = File.ReadAllText(Path.Combine(RepositoryRoot, "PatchGuard", "Resources", "Styles.xaml"));
        var foreground = ReadBrush(styles, "PrimaryButtonForegroundBrush");
        var normal = ReadBrush(styles, "PrimaryButtonBrush");
        var hover = ReadBrush(styles, "PrimaryButtonHoverBrush");

        Assert.True(ContrastRatio(foreground, normal) >= 4.5);
        Assert.True(ContrastRatio(foreground, hover) >= 4.5);
    }

    private static string ReadView(string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "PatchGuard", "Views", fileName));

    private static string ReadBrush(string xaml, string key)
    {
        var match = Regex.Match(
            xaml,
            "<SolidColorBrush\\s+x:Key=\"" + Regex.Escape(key)
            + "\"\\s+Color=\"(?<color>#[0-9A-Fa-f]{6})\"\\s*/>");
        Assert.True(match.Success, $"Brush '{key}' was not found.");
        return match.Groups["color"].Value;
    }

    private static double ContrastRatio(string first, string second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05)
            / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var red = int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber) / 255d;
        var green = int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber) / 255d;
        var blue = int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber) / 255d;
        return 0.2126 * Linearize(red) + 0.7152 * Linearize(green) + 0.0722 * Linearize(blue);
    }

    private static double Linearize(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "PatchGuard"))
                && Directory.Exists(Path.Combine(directory.FullName, "PatchGuard.Tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the PatchGuard repository root.");
    }

    [GeneratedRegex("AutomationProperties\\.Name=")]
    private static partial Regex AutomationNameRegex();

    [GeneratedRegex("(?<![:A-Za-z])Width=\"(?<width>[0-9]+)\"")]
    private static partial Regex ExplicitWidthRegex();
}
