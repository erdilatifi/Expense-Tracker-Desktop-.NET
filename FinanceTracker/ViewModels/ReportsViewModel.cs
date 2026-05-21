using System.Globalization;
using System.IO;
using System.Windows.Media;
using FinanceTracker.Data;
using FinanceTracker.Models;
using FinanceTracker.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Win32;
using SkiaSharp;

namespace FinanceTracker.ViewModels;

/// <summary>
/// Provides report filters, chart series, heatmaps, flagged transactions, and export commands.
/// </summary>
public partial class ReportsViewModel : ObservableObject
{
    private static readonly string[] Palette = ["#A78BFA", "#8B5CF6", "#7C3AED", "#6D28D9", "#5B21B6", "#4C1D95"];
    private readonly FinanceDbContext? dbContext;
    private readonly ReportService? reportService;
    private readonly AppSettingsService? appSettings;
    private CultureInfo CurrencyCulture => appSettings?.CurrencyCulture ?? CultureInfo.GetCultureInfo("en-US");
    private List<ReportTransactionRow> allFlaggedRows = [];

    [ObservableProperty]
    private DateTime fromDate;

    [ObservableProperty]
    private DateTime toDate;

    [ObservableProperty]
    private string selectedPreset = "Last 3 months";

    [ObservableProperty]
    private string selectedAccountsText = "All accounts";

    [ObservableProperty]
    private ISeries[] topCategorySeries = [];

    [ObservableProperty]
    private Axis[] topCategoryXAxes = [];

    [ObservableProperty]
    private Axis[] topCategoryYAxes = [];

    [ObservableProperty]
    private ISeries[] statusGaugeSeries = [];

    [ObservableProperty]
    private string transactionCountText = "0";

    [ObservableProperty]
    private string averageDailySpendText = string.Empty;

    [ObservableProperty]
    private string forecastText = string.Empty;

    [ObservableProperty]
    private string yearOverYearText = string.Empty;

    public ObservableCollection<MonthSummaryRow> MonthSummaryRows { get; } = [];

    public ObservableCollection<ReportAccountFilter> AccountFilters { get; } = [];

    public ObservableCollection<HeatmapColumn> HeatmapColumns { get; } = [];

    public ObservableCollection<string> HeatmapRows { get; } = [];

    public ObservableCollection<HeatmapCell> HeatmapCells { get; } = [];

    public ObservableCollection<StatusLegendRow> StatusLegend { get; } = [];

    public ObservableCollection<ReportTransactionRow> FlaggedTransactions { get; } = [];

    public ReportsViewModel()
    {
        SetPresetRange("Last 3 months");
        LoadSampleData();
    }

    public ReportsViewModel(FinanceDbContext dbContext, ReportService reportService, AppSettingsService? appSettings = null)
    {
        this.dbContext = dbContext;
        this.reportService = reportService;
        this.appSettings = appSettings;
        SetPresetRange("Last 3 months");
        _ = LoadAsync();
    }

    partial void OnFromDateChanged(DateTime value) => _ = LoadAsync();

    partial void OnToDateChanged(DateTime value) => _ = LoadAsync();

    [RelayCommand]
    private void SetPreset(string preset)
    {
        SelectedPreset = preset;
        SetPresetRange(preset);
        _ = LoadAsync();
    }

    [RelayCommand]
    private void ToggleAccount(ReportAccountFilter account)
    {
        account.IsSelected = !account.IsSelected;
        UpdateSelectedAccountsText();
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        if (reportService is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PDF file (*.pdf)|*.pdf",
            FileName = $"finance-report-{DateTime.Today:yyyy-MM-dd}.pdf"
        };

        if (dialog.ShowDialog() == true)
        {
            await reportService.ExportPdfAsync(CreateReportData(), dialog.FileName);
        }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"flagged-transactions-{DateTime.Today:yyyy-MM-dd}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var lines = new List<string> { "Date,Description,Account,Category,Amount,Status" };
        lines.AddRange(FlaggedTransactions.Select(row =>
            string.Join(",", Escape(row.DateText), Escape(row.Description), Escape(row.AccountName), Escape(row.CategoryName), Escape(row.AmountText), Escape(row.Status))));
        File.WriteAllLines(dialog.FileName, lines);
    }

