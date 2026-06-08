using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PatchGuard;

public partial class BenchmarkWindow : Window
{
    private readonly List<UIElement> _shapes = [];
    private int _frameCount;
    private readonly Stopwatch _sw = new();
    private EventHandler? _renderHandler;

    public BenchmarkWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public Task<double> RunAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<double>();

        void OnTick(object? s, EventArgs e)
        {
            _frameCount++;
            if (_sw.Elapsed < duration)
            {
                return;
            }

            CompositionTarget.Rendering -= OnTick;
            var fps = _frameCount / _sw.Elapsed.TotalSeconds;
            tcs.TrySetResult(Math.Round(fps, 1));
            Close();
        }

        Closed += (_, _) => tcs.TrySetResult(0);

        cancellationToken.Register(() =>
        {
            Dispatcher.Invoke(() =>
            {
                CompositionTarget.Rendering -= OnTick;
                Close();
            });
            tcs.TrySetCanceled(cancellationToken);
        });

        _renderHandler = OnTick;
        CompositionTarget.Rendering += OnTick;
        _sw.Start();

        return tcs.Task;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var random = new Random(42);
        for (var i = 0; i < 40; i++)
        {
            var ellipse = new Ellipse
            {
                Width = random.Next(20, 60),
                Height = random.Next(20, 60),
                Fill = new SolidColorBrush(Color.FromRgb(
                    (byte)random.Next(80, 255),
                    (byte)random.Next(40, 120),
                    (byte)random.Next(60, 180)))
            };
            Canvas.SetLeft(ellipse, random.Next(0, 400));
            Canvas.SetTop(ellipse, random.Next(0, 260));
            RenderCanvas.Children.Add(ellipse);
            _shapes.Add(ellipse);
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            foreach (UIElement shape in _shapes)
            {
                var left = Canvas.GetLeft(shape) + 2;
                if (left > 460)
                {
                    left = 0;
                }

                Canvas.SetLeft(shape, left);
            }
        };
        timer.Start();
    }
}
