using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FinanceTracker.Data;
using FinanceTracker.Models;

namespace FinanceTracker.Services;

/// <summary>
/// Evaluates category budgets and creates user alerts when spending approaches or exceeds limits.
/// </summary>
public class BudgetAlertService
{
    private static readonly CultureInfo CurrencyCulture = CultureInfo.GetCultureInfo("en-US");
    private readonly NotificationService? notificationService;

    public BudgetAlertService()
    {
    }

    public BudgetAlertService(NotificationService notificationService)
    {
        this.notificationService = notificationService;
    }

    public async Task CheckBudgetsAsync(FinanceDbContext dbContext, int categoryId)
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var nextMonth = monthStart.AddMonths(1);

        var budget = await dbContext.Budgets
            .Include(item => item.Category)
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.CategoryId == categoryId && item.Month == today.Month && item.Year == today.Year);

        if (budget is null || budget.MonthlyLimit <= 0m)
        {
            return;
        }

        var spent = (decimal)await dbContext.Transactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.CategoryId == categoryId &&
                transaction.Type == TransactionType.Expense &&
                transaction.Date >= monthStart &&
                transaction.Date < nextMonth)
            .SumAsync(transaction => (double)transaction.Amount);

        var ratio = spent / budget.MonthlyLimit;
        if (ratio < 0.80m)
        {
            return;
        }

        var categoryName = budget.Category?.Name ?? "Budget";
        var exceeded = spent > budget.MonthlyLimit;
        var message = exceeded
            ? $"{categoryName} exceeded by {(spent - budget.MonthlyLimit).ToString("C0", CurrencyCulture)}"
            : $"{categoryName} reached {ratio:P0} of its monthly budget";

        if (notificationService is not null)
        {
            notificationService.Add(message, NotificationKind.BudgetWarning);
            return;
        }

        ShowToast(message, exceeded);
    }

    private static void ShowToast(string message, bool exceeded)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var window = Application.Current.MainWindow;
            if (window is null)
            {
                return;
            }

            var accent = exceeded ? "#A78BFA" : "#8B5CF6";
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
                BorderBrush = BrushFrom("#1E293B"),
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
                        CreateIcon(accent),
                        CreateMessage(message)
                    }
                }
            };

            card.BorderBrush = BrushFrom(accent);
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

    private static TextBlock CreateIcon(string accent)
    {
        var icon = new TextBlock
        {
            Text = "\uE7BA",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            Foreground = BrushFrom(accent),
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
}
