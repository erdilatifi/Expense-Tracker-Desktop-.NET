using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FinanceTracker.Services;

/// <summary>
/// Presents application errors in the app's dark visual language instead of the default WPF crash dialog.
/// </summary>
public static class ErrorDialogService
{
    public static void Show(Exception exception)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var details = exception.ToString();
            var window = new Window
            {
                Title = "FinanceTracker Error",
                Width = 520,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = BrushFrom("#0D0F14"),
                Foreground = BrushFrom("#F1F5F9"),
                Content = CreateContent(exception.Message, details)
            };

            if (Application.Current.MainWindow is { IsVisible: true } owner)
            {
                window.Owner = owner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            window.ShowDialog();
        });
    }

    private static UIElement CreateContent(string message, string details)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(22)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Something went wrong",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFrom("#F1F5F9")
        });

        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = BrushFrom("#CBD5E1"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 18)
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var copyButton = new Button
        {
            Content = "Copy details",
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 10, 0),
            Background = BrushFrom("#1E2130"),
            BorderBrush = BrushFrom("#1E293B"),
            Foreground = BrushFrom("#F1F5F9")
        };
        copyButton.Click += (_, _) => Clipboard.SetText(details);

        var reportButton = new Button
        {
            Content = "Report issue",
            Padding = new Thickness(14, 8, 14, 8),
            Background = BrushFrom("#7C3AED"),
            BorderBrush = BrushFrom("#7C3AED"),
            Foreground = Brushes.White
        };
        reportButton.Click += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/") { UseShellExecute = true });

        actions.Children.Add(copyButton);
        actions.Children.Add(reportButton);
        panel.Children.Add(actions);

        return new Border
        {
            Background = BrushFrom("#151820"),
            BorderBrush = BrushFrom("#2D3748"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = panel
        };
    }

    private static SolidColorBrush BrushFrom(string colorHex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!;
        brush.Freeze();
        return brush;
    }
}
