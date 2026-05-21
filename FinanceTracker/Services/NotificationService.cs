using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FinanceTracker.Services;

/// <summary>
/// Stores recent user-facing notifications and displays compact toast alerts.
/// </summary>
public sealed class NotificationService : INotifyPropertyChanged
{
    private readonly List<NotificationItem> notifications = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<NotificationItem> Notifications => notifications;

    public int NotificationCount => notifications.Count;

    public void Add(string title, NotificationKind kind)
    {
        var notification = new NotificationItem(title, kind, DateTimeOffset.Now);
        notifications.Insert(0, notification);
        if (notifications.Count > 20)
        {
            notifications.RemoveRange(20, notifications.Count - 20);
        }

        OnPropertyChanged(nameof(Notifications));
        OnPropertyChanged(nameof(NotificationCount));
        ShowToast(notification);
    }

    public void ClearAll()
    {
        notifications.Clear();
        OnPropertyChanged(nameof(Notifications));
        OnPropertyChanged(nameof(NotificationCount));
    }

    private static void ShowToast(NotificationItem notification)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var window = Application.Current.MainWindow;
            if (window is null)
            {
                return;
            }

            var popup = new Popup
            {
                AllowsTransparency = true,
                PlacementTarget = window,
                Placement = PlacementMode.Relative,
                HorizontalOffset = Math.Max(16, window.ActualWidth - 360),
                VerticalOffset = Math.Max(16, window.ActualHeight - 96),
                StaysOpen = true
            };

            var translate = new TranslateTransform(44, 0);
            var card = new Border
            {
                Width = 330,
                Background = BrushFrom("#1E2130"),
                BorderBrush = BrushFrom(notification.AccentHex),
                BorderThickness = new Thickness(3, 1, 1, 1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Opacity = 0,
                RenderTransform = translate,
                Child = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(28) },
                        new ColumnDefinition()
                    },
                    Children =
                    {
                        CreateIcon(notification),
                        CreateMessage(notification.Title)
                    }
                }
            };

            popup.Child = card;
            popup.IsOpen = true;

            var storyboard = new Storyboard();
            var slide = new DoubleAnimation(44, 0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
            Storyboard.SetTarget(slide, card);
            Storyboard.SetTargetProperty(slide, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
            Storyboard.SetTarget(fade, card);
            Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(slide);
            storyboard.Children.Add(fade);
            storyboard.Begin();

            _ = AutoDismissAsync(popup, card);
        });
    }

    private static TextBlock CreateIcon(NotificationItem notification)
    {
        var icon = new TextBlock
        {
            Text = notification.Icon,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            Foreground = BrushFrom(notification.AccentHex),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(icon, 0);
        return icon;
    }

    private static TextBlock CreateMessage(string message)
    {
        var text = new TextBlock
        {
            Text = message,
            Foreground = BrushFrom("#F1F5F9"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        return text;
    }

    private static async Task AutoDismissAsync(Popup popup, UIElement card)
    {
        await Task.Delay(TimeSpan.FromSeconds(4));

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            fade.Completed += (_, _) => popup.IsOpen = false;
            card.BeginAnimation(UIElement.OpacityProperty, fade);
        });
    }

    private static SolidColorBrush BrushFrom(string colorHex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!;
        brush.Freeze();
        return brush;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Classifies notifications for icon and accent styling.
/// </summary>
public enum NotificationKind
{
    BudgetWarning,
    RecurringTransactions,
    ImportComplete,
    General
}

/// <summary>
/// Represents a notification displayed in the notification center.
/// </summary>
public sealed record NotificationItem(string Title, NotificationKind Kind, DateTimeOffset CreatedAt)
{
    public string Icon => Kind switch
    {
        NotificationKind.BudgetWarning => "\uE7BA",
        NotificationKind.RecurringTransactions => "\uE823",
        NotificationKind.ImportComplete => "\uE896",
        _ => "\uE7F4"
    };

    public string AccentHex => Kind switch
    {
        NotificationKind.BudgetWarning => "#8B5CF6",
        NotificationKind.RecurringTransactions => "#7C3AED",
        NotificationKind.ImportComplete => "#A78BFA",
        _ => "#C4B5FD"
    };

    public string TimeAgo
    {
        get
        {
            var elapsed = DateTimeOffset.Now - CreatedAt;
            if (elapsed.TotalMinutes < 1)
            {
                return "just now";
            }

            if (elapsed.TotalHours < 1)
            {
                return $"{(int)elapsed.TotalMinutes} minutes ago";
            }

            if (elapsed.TotalDays < 1)
            {
                return $"{(int)elapsed.TotalHours} hours ago";
            }

            return $"{(int)elapsed.TotalDays} days ago";
        }
    }
}
