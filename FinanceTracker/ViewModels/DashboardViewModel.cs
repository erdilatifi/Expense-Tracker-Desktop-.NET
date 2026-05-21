using System.Globalization;
using FinanceTracker.Data;
using FinanceTracker.Models;
using FinanceTracker.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace FinanceTracker.ViewModels;

/// <summary>
/// Provides dashboard KPI, chart, and recent activity data.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private static readonly string[] Palette = ["#A78BFA", "#8B5CF6", "#7C3AED", "#6D28D9", "#5B21B6", "#4C1D95"];
    private readonly FinanceDbContext? dbContext;
    private readonly AppSettingsService? appSettings;
    private CultureInfo CurrencyCulture => appSettings?.CurrencyCulture ?? CultureInfo.GetCultureInfo("en-US");

    [ObservableProperty]
    private string todayText = DateTime.Today.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private double totalBalance = 24381.50;

    [ObservableProperty]
    private string totalBalanceText = "$24,381.50";

    [ObservableProperty]
    private double monthlyIncome = 8250.00;

    [ObservableProperty]
    private string monthlyIncomeText = "$8,250.00";

    [ObservableProperty]
    private double monthlyExpenses = 5417.00;

    [ObservableProperty]
    private string monthlyExpensesText = "$5,417.00";

    [ObservableProperty]
    private double savingsRate = 34;

    [ObservableProperty]
    private string savingsRateText = "34%";

    [ObservableProperty]
    private string balanceDeltaText = string.Empty;

    [ObservableProperty]
    private string incomeDeltaText = string.Empty;

    [ObservableProperty]
    private string expensesDeltaText = string.Empty;

    [ObservableProperty]
    private string savingsDeltaText = string.Empty;

    [ObservableProperty]
    private ISeries[] netWorthSeries = [];

    [ObservableProperty]
    private Axis[] netWorthXAxes = CreateMonthAxis();

    [ObservableProperty]
    private Axis[] netWorthYAxes = CreateCurrencyAxis();

    [ObservableProperty]
    private ISeries[] savingsRateSeries = CreateSavingsRateSeries(34);

    [ObservableProperty]
    private ISeries[] cashflowSeries = CreateCashflowSeries(
        new[] { 4.2, 5.1, 4.8, 6.5, 7.2, 8.2 },
        new[] { 3.1, 3.8, 4.4, 4.2, 5.0, 5.4 });

    [ObservableProperty]
    private Axis[] cashflowXAxes = CreateMonthAxis();

    [ObservableProperty]
    private Axis[] cashflowYAxes = CreateCurrencyAxis();

    public SolidColorPaint TooltipBackgroundPaint { get; } = new(SKColor.Parse("#151820"));

    public SolidColorPaint TooltipTextPaint { get; } = new(SKColor.Parse("#F1F5F9"));

    [ObservableProperty]
    private ISeries[] categorySeries = CreateCategorySeries(
        new[]
        {
            new CategorySlice("Housing", 32, "#A78BFA"),
            new CategorySlice("Food", 22, "#8B5CF6"),
            new CategorySlice("Transport", 15, "#7C3AED"),
            new CategorySlice("Health", 12, "#6D28D9"),
            new CategorySlice("Entertainment", 11, "#5B21B6"),
            new CategorySlice("Subscriptions", 8, "#4C1D95")
        });

    public ObservableCollection<CategoryLegendItem> CategoryLegend { get; } =
    [
        new("Housing", "32%", "#A78BFA"),
        new("Food", "22%", "#8B5CF6"),
        new("Transport", "15%", "#7C3AED"),
        new("Health", "12%", "#6D28D9"),
        new("Entertainment", "11%", "#5B21B6"),
        new("Subscriptions", "8%", "#4C1D95")
    ];

    public ObservableCollection<RecentTransactionItem> RecentTransactions { get; } =
    [
        new("Rent payment", "Housing", "Today", "-$1,850.00", "#C4B5FD", "#A78BFA", "Transparent"),
        new("Salary deposit", "Income", "May 18", "+$4,125.00", "#DDD6FE", "#8B5CF6", "#33243F"),
        new("Grocery market", "Food", "May 17", "-$126.45", "#C4B5FD", "#7C3AED", "Transparent"),
        new("Streaming services", "Subscriptions", "May 15", "-$42.99", "#C4B5FD", "#6D28D9", "#33243F"),
        new("Train pass", "Transport", "May 14", "-$78.00", "#C4B5FD", "#5B21B6", "Transparent")
    ];

    public DashboardViewModel()
    {
    }

    public DashboardViewModel(FinanceDbContext dbContext, AppSettingsService? appSettings = null)
    {
        this.dbContext = dbContext;
        this.appSettings = appSettings;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (dbContext is null)
        {
            return;
        }

        IsLoading = true;

        try
        {
            var accounts = await dbContext.Accounts.AsNoTracking().ToListAsync();
            var transactions = await dbContext.Transactions
                .AsNoTracking()
                .Include(transaction => transaction.Category)
                .OrderByDescending(transaction => transaction.Date)
                .ToListAsync();

            var now = DateTime.Today;
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var nextMonthStart = monthStart.AddMonths(1);

            var currentMonth = transactions
                .Where(transaction => transaction.Date >= monthStart && transaction.Date < nextMonthStart)
                .ToList();

            var calculatedTotalBalance = appSettings is not null
                ? appSettings.SumAccountsInBaseCurrency(accounts.Select(a => (a.Balance, a.Currency)))
                : accounts.Sum(account => account.Balance);
            var calculatedMonthlyIncome = currentMonth
                .Where(transaction => transaction.Type == TransactionType.Income)
                .Sum(transaction => transaction.Amount);
            var calculatedMonthlyExpenses = currentMonth
                .Where(transaction => transaction.Type == TransactionType.Expense)
                .Sum(transaction => transaction.Amount);
            var calculatedSavingsRate = calculatedMonthlyIncome == 0m ? 0m : Math.Max(0m, (calculatedMonthlyIncome - calculatedMonthlyExpenses) / calculatedMonthlyIncome * 100m);

            TotalBalance = (double)calculatedTotalBalance;
            MonthlyIncome = (double)calculatedMonthlyIncome;
            MonthlyExpenses = (double)calculatedMonthlyExpenses;
            SavingsRate = (double)Math.Round(calculatedSavingsRate);

            TotalBalanceText = FormatMoney(calculatedTotalBalance);
            MonthlyIncomeText = FormatMoney(calculatedMonthlyIncome);
            MonthlyExpensesText = FormatMoney(calculatedMonthlyExpenses);
            SavingsRateText = $"{Math.Round(calculatedSavingsRate):0}%";
            SavingsRateSeries = CreateSavingsRateSeries((double)Math.Clamp(calculatedSavingsRate, 0m, 100m));

            var lastMonthStart = monthStart.AddMonths(-1);
            var lastMonth = transactions
                .Where(t => t.Date >= lastMonthStart && t.Date < monthStart)
                .ToList();

            var previousBalance = accounts.Sum(a => a.OpeningBalance) + transactions
                .Where(t => t.Date < monthStart)
                .Sum(t => t.Type switch
                {
                    TransactionType.Income => t.Amount,
                    TransactionType.Expense => -t.Amount,
                    _ => 0m
                });
            BalanceDeltaText = FormatMomDelta(calculatedTotalBalance, previousBalance);
            IncomeDeltaText = FormatMomDelta(
                calculatedMonthlyIncome,
                lastMonth.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount));
            ExpensesDeltaText = FormatMomDelta(
                calculatedMonthlyExpenses,
                lastMonth.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount),
                invertGood: true);
            var lastSavings = lastMonth.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount)
                - lastMonth.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
            var currentSavings = calculatedMonthlyIncome - calculatedMonthlyExpenses;
            SavingsDeltaText = FormatMomDelta(currentSavings, lastSavings);

            UpdateCashflowSeries(transactions, monthStart);
            UpdateNetWorthSeries(transactions, accounts, monthStart);
            UpdateCategorySeries(currentMonth);
            UpdateRecentTransactions(transactions.Take(5).ToList());
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateNetWorthSeries(IReadOnlyCollection<Transaction> transactions, IReadOnlyCollection<Account> accounts, DateTime monthStart)
    {
        var opening = accounts.Sum(a => a.OpeningBalance);
        var starts = Enumerable.Range(0, 6).Select(offset => monthStart.AddMonths(offset - 5)).ToArray();
        var values = starts.Select(start =>
        {
            var end = start.AddMonths(1);
            var delta = transactions
                .Where(t => t.Date < end)
                .Sum(t => t.Type switch
                {
                    TransactionType.Income => t.Amount,
                    TransactionType.Expense => -t.Amount,
                    TransactionType.Transfer => 0m,
                    _ => 0m
                });
            return (double)(opening + delta) / 1000d;
        }).ToArray();

        NetWorthSeries =
        [
            new LineSeries<double>
            {
                Name = "Net worth",
                Values = values,
                GeometrySize = 0,
                LineSmoothness = 0.8,
                Stroke = new SolidColorPaint(SKColor.Parse("#C4B5FD"), 2),
                Fill = new LinearGradientPaint(
                    [new SKColor(196, 181, 253, 90), new SKColor(196, 181, 253, 4)],
                    new SKPoint(0, 0),
                    new SKPoint(0, 1))
            }
        ];
        NetWorthXAxes = CreateMonthAxis(starts.Select(s => s.ToString("MMM", CultureInfo.CurrentCulture)).ToArray());
    }

    private string FormatMomDelta(decimal current, decimal previous, bool invertGood = false)
    {
        if (previous == 0m)
        {
            return current == 0m ? "— vs last month" : "↑ new vs last month";
        }

        var change = (current - previous) / Math.Abs(previous) * 100m;
        var up = change >= 0m;
        var arrow = up ? "↑" : "↓";
        return $"{arrow} {Math.Abs(change):0.#}% vs last month";
    }

    private void UpdateCashflowSeries(IReadOnlyCollection<Transaction> transactions, DateTime monthStart)
    {
        var starts = Enumerable.Range(0, 6)
            .Select(offset => monthStart.AddMonths(offset - 5))
            .ToArray();

        var income = starts
            .Select(start => (double)transactions
                .Where(transaction => transaction.Date >= start && transaction.Date < start.AddMonths(1) && transaction.Type == TransactionType.Income)
                .Sum(transaction => transaction.Amount) / 1000d)
            .ToArray();

        var expenses = starts
            .Select(start => (double)transactions
                .Where(transaction => transaction.Date >= start && transaction.Date < start.AddMonths(1) && transaction.Type == TransactionType.Expense)
                .Sum(transaction => transaction.Amount) / 1000d)
            .ToArray();

        CashflowSeries = CreateCashflowSeries(income, expenses);
        CashflowXAxes = CreateMonthAxis(starts.Select(start => start.ToString("MMM", CultureInfo.CurrentCulture)).ToArray());
    }

    private void UpdateCategorySeries(IReadOnlyCollection<Transaction> currentMonth)
    {
        var slices = currentMonth
            .Where(transaction => transaction.Type == TransactionType.Expense && transaction.Category is not null)
            .GroupBy(transaction => transaction.Category!)
            .Select(group => new CategorySlice(group.Key.Name, (double)group.Sum(transaction => transaction.Amount), group.Key.ColorHex))
            .OrderByDescending(slice => slice.Value)
            .Take(6)
            .ToList();

        if (slices.Count == 0)
        {
            CategorySeries = [];
            CategoryLegend.Clear();
            return;
        }

        var total = slices.Sum(slice => slice.Value);

        CategorySeries = CreateCategorySeries(slices);
        CategoryLegend.Clear();
        for (var index = 0; index < slices.Count; index++)
        {
            var slice = slices[index];
            CategoryLegend.Add(new CategoryLegendItem(slice.Name, $"{slice.Value / total:P0}", slice.Value / total * 100d, Palette[index % Palette.Length]));
        }
    }

    private void UpdateRecentTransactions(IReadOnlyList<Transaction> transactions)
    {
        RecentTransactions.Clear();

        for (var index = 0; index < transactions.Count; index++)
        {
            var transaction = transactions[index];
            var amount = GetSignedAmount(transaction);
            var (typeLabel, typeFg, typeBg) = transaction.Type switch
            {
                TransactionType.Income => ("Income", "#A78BFA", "#1E1533"),
                TransactionType.Expense => ("Expense", "#C4B5FD", "#1A1025"),
                TransactionType.Transfer => ("Transfer", "#8B8B9A", "#1C1C21"),
                _ => ("Other", "#8B8B9A", "#1C1C21")
            };

            RecentTransactions.Add(new RecentTransactionItem(
                transaction.Description,
                transaction.Category?.Name ?? "Uncategorized",
                FormatTransactionDate(transaction.Date),
                FormatSignedMoney(amount),
                amount >= 0m ? "#DDD6FE" : "#C4B5FD",
                Palette[index % Palette.Length],
                index % 2 == 0 ? "Transparent" : "#4D252A3A",
                typeLabel,
                typeFg,
                typeBg));
        }
    }

    private static ISeries[] CreateSavingsRateSeries(double savingsRate)
    {
        var clamped = Math.Clamp(savingsRate, 0, 100);
        return
        [
            new PieSeries<double>
            {
                Values = [clamped],
                Fill = new SolidColorPaint(SKColor.Parse("#8B5CF6")),
                Stroke = null,
                InnerRadius = 21,
                HoverPushout = 0
            },
            new PieSeries<double>
            {
                Values = [100 - clamped],
                Fill = new SolidColorPaint(SKColor.Parse("#211A32")),
                Stroke = null,
                InnerRadius = 21,
                HoverPushout = 0
            }
        ];
    }

    private static ISeries[] CreateCashflowSeries(double[] income, double[] expenses)
    {
        return
        [
            new LineSeries<double>
            {
                Name = "Income",
                Values = income,
                GeometrySize = 0,
                LineSmoothness = 0.8,
                Stroke = new SolidColorPaint(SKColor.Parse("#C4B5FD"), 2),
                Fill = new LinearGradientPaint(
                    [new SKColor(196, 181, 253, 100), new SKColor(196, 181, 253, 4)],
                    new SKPoint(0, 0),
                    new SKPoint(0, 1))
            },
            new LineSeries<double>
            {
                Name = "Expenses",
                Values = expenses,
                GeometrySize = 0,
                LineSmoothness = 0.8,
                Stroke = new SolidColorPaint(SKColor.Parse("#7C3AED"), 2),
                Fill = new LinearGradientPaint(
                    [new SKColor(124, 58, 237, 80), new SKColor(124, 58, 237, 4)],
                    new SKPoint(0, 0),
                    new SKPoint(0, 1))
            }
        ];
    }

    private static Axis[] CreateMonthAxis(string[]? labels = null)
    {
        return
        [
            new Axis
            {
                Labels = labels ?? ["Jan", "Feb", "Mar", "Apr", "May", "Jun"],
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#55555F")),
                SeparatorsPaint = null,
                TextSize = 11
            }
        ];
    }

    private static Axis[] CreateCurrencyAxis()
    {
        return
        [
            new Axis
            {
                Labeler = value => $"${value:0}k",
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#55555F")),
                SeparatorsPaint = new SolidColorPaint(new SKColor(46, 46, 54, 120), 1),
                TextSize = 11,
                MinLimit = 0
            }
        ];
    }

    private static ISeries[] CreateCategorySeries(IEnumerable<CategorySlice> slices)
    {
        return slices
            .Select((slice, index) => new PieSeries<double>
            {
                Name = slice.Name,
                Values = [slice.Value],
                Fill = new SolidColorPaint(SKColor.Parse(Palette[index % Palette.Length])),
                Stroke = null,
                InnerRadius = 55,
                HoverPushout = 2
            })
            .ToArray();
    }

    private static decimal GetSignedAmount(Transaction transaction)
    {
        return transaction.Type == TransactionType.Expense ? -transaction.Amount : transaction.Amount;
    }

    private string FormatMoney(decimal amount)
    {
        return amount.ToString("C2", CurrencyCulture);
    }

    private string FormatSignedMoney(decimal amount)
    {
        var prefix = amount >= 0m ? "+" : "-";
        return $"{prefix}{Math.Abs(amount).ToString("C2", CurrencyCulture)}";
    }

    private static string FormatTransactionDate(DateTime date)
    {
        if (date.Date == DateTime.Today)
        {
            return "Today";
        }

        if (date.Date == DateTime.Today.AddDays(-1))
        {
            return "Yesterday";
        }

        return date.ToString("MMM d", CultureInfo.CurrentCulture);
    }

    private readonly record struct CategorySlice(string Name, double Value, string ColorHex);
}

/// <summary>
/// Describes a single category legend row in the dashboard chart.
/// </summary>
public sealed record CategoryLegendItem(string Name, string Percentage, string ColorHex)
{
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1];
    public double PercentValue { get; init; }

    public CategoryLegendItem(string name, string percentage, double percentValue, string colorHex)
        : this(name, percentage, colorHex)
    {
        PercentValue = percentValue;
    }
}

/// <summary>
/// Describes a recent transaction row displayed on the dashboard.
/// </summary>
public sealed record RecentTransactionItem(
    string Description,
    string CategoryName,
    string DateText,
    string AmountText,
    string AmountColor,
    string CategoryColor,
    string RowBackground,
    string TypeLabel = "Done",
    string TypeFg = "#A78BFA",
    string TypeBg = "#1E1533")
{
    public string Initial => string.IsNullOrWhiteSpace(CategoryName) ? "?" : CategoryName[..1];
}
