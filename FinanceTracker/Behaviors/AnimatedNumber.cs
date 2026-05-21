using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FinanceTracker.Behaviors;

/// <summary>
/// Animates numeric text from zero to the bound value when a TextBlock receives new KPI data.
/// </summary>
public static class AnimatedNumber
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "Value",
        typeof(double),
        typeof(AnimatedNumber),
        new PropertyMetadata(0d, OnValueChanged));

    public static readonly DependencyProperty FormatProperty = DependencyProperty.RegisterAttached(
        "Format",
        typeof(string),
        typeof(AnimatedNumber),
        new PropertyMetadata("N0"));

    private static readonly DependencyProperty AnimationStateProperty = DependencyProperty.RegisterAttached(
        "AnimationState",
        typeof(AnimationState),
        typeof(AnimatedNumber),
        new PropertyMetadata(null));

    public static void SetValue(DependencyObject element, double value) => element.SetValue(ValueProperty, value);

    public static double GetValue(DependencyObject element) => (double)element.GetValue(ValueProperty);

    public static void SetFormat(DependencyObject element, string value) => element.SetValue(FormatProperty, value);

    public static string GetFormat(DependencyObject element) => (string)element.GetValue(FormatProperty);

    private static void OnValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not TextBlock textBlock)
        {
            return;
        }

        var target = (double)e.NewValue;
        var state = (AnimationState?)textBlock.GetValue(AnimationStateProperty);
        if (state is not null)
        {
            CompositionTarget.Rendering -= state.OnRendering;
        }

        state = new AnimationState(textBlock, target);
        textBlock.SetValue(AnimationStateProperty, state);
        CompositionTarget.Rendering += state.OnRendering;
    }

    private sealed class AnimationState
    {
        private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(800);
        private readonly DateTimeOffset startTime = DateTimeOffset.Now;
        private readonly TextBlock textBlock;
        private readonly double target;

        public AnimationState(TextBlock textBlock, double target)
        {
            this.textBlock = textBlock;
            this.target = target;
        }

        public void OnRendering(object? sender, EventArgs e)
        {
            var progress = Math.Clamp((DateTimeOffset.Now - startTime).TotalMilliseconds / Duration.TotalMilliseconds, 0d, 1d);
            var eased = 1 - Math.Pow(1 - progress, 3);
            var current = target * eased;
            textBlock.Text = FormatValue(current, GetFormat(textBlock));

            if (progress >= 1)
            {
                textBlock.Text = FormatValue(target, GetFormat(textBlock));
                CompositionTarget.Rendering -= OnRendering;
                textBlock.ClearValue(AnimationStateProperty);
            }
        }

        private static string FormatValue(double value, string format)
        {
            return format switch
            {
                "C2" => value.ToString("C2", CultureInfo.GetCultureInfo("en-US")),
                "P0" => $"{value:0}%",
                _ => value.ToString(format, CultureInfo.CurrentCulture)
            };
        }
    }
}
