using System.Globalization;
using System.Windows.Media;
using FinanceTracker.Data;
using FinanceTracker.Models;
using FinanceTracker.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace FinanceTracker.ViewModels;

/// <summary>
/// Provides budget progress, spending sources, risk indicators, and budget card data.
/// </summary>
public partial class BudgetViewModel : ObservableObject
{
    private static readonly string[] Palette = ["#A78BFA", "#8B5CF6", "#7C3AED", "#6D28D9", "#5B21B6", "#4C1D95"];
    private readonly FinanceDbContext? dbContext;
    private readonly AppSettingsService? appSettings;

    [ObservableProperty]
    private string spentText = "Spent $0";

    [ObservableProperty]
    private string remainingText = "Remaining $0";

    [ObservableProperty]
    private string selectedRange = "1M";

    [ObservableProperty]
    private bool isBudgetDialogOpen;

    [ObservableProperty]
    private ISeries[] budgetSeries = [];

    [ObservableProperty]
    private Axis[] budgetXAxes = CreateDayAxis();

    [ObservableProperty]
    private Axis[] budgetYAxes = CreateCurrencyAxis();

    public SolidColorPaint TooltipBackgroundPaint { get; } = new(SKColor.Parse("#151820"));

    public SolidColorPaint TooltipTextPaint { get; } = new(SKColor.Parse("#F1F5F9"));

    public ObservableCollection<AccountSpendingSource> SpendingSources { get; } = [];

    public ObservableCollection<OverspentCategoryRow> OverspentCategories { get; } = [];

    public ObservableCollection<BudgetCardItem> BudgetCards { get; } = [];

    public ObservableCollection<BudgetEditorRow> BudgetEditorRows { get; } = [];

    private CultureInfo CurrencyCulture => appSettings?.CurrencyCulture ?? CultureInfo.GetCultureInfo("en-US");

    public BudgetViewModel()
    {
    }

