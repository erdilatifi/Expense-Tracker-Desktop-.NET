using FinanceTracker.Data;
using FinanceTracker.Services;

namespace FinanceTracker.ViewModels;

/// <summary>
/// Coordinates top-level navigation, global commands, and shared notification state.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly Dictionary<string, ObservableObject> pages;

    [ObservableProperty]
    private ObservableObject currentView;

    [ObservableProperty]
    private string currentPage = "Dashboard";

    public NotificationService NotificationService { get; }

    public MainViewModel()
        : this(null, null, null, null)
    {
    }

    public MainViewModel(
        FinanceDbContext? dbContext,
        BudgetAlertService? budgetAlertService = null,
        ReportService? reportService = null,
        NotificationService? notificationService = null,
        AppSettingsService? appSettings = null)
    {
        var settings = appSettings ?? new AppSettingsService();
        NotificationService = notificationService ?? new NotificationService();
        pages = new Dictionary<string, ObservableObject>
        {
            ["Dashboard"] = dbContext is null ? new DashboardViewModel() : new DashboardViewModel(dbContext, settings),
            ["Transactions"] = dbContext is null ? new TransactionsViewModel() : new TransactionsViewModel(dbContext, budgetAlertService, settings),
            ["Recurring"] = dbContext is null ? new RecurringViewModel() : new RecurringViewModel(dbContext, settings),
            ["Budgets"] = dbContext is null ? new BudgetViewModel() : new BudgetViewModel(dbContext, settings),
            ["Reports"] = dbContext is null || reportService is null ? new ReportsViewModel() : new ReportsViewModel(dbContext, reportService, settings),
            ["Accounts"] = dbContext is null ? new AccountsViewModel() : new AccountsViewModel(dbContext, settings),
            ["Settings"] = dbContext is null ? new SettingsViewModel(null, NotificationService, settings) : new SettingsViewModel(dbContext, NotificationService, settings)
        };

        currentView = pages["Dashboard"];
        NavigateTo("Dashboard");
    }

    public SettingsViewModel Settings => (SettingsViewModel)pages["Settings"];

    [RelayCommand]
    private void NavigateTo(string page)
    {
        if (pages.TryGetValue(page, out var viewModel))
        {
            CurrentPage = page;
            CurrentView = viewModel;

            if (viewModel is DashboardViewModel dashboard) _ = dashboard.LoadAsync();
            else if (viewModel is TransactionsViewModel transactions) _ = transactions.LoadAsync();
            else if (viewModel is RecurringViewModel recurring) _ = recurring.LoadAsync();
            else if (viewModel is BudgetViewModel budget) _ = budget.LoadAsync();
            else if (viewModel is ReportsViewModel reports) _ = reports.LoadAsync();
            else if (viewModel is AccountsViewModel accounts) _ = accounts.LoadAsync();
            else if (viewModel is SettingsViewModel settings) _ = settings.LoadRowsAsync();
        }
    }

    public void OpenAddTransactionDialog()
    {
        NavigateTo("Transactions");
        if (pages["Transactions"] is TransactionsViewModel transactionsViewModel)
        {
            transactionsViewModel.OpenDialogCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void OpenAddTransaction()
    {
        OpenAddTransactionDialog();
    }

    public void ExportCurrentViewCsv()
    {
        switch (CurrentView)
        {
            case TransactionsViewModel transactionsViewModel:
                transactionsViewModel.ExportCsvCommand.Execute(null);
                break;
            case ReportsViewModel reportsViewModel:
                reportsViewModel.ExportCsvCommand.Execute(null);
                break;
            default:
                NotificationService.Add("CSV export is not available for this view", NotificationKind.General);
                break;
        }
    }

    [RelayCommand]
    private void ExportCurrentView()
    {
        ExportCurrentViewCsv();
    }

    public void CloseOpenOverlay()
    {
        if (pages["Transactions"] is TransactionsViewModel { IsDialogOpen: true } transactionsViewModel)
        {
            transactionsViewModel.CloseDialogCommand.Execute(null);
        }
    }
}