    public async Task LoadAsync()
    {
        if (dbContext is null)
        {
            return;
        }

        var accounts = await dbContext.Accounts.AsNoTracking().OrderBy(account => account.Name).ToListAsync();
        if (AccountFilters.Count == 0)
        {
            foreach (var account in accounts)
            {
                AccountFilters.Add(new ReportAccountFilter(account.Id, account.Name, true));
            }
        }

        var selectedAccountIds = AccountFilters.Where(account => account.IsSelected).Select(account => account.Id).ToHashSet();

        var transactions = await dbContext.Transactions
            .AsNoTracking()
            .Include(transaction => transaction.Category)
            .Include(transaction => transaction.Account)
            .Where(transaction => transaction.Date >= FromDate.Date && transaction.Date <= ToDate.Date)
            .Where(transaction => selectedAccountIds.Count == 0 || selectedAccountIds.Contains(transaction.AccountId))
            .OrderByDescending(transaction => transaction.Date)
            .ToListAsync();

        if (transactions.Count == 0)
        {
            LoadSampleData();
            return;
        }

        BuildTopCategories(transactions);
        BuildStackedCategories(transactions);

        var heatmapWindowStart = StartOfWeek(DateTime.Today).AddDays(-11 * 7);
        var heatmapTransactions = await dbContext.Transactions
            .AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.Date >= heatmapWindowStart && t.Date <= DateTime.Today
                        && t.Type == TransactionType.Expense
                        && (selectedAccountIds.Count == 0 || selectedAccountIds.Contains(t.AccountId)))
            .ToListAsync();
        BuildHeatmap(heatmapTransactions);
        BuildStatus(transactions);
        BuildFlaggedRows(transactions);

