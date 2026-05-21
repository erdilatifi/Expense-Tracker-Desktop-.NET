using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using FinanceTracker.ViewModels;

namespace FinanceTracker;

/// <summary>
/// Hosts global navigation, page transitions, notifications, and keyboard shortcuts.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private bool isUpdatingNavigation;
    private bool isTransitioning;

    public MainWindow()
        : this(new MainViewModel())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        ContentHost.Content = viewModel.CurrentView;
        KeyDown += OnMainWindowKeyDown;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnDragHandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnNavigationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isUpdatingNavigation || NavList.SelectedItem is not ListBoxItem item || item.Tag is not string page)
        {
            return;
        }

        viewModel.NavigateToCommand.Execute(page);
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage))
        {
            SelectNavigationItem(viewModel.CurrentPage);
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.CurrentView))
        {
            await TransitionToCurrentViewAsync();
        }
    }

    private async Task TransitionToCurrentViewAsync()
    {
        if (isTransitioning)
        {
            ContentHost.Content = viewModel.CurrentView;
            return;
        }

        isTransitioning = true;
        await AnimateContentAsync(1, 0, 0, -20, 150, EasingMode.EaseIn);

        ContentHost.Content = viewModel.CurrentView;
        ContentHost.Opacity = 0;
        if (ContentHost.RenderTransform is System.Windows.Media.TranslateTransform transform)
        {
            transform.X = 20;
        }

        await AnimateContentAsync(0, 1, 20, 0, 200, EasingMode.EaseOut);
        isTransitioning = false;
    }

    private Task AnimateContentAsync(double opacityFrom, double opacityTo, double xFrom, double xTo, int durationMs, EasingMode easingMode)
    {
        var completion = new TaskCompletionSource();
        var storyboard = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var easing = new CubicEase { EasingMode = easingMode };

        var opacity = new DoubleAnimation
        {
            From = opacityFrom,
            To = opacityTo,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(opacity, ContentHost);
        Storyboard.SetTargetProperty(opacity, new PropertyPath(OpacityProperty));

        var translate = new DoubleAnimation
        {
            From = xFrom,
            To = xTo,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(translate, ContentHost);
        Storyboard.SetTargetProperty(translate, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));

        storyboard.Children.Add(opacity);
        storyboard.Children.Add(translate);
        storyboard.Completed += (_, _) => completion.TrySetResult();

        var beginStoryboard = new BeginStoryboard { Storyboard = storyboard };
        beginStoryboard.Storyboard.Begin(this);

        return completion.Task;
    }

    private void OnMainWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            NotificationPopup.IsOpen = false;
            viewModel.CloseOpenOverlay();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.N:
                viewModel.OpenAddTransactionDialog();
                e.Handled = true;
                break;
            case Key.E:
                viewModel.ExportCurrentViewCsv();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                NavigateTo("Dashboard");
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                NavigateTo("Transactions");
                e.Handled = true;
                break;
            case Key.D3:
            case Key.NumPad3:
                NavigateTo("Recurring");
                e.Handled = true;
                break;
            case Key.D4:
            case Key.NumPad4:
                NavigateTo("Budgets");
                e.Handled = true;
                break;
            case Key.D5:
            case Key.NumPad5:
                NavigateTo("Reports");
                e.Handled = true;
                break;
            case Key.D6:
            case Key.NumPad6:
                NavigateTo("Accounts");
                e.Handled = true;
                break;
            case Key.D7:
            case Key.NumPad7:
                NavigateTo("Settings");
                e.Handled = true;
                break;
        }
    }

    private void NavigateTo(string page)
    {
        viewModel.NavigateToCommand.Execute(page);
    }

    private void SelectNavigationItem(string page)
    {
        isUpdatingNavigation = true;
        foreach (var candidate in NavList.Items.OfType<ListBoxItem>())
        {
            if (candidate.Tag is string tag && tag == page)
            {
                NavList.SelectedItem = candidate;
                break;
            }
        }

        isUpdatingNavigation = false;
    }

    private void OnNotificationButtonClick(object sender, RoutedEventArgs e)
    {
        NotificationPopup.IsOpen = !NotificationPopup.IsOpen;
    }

    private void OnClearNotificationsClick(object sender, RoutedEventArgs e)
    {
        viewModel.NotificationService.ClearAll();
        NotificationPopup.IsOpen = false;
    }
}