    public BudgetViewModel(FinanceDbContext dbContext, AppSettingsService? appSettings = null)
    {
        this.dbContext = dbContext;
        this.appSettings = appSettings;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task SelectRangeAsync(string range)
    {
        SelectedRange = range;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task OpenBudgetDialogAsync()
    {
        if (dbContext is null)
        {
            return;
        }

        var today = DateTime.Today;
        var categories = await dbContext.Categories
            .AsNoTracking()
            .Where(c => c.Type == CategoryType.Expense)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var budgets = await dbContext.Budgets
            .Where(b => b.Month == today.Month && b.Year == today.Year)
            .ToListAsync();

        BudgetEditorRows.Clear();
        foreach (var category in categories)
        {
            var budget = budgets.FirstOrDefault(b => b.CategoryId == category.Id);
            BudgetEditorRows.Add(new BudgetEditorRow(
                category.Id,
                category.Name,
                budget?.MonthlyLimit ?? 0m,
                budget?.Id));
        }

        IsBudgetDialogOpen = true;
    }

    [RelayCommand]
    private void CloseBudgetDialog() => IsBudgetDialogOpen = false;

    [RelayCommand]
    private async Task SaveBudgetsAsync()
    {
        if (dbContext is null)
        {
            return;
        }

        var today = DateTime.Today;
        foreach (var row in BudgetEditorRows)
        {
            if (row.BudgetId.HasValue)
            {
                var existing = await dbContext.Budgets.FindAsync(row.BudgetId.Value);
                if (existing is not null)
                {
                    if (row.MonthlyLimit <= 0m)
                    {
                        dbContext.Budgets.Remove(existing);
                    }
                    else
                    {
                        existing.MonthlyLimit = row.MonthlyLimit;
                    }
                }
            }
            else if (row.MonthlyLimit > 0m)
            {
                dbContext.Budgets.Add(new Budget
                {
                    CategoryId = row.CategoryId,
                    MonthlyLimit = row.MonthlyLimit,
                    Month = today.Month,
                    Year = today.Year
                });
            }
        }

        await dbContext.SaveChangesAsync();
        IsBudgetDialogOpen = false;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task CopyLastMonthBudgetsAsync()
    {
        if (dbContext is null)
        {
            return;
        }

        var today = DateTime.Today;
        var lastMonth = today.AddMonths(-1);
        var source = await dbContext.Budgets
            .Where(b => b.Month == lastMonth.Month && b.Year == lastMonth.Year)
            .ToListAsync();

        if (source.Count == 0)
        {
            return;
        }

        var existing = await dbContext.Budgets
            .Where(b => b.Month == today.Month && b.Year == today.Year)
            .ToListAsync();

        foreach (var budget in source)
        {
            var current = existing.FirstOrDefault(b => b.CategoryId == budget.CategoryId);
            if (current is null)
            {
                dbContext.Budgets.Add(new Budget
                {
                    CategoryId = budget.CategoryId,
                    MonthlyLimit = budget.MonthlyLimit,
                    Month = today.Month,
                    Year = today.Year
                });
            }
            else
            {
                current.MonthlyLimit = budget.MonthlyLimit;
            }
        }

        await dbContext.SaveChangesAsync();
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (dbContext is null)
        {
            return;
        }

        var today = DateTime.Today;
        DateTime startDate;
        DateTime endDate = today.AddDays(1);

        switch (SelectedRange)
        {
            case "1D":
                startDate = today;
                break;
            case "1W":
                startDate = today.AddDays(-7);
                break;
            case "1Y":
                startDate = new DateTime(today.Year, 1, 1);
                endDate = startDate.AddYears(1);
                break;
            case "1M":
            default:
                startDate = new DateTime(today.Year, today.Month, 1);
                endDate = startDate.AddMonths(1);
                break;
        }

        var budgets = await dbContext.Budgets
            .AsNoTracking()
            .Include(budget => budget.Category)
            .Where(budget => budget.Month == today.Month && budget.Year == today.Year)
            .ToListAsync();

        var transactions = await dbContext.Transactions
            .AsNoTracking()
            .Include(transaction => transaction.Category)
            .Include(transaction => transaction.Account)
            .Where(transaction => transaction.Date >= startDate && transaction.Date < endDate && transaction.Type == TransactionType.Expense)
            .ToListAsync();

        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthTransactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Date >= monthStart && t.Date < monthStart.AddMonths(1) && t.Type == TransactionType.Expense)
            .ToListAsync();

        if (budgets.Count == 0 && transactions.Count == 0)
        {
            SpentText = $"Spent {FormatMoney(0m)}";
            RemainingText = $"Remaining {FormatMoney(0m)}";
            BudgetSeries = [];
            SpendingSources.Clear();
            BudgetCards.Clear();
            OverspentCategories.Clear();
            return;
        }

        var spent = transactions.Sum(transaction => transaction.Amount);
        var limit = budgets.Sum(budget => budget.MonthlyLimit);
        SpentText = $"Spent {FormatMoney(spent)}";
        RemainingText = $"Remaining {FormatMoney(Math.Max(0m, limit - spent))}";

        UpdateBudgetSeries(transactions, startDate, endDate, limit);
        UpdateSpendingSources(transactions);
        UpdateBudgetCards(budgets, monthTransactions, today);
        UpdateOverspentCategories(budgets, monthTransactions);
    }

    private void UpdateOverspentCategories(IReadOnlyCollection<Budget> budgets, IReadOnlyCollection<Transaction> transactions)
    {
        var rows = budgets
            .Where(b => b.Category is not null && b.MonthlyLimit > 0m)
            .Select(b =>
            {
                var spent = transactions.Where(t => t.CategoryId == b.CategoryId).Sum(t => t.Amount);
                var ratio = spent / b.MonthlyLimit;
                return new { b.Category!.Name, Spent = spent, b.MonthlyLimit, Ratio = ratio };
            })
            .Where(x => x.Ratio >= 0.8m)
            .OrderByDescending(x => x.Ratio)
            .Select(x =>
            {
                var risk = x.Ratio >= 1.1m ? 5 : x.Ratio >= 1m ? 4 : x.Ratio >= 0.9m ? 3 : 2;
                return CreateRisk(x.Name, risk, $"{FormatMoney(x.Spent)} / {FormatMoney(x.MonthlyLimit)}");
            })
            .ToList();

        OverspentCategories.Clear();
        foreach (var row in rows)
        {
            OverspentCategories.Add(row);
        }
    }

    private void UpdateBudgetSeries(IEnumerable<Transaction> transactions, DateTime startDate, DateTime endDate, decimal limitValue)
    {
        double[] spentValues;
        double[] limitValues;
        string[] labels;

        if (SelectedRange == "1D")
        {
            labels = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToArray();
            var hourly = new decimal[24];
            foreach (var transaction in transactions)
            {
                var hour = Math.Clamp(transaction.Date.Hour, 0, 23);
                hourly[hour] += transaction.Amount;
            }
            spentValues = new double[24];
            decimal running = 0;
            for (int i = 0; i < 24; i++)
            {
                running += hourly[i];
                spentValues[i] = (double)(running / 1000m);
            }
            limitValues = Enumerable.Range(0, 24).Select(_ => (double)(limitValue / 30m / 1000m)).ToArray();
        }
        else if (SelectedRange == "1W")
        {
            labels = Enumerable.Range(0, 7).Select(i => startDate.AddDays(i).ToString("ddd", CultureInfo.CurrentCulture)).ToArray();
            var daily = new decimal[7];
            foreach (var transaction in transactions)
            {
                var dayIndex = Math.Clamp((transaction.Date.Date - startDate.Date).Days, 0, 6);
                daily[dayIndex] += transaction.Amount;
            }
            spentValues = new double[7];
            decimal running = 0;
            for (int i = 0; i < 7; i++)
            {
                running += daily[i];
                spentValues[i] = (double)(running / 1000m);
            }
            limitValues = Enumerable.Range(0, 7).Select(_ => (double)(limitValue / 4m / 1000m)).ToArray();
        }
        else if (SelectedRange == "1Y")
        {
            labels = Enumerable.Range(0, 12).Select(i => startDate.AddMonths(i).ToString("MMM", CultureInfo.CurrentCulture)).ToArray();
            var monthly = new decimal[12];
            foreach (var transaction in transactions)
            {
                var monthIndex = Math.Clamp(((transaction.Date.Year - startDate.Year) * 12) + transaction.Date.Month - startDate.Month, 0, 11);
                monthly[monthIndex] += transaction.Amount;
            }
            spentValues = new double[12];
            decimal running = 0;
            for (int i = 0; i < 12; i++)
            {
                running += monthly[i];
                spentValues[i] = (double)(running / 1000m);
            }
            limitValues = Enumerable.Range(0, 12).Select(i => (double)(limitValue * (i + 1) / 1000m)).ToArray();
        }
        else
        {
            labels = Enumerable.Range(1, 30).Select(day => day % 5 == 0 ? day.ToString(CultureInfo.CurrentCulture) : string.Empty).ToArray();
            var daily = new decimal[30];
            foreach (var transaction in transactions)
            {
                var dayIndex = Math.Clamp((transaction.Date.Date - startDate.Date).Days, 0, 29);
                daily[dayIndex] += transaction.Amount;
            }
            spentValues = new double[30];
            decimal running = 0;
            for (int i = 0; i < 30; i++)
            {
                running += daily[i];
                spentValues[i] = (double)(running / 1000m);
            }
            limitValues = Enumerable.Range(0, 30).Select(_ => (double)(limitValue / 1000m)).ToArray();
        }

        BudgetSeries = CreateBudgetSeries(spentValues, limitValues);
        BudgetXAxes =
        [
            new Axis
            {
                Labels = labels,
                LabelsPaint = new SolidColorPaint(SKColor.Parse("#9A93AA")),
                SeparatorsPaint = null,
                TextSize = 11
            }
        ];
    }

    private void UpdateSpendingSources(IEnumerable<Transaction> transactions)
    {
        var transactionList = transactions.ToList();
        var total = transactionList.Sum(t => t.Amount);
        var rows = transactionList
            .GroupBy(transaction => transaction.Account)
            .Where(group => group.Key is not null)
            .Select(group => new
            {
                Account = group.Key!,
                Spent = group.Sum(t => t.Amount),
                Pct = total == 0m ? 0d : (double)(group.Sum(t => t.Amount) / total * 100m)
            })
            .OrderByDescending(row => row.Spent)
            .Take(4)
            .ToList();

        SpendingSources.Clear();
        foreach (var row in rows)
        {
            SpendingSources.Add(CreateSource(row.Account.Name, FormatMoney(row.Spent), row.Account.ColorHex, (double)row.Pct));
        }
    }

    private void UpdateBudgetCards(IReadOnlyCollection<Budget> budgets, IReadOnlyCollection<Transaction> transactions, DateTime today)
    {
        var daysLeft = DateTime.DaysInMonth(today.Year, today.Month) - today.Day;
        var rows = budgets
            .Where(budget => budget.Category is not null)
            .Select(budget =>
            {
                var spent = transactions.Where(transaction => transaction.CategoryId == budget.CategoryId).Sum(transaction => transaction.Amount);
                var ratio = budget.MonthlyLimit == 0m ? 0 : (double)(spent / budget.MonthlyLimit);
                var spentText = spent > budget.MonthlyLimit
                    ? $"{FormatMoney(spent)} / {FormatMoney(budget.MonthlyLimit)}"
                    : $"{FormatMoney(spent)} / {FormatMoney(budget.MonthlyLimit)}";
                return CreateBudgetCard(
                    budget.Category!.Name,
                    budget.Category.IconCode,
                    spentText,
                    ratio,
                    budget.Category.ColorHex,
                    $"{daysLeft} days left this month",
                    spent > budget.MonthlyLimit);
            })
            .ToList();

        BudgetCards.Clear();
        foreach (var row in rows)
        {
            BudgetCards.Add(row);
        }
    }

    private static ISeries[] CreateBudgetSeries(double[] spent, double[] limit) =>
    [
        new LineSeries<double>
        {
            Name = "Spent",
            Values = spent,
            GeometrySize = 0,
            LineSmoothness = 0.8,
            Stroke = new SolidColorPaint(SKColor.Parse("#C4B5FD"), 2),
            Fill = new LinearGradientPaint(
                [new SKColor(196, 181, 253, 90), new SKColor(196, 181, 253, 4)],
                new SKPoint(0, 0),
                new SKPoint(0, 1))
        },
        new LineSeries<double>
        {
            Name = "Budget limit",
            Values = limit,
            GeometrySize = 0,
            LineSmoothness = 0.3,
            Stroke = new SolidColorPaint(new SKColor(124, 58, 237, 120), 1.5f),
            Fill = null
        }
    ];

    private static Axis[] CreateDayAxis() =>
    [
        new Axis
        {
            Labels = Enumerable.Range(1, 30).Select(day => day % 5 == 0 ? day.ToString(CultureInfo.CurrentCulture) : string.Empty).ToArray(),
            LabelsPaint = new SolidColorPaint(SKColor.Parse("#55555F")),
            SeparatorsPaint = null,
            TextSize = 11
        }
    ];

    private static Axis[] CreateCurrencyAxis() =>
    [
        new Axis
        {
            Labeler = value => $"${value:0}k",
            LabelsPaint = new SolidColorPaint(SKColor.Parse("#55555F")),
            SeparatorsPaint = new SolidColorPaint(new SKColor(46, 46, 54, 100), 1),
            TextSize = 11,
            MinLimit = 0
        }
    ];

    private static AccountSpendingSource CreateSource(string name, string fraction, string colorHex, double pct) =>
        new(name, fraction, ToBrush(NormalizePaletteColor(colorHex)), pct);

    private static OverspentCategoryRow CreateRisk(string name, int risk, string exposures)
    {
        var brush = risk <= 1 ? ToBrush("#A78BFA") : risk <= 3 ? ToBrush("#8B5CF6") : ToBrush("#7C3AED");
        return new OverspentCategoryRow(name, exposures, Enumerable.Range(1, 5).Select(index => new RiskDot(index <= risk ? brush : ToBrush("#1E293B"))).ToArray());
    }

    private static string GetCategoryGlyph(string iconCode) => iconCode?.ToLowerInvariant() switch
    {
        "home" => "\uE80F",
        "restaurant" => "\uEA8B",
        "directions_car" => "\uE701",
        "local_hospital" or "medical_services" => "\uECAD",
        "movie" => "\uE8B2",
        "subscriptions" or "receipt" => "\uE9F4",
        "shopping_bag" or "store" => "\uE7BF",
        "payments" or "credit_card" => "\uE8C7",
        "flight" => "\uE709",
        "school" => "\uE82D",
        "fitness" => "\uEBBD",
        "coffee" => "\uED34",
        "gift" => "\uE8EB",
        "phone" => "\uE717",
        "savings" or "wallet" => "\uE825",
        "bolt" => "\uE945",
        "train" or "directions_bus" => "\uE7C0",
        "local_gas_station" => "\uEAA0",
        "sports_esports" => "\uE7FC",
        "work" => "\uE821",
        "book" => "\uED24",
        _ => "\uE8B9"
    };

    private static BudgetCardItem CreateBudgetCard(string name, string icon, string spentLimit, double ratio, string colorHex, string daysLeft, bool isOverspent)
    {
        var fill = isOverspent ? "#EF4444" : ratio < 0.75 ? "#A78BFA" : ratio <= 1 ? "#8B5CF6" : "#7C3AED";
        return new BudgetCardItem(name, GetCategoryGlyph(icon), spentLimit, Math.Clamp(ratio * 100, 0, 100), ToBrush(NormalizePaletteColor(colorHex)), ToBrush(fill), daysLeft, isOverspent);
    }

    private static SolidColorBrush ToBrush(string colorHex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!;
        brush.Freeze();
        return brush;
    }

    private static string NormalizePaletteColor(string colorHex)
    {
        var index = Math.Abs(colorHex.GetHashCode(StringComparison.Ordinal)) % Palette.Length;
        return Palette[index];
    }

    private string FormatMoney(decimal amount) => amount.ToString("C0", CurrencyCulture);
}

public partial class BudgetEditorRow(int categoryId, string categoryName, decimal monthlyLimit, int? budgetId) : ObservableObject
{
    public int CategoryId { get; } = categoryId;

    public string CategoryName { get; } = categoryName;

    public int? BudgetId { get; } = budgetId;

    [ObservableProperty]
    private decimal monthlyLimit = monthlyLimit;
}

public sealed record AccountSpendingSource(string Name, string Fraction, Brush IconBrush, double Pct);

public sealed record OverspentCategoryRow(string Category, string Exposures, IReadOnlyList<RiskDot> RiskDots);

public sealed record RiskDot(Brush Fill);

public sealed record BudgetCardItem(string Name, string IconGlyph, string SpentLimit, double ProgressValue, Brush IconBrush, Brush ProgressBrush, string DaysLeftText, bool IsOverspent = false);
