using System.Windows;

namespace FinanceTracker.Services;

/// <summary>
/// Simple modal prompts for confirmation text entry.
/// </summary>
public static class InputDialogService
{
    public static string? Prompt(string title, string message, string defaultValue = "")
    {
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = System.Windows.Media.Brushes.White
        });

        var input = new System.Windows.Controls.TextBox
        {
            Text = defaultValue,
            MinWidth = 280,
            Height = 32
        };
        panel.Children.Add(input);

        var window = new Window
        {
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#151820")!,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        string? result = null;
        var ok = new System.Windows.Controls.Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) =>
        {
            result = input.Text;
            window.DialogResult = true;
            window.Close();
        };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", Width = 80, IsCancel = true };
        cancel.Click += (_, _) => window.Close();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        input.Focus();
        return window.ShowDialog() == true ? result : null;
    }
}