        var priorYearStart = FromDate.AddYears(-1);
        var priorYearEnd = ToDate.AddYears(-1);
        var priorYearTransactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Date >= priorYearStart && t.Date <= priorYearEnd)
            .ToListAsync();

        UpdateSummary(transactions, priorYearTransactions);
    }

    [RelayCommand]
    private async Task ExportAllTransactionsCsvAsync()
    {
        if (dbContext is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"all-transactions-{FromDate:yyyy-MM-dd}-to-{ToDate:yyyy-MM-dd}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var transactions = await dbContext.Transactions
            .AsNoTracking()
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Where(t => t.Date >= FromDate.Date && t.Date <= ToDate.Date)
            .OrderByDescending(t => t.Date)
            .ToListAsync();

        var lines = new List<string> { "Date,Description,Account,Category,Type,Amount,Notes" };
        lines.AddRange(transactions.Select(t => string.Join(",",
            Escape(t.Date.ToString("yyyy-MM-dd")),
            Escape(t.Description),
            Escape(t.Account?.Name ?? ""),
            Escape(t.Category?.Name ?? ""),
            Escape(t.Type.ToString()),
            Escape(t.Amount.ToString("F2", CurrencyCulture)),
            Escape(t.Notes ?? ""))));

        await File.WriteAllLinesAsync(dialog.FileName, lines);
    }

    private void UpdateSummary(IReadOnlyCollection<Transaction> transactions, IReadOnlyCollection<Transaction> priorYearTransactions)
    {
        var days = Math.Max(1, (ToDate.Date - FromDate.Date).Days + 1);
        var expenses = transactions.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
        var income = transactions.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
        AverageDailySpendText = $"Avg daily spend: {FormatMoney(expenses / days)}";

        var daysInMonth = DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month);
        var dayOfMonth = Math.Max(1, DateTime.Today.Day);
        var dailyAvg = expenses / days;
        ForecastText = $"Month-end forecast: {FormatMoney(dailyAvg * daysInMonth)}";

        var priorExp = priorYearTransactions.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
        var priorInc = priorYearTransactions.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
        var expChange = priorExp == 0m ? 0m : (expenses - priorExp) / priorExp * 100m;
        var incChange = priorInc == 0m ? 0m : (income - priorInc) / priorInc * 100m;
        YearOverYearText = $"YoY expenses {expChange:+0.#;-0.#}% · income {incChange:+0.#;-0.#}%";

        MonthSummaryRows.Clear();
        var monthGroups = transactions
            .GroupBy(t => new DateTime(t.Date.Year, t.Date.Month, 1))
            .OrderBy(g => g.Key);

        foreach (var group in monthGroups)
        {
            var inc = group.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
            var exp = group.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
            var sav = inc - exp;
            var pct = inc == 0m ? 0m : sav / inc * 100m;
            MonthSummaryRows.Add(new MonthSummaryRow(
                group.Key.ToString("MMM yyyy", CultureInfo.CurrentCulture),
                FormatMoney(inc),
                FormatMoney(exp),
                FormatMoney(sav),
                $"{pct:0.#}%"));
        }
    }

    private void BuildStackedCategories(IReadOnlyCollection<Transaction> transactions)
    {
        var monthCount = ((ToDate.Year - FromDate.Year) * 12) + ToDate.Month - FromDate.Month + 1;
        if (monthCount < 2)
        {
            return;
        }

        var months = Enumerable.Range(0, Math.Min(12, monthCount))
            .Select(i => new DateTime(FromDate.Year, FromDate.Month, 1).AddMonths(i))
            .Where(m => m <= ToDate)
            .ToArray();

        if (months.Length == 0)
        {
            return;
        }

        var topCategories = transactions
            .Where(t => t.Type == TransactionType.Expense && t.Category is not null)
            .GroupBy(t => t.Category!.Name)
            .OrderByDescending(g => g.Sum(t => t.Amount))
            .Take(5)
            .Select(g => g.Key)
            .ToList();

        var series = new List<ISeries>();
        var colors = DefaultCategoryColors;
        for (var i = 0; i < topCategories.Count; i++)
        {
            var name = topCategories[i];
            var values = months.Select(month =>
            {
                var end = month.AddMonths(1);
                return (double)transactions
                    .Where(t => t.Type == TransactionType.Expense && t.Category?.Name == name && t.Date >= month && t.Date < end)
                    .Sum(t => t.Amount);
            }).ToArray();

            series.Add(new StackedColumnSeries<double>
            {
                Name = name,
                Values = values,
                Fill = new SolidColorPaint(SKColor.Parse(colors[i % colors.Length]))
            });
        }

        if (series.Count > 0)
        {
            TopCategorySeries = series.ToArray();
            TopCategoryXAxes = [new Axis { Labels = months.Select(m => m.ToString("MMM", CultureInfo.CurrentCulture)).ToArray(), LabelsPaint = MutedPaint(), TextSize = 11 }];
            TopCategoryYAxes = [new Axis { Labeler = v => v.ToString("C0", CurrencyCulture), LabelsPaint = MutedPaint(), SeparatorsPaint = GridPaint(), TextSize = 11, MinLimit = 0 }];
        }
    }

    private string FormatMoney(decimal amount) => amount.ToString("C0", CurrencyCulture);

    private void LoadSampleData()
    {
        AccountFilters.Clear();
        AccountFilters.Add(new ReportAccountFilter(1, "Checking", true));
        AccountFilters.Add(new ReportAccountFilter(2, "Savings", true));
        UpdateSelectedAccountsText();

        var categories = new[]
        {
            ("Housing", "#A78BFA"),
            ("Food", "#8B5CF6"),
            ("Transport", "#7C3AED"),
            ("Health", "#6D28D9"),
            ("Shopping", "#5B21B6"),
            ("Subscriptions", "#4C1D95")
        };

        var samples = new List<SampleSpend>();
        foreach (var year in new[] { 2022, 2023, 2024 })
        {
            for (var index = 0; index < categories.Length; index++)
            {
                samples.Add(new SampleSpend(year, categories[index].Item1, categories[index].Item2, 1200 + (year - 2022) * 430 + index * 360));
            }
        }

        BuildTopCategories(samples);
        BuildSampleHeatmap(categories);
        BuildStatus(84, 14, 9, 61);

        AverageDailySpendText = "Avg daily spend: $174.12";
        ForecastText = "Month-end forecast: $5,397.72";
        YearOverYearText = "YoY expenses +12.4% · income +8.1%";

        MonthSummaryRows.Clear();
        var today = DateTime.Today;
        for (var i = 5; i >= 0; i--)
        {
            var m = today.AddMonths(-i);
            var inc = 8250m;
            var exp = 4800m + (i * 120m);
            var sav = inc - exp;
            MonthSummaryRows.Add(new MonthSummaryRow(
                m.ToString("MMM yyyy", System.Globalization.CultureInfo.CurrentCulture),
                FormatMoney(inc),
                FormatMoney(exp),
                FormatMoney(sav),
                $"{sav / inc * 100m:0.#}%"));
        }

        allFlaggedRows =
        [
            CreateReportRow(DateTime.Today.AddDays(-1), "Duplicate card charge", "Checking", "Shopping", -129.90m, "Open"),
            CreateReportRow(DateTime.Today.AddDays(-4), "Large restaurant bill", "Checking", "Food", -248.15m, "Reviewing"),
            CreateReportRow(DateTime.Today.AddDays(-8), "Subscription retry", "Checking", "Subscriptions", -19.99m, "Cleared"),
            CreateReportRow(DateTime.Today.AddDays(-11), "ATM withdrawal", "Cash", "Cash", -200m, "Open")
        ];
        RefreshFlaggedRows();
    }

    private void BuildTopCategories(IReadOnlyCollection<Transaction> transactions)
    {
        var expenseTransactions = transactions
            .Where(transaction => transaction.Type == TransactionType.Expense && transaction.Category is not null)
            .ToList();

        var categories = expenseTransactions
            .GroupBy(transaction => transaction.Category!.Name)
            .Select(group => new { Name = group.Key, Amount = (double)group.Sum(transaction => transaction.Amount) })
            .OrderByDescending(category => category.Amount)
            .Take(6)
            .ToList();

        TopCategorySeries =
        [
            new RowSeries<double>
            {
                Name = "Spend",
                Values = categories.Select(category => category.Amount).ToArray(),
                Fill = new LinearGradientPaint(
                    [SKColor.Parse("#A78BFA"), SKColor.Parse("#7C3AED")],
                    new SKPoint(0, 0),
                    new SKPoint(1, 0)),
                Stroke = null,
                MaxBarWidth = 20,
                DataLabelsPaint = MutedPaint(),
                DataLabelsSize = 10,
                DataLabelsFormatter = point => point.Coordinate.PrimaryValue.ToString("C0", CurrencyCulture)
            }
        ];

        TopCategoryYAxes = [new Axis { Labels = categories.Select(category => category.Name).ToArray(), LabelsPaint = MutedPaint(), TextSize = 11, SeparatorsPaint = null }];
        TopCategoryXAxes = [new Axis { Labeler = value => value.ToString("C0", CurrencyCulture), LabelsPaint = MutedPaint(), SeparatorsPaint = GridPaint(), TextSize = 11, MinLimit = 0 }];
    }

    private void BuildTopCategories(IReadOnlyCollection<SampleSpend> samples)
    {
        var categories = samples
            .GroupBy(sample => sample.Category)
            .Select(group => new { Name = group.Key, Amount = group.Sum(sample => sample.Amount) })
            .OrderByDescending(category => category.Amount)
            .Take(6)
            .ToList();

        TopCategorySeries =
        [
            new RowSeries<double>
            {
                Name = "Spend",
                Values = categories.Select(category => category.Amount).ToArray(),
                Fill = new LinearGradientPaint(
                    [SKColor.Parse("#A78BFA"), SKColor.Parse("#7C3AED")],
                    new SKPoint(0, 0),
                    new SKPoint(1, 0)),
                Stroke = null,
                MaxBarWidth = 20,
                DataLabelsPaint = MutedPaint(),
                DataLabelsSize = 10,
                DataLabelsFormatter = point => point.Coordinate.PrimaryValue.ToString("C0", CurrencyCulture)
            }
        ];

        TopCategoryYAxes = [new Axis { Labels = categories.Select(category => category.Name).ToArray(), LabelsPaint = MutedPaint(), TextSize = 11, SeparatorsPaint = null }];
        TopCategoryXAxes = [new Axis { Labeler = value => value.ToString("C0", CurrencyCulture), LabelsPaint = MutedPaint(), SeparatorsPaint = GridPaint(), TextSize = 11, MinLimit = 0 }];
    }

    private void BuildHeatmap(IReadOnlyCollection<Transaction> transactions)
    {
        var categories = transactions
            .Where(transaction => transaction.Type == TransactionType.Expense && transaction.Category is not null)
            .GroupBy(transaction => transaction.Category!.Name)
            .OrderByDescending(group => group.Sum(transaction => transaction.Amount))
            .Take(6)
            .Select(group => group.Key)
            .ToArray();

        var weeks = Enumerable.Range(0, 12)
            .Select(offset => StartOfWeek(DateTime.Today).AddDays((offset - 11) * 7))
            .ToArray();

        var lookup = transactions
            .Where(transaction => transaction.Type == TransactionType.Expense)
            .GroupBy(transaction => (Week: StartOfWeek(transaction.Date), Category: transaction.Category?.Name ?? "Uncategorized"))
            .ToDictionary(group => group.Key, group => group.Sum(transaction => transaction.Amount));

        BuildHeatmapGrid(weeks, categories, lookup);
    }

    private void BuildSampleHeatmap(IReadOnlyList<(string Name, string ColorHex)> categories)
    {
        var weeks = Enumerable.Range(0, 12)
            .Select(offset => StartOfWeek(DateTime.Today).AddDays((offset - 11) * 7))
            .ToArray();

        var lookup = new Dictionary<(DateTime Week, string Category), decimal>();
        foreach (var week in weeks)
        {
            for (var index = 0; index < categories.Count; index++)
            {
                lookup[(week, categories[index].Name)] = (decimal)(((week.DayOfYear + index * 13) % 9) * 34 + index * 18);
            }
        }

        BuildHeatmapGrid(weeks, categories.Select(category => category.Name).ToArray(), lookup);
    }

    private void BuildHeatmapGrid(DateTime[] weeks, string[] categories, Dictionary<(DateTime Week, string Category), decimal> lookup)
    {
        HeatmapColumns.Clear();
        HeatmapRows.Clear();
        HeatmapCells.Clear();

        for (var index = 0; index < weeks.Length; index++)
        {
            HeatmapColumns.Add(new HeatmapColumn(index % 3 == 0 ? weeks[index].ToString("MMM d", CultureInfo.CurrentCulture) : string.Empty));
        }

        foreach (var category in categories)
        {
            HeatmapRows.Add(category);
        }

        var max = lookup.Count == 0 ? 1m : Math.Max(lookup.Values.Max(), 1m);
        foreach (var category in categories)
        {
            foreach (var week in weeks)
            {
                lookup.TryGetValue((week, category), out var amount);
                HeatmapCells.Add(new HeatmapCell(category, week, amount, CreateHeatmapBrush(amount / max)));
            }
        }
    }

    private void BuildStatus(IReadOnlyCollection<Transaction> transactions)
    {
        var reviewing = transactions.Count(transaction => transaction.Amount >= 200m && transaction.Type == TransactionType.Expense);
        var open = transactions.Count(transaction => transaction.Amount >= 500m && transaction.Type == TransactionType.Expense);
        var cleared = Math.Max(0, transactions.Count - reviewing - open);
        BuildStatus(transactions.Count, open, reviewing, cleared);
    }

    private void BuildStatus(int total, int open, int reviewing, int cleared)
    {
        TransactionCountText = total.ToString("N0", CultureInfo.CurrentCulture);

        StatusGaugeSeries =
        [
            new PieSeries<double>
            {
                Name = "Open",
                Values = [Math.Max(open, 0)],
                Fill = new SolidColorPaint(SKColor.Parse("#A78BFA")),
                Stroke = null,
                InnerRadius = 56,
                HoverPushout = 4,
                MaxRadialColumnWidth = 24
            },
            new PieSeries<double>
            {
                Name = "Reviewing",
                Values = [Math.Max(reviewing, 0)],
                Fill = new SolidColorPaint(SKColor.Parse("#7C3AED")),
                Stroke = null,
                InnerRadius = 56,
                HoverPushout = 4,
                MaxRadialColumnWidth = 24
            },
            new PieSeries<double>
            {
                Name = "Cleared",
                Values = [Math.Max(cleared, 1)],
                Fill = new SolidColorPaint(SKColor.Parse("#4C1D95")),
                Stroke = null,
                InnerRadius = 56,
                HoverPushout = 4,
                MaxRadialColumnWidth = 24
            }
        ];

        StatusLegend.Clear();
        StatusLegend.Add(new StatusLegendRow("Open", open, ToBrush("#A78BFA")));
        StatusLegend.Add(new StatusLegendRow("Reviewing", reviewing, ToBrush("#7C3AED")));
        StatusLegend.Add(new StatusLegendRow("Cleared", cleared, ToBrush("#4C1D95")));
    }

    private void BuildFlaggedRows(IReadOnlyCollection<Transaction> transactions)
    {
        allFlaggedRows = transactions
            .Where(transaction => transaction.Type == TransactionType.Expense && (transaction.Amount >= 150m || transaction.IsRecurring))
            .Take(25)
            .Select(transaction =>
            {
                var status = transaction.Amount >= 500m ? "Open" : transaction.IsRecurring ? "Reviewing" : "Cleared";
                return CreateReportRow(
                    transaction.Date,
                    transaction.Description,
                    transaction.Account?.Name ?? "Unknown",
                    transaction.Category?.Name ?? "Uncategorized",
                    -transaction.Amount,
                    status);
            })
            .ToList();

        RefreshFlaggedRows();
    }

    private void RefreshFlaggedRows()
    {
        FlaggedTransactions.Clear();
        foreach (var row in allFlaggedRows)
        {
            FlaggedTransactions.Add(row);
        }
    }

    private ReportData CreateReportData()
    {
        var cashflow = reportService?.GetCashflowByMonth(FromDate, ToDate)
            .Select(point => new CashflowPoint(point.Month, point.Income, point.Expenses, point.Savings))
            .ToList() ?? [];

        var spend = reportService?.GetSpendByCategory(FromDate, ToDate)
            .Select((point, index) => new CategorySpendPoint(point.Category, point.Amount, DefaultCategoryColors[index % DefaultCategoryColors.Length]))
            .ToList() ?? [];

        return new ReportData(
            FromDate,
            ToDate,
            cashflow,
            spend,
            FlaggedTransactions.Select(row => new FlaggedTransactionReportItem(row.DateText, row.Description, row.AccountName, row.CategoryName, row.AmountText, row.Status)).ToList());
    }

    private void SetPresetRange(string preset)
    {
        var today = DateTime.Today;
        var thisMonth = new DateTime(today.Year, today.Month, 1);

        (FromDate, ToDate) = preset switch
        {
            "Last month" => (thisMonth.AddMonths(-1), thisMonth.AddDays(-1)),
            "Last 3 months" => (thisMonth.AddMonths(-2), today),
            "This year" => (new DateTime(today.Year, 1, 1), today),
            _ => (thisMonth, today)
        };
    }

    private void UpdateSelectedAccountsText()
    {
        var selected = AccountFilters.Where(account => account.IsSelected).ToList();
        SelectedAccountsText = selected.Count switch
        {
            0 => "No accounts",
            var count when count == AccountFilters.Count => "All accounts",
            1 => selected[0].Name,
            _ => $"{selected.Count} accounts"
        };
    }

    private ReportTransactionRow CreateReportRow(DateTime date, string description, string account, string category, decimal amount, string status)
    {
        var statusBrush = status switch
        {
            "Open" => ToBrush("#A78BFA"),
            "Reviewing" => ToBrush("#8B5CF6"),
            _ => ToBrush("#7C3AED")
        };

        return new ReportTransactionRow(
            date.ToString("MMM d", CultureInfo.CurrentCulture),
            description,
            account,
            category,
            FormatSignedMoney(amount),
            status,
            statusBrush);
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-offset);
    }

    private static Brush CreateHeatmapBrush(decimal ratio)
    {
        var clamped = (byte)(Math.Clamp(ratio, 0m, 1m) * 215m);
        return new SolidColorBrush(Color.FromArgb(clamped, 124, 58, 237));
    }

    private static SolidColorPaint MutedPaint() => new(SKColor.Parse("#55555F"));

    private static SolidColorPaint GridPaint() => new(new SKColor(46, 46, 54, 100), 1);

    private string FormatSignedMoney(decimal amount)
    {
        var prefix = amount >= 0m ? "+" : "-";
        return $"{prefix}{Math.Abs(amount).ToString("C2", CurrencyCulture)}";
    }

    private static string Escape(string value)
    {
        return value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static SolidColorBrush ToBrush(string colorHex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!;
        brush.Freeze();
        return brush;
    }

    private static readonly string[] DefaultCategoryColors =
    [
        "#A78BFA",
        "#8B5CF6",
        "#7C3AED",
        "#6D28D9",
        "#5B21B6",
        "#4C1D95"
    ];

    private sealed record SampleSpend(int Year, string Category, string ColorHex, double Amount);
}

