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
/// Provides account summaries, account tiles, sparklines, and account transaction history.
/// </summary>
public partial class AccountsViewModel : ObservableObject
{
    private static readonly string[] Palette = ["#A78BFA", "#8B5CF6", "#7C3AED", "#6D28D9", "#5B21B6", "#4C1D95"];
    private readonly FinanceDbContext? dbContext;
    private readonly AppSettingsService? appSettings;
    private CultureInfo CurrencyCulture => appSettings?.CurrencyCulture ?? CultureInfo.GetCultureInfo("en-US");
    private List<AccountTransactionRow> allTransactions = [];

    private const int PageSize = 10;

    [ObservableProperty]
    private AccountCardItem? selectedAccount;

    [ObservableProperty]
    private int currentPage = 1;

    [ObservableProperty]
    private int totalPages = 1;

    public ObservableCollection<AccountCardItem> Accounts { get; } = [];

    public ObservableCollection<AccountTileItem> AccountTiles { get; } = [];

    public ObservableCollection<AccountTransactionRow> Transactions { get; } = [];

    public Axis[] HiddenAxes { get; } = [new Axis { IsVisible = false }];

    public AccountsViewModel()
    {
        LoadSampleData();
    }

    public AccountsViewModel(FinanceDbContext dbContext, AppSettingsService? appSettings = null)
    {
        this.dbContext = dbContext;
        this.appSettings = appSettings;
        _ = LoadAsync();
    }

