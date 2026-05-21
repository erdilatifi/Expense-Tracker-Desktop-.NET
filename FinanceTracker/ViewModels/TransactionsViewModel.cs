using System.Globalization;
using System.IO;
using FinanceTracker.Data;
using FinanceTracker.Models;
using FinanceTracker.Services;
using System.Windows.Media;
using System.Text;
using Microsoft.Win32;

namespace FinanceTracker.ViewModels;

/// <summary>
/// Provides transaction list filtering, editing, dialog state, and CSV export.
/// </summary>
public partial class TransactionsViewModel : ObservableObject
{
    private static readonly string[] Palette = ["#A78BFA", "#8B5CF6", "#7C3AED", "#6D28D9", "#5B21B6", "#4C1D95"];
    private readonly FinanceDbContext? dbContext;
    private readonly BudgetAlertService? budgetAlertService;
    private readonly AppSettingsService? appSettings;
    private Dictionary<int, decimal> budgetLimitsByCategory = [];
    private List<TransactionListItem> allTransactions = [];
    private List<TransactionListItem> filteredTransactions = [];
    private string sortColumn = "Date";
    private bool sortAscending;
    private int currentPage = 1;
    private int filteredCount;
    private const int PageSize = 25;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private DateTime? fromDate;

    [ObservableProperty]
    private DateTime? toDate;

    [ObservableProperty]
    private AccountOption? selectedAccount;

    [ObservableProperty]
    private TransactionType? selectedType;

    [ObservableProperty]
    private bool isDialogOpen;

    [ObservableProperty]
    private string selectedCategoryText = "All categories";

    [ObservableProperty]
    private string paginationText = "Showing 0-0 of 0 transactions";