/// <summary>
/// Represents a selectable account filter for reports.
/// </summary>
public partial class ReportAccountFilter(int id, string name, bool isSelected) : ObservableObject
{
    [ObservableProperty]
    private bool isSelected = isSelected;

    public int Id { get; } = id;

    public string Name { get; } = name;

    public override string ToString() => Name;
}

/// <summary>
/// Represents a date column in the weekly activity heatmap.
/// </summary>
public sealed record HeatmapColumn(string Label);

/// <summary>
/// Represents a spending cell in the weekly activity heatmap.
/// </summary>
public sealed record HeatmapCell(string Category, DateTime Week, decimal Amount, Brush Fill)
{
    public string Tooltip => $"Week of {Week:MMM d} · Category {Category} · {Amount.ToString("C0", CultureInfo.GetCultureInfo("en-US"))}";
}

/// <summary>
/// Represents one status summary legend row.
/// </summary>
public sealed record StatusLegendRow(string Label, int Count, Brush Brush);

/// <summary>
/// Represents one flagged transaction in the report table.
/// </summary>
public sealed record MonthSummaryRow(string Month, string Income, string Expenses, string Savings, string SavingsPercent);

public sealed record ReportTransactionRow(
    string DateText,
    string Description,
    string AccountName,
    string CategoryName,
    string AmountText,
    string Status,
    Brush StatusBrush);
