using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using PatchGuard.Controls;
using PatchGuard.Views;

namespace PatchGuard.Tests;

public sealed class WpfSmokeTests
{
    [Theory]
    [InlineData(typeof(HomeView))]
    [InlineData(typeof(DiagnoseView))]
    [InlineData(typeof(ScanView))]
    [InlineData(typeof(MonitorView))]
    [InlineData(typeof(FpsView))]
    [InlineData(typeof(OptimizeView))]
    [InlineData(typeof(FindingsView))]
    [InlineData(typeof(GuideView))]
    public void PrimaryViewFitsInsideMinimumWidthWindow(Type viewType)
    {
        StaTestHost.Run(() =>
        {
            var view = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(viewType));
            var window = CreateMinimumWidthWindow();

            try
            {
                window.Show();
                var contentHost = FindVisualDescendants<ContentControl>(window)
                    .Single(control => control.GetType() == typeof(ContentControl));
                BindingOperations.ClearBinding(contentHost, ContentControl.ContentProperty);
                contentHost.Content = view;
                window.UpdateLayout();

                Assert.True(contentHost.ActualWidth > 0);
                Assert.InRange(view.ActualWidth, 0, contentHost.ActualWidth + 0.5);
                Assert.InRange(view.DesiredSize.Width, 0, contentHost.ActualWidth + 0.5);

                var visibleScrollViewers = FindVisualDescendants<ScrollViewer>(view)
                    .Where(scrollViewer => scrollViewer.IsVisible);
                Assert.All(
                    visibleScrollViewers,
                    scrollViewer => Assert.InRange(
                        scrollViewer.ExtentWidth,
                        0,
                        scrollViewer.ViewportWidth + 0.5));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(typeof(DiagnoseView), 1)]
    [InlineData(typeof(ScanView), 2)]
    [InlineData(typeof(FindingsView), 3)]
    [InlineData(typeof(GuideView), 4)]
    public void DiagnosticJourneyIndicatorRendersWithAccessibleCurrentStep(Type viewType, int expectedStep)
    {
        StaTestHost.Run(() =>
        {
            var view = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(viewType));
            var window = CreateControlWindow(view);

            try
            {
                window.Show();
                window.UpdateLayout();

                var indicator = Assert.Single(FindVisualDescendants<JourneyStepIndicator>(view));
                Assert.Equal(expectedStep, indicator.CurrentStep);
                Assert.Equal("Diagnostic journey progress", AutomationProperties.GetName(indicator));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void SidebarItemsExposeAutomationAndKeyboardFocusBehavior()
    {
        StaTestHost.Run(() =>
        {
            var window = CreateMinimumWidthWindow();

            try
            {
                window.Show();
                window.UpdateLayout();
                var items = FindVisualDescendants<RadioButton>(window).ToList();

                Assert.Equal(7, items.Count);
                Assert.All(items, item =>
                {
                    Assert.True(item.Focusable);
                    Assert.True(item.IsTabStop);
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(item)));
                    Assert.NotNull(item.FocusVisualStyle);
                });

                Assert.True(items[0].Focus());
                Assert.Same(items[0], Keyboard.FocusedElement);
                Assert.True(items[0].MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                Assert.NotSame(items[0], Keyboard.FocusedElement);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ScrollBarsSupportBothOrientationsPagingAndThumbDragging()
    {
        StaTestHost.Run(() =>
        {
            var vertical = new ScrollBar
            {
                Orientation = Orientation.Vertical,
                Minimum = 0,
                Maximum = 100,
                Value = 50,
                ViewportSize = 10,
                Height = 180
            };
            var horizontal = new ScrollBar
            {
                Orientation = Orientation.Horizontal,
                Minimum = 0,
                Maximum = 100,
                Value = 50,
                ViewportSize = 10,
                Width = 180
            };
            var panel = new StackPanel();
            panel.Children.Add(vertical);
            panel.Children.Add(horizontal);
            var window = CreateControlWindow(panel);

            try
            {
                window.Show();
                window.UpdateLayout();

                var verticalTrack = Assert.IsType<Track>(
                    vertical.Template.FindName("PART_Track", vertical));
                var horizontalTrack = Assert.IsType<Track>(
                    horizontal.Template.FindName("PART_Track", horizontal));
                Assert.Equal(vertical.Minimum, verticalTrack.Minimum);
                Assert.Equal(vertical.Maximum, verticalTrack.Maximum);
                Assert.Equal(vertical.Value, verticalTrack.Value);
                Assert.Equal(horizontal.Minimum, horizontalTrack.Minimum);
                Assert.Equal(horizontal.Maximum, horizontalTrack.Maximum);
                Assert.Equal(horizontal.Value, horizontalTrack.Value);
                Assert.True(vertical.ActualHeight > vertical.ActualWidth);
                Assert.True(horizontal.ActualWidth > horizontal.ActualHeight);
                Assert.Same(
                    Application.Current.Resources["PanelBackgroundBrush"],
                    vertical.Background);
                Assert.Same(
                    Application.Current.Resources["TextMutedBrush"],
                    horizontal.Foreground);

                ScrollBar.PageDownCommand.Execute(null, vertical);
                Assert.True(vertical.Value > 50);
                ScrollBar.PageRightCommand.Execute(null, horizontal);
                Assert.True(horizontal.Value > 50);

                vertical.Value = 50;
                horizontal.Value = 50;
                RaiseDrag(verticalTrack.Thumb, 0, 20);
                RaiseDrag(horizontalTrack.Thumb, 20, 0);
                Assert.NotEqual(50, vertical.Value);
                Assert.NotEqual(50, horizontal.Value);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ComboBoxPopupSupportsFocusSelectionAndAutomation()
    {
        StaTestHost.Run(() =>
        {
            var comboBox = new ComboBox
            {
                Width = 220,
                ItemsSource = new[] { "One", "Two", "Three" },
                SelectedIndex = 0
            };
            AutomationProperties.SetName(comboBox, "Test options");
            var window = CreateControlWindow(comboBox);

            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.True(window.Activate());
                Assert.True(comboBox.Focus());
                Assert.Same(comboBox, Keyboard.FocusedElement);

                var peer = new ComboBoxAutomationPeer(comboBox);
                var expandProvider = Assert.IsAssignableFrom<IExpandCollapseProvider>(
                    peer.GetPattern(PatternInterface.ExpandCollapse));
                expandProvider.Expand();
                window.UpdateLayout();
                var popup = Assert.IsType<Popup>(
                    comboBox.Template.FindName("PART_Popup", comboBox));
                Assert.True(popup.IsOpen);

                var itemPeers = Assert.IsAssignableFrom<IReadOnlyList<AutomationPeer>>(
                    peer.GetChildren());
                var selectionProvider = Assert.IsAssignableFrom<ISelectionItemProvider>(
                    itemPeers[1].GetPattern(PatternInterface.SelectionItem));
                selectionProvider.Select();
                Assert.Equal(1, comboBox.SelectedIndex);
                expandProvider.Collapse();
                Assert.False(comboBox.IsDropDownOpen);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static MainWindow CreateMinimumWidthWindow() =>
        new()
        {
            Width = 860,
            Height = 800,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

    private static Window CreateControlWindow(object content) =>
        new()
        {
            Content = content,
            Width = 320,
            Height = 320,
            Left = -10000,
            Top = -10000,
            ShowActivated = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

    private static void RaiseDrag(Thumb thumb, double horizontalChange, double verticalChange)
    {
        var args = new DragDeltaEventArgs(horizontalChange, verticalChange)
        {
            RoutedEvent = Thumb.DragDeltaEvent,
            Source = thumb
        };
        thumb.RaiseEvent(args);
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