    [ObservableProperty]
    private string amountText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTransfer))]
    [NotifyPropertyChangedFor(nameof(IsNotTransfer))]
    private TransactionType newTransactionType = TransactionType.Expense;

    public bool IsTransfer => NewTransactionType == TransactionType.Transfer;
    public bool IsNotTransfer => NewTransactionType != TransactionType.Transfer;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private DateTime? transactionDate = DateTime.Today;

    [ObservableProperty]
    private CategoryOption? selectedDialogCategory;

    [ObservableProperty]
    private AccountOption? selectedDialogAccount;

    [ObservableProperty]
    private AccountOption? selectedDialogTargetAccount;

    [ObservableProperty]
    private string notes = string.Empty;

    [ObservableProperty]
    private string receiptPath = string.Empty;

    [ObservableProperty]
    private bool isRecurring;

    [ObservableProperty]
    private string selectedRecurrenceFrequency = nameof(RecurrenceFrequency.Monthly);

    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private int? editingTransactionId;

    [ObservableProperty]
    private string amountError = string.Empty;

    [ObservableProperty]
    private string descriptionError = string.Empty;

    [ObservableProperty]
    private string dateError = string.Empty;

    [ObservableProperty]
    private string categoryError = string.Empty;

    [ObservableProperty]
    private string accountError = string.Empty;

    [ObservableProperty]
    private string targetAccountError = string.Empty;

    public ObservableCollection<TransactionListItem> Transactions { get; } = [];

    public ObservableCollection<CategoryFilterOption> CategoryFilters { get; } = [];

    public ObservableCollection<CategoryOption> Categories { get; } = [];

    public ObservableCollection<AccountOption> Accounts { get; } = [];

    public ObservableCollection<string> RecurrenceFrequencies { get; } =
    [
        nameof(RecurrenceFrequency.Daily),
        nameof(RecurrenceFrequency.Weekly),
        nameof(RecurrenceFrequency.Monthly),
        nameof(RecurrenceFrequency.Yearly)
    ];

    private CultureInfo CurrencyCulture => appSettings?.CurrencyCulture ?? CultureInfo.GetCultureInfo("en-US");

    public TransactionsViewModel()
    {
        LoadSampleData();
    }

    public TransactionsViewModel(FinanceDbContext dbContext, BudgetAlertService? budgetAlertService = null, AppSettingsService? appSettings = null)
    {
        this.dbContext = dbContext;
        this.budgetAlertService = budgetAlertService;
        this.appSettings = appSettings;
        _ = LoadAsync();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    partial void OnFromDateChanged(DateTime? value) => ApplyFilters();

    partial void OnToDateChanged(DateTime? value) => ApplyFilters();

    partial void OnSelectedAccountChanged(AccountOption? value) => ApplyFilters();

    [RelayCommand]
    private void SetTypeFilter(string type)
    {
        SelectedType = type switch
        {
            "Income" => SelectedType == TransactionType.Income ? null : TransactionType.Income,
            "Expense" => SelectedType == TransactionType.Expense ? null : TransactionType.Expense,
            "Transfer" => SelectedType == TransactionType.Transfer ? null : TransactionType.Transfer,
            _ => null
        };

        ApplyFilters();
    }

    [RelayCommand]
    private void Sort(string column)
    {
        if (sortColumn == column)
        {
            sortAscending = !sortAscending;
        }
        else
        {
            sortColumn = column;
            sortAscending = true;
        }

        ApplyFilters();
    }

    [RelayCommand]
    private void OpenDialog()
    {
        ClearDialog();
        IsDialogOpen = true;
        IsEditing = false;
        EditingTransactionId = null;
    }

    [RelayCommand]
    private void CloseDialog()
    {
        IsDialogOpen = false;
    }

    [RelayCommand]
    private void SetNewTransactionType(string type)
    {
        NewTransactionType = type switch
        {
            "Income" => TransactionType.Income,
            "Transfer" => TransactionType.Transfer,
            _ => TransactionType.Expense
        };
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ClearErrors();

        var isValid = true;
        if (!TryParseAmount(AmountText, out var amount) || amount <= 0m)
        {
            AmountError = "Enter a valid amount.";
            isValid = false;
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            DescriptionError = "Description is required.";
            isValid = false;
        }

        if (TransactionDate is null)
        {
            DateError = "Date is required.";
            isValid = false;
        }

        if (SelectedDialogCategory is null)
        {
            CategoryError = "Category is required.";
            isValid = false;
        }

        if (SelectedDialogAccount is null || SelectedDialogAccount.Id == 0)
        {
            AccountError = "Account is required.";
            isValid = false;
        }

        if (NewTransactionType == TransactionType.Transfer)
        {
            if (SelectedDialogTargetAccount is null || SelectedDialogTargetAccount.Id == 0)
            {
                TargetAccountError = "Target account is required.";
                isValid = false;
            }
            else if (SelectedDialogAccount is not null && SelectedDialogAccount.Id == SelectedDialogTargetAccount.Id)
            {
                TargetAccountError = "From and To accounts must be different.";
                isValid = false;
            }
        }

        if (!isValid || SelectedDialogCategory is null || SelectedDialogAccount is null || TransactionDate is null)
        {
            return;
        }

        if (dbContext is not null)
        {
            Transaction transaction;
            int oldAccountId = 0;
            int? oldTargetAccountId = null;

            if (IsEditing && EditingTransactionId.HasValue)
            {
                transaction = await dbContext.Transactions.FirstAsync(t => t.Id == EditingTransactionId.Value);
                oldAccountId = transaction.AccountId;
                oldTargetAccountId = transaction.TargetAccountId;
                transaction.Amount = amount;
                transaction.Date = TransactionDate.Value;
                transaction.Description = Description.Trim();
                transaction.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
                transaction.ReceiptPath = string.IsNullOrWhiteSpace(ReceiptPath) ? null : ReceiptPath.Trim();
                transaction.CategoryId = SelectedDialogCategory.Id;
                transaction.AccountId = SelectedDialogAccount.Id;
                transaction.Type = NewTransactionType;
                transaction.IsRecurring = IsRecurring;
                transaction.TargetAccountId = NewTransactionType == TransactionType.Transfer ? SelectedDialogTargetAccount?.Id : null;
            }
            else
            {
                transaction = new Transaction
                {
                    Amount = amount,
                    Date = TransactionDate.Value,
                    Description = Description.Trim(),
                    Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                    ReceiptPath = string.IsNullOrWhiteSpace(ReceiptPath) ? null : ReceiptPath.Trim(),
                    CategoryId = SelectedDialogCategory.Id,
                    AccountId = SelectedDialogAccount.Id,
                    Type = NewTransactionType,
                    IsRecurring = IsRecurring,
                    TargetAccountId = NewTransactionType == TransactionType.Transfer ? SelectedDialogTargetAccount?.Id : null
                };
                dbContext.Transactions.Add(transaction);
            }

            await dbContext.SaveChangesAsync();
            await SyncRecurringRuleAsync(transaction.Id);
            await RecalculateAccountBalancesAsync(transaction.AccountId, transaction.TargetAccountId, oldAccountId == 0 ? null : oldAccountId, oldTargetAccountId);

            if (NewTransactionType == TransactionType.Expense && budgetAlertService is not null)
            {
                await budgetAlertService.CheckBudgetsAsync(dbContext, SelectedDialogCategory.Id);
            }

            await LoadAsync();
        }
        else
        {
            AddLocalTransaction(amount);
        }

        IsDialogOpen = false;
    }

    [RelayCommand]
    private void NextPage()
    {
        if (currentPage * PageSize < filteredCount)
        {
            currentPage++;
            ApplyFilters(resetPage: false);
        }
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (currentPage > 1)
        {
            currentPage--;
            ApplyFilters(resetPage: false);
        }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"transactions-{DateTime.Today:yyyy-MM-dd}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var exportRows = GetFilteredTransactions();
        var lines = new List<string> { "Date,Description,Account,Category,Type,Amount,Notes" };
        lines.AddRange(exportRows.Select(transaction =>
            string.Join(
                ",",
                Escape(transaction.Date.ToString("yyyy-MM-dd")),
                Escape(transaction.Description),
                Escape(transaction.AccountName),
                Escape(transaction.CategoryName),
                Escape(transaction.Type.ToString()),
                Escape(Math.Abs(transaction.SignedAmount).ToString("F2", CurrencyCulture)),
                Escape(transaction.Notes ?? string.Empty))));

        File.WriteAllLines(dialog.FileName, lines, Encoding.UTF8);
    }

    public void FormatAmount()
    {
        if (TryParseAmount(AmountText, out var amount))
        {
            AmountText = amount.ToString("C2", CurrencyCulture);
        }
    }

    [RelayCommand]
    private void ToggleCategory(CategoryFilterOption option)
    {
        UpdateSelectedCategoryText();
        ApplyFilters();
    }

    public async Task LoadAsync()
    {
        if (dbContext is null)
        {
            return;
        }

        var categories = await dbContext.Categories.AsNoTracking().OrderBy(category => category.Name).ToListAsync();
        var accounts = await dbContext.Accounts.AsNoTracking().OrderBy(account => account.Name).ToListAsync();
        var today = DateTime.Today;
        var budgets = await dbContext.Budgets
            .AsNoTracking()
            .Where(b => b.Month == today.Month && b.Year == today.Year)
            .ToListAsync();
        budgetLimitsByCategory = budgets.ToDictionary(b => b.CategoryId, b => b.MonthlyLimit);

        var transactions = await dbContext.Transactions
            .AsNoTracking()
            .Include(transaction => transaction.Category)
            .Include(transaction => transaction.Account)
            .Include(transaction => transaction.TargetAccount)
            .OrderByDescending(transaction => transaction.Date)
            .ToListAsync();

        Categories.Clear();
        CategoryFilters.Clear();
        foreach (var category in categories)
        {
            var option = new CategoryOption(category.Id, category.Name, category.ColorHex);
            Categories.Add(option);
            CategoryFilters.Add(new CategoryFilterOption(category.Id, category.Name, category.ColorHex));
        }

        Accounts.Clear();
        Accounts.Add(new AccountOption(0, "All accounts"));
        foreach (var account in accounts)
        {
            Accounts.Add(new AccountOption(account.Id, account.Name));
        }

        SelectedAccount = Accounts.FirstOrDefault();
        SelectedDialogCategory = Categories.FirstOrDefault();
        SelectedDialogAccount = Accounts.FirstOrDefault(account => account.Id != 0);

        allTransactions = transactions.Select(ToListItem).ToList();
        if (allTransactions.Count == 0)
        {
            LoadSampleRowsOnly();
        }

        ApplyFilters();
    }

    private void LoadSampleData()
    {
        Categories.Clear();
        foreach (var category in new[]
        {
            new CategoryOption(1, "Income", "#A78BFA"),
            new CategoryOption(2, "Housing", "#8B5CF6"),
            new CategoryOption(3, "Food", "#7C3AED"),
            new CategoryOption(4, "Transport", "#6D28D9"),
            new CategoryOption(5, "Health", "#5B21B6"),
            new CategoryOption(6, "Entertainment", "#4C1D95"),
            new CategoryOption(7, "Subscriptions", "#7C3AED"),
            new CategoryOption(8, "Shopping", "#8B5CF6")
        })
        {
            Categories.Add(category);
            CategoryFilters.Add(new CategoryFilterOption(category.Id, category.Name, category.ColorHex));
        }

        Accounts.Clear();
        Accounts.Add(new AccountOption(0, "All accounts"));
        Accounts.Add(new AccountOption(1, "Checking"));
        Accounts.Add(new AccountOption(2, "Savings"));
        SelectedAccount = Accounts[0];
        SelectedDialogCategory = Categories.FirstOrDefault(category => category.Name == "Food");
        SelectedDialogAccount = Accounts.FirstOrDefault(account => account.Id == 1);

        LoadSampleRowsOnly();
        ApplyFilters();
    }

    private void LoadSampleRowsOnly()
    {
        allTransactions =
        [
            CreateRow(1, "Rent payment", "Housing", "Checking", DateTime.Today, 1850m, TransactionType.Expense, "#7C3AED", 2400m),
            CreateRow(2, "Salary deposit", "Income", "Checking", DateTime.Today.AddDays(-2), 4125m, TransactionType.Income, "#A78BFA", 8000m),
            CreateRow(3, "Grocery market", "Food", "Checking", DateTime.Today.AddDays(-3), 126.45m, TransactionType.Expense, "#8B5CF6", 650m),
            CreateRow(4, "Streaming services", "Subscriptions", "Checking", DateTime.Today.AddDays(-5), 42.99m, TransactionType.Expense, "#7C3AED", 120m),
            CreateRow(5, "Train pass", "Transport", "Checking", DateTime.Today.AddDays(-6), 78m, TransactionType.Expense, "#6D28D9", 300m)
        ];
    }

    private void ApplyFilters(bool resetPage = true)
    {
        if (resetPage)
        {
            currentPage = 1;
        }

        var selectedCategoryIds = CategoryFilters
            .Where(category => category.IsSelected)
            .Select(category => category.Id)
            .ToHashSet();

        IEnumerable<TransactionListItem> query = allTransactions;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(transaction =>
                transaction.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || (transaction.Notes?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (FromDate is not null)
        {
            query = query.Where(transaction => transaction.Date >= FromDate.Value.Date);
        }

        if (ToDate is not null)
        {
            query = query.Where(transaction => transaction.Date <= ToDate.Value.Date);
        }

        if (selectedCategoryIds.Count > 0)
        {
            query = query.Where(transaction => selectedCategoryIds.Contains(transaction.CategoryId));
        }

        if (SelectedAccount is { Id: > 0 })
        {
            query = query.Where(transaction => transaction.AccountId == SelectedAccount.Id || transaction.TargetAccountId == SelectedAccount.Id);
        }

        if (SelectedType is not null)
        {
            query = query.Where(transaction => transaction.Type == SelectedType);
        }

        query = (sortColumn, sortAscending) switch
        {
            ("Description", true) => query.OrderBy(transaction => transaction.Description),
            ("Description", false) => query.OrderByDescending(transaction => transaction.Description),
            ("Category", true) => query.OrderBy(transaction => transaction.CategoryName),
            ("Category", false) => query.OrderByDescending(transaction => transaction.CategoryName),
            ("Account", true) => query.OrderBy(transaction => transaction.AccountName),
            ("Account", false) => query.OrderByDescending(transaction => transaction.AccountName),
            ("Amount", true) => query.OrderBy(transaction => transaction.SignedAmount),
            ("Amount", false) => query.OrderByDescending(transaction => transaction.SignedAmount),
            ("Date", true) => query.OrderBy(transaction => transaction.Date),
            _ => query.OrderByDescending(transaction => transaction.Date)
        };

        filteredTransactions = query.ToList();
        filteredCount = filteredTransactions.Count;

        var skip = (currentPage - 1) * PageSize;
        var pageItems = filteredTransactions.Skip(skip).Take(PageSize).ToList();

        Transactions.Clear();
        foreach (var transaction in pageItems)
        {
            Transactions.Add(transaction);
        }

        if (filteredCount == 0)
        {
            PaginationText = "Showing 0-0 of 0 transactions";
        }
        else
        {
            var start = skip + 1;
            var end = Math.Min(skip + PageSize, filteredCount);
            PaginationText = $"Showing {start}-{end} of {filteredCount} transactions";
        }
    }

    private void UpdateSelectedCategoryText()
    {
        var selected = CategoryFilters.Where(category => category.IsSelected).Select(category => category.Name).ToList();
        SelectedCategoryText = selected.Count switch
        {
            0 => "All categories",
            1 => selected[0],
            _ => $"{selected.Count} categories"
        };
    }

    private TransactionListItem ToListItem(Transaction transaction)
    {
        var categoryColor = transaction.Category?.ColorHex ?? "#64748B";

        var isTargetAccountSelected = SelectedAccount is not null && SelectedAccount.Id == transaction.TargetAccountId;
        var signedAmount = transaction.Type switch
        {
            TransactionType.Income => transaction.Amount,
            TransactionType.Expense => -transaction.Amount,
            TransactionType.Transfer => isTargetAccountSelected ? transaction.Amount : -transaction.Amount,
            _ => transaction.Amount
        };

        var accountName = transaction.Account?.Name ?? "Unknown";
        if (transaction.Type == TransactionType.Transfer && transaction.TargetAccount is not null)
        {
            accountName = $"{transaction.Account?.Name} → {transaction.TargetAccount?.Name}";
        }

        return CreateRow(
            transaction.Id,
            transaction.Description,
            transaction.Category?.Name ?? "Uncategorized",
            accountName,
            transaction.Date,
            transaction.Amount,
            transaction.Type,
            categoryColor,
            GetBudgetLimit(transaction.CategoryId),
            transaction.CategoryId,
            transaction.AccountId,
            transaction.TargetAccountId,
            transaction.TargetAccount?.Name,
            signedAmount,
            transaction.Notes,
            CurrencyCulture);
    }

    private static TransactionListItem CreateRow(
        int id,
        string description,
        string categoryName,
        string accountName,
        DateTime date,
        decimal amount,
        TransactionType type,
        string categoryColor,
        decimal monthlyLimit,
        int categoryId = 0,
        int accountId = 0,
        int? targetAccountId = null,
        string? targetAccountName = null,
        decimal? customSignedAmount = null,
        string? notes = null,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.GetCultureInfo("en-US");
        var signedAmount = customSignedAmount ?? (type == TransactionType.Expense ? -amount : amount);
        var paletteColor = NormalizePaletteColor(categoryColor);
        var fillCount = monthlyLimit <= 0m ? 0 : (int)Math.Ceiling(Math.Clamp(amount / monthlyLimit, 0m, 1m) * 20m);

        return new TransactionListItem(
            id,
            categoryId,
            accountId,
            description,
            categoryName,
            accountName,
            date,
            date.ToString("MMM d", CultureInfo.CurrentCulture),
            signedAmount,
            FormatSignedMoneyStatic(signedAmount, culture),
            type,
            paletteColor,
            categoryName[..1].ToUpper(CultureInfo.CurrentCulture),
            ToBrush(paletteColor),
            ToBrush(signedAmount >= 0m ? "#DDD6FE" : "#C4B5FD"),
            ToBrushWithOpacity(paletteColor, 0.20),
            BuildSpendSegments(fillCount, paletteColor),
            targetAccountId,
            targetAccountName,
            notes);
    }

    private void AddLocalTransaction(decimal amount)
    {
        if (SelectedDialogCategory is null || SelectedDialogAccount is null || TransactionDate is null)
        {
            return;
        }

        allTransactions.Insert(0, CreateRow(
            0,
            Description.Trim(),
            SelectedDialogCategory.Name,
            SelectedDialogAccount.Name,
            TransactionDate.Value,
            amount,
            NewTransactionType,
            SelectedDialogCategory.ColorHex,
            GetBudgetLimit(SelectedDialogCategory.Id),
            SelectedDialogCategory.Id,
            SelectedDialogAccount.Id,
            SelectedDialogTargetAccount?.Id,
            SelectedDialogTargetAccount?.Name,
            null,
            null,
            CurrencyCulture));

        ApplyFilters();
    }

    private async Task RecalculateAccountBalancesAsync(params int?[] accountIds)
    {
        if (dbContext is null) return;
        var ids = accountIds.Where(id => id.HasValue && id.Value > 0).Select(id => id!.Value).Distinct().ToList();
        foreach (var id in ids)
        {
            var account = await dbContext.Accounts.FindAsync(id);
            if (account is null) continue;
            var income      = (decimal)await dbContext.Transactions.Where(t => t.AccountId == id && t.Type == TransactionType.Income).SumAsync(t => (double)t.Amount);
            var expense     = (decimal)await dbContext.Transactions.Where(t => t.AccountId == id && t.Type == TransactionType.Expense).SumAsync(t => (double)t.Amount);
            var transferOut = (decimal)await dbContext.Transactions.Where(t => t.AccountId == id && t.Type == TransactionType.Transfer).SumAsync(t => (double)t.Amount);
            var transferIn  = (decimal)await dbContext.Transactions.Where(t => t.TargetAccountId == id && t.Type == TransactionType.Transfer).SumAsync(t => (double)t.Amount);
            account.Balance = account.OpeningBalance + income - expense - transferOut + transferIn;
        }
        await dbContext.SaveChangesAsync();
    }

    private decimal GetBudgetLimit(int categoryId) =>
        budgetLimitsByCategory.TryGetValue(categoryId, out var limit) && limit > 0m ? limit : 0m;

    private List<TransactionListItem> GetFilteredTransactions() => filteredTransactions;

    private async Task SyncRecurringRuleAsync(int transactionId)
    {
        if (dbContext is null || TransactionDate is null)
        {
            return;
        }

        var transaction = await dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId);
        if (transaction is null)
        {
            return;
        }

        var existingRule = await dbContext.RecurringRules.FirstOrDefaultAsync(r => r.TransactionId == transactionId);

        if (!IsRecurring)
        {
            if (existingRule is not null)
            {
                dbContext.RecurringRules.Remove(existingRule);
                await dbContext.SaveChangesAsync();
            }

            return;
        }

        if (!Enum.TryParse<RecurrenceFrequency>(SelectedRecurrenceFrequency, out var frequency))
        {
            frequency = RecurrenceFrequency.Monthly;
        }

        var next = RecurringScheduleHelper.NextFrom(TransactionDate.Value, frequency);

        if (existingRule is null)
        {
            dbContext.RecurringRules.Add(new RecurringRule
            {
                TransactionId = transactionId,
                Frequency = frequency,
                NextOccurrence = next,
                IsPaused = false
            });
        }
        else
        {
            existingRule.Frequency = frequency;
            if (existingRule.NextOccurrence < DateTime.Today)
            {
                existingRule.NextOccurrence = next;
            }
        }

        transaction.IsRecurring = true;
        await dbContext.SaveChangesAsync();
    }

    private void ClearDialog()
    {
        AmountText = string.Empty;
        Description = string.Empty;
        TransactionDate = DateTime.Today;
        Notes = string.Empty;
        ReceiptPath = string.Empty;
        IsRecurring = false;
        SelectedRecurrenceFrequency = nameof(RecurrenceFrequency.Monthly);
        NewTransactionType = TransactionType.Expense;
        SelectedDialogCategory ??= Categories.FirstOrDefault();
        SelectedDialogAccount ??= Accounts.FirstOrDefault(account => account.Id != 0);
        SelectedDialogTargetAccount = null;
        IsEditing = false;
        EditingTransactionId = null;
        ClearErrors();
    }

    private void ClearErrors()
    {
        AmountError = string.Empty;
        DescriptionError = string.Empty;
        DateError = string.Empty;
        CategoryError = string.Empty;
        AccountError = string.Empty;
        TargetAccountError = string.Empty;
    }

    [RelayCommand]
    private async Task EditTransactionAsync(TransactionListItem item)
    {
        if (dbContext is null) return;
        
        var transaction = await dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == item.Id);
        if (transaction is null) return;

        ClearDialog();
        IsDialogOpen = true;
        IsEditing = true;
        EditingTransactionId = item.Id;
        
        AmountText = transaction.Amount.ToString("F2");
        NewTransactionType = transaction.Type;
        Description = transaction.Description;
        TransactionDate = transaction.Date;
        SelectedDialogCategory = Categories.FirstOrDefault(c => c.Id == transaction.CategoryId);
        SelectedDialogAccount = Accounts.FirstOrDefault(a => a.Id == transaction.AccountId);
        SelectedDialogTargetAccount = Accounts.FirstOrDefault(a => a.Id == (transaction.TargetAccountId ?? 0));
        Notes = transaction.Notes ?? string.Empty;
        ReceiptPath = transaction.ReceiptPath ?? string.Empty;
        IsRecurring = transaction.IsRecurring;

        var rule = await dbContext.RecurringRules.FirstOrDefaultAsync(r => r.TransactionId == transaction.Id);
        SelectedRecurrenceFrequency = rule?.Frequency.ToString() ?? nameof(RecurrenceFrequency.Monthly);
    }

    [RelayCommand]
    private async Task DeleteTransactionAsync(TransactionListItem item)
    {
        if (dbContext is null) return;
        
        var result = System.Windows.MessageBox.Show($"Are you sure you want to delete \"{item.Description}\"?", "Confirm Delete", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result == System.Windows.MessageBoxResult.Yes)
        {
            var transaction = await dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == item.Id);
            if (transaction is not null)
            {
                var accountId = transaction.AccountId;
                var targetId = transaction.TargetAccountId;
                dbContext.Transactions.Remove(transaction);
                await dbContext.SaveChangesAsync();
                await RecalculateAccountBalancesAsync(accountId, targetId);
                await LoadAsync();
            }
        }
    }

    [RelayCommand]
    private void AttachReceipt()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Receipts|*.pdf;*.png;*.jpg;*.jpeg;*.webp|All files|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            ReceiptPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void DuplicateTransaction(TransactionListItem item)
    {
        ClearDialog();
        IsDialogOpen = true;
        IsEditing = false;
        EditingTransactionId = null;
        
        AmountText = Math.Abs(item.SignedAmount).ToString("F2");
        NewTransactionType = item.Type;
        Description = item.Description + " (Copy)";
        TransactionDate = DateTime.Today;
        SelectedDialogCategory = Categories.FirstOrDefault(c => c.Id == item.CategoryId);
        SelectedDialogAccount = Accounts.FirstOrDefault(a => a.Id == item.AccountId);
        SelectedDialogTargetAccount = Accounts.FirstOrDefault(a => a.Id == (item.TargetAccountId ?? 0));
        Notes = string.Empty;
        IsRecurring = false;
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync(object parameter)
    {
        if (dbContext is null || parameter is not System.Collections.IList selectedList || selectedList.Count == 0) return;
        
        var itemsToDelete = selectedList.Cast<TransactionListItem>().ToList();
        var result = System.Windows.MessageBox.Show($"Are you sure you want to delete {itemsToDelete.Count} selected transaction(s)?", "Confirm Bulk Delete", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result == System.Windows.MessageBoxResult.Yes)
        {
            var affectedAccountIds = itemsToDelete.SelectMany(i => new int?[] { i.AccountId, i.TargetAccountId }).ToArray();
            foreach (var item in itemsToDelete)
            {
                var transaction = await dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == item.Id);
                if (transaction is not null)
                {
                    dbContext.Transactions.Remove(transaction);
                }
            }
            await dbContext.SaveChangesAsync();
            await RecalculateAccountBalancesAsync(affectedAccountIds);
            await LoadAsync();
        }
    }

    private static ObservableCollection<SpendSegment> BuildSpendSegments(int filledCount, string colorHex)
    {
        var segments = new ObservableCollection<SpendSegment>();
        var filledBrush = ToBrush(colorHex);
        var emptyBrush = ToBrush("#1E293B");

        for (var index = 0; index < 20; index++)
        {
            segments.Add(new SpendSegment(index < filledCount ? filledBrush : emptyBrush));
        }

        return segments;
    }

    private bool TryParseAmount(string value, out decimal amount)
    {
        var cleaned = value.Replace("$", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.CurrentCulture, out amount)
            || decimal.TryParse(cleaned, NumberStyles.Number, CurrencyCulture, out amount);
    }

    private string FormatSignedMoney(decimal amount) => FormatSignedMoneyStatic(amount, CurrencyCulture);

    private static string FormatSignedMoneyStatic(decimal amount, CultureInfo culture)
    {
        var prefix = amount >= 0m ? "+" : "-";
        return $"{prefix}{Math.Abs(amount).ToString("C2", culture)}";
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

    private static SolidColorBrush ToBrushWithOpacity(string colorHex, double opacity)
    {
        var brush = ToBrush(colorHex);
        return new SolidColorBrush(brush.Color) { Opacity = opacity };
    }

    private static string NormalizePaletteColor(string colorHex)
    {
        var index = Math.Abs(colorHex.GetHashCode(StringComparison.Ordinal)) % Palette.Length;
        return Palette[index];
    }
}