    partial void OnSelectedAccountChanged(AccountCardItem? value)
    {
        CurrentPage = 1;
        ApplyAccountFilter();
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            ApplyAccountFilter();
        }
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            ApplyAccountFilter();
        }
    }

    [RelayCommand]
    private void SelectAccount(AccountTileItem account)
    {
        if (!account.IsAddTile)
        {
            SelectedAccount = Accounts.FirstOrDefault(item => item.Id == account.Id);
        }
    }

    [RelayCommand]
    private async Task AddAccountAsync()
    {
        if (dbContext is null)
        {
            var id = Accounts.Count + 1;
            Accounts.Add(CreateCard(id, $"Account {id}", AccountType.Checking, 0m, "#7C3AED", []));
            RebuildAccountTiles();
            return;
        }

        var account = new Account
        {
            Name = $"Account {Accounts.Count + 1}",
            Balance = 0m,
            Currency = "USD",
            Type = AccountType.Checking,
            ColorHex = "#7C3AED"
        };

        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync();
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (dbContext is null)
        {
            return;
        }

        var accounts = await dbContext.Accounts.AsNoTracking().OrderBy(account => account.Name).ToListAsync();
        var transactions = await dbContext.Transactions
            .AsNoTracking()
            .Include(transaction => transaction.Account)
            .Include(transaction => transaction.Category)
            .OrderByDescending(transaction => transaction.Date)
            .ToListAsync();

        if (accounts.Count == 0)
        {
            LoadSampleData();
            return;
        }

        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        Accounts.Clear();
        foreach (var account in accounts)
        {
            var acctTx = transactions.Where(t => t.AccountId == account.Id).ToList();
            var balances = BuildBalanceHistory(account, acctTx);
            var monthDelta = acctTx
                .Where(t => t.Date >= monthStart)
                .Sum(t => t.Type == TransactionType.Expense ? -t.Amount : t.Amount);
            Accounts.Add(CreateCard(account.Id, account.Name, account.Type, account.Balance, account.ColorHex, balances, monthDelta));
        }
        RebuildAccountTiles();

        allTransactions = transactions.Select(transaction => CreateTransactionRow(
            transaction.AccountId,
            transaction.Date,
            transaction.Description,
            transaction.Category?.Name ?? "Uncategorized",
            transaction.Type == TransactionType.Expense ? -transaction.Amount : transaction.Amount,
            transaction.Category?.ColorHex ?? "#64748B")).ToList();

        SelectedAccount = Accounts.FirstOrDefault();
        ApplyAccountFilter();
    }

    private void LoadSampleData()
    {
        Accounts.Clear();
        Accounts.Add(CreateCard(1, "Checking", AccountType.Checking, 12480.25m, "#A78BFA", [11800, 12040, 11920, 12200, 12480]));
        Accounts.Add(CreateCard(2, "Savings", AccountType.Savings, 8320.00m, "#7C3AED", [7900, 8040, 8120, 8210, 8320]));
        Accounts.Add(CreateCard(3, "Travel card", AccountType.CreditCard, -1480.75m, "#6D28D9", [-1200, -1320, -1288, -1510, -1480]));
        RebuildAccountTiles();

        allTransactions =
        [
            CreateTransactionRow(1, DateTime.Today, "Salary deposit", "Income", 4125m, "#A78BFA"),
            CreateTransactionRow(1, DateTime.Today.AddDays(-2), "Grocery market", "Food", -126.45m, "#8B5CF6"),
            CreateTransactionRow(2, DateTime.Today.AddDays(-4), "Monthly transfer", "Savings", 500m, "#7C3AED"),
            CreateTransactionRow(3, DateTime.Today.AddDays(-5), "Flight booking", "Travel", -420.00m, "#6D28D9")
        ];

        SelectedAccount = Accounts.FirstOrDefault();
        ApplyAccountFilter();
    }

    private void ApplyAccountFilter()
    {
        Transactions.Clear();
        var accountId = SelectedAccount?.Id;
        var filtered = allTransactions.Where(row => accountId is null || row.AccountId == accountId).ToList();
        TotalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
        CurrentPage = Math.Clamp(CurrentPage, 1, TotalPages);
        foreach (var row in filtered.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
        {
            Transactions.Add(row);
        }
    }

    private void RebuildAccountTiles()
    {
        AccountTiles.Clear();
        foreach (var account in Accounts)
        {
            AccountTiles.Add(AccountTileItem.FromAccount(account));
        }

        AccountTiles.Add(AccountTileItem.AddTile());
    }

    private static decimal[] BuildBalanceHistory(Account account, IReadOnlyCollection<Transaction> transactions)
    {
        var today = DateTime.Today;
        var balances = new decimal[30];

        for (var offset = 29; offset >= 0; offset--)
        {
            var day = today.AddDays(-offset);
            var futureDelta = transactions
                .Where(transaction => transaction.Date.Date > day)
                .Sum(transaction => transaction.Type == TransactionType.Expense ? -transaction.Amount : transaction.Amount);
            balances[29 - offset] = account.Balance - futureDelta;
        }

        return balances;
    }

    private AccountCardItem CreateCard(int id, string name, AccountType type, decimal balance, string colorHex, IEnumerable<decimal> balances, decimal monthDelta = 0m)
    {
        var points = balances.Any() ? balances.Select(value => (double)value).ToArray() : new[] { 0d, 0d, 0d, 0d };
        var trendText = monthDelta == 0m
            ? "— No change this month"
            : monthDelta > 0m
                ? $"↑ +{FormatMoney(monthDelta)} this month"
                : $"↓ {FormatMoney(monthDelta)} this month";
        return new AccountCardItem(
            id,
            name,
            type,
            GetIcon(type),
            FormatMoney(balance),
            trendText,
            ToBrush(NormalizePaletteColor(colorHex)),
            CreateSparkline(points, colorHex));
    }

    private AccountTransactionRow CreateTransactionRow(int accountId, DateTime date, string description, string category, decimal amount, string colorHex)
    {
        return new AccountTransactionRow(
            accountId,
            date.ToString("MMM d", CultureInfo.CurrentCulture),
            description,
            category,
            FormatSignedMoney(amount),
            amount >= 0m ? ToBrush("#DDD6FE") : ToBrush("#C4B5FD"),
            ToBrush(NormalizePaletteColor(colorHex)));
    }

    private static ISeries[] CreateSparkline(double[] values, string colorHex)
    {
        var paletteColor = NormalizePaletteColor(colorHex);
        return
        [
            new LineSeries<double>
            {
                Values = values,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(SKColor.Parse(paletteColor), 2),
                Fill = new SolidColorPaint(SKColor.Parse(paletteColor).WithAlpha(40)),
                LineSmoothness = 0.6
            }
        ];
    }

    private static string GetIcon(AccountType type) => type switch
    {
        AccountType.Savings => "\uE825",
        AccountType.CreditCard => "\uE8C7",
        AccountType.Cash => "\uEAFD",
        AccountType.Investment => "\uE9D2",
        _ => "\uE8D4"
    };

    private string FormatMoney(decimal amount) => amount.ToString("C2", CurrencyCulture);

    private string FormatSignedMoney(decimal amount)
    {
        var prefix = amount >= 0m ? "+" : "-";
        return $"{prefix}{Math.Abs(amount).ToString("C2", CurrencyCulture)}";
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
}

/// <summary>
/// Represents a detailed account card and its sparkline data.
/// </summary>
public sealed record AccountCardItem(
    int Id,
    string Name,
    AccountType Type,
    string Icon,
    string BalanceText,
    string TrendText,
    Brush ColorBrush,
    ISeries[] SparklineSeries);

/// <summary>
/// Represents a selectable account tile in the accounts overview.
/// </summary>
public sealed record AccountTileItem(
    int Id,
    string Name,
    string Icon,
    string BalanceText,
    string TrendText,
    Brush ColorBrush,
    ISeries[] SparklineSeries,
    bool IsAddTile)
{
    public static AccountTileItem FromAccount(AccountCardItem account)
    {
        return new AccountTileItem(account.Id, account.Name, account.Icon, account.BalanceText, account.TrendText, account.ColorBrush, account.SparklineSeries, false);
    }

    public static AccountTileItem AddTile()
    {
        return new AccountTileItem(0, "Add account", "+", string.Empty, string.Empty, Brushes.Transparent, [], true);
    }
}

/// <summary>
/// Represents a transaction row scoped to an account.
/// </summary>
public sealed record AccountTransactionRow(
    int AccountId,
    string DateText,
    string Description,
    string CategoryName,
    string AmountText,
    Brush AmountBrush,
    Brush CategoryBrush);
