using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace FinanceTracker.Controls;

/// <summary>
/// Displays an animated loading skeleton rectangle with a left-to-right shimmer.
/// </summary>
public sealed class ShimmerRectangle : UserControl
{
    private readonly GradientStop highlightStop;

    public ShimmerRectangle()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            GradientStops =
            {
                new GradientStop((Color)ColorConverter.ConvertFromString("#1E2130")!, 0),
                new GradientStop((Color)ColorConverter.ConvertFromString("#2A3045")!, 0),
                new GradientStop((Color)ColorConverter.ConvertFromString("#1E2130")!, 1)
            }
        };

        highlightStop = brush.GradientStops[1];

        Content = new Rectangle
        {
            RadiusX = 10,
            RadiusY = 10,
            Fill = brush
        };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromSeconds(1.2),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        highlightStop.BeginAnimation(GradientStop.OffsetProperty, animation);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        highlightStop.BeginAnimation(GradientStop.OffsetProperty, null);
    }
}