/// <summary>
/// Represents a selectable category filter in the transactions list.
/// </summary>
public partial class CategoryFilterOption(int id, string name, string colorHex) : ObservableObject
{
    [ObservableProperty]
    private bool isSelected;

    public int Id { get; } = id;

    public string Name { get; } = name;

    public string ColorHex { get; } = colorHex;
}

/// <summary>
/// Represents a category option for filters and transaction entry.
/// </summary>
public sealed record CategoryOption(int Id, string Name, string ColorHex)
{
    public Brush ColorBrush { get; } = CreateBrush(ColorHex);

    private static SolidColorBrush CreateBrush(string colorHex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!;
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Represents an account option for filters and transaction entry.
/// </summary>
public sealed record AccountOption(int Id, string Name)
{
    public override string ToString() => Name;
}


/// <summary>
/// Represents one segment in a compact spend indicator.
/// </summary>
public sealed record SpendSegment(Brush Fill);

/// <summary>
/// Represents a row in the transactions table.
/// </summary>
public sealed record TransactionListItem(
    int Id,
    int CategoryId,
    int AccountId,
    string Description,
    string CategoryName,
    string AccountName,
    DateTime Date,
    string DateText,
    decimal SignedAmount,
    string AmountText,
    TransactionType Type,
    string CategoryColor,
    string Initial,
    Brush CategoryBrush,
    Brush AmountBrush,
    Brush CategoryBadgeBrush,
    ObservableCollection<SpendSegment> SpendSegments,
    int? TargetAccountId = null,
    string? TargetAccountName = null,
    string? Notes = null);
