using System.Windows;
using System.Windows.Media;
using FinanceTracker.Data;
using FinanceTracker.Services;
using FinanceTracker.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceTracker;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private ServiceProvider? serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var services = new ServiceCollection();
        ConfigureServices(services);

        serviceProvider = services.BuildServiceProvider();

        var settings = serviceProvider.GetRequiredService<AppSettingsService>();
        ApplyAccentColor(settings.AccentColor);

        var databaseService = serviceProvider.GetRequiredService<DatabaseService>();
        await databaseService.InitializeAsync();

        var recurringService = serviceProvider.GetRequiredService<RecurringService>();
        await recurringService.ProcessDueRulesAsync();

        var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    public static void ApplyAccentColor(string hexColor)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hexColor)) return;
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            var dimColor = Color.FromArgb(40, color.R, color.G, color.B);
            var hoverColor = Color.FromArgb(255,
                (byte)Math.Min(255, color.R + 35),
                (byte)Math.Min(255, color.G + 35),
                (byte)Math.Min(255, color.B + 35));
            Current.Resources["AccentPurpleBrush"] = new SolidColorBrush(color);
            Current.Resources["AccentPurpleDimBrush"] = new SolidColorBrush(dimColor);
            Current.Resources["AccentPurpleHoverBrush"] = new SolidColorBrush(hoverColor);
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<FinanceDbContext>(options => options.UseSqlite($"Data Source={FinanceDbContext.DatabasePath}"));
        services.AddScoped<DatabaseService>();
        services.AddScoped<ReportService>();
        services.AddScoped<RecurringService>();
        services.AddSingleton<AppSettingsService>(provider =>
        {
            var settings = new AppSettingsService();
            settings.Load();
            return settings;
        });
        services.AddSingleton<NotificationService>();
        services.AddSingleton<BudgetAlertService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorDialogService.Show(e.Exception);
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ErrorDialogService.Show(e.Exception);
        e.SetObserved();
    }
}
