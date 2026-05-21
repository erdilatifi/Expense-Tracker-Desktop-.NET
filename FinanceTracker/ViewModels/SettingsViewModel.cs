using System.IO;
using System.Windows;
using System.Windows.Media;
using FinanceTracker.Data;
using FinanceTracker.Models;
using FinanceTracker.Services;
using Microsoft.Win32;

namespace FinanceTracker.ViewModels;

/// <summary>
/// Provides account/category maintenance, database utilities, and import wizard state.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly FinanceDbContext? dbContext;
    private readonly Services.NotificationService? notificationService;
    private readonly AppSettingsService appSettings;
    private bool isLoading;

    [ObservableProperty]
    private string selectedSection = "Accounts";

    [ObservableProperty]
    private string currency = "USD";

    [ObservableProperty]
    private string dateFormat = "MMM d, yyyy";

    [ObservableProperty]
    private string firstDayOfWeek = "Monday";

    [ObservableProperty]
    private string accentColor = "#7C3AED";

    [ObservableProperty]
    private double fontSize = 14;

    [ObservableProperty]
    private double sidebarWidth = 220;

    [ObservableProperty]
    private bool isImportWizardOpen;

    [ObservableProperty]
    private int importStep = 1;

    [ObservableProperty]
    private string importFilePath = string.Empty;

    [ObservableProperty]
    private string dateColumn = string.Empty;

    [ObservableProperty]
    private string amountColumn = string.Empty;

    [ObservableProperty]
    private string descriptionColumn = string.Empty;

    [ObservableProperty]
    private string categoryColumn = string.Empty;

    [ObservableProperty]
    private string accountColumn = string.Empty;

    [ObservableProperty]
    private string typeColumn = string.Empty;

    [ObservableProperty]
    private string importSummary = "No file selected";

    public ObservableCollection<string> Sections { get; } = ["Accounts", "Categories", "General", "Data", "About"];

    public ObservableCollection<string> Currencies { get; } = ["USD", "EUR", "GBP", "CAD", "AUD", "JPY", "CHF"];

    public ObservableCollection<string> DateFormats { get; } = ["MMM d, yyyy", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"];

    public ObservableCollection<string> WeekDays { get; } = ["Monday", "Sunday", "Saturday"];

    public ObservableCollection<ColorSwatch> AccentSwatches { get; } =
    [
        new("Violet", "#7C3AED"),
        new("Lavender", "#8B5CF6"),
        new("Amethyst", "#A78BFA"),
        new("Royal", "#6D28D9"),
        new("Indigo", "#5B21B6"),
        new("Plum", "#4C1D95"),
        new("Teal", "#14B8A6"),
        new("Rose", "#F472B6")
    ];

    public ObservableCollection<SettingsAccountRow> AccountRows { get; } = [];

    public ObservableCollection<SettingsCategoryRow> CategoryRows { get; } = [];

    public ObservableCollection<string> AccountTypes { get; } = [.. Enum.GetNames<AccountType>()];

    public ObservableCollection<string> CategoryTypes { get; } = [.. Enum.GetNames<CategoryType>()];

    public ObservableCollection<string> IconOptions { get; } =
    [
        "home", "restaurant", "directions_car", "local_hospital", "movie", "subscriptions", "shopping_bag", "payments",
        "flight", "school", "fitness", "pets", "coffee", "gift", "phone", "wifi",
        "bolt", "water", "work", "child", "savings", "credit_card", "wallet", "receipt",
        "store", "local_gas_station", "train", "directions_bus", "medical_services", "sports_esports", "book", "more"
    ];

    public ObservableCollection<CsvPreviewRow> PreviewRows { get; } = [];

    public ObservableCollection<string> DetectedColumns { get; } = [];

    public SettingsViewModel()
        : this(null, null, new AppSettingsService())
    {
    }

    public SettingsViewModel(FinanceDbContext? dbContext, Services.NotificationService? notificationService = null, AppSettingsService? appSettings = null)
    {
        this.dbContext = dbContext;
        this.notificationService = notificationService;
        this.appSettings = appSettings ?? new AppSettingsService();
        this.appSettings.Load();
        LoadSettingsFromService();
        if (dbContext is null)
        {
            LoadSampleRows();
        }
        else
        {
            _ = LoadRowsAsync();
        }
    }

    partial void OnCurrencyChanged(string value) => PersistSettings();

    partial void OnDateFormatChanged(string value) => PersistSettings();

    partial void OnFirstDayOfWeekChanged(string value) => PersistSettings();

    partial void OnAccentColorChanged(string value)
    {
        PersistSettings();
        App.ApplyAccentColor(value);
    }

    partial void OnFontSizeChanged(double value) => PersistSettings();

    partial void OnSidebarWidthChanged(double value) => PersistSettings();

    [RelayCommand]
    private void SelectSection(string section)
    {
        SelectedSection = section;
    }

    [RelayCommand]
    private void SelectAccent(ColorSwatch swatch)
    {
        AccentColor = swatch.ColorHex;
    }

    [RelayCommand]
    private async Task AddAccountAsync()
    {
        if (dbContext is not null)
        {
            dbContext.Accounts.Add(new Account { Name = "New account", OpeningBalance = 0m, Balance = 0m, Currency = Currency, Type = AccountType.Checking, ColorHex = AccentColor });
            await dbContext.SaveChangesAsync();
            await LoadRowsAsync();
        }
        else
        {
            AccountRows.Add(new SettingsAccountRow(0, "New account", 0m, 0m, null, AccentColor, AccountType.Checking.ToString()));
        }
    }

    [RelayCommand]
    private async Task DeleteAccountAsync(SettingsAccountRow row)
    {
        if (dbContext is not null && row.Id > 0)
        {
            var isUsed = await dbContext.Transactions.AnyAsync(t => t.AccountId == row.Id || t.TargetAccountId == row.Id);
            if (isUsed)
            {
                MessageBox.Show("This account cannot be deleted because it has transactions associated with it.", "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var confirm = MessageBox.Show($"Are you sure you want to delete the account \"{row.Name}\"?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            var account = await dbContext.Accounts.FindAsync(row.Id);
            if (account is not null)
            {
                dbContext.Accounts.Remove(account);
                await dbContext.SaveChangesAsync();
            }
        }

        AccountRows.Remove(row);
    }

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        if (dbContext is not null)
        {
            dbContext.Categories.Add(new Category { Name = "New category", IconCode = "more", ColorHex = AccentColor, Type = CategoryType.Expense });
            await dbContext.SaveChangesAsync();
            await LoadRowsAsync();
        }
        else
        {
            CategoryRows.Add(new SettingsCategoryRow(0, "New category", AccentColor, "more", CategoryType.Expense.ToString(), 0));
        }
    }

    [RelayCommand]
    private async Task SaveAccountsAsync()
    {
        if (dbContext is null)
        {
            return;
        }

        foreach (var row in AccountRows)
        {
            if (row.Id <= 0)
            {
                continue;
            }

            var account = await dbContext.Accounts.FindAsync(row.Id);
            if (account is null)
            {
                continue;
            }

            account.Name = row.Name.Trim();
            account.OpeningBalance = row.OpeningBalance;
            account.CreditLimit = row.CreditLimit;
            account.ColorHex = row.ColorHex;
            if (Enum.TryParse<AccountType>(row.Type, out var accountType))
            {
                account.Type = accountType;
            }
        }

        await dbContext.SaveChangesAsync();
        await LoadRowsAsync();
        notificationService?.Add("Accounts saved", Services.NotificationKind.General);
    }

    [RelayCommand]
    private async Task SaveCategoriesAsync()
    {
        if (dbContext is null)
        {
            return;
        }

        foreach (var row in CategoryRows)
        {
            if (row.Id <= 0)
            {
                continue;
            }

            var category = await dbContext.Categories.FindAsync(row.Id);
            if (category is null)
            {
                continue;
            }

            category.Name = row.Name.Trim();
            category.ColorHex = row.ColorHex;
            category.IconCode = row.IconCode;
            if (Enum.TryParse<CategoryType>(row.Type, out var categoryType))
            {
                category.Type = categoryType;
            }
        }

        await dbContext.SaveChangesAsync();
        await LoadRowsAsync();
        notificationService?.Add("Categories saved", Services.NotificationKind.General);
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync(SettingsCategoryRow row)
    {
        if (dbContext is not null && row.Id > 0)
        {
            var transactions = await dbContext.Transactions.Where(t => t.CategoryId == row.Id).ToListAsync();
            if (transactions.Count > 0)
            {
                var others = await dbContext.Categories.Where(c => c.Id != row.Id).OrderBy(c => c.Name).ToListAsync();
                if (others.Count == 0)
                {
                    MessageBox.Show("Create another category before deleting this one.", "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var names = string.Join(", ", others.Select(c => c.Name));
                var replacementName = InputDialogService.Prompt(
                    "Reassign transactions",
                    $"Category \"{row.Name}\" is used by {transactions.Count} transaction(s).\nEnter replacement category name:\n({names})");

                if (string.IsNullOrWhiteSpace(replacementName))
                {
                    return;
                }

                var replacement = others.FirstOrDefault(c => c.Name.Equals(replacementName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (replacement is null)
                {
                    MessageBox.Show("Replacement category not found.", "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                foreach (var transaction in transactions)
                {
                    transaction.CategoryId = replacement.Id;
                }
            }

            var confirm = MessageBox.Show($"Are you sure you want to delete the category \"{row.Name}\"?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var category = await dbContext.Categories.FindAsync(row.Id);
            if (category is not null)
            {
                dbContext.Categories.Remove(category);
                await dbContext.SaveChangesAsync();
            }
        }

        CategoryRows.Remove(row);
        await LoadRowsAsync();
    }

    [RelayCommand]
    private void OpenImportWizard()
    {
        ImportStep = 1;
        IsImportWizardOpen = true;
    }

    [RelayCommand]
    private void CloseImportWizard()
    {
        IsImportWizardOpen = false;
    }

    [RelayCommand]
    private void PickImportFile()
    {
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
        {
            ImportFilePath = dialog.FileName;
            LoadCsvPreview(dialog.FileName);
        }
    }

    [RelayCommand]
    private void NextImportStep()
    {
        if (ImportStep < 3)
        {
            ImportStep++;
            if (ImportStep == 3)
            {
                var linesCount = 0;
                if (File.Exists(ImportFilePath))
                {
                    linesCount = Math.Max(0, File.ReadLines(ImportFilePath).Count() - 1);
                }
                ImportSummary = $"{linesCount} transactions found, ready to import.";
            }
        }
    }

    [RelayCommand]
    private void PreviousImportStep()
    {
        if (ImportStep > 1)
        {
            ImportStep--;
        }
    }

    [RelayCommand]
    private async Task ConfirmImportAsync()
    {
        if (dbContext is null || string.IsNullOrEmpty(ImportFilePath) || !File.Exists(ImportFilePath))
        {
            IsImportWizardOpen = false;
            return;
        }

        try
        {
            var lines = File.ReadLines(ImportFilePath).ToList();
            if (lines.Count <= 1)
            {
                IsImportWizardOpen = false;
                return;
            }

            var headers = SplitCsvLine(lines[0]).ToList();
            var dateIdx = headers.IndexOf(DateColumn);
            var amountIdx = headers.IndexOf(AmountColumn);
            var descIdx = headers.IndexOf(DescriptionColumn);
            var categoryIdx = headers.IndexOf(CategoryColumn);
            var accountIdx = headers.IndexOf(AccountColumn);
            var typeIdx = headers.IndexOf(TypeColumn);

            if (dateIdx < 0 || amountIdx < 0 || descIdx < 0)
            {
                notificationService?.Add("Import failed: Date, Amount, and Description columns must be mapped.", Services.NotificationKind.General);
                IsImportWizardOpen = false;
                return;
            }

            var existingCategories = await dbContext.Categories.ToListAsync();
            var existingAccounts = await dbContext.Accounts.ToListAsync();
            var existingTransactions = await dbContext.Transactions.ToListAsync();

            var importedCount = 0;
            var skippedCount = 0;
            var newTransactions = new List<Transaction>();

            var defaultAccount = existingAccounts.FirstOrDefault() ?? new Account
            {
                Name = "Checking",
                Balance = 0m,
                Currency = Currency,
                Type = AccountType.Checking,
                ColorHex = AccentColor
            };
            if (existingAccounts.Count == 0)
            {
                dbContext.Accounts.Add(defaultAccount);
                await dbContext.SaveChangesAsync();
                existingAccounts.Add(defaultAccount);
            }

            var defaultCategory = existingCategories.FirstOrDefault(c => c.Name == "Food") ?? existingCategories.FirstOrDefault() ?? new Category
            {
                Name = "General",
                IconCode = "more",
                ColorHex = AccentColor,
                Type = CategoryType.Expense
            };
            if (existingCategories.Count == 0)
            {
                dbContext.Categories.Add(defaultCategory);
                await dbContext.SaveChangesAsync();
                existingCategories.Add(defaultCategory);
            }

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var values = SplitCsvLine(line);

                // Date
                var dateStr = values.ElementAtOrDefault(dateIdx) ?? string.Empty;
                if (!DateTime.TryParse(dateStr, out var date)) continue;

                // Amount
                var amountStr = values.ElementAtOrDefault(amountIdx) ?? string.Empty;
                amountStr = new string(amountStr.Where(c => char.IsDigit(c) || c == '.' || c == '-' || c == '+').ToArray());
                if (!decimal.TryParse(amountStr, out var amount)) continue;

                // Description
                var description = values.ElementAtOrDefault(descIdx) ?? "Imported Transaction";

                // Account
                var accountName = accountIdx >= 0 ? (values.ElementAtOrDefault(accountIdx) ?? string.Empty) : string.Empty;
                var account = existingAccounts.FirstOrDefault(a => a.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase));
                if (account == null)
                {
                    if (!string.IsNullOrEmpty(accountName))
                    {
                        account = new Account
                        {
                            Name = accountName,
                            Balance = 0m,
                            Currency = Currency,
                            Type = AccountType.Checking,
                            ColorHex = AccentColor
                        };
                        dbContext.Accounts.Add(account);
                        await dbContext.SaveChangesAsync();
                        existingAccounts.Add(account);
                    }
                    else
                    {
                        account = defaultAccount;
                    }
                }

                // Type
                var typeStr = typeIdx >= 0 ? (values.ElementAtOrDefault(typeIdx) ?? string.Empty) : string.Empty;
                var type = TransactionType.Expense;
                if (typeStr.Equals("Income", StringComparison.OrdinalIgnoreCase) || (amount > 0 && string.IsNullOrEmpty(typeStr)))
                {
                    type = TransactionType.Income;
                }
                amount = Math.Abs(amount);

                // Category
                var categoryName = categoryIdx >= 0 ? (values.ElementAtOrDefault(categoryIdx) ?? string.Empty) : string.Empty;
                var category = existingCategories.FirstOrDefault(c => c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));
                if (category == null)
                {
                    if (!string.IsNullOrEmpty(categoryName))
                    {
                        category = new Category
                        {
                            Name = categoryName,
                            IconCode = "more",
                            ColorHex = AccentColor,
                            Type = type == TransactionType.Income ? CategoryType.Income : CategoryType.Expense
                        };
                        dbContext.Categories.Add(category);
                        await dbContext.SaveChangesAsync();
                        existingCategories.Add(category);
                    }
                    else
                    {
                        category = defaultCategory;
                    }
                }

                // Check duplicates (avoid adding same transaction twice)
                var isDuplicate = existingTransactions.Any(t =>
                    t.AccountId == account.Id &&
                    t.Date.Date == date.Date &&
                    t.Amount == amount &&
                    t.Description.Equals(description, StringComparison.OrdinalIgnoreCase) &&
                    t.Type == type);

                if (isDuplicate)
                {
                    skippedCount++;
                }
                else
                {
                    var transaction = new Transaction
                    {
                        AccountId = account.Id,
                        CategoryId = category.Id,
                        Date = date,
                        Amount = amount,
                        Description = description,
                        Type = type
                    };
                    newTransactions.Add(transaction);
                    importedCount++;
                }
            }

            if (newTransactions.Count > 0)
            {
                await dbContext.Transactions.AddRangeAsync(newTransactions);
                await dbContext.SaveChangesAsync();
            }

            ImportSummary = $"{importedCount} transactions imported, {skippedCount} duplicates skipped";
            notificationService?.Add("Import completed successfully", Services.NotificationKind.ImportComplete);
        }
        catch (Exception ex)
        {
            ImportSummary = "Import failed due to an error";
            notificationService?.Add($"Import error: {ex.Message}", Services.NotificationKind.General);
        }
        finally
        {
            IsImportWizardOpen = false;
        }
    }

    [RelayCommand]
    private void BackupDatabase()
    {
        var dialog = new SaveFileDialog { Filter = "Database file (*.db)|*.db", FileName = "FinanceTracker.db" };
        if (dialog.ShowDialog() == true && File.Exists(FinanceDbContext.DatabasePath))
        {
            File.Copy(FinanceDbContext.DatabasePath, dialog.FileName, overwrite: true);
        }
    }

    [RelayCommand]
    private void RestoreDatabase()
    {
        var dialog = new OpenFileDialog { Filter = "Database file (*.db)|*.db" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (MessageBox.Show("Replace the current database with the selected file?", "Restore database", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            File.Copy(dialog.FileName, FinanceDbContext.DatabasePath, overwrite: true);
        }
    }

    [RelayCommand]
    private async Task ClearAllDataAsync()
    {
        if (MessageBox.Show("Clear all local finance data? This cannot be undone.", "Clear all data", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var confirmation = InputDialogService.Prompt("Confirm clear", "Type DELETE to confirm:");
        if (!string.Equals(confirmation, "DELETE", StringComparison.Ordinal))
        {
            return;
        }

        if (dbContext is null)
        {
            AccountRows.Clear();
            CategoryRows.Clear();
            return;
        }

        dbContext.Transactions.RemoveRange(dbContext.Transactions);
        dbContext.Budgets.RemoveRange(dbContext.Budgets);
        dbContext.Accounts.RemoveRange(dbContext.Accounts);
        dbContext.Categories.RemoveRange(dbContext.Categories);
        await dbContext.SaveChangesAsync();
        await LoadRowsAsync();
    }

    public async Task LoadRowsAsync()
    {
        if (dbContext is null)
        {
            return;
        }

        var accounts = await dbContext.Accounts.AsNoTracking().OrderBy(account => account.Name).ToListAsync();
        var categories = await dbContext.Categories.AsNoTracking().OrderBy(category => category.Name).ToListAsync();
        var usageCounts = await dbContext.Transactions
            .GroupBy(t => t.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

        AccountRows.Clear();
        foreach (var account in accounts)
        {
            AccountRows.Add(new SettingsAccountRow(
                account.Id,
                account.Name,
                account.Balance,
                account.OpeningBalance,
                account.CreditLimit,
                account.ColorHex,
                account.Type.ToString()));
        }

        CategoryRows.Clear();
        foreach (var category in categories)
        {
            usageCounts.TryGetValue(category.Id, out var count);
            CategoryRows.Add(new SettingsCategoryRow(
                category.Id,
                category.Name,
                category.ColorHex,
                category.IconCode,
                category.Type.ToString(),
                count));
        }
    }

    private void LoadSampleRows()
    {
        AccountRows.Add(new SettingsAccountRow(1, "Checking", 12480.25m, 0m, null, "#A78BFA", AccountType.Checking.ToString()));
        AccountRows.Add(new SettingsAccountRow(2, "Savings", 8320.00m, 0m, null, "#7C3AED", AccountType.Savings.ToString()));
        CategoryRows.Add(new SettingsCategoryRow(1, "Housing", "#7C3AED", "home", CategoryType.Expense.ToString(), 12));
        CategoryRows.Add(new SettingsCategoryRow(2, "Food", "#8B5CF6", "restaurant", CategoryType.Expense.ToString(), 8));
    }

    private void LoadCsvPreview(string path)
    {
        PreviewRows.Clear();
        DetectedColumns.Clear();

        var lines = File.ReadLines(path).Take(6).ToList();
        if (lines.Count == 0)
        {
            return;
        }

        var headers = SplitCsvLine(lines[0]);
        foreach (var header in headers)
        {
            DetectedColumns.Add(header);
        }

        DateColumn = headers.FirstOrDefault(header => header.Contains("date", StringComparison.OrdinalIgnoreCase)) ?? headers.FirstOrDefault() ?? string.Empty;
        AmountColumn = headers.FirstOrDefault(header => header.Contains("amount", StringComparison.OrdinalIgnoreCase)) ?? headers.Skip(1).FirstOrDefault() ?? string.Empty;
        DescriptionColumn = headers.FirstOrDefault(header => header.Contains("description", StringComparison.OrdinalIgnoreCase)) ?? headers.Skip(2).FirstOrDefault() ?? string.Empty;
        CategoryColumn = headers.FirstOrDefault(header => header.Contains("category", StringComparison.OrdinalIgnoreCase)) ?? headers.Skip(3).FirstOrDefault() ?? string.Empty;
        AccountColumn = headers.FirstOrDefault(header => header.Contains("account", StringComparison.OrdinalIgnoreCase)) ?? headers.Skip(4).FirstOrDefault() ?? string.Empty;
        TypeColumn = headers.FirstOrDefault(header => header.Contains("type", StringComparison.OrdinalIgnoreCase)) ?? headers.Skip(5).FirstOrDefault() ?? string.Empty;

        foreach (var line in lines.Skip(1))
        {
            var values = SplitCsvLine(line);
            PreviewRows.Add(new CsvPreviewRow(
                values.ElementAtOrDefault(0) ?? string.Empty,
                values.ElementAtOrDefault(1) ?? string.Empty,
                values.ElementAtOrDefault(2) ?? string.Empty,
                values.ElementAtOrDefault(3) ?? string.Empty));
        }
    }

    private void LoadSettingsFromService()
    {
        isLoading = true;
        Currency = appSettings.Currency;
        DateFormat = appSettings.DateFormat;
        FirstDayOfWeek = appSettings.FirstDayOfWeek;
        AccentColor = appSettings.AccentColor;
        FontSize = appSettings.FontSize;
        SidebarWidth = appSettings.SidebarWidth;
        isLoading = false;
    }

    private void PersistSettings()
    {
        if (isLoading)
        {
            return;
        }

        appSettings.Save(new AppSettingsState(Currency, DateFormat, FirstDayOfWeek, AccentColor, FontSize, SidebarWidth));
    }

    private static string[] SplitCsvLine(string line)
    {
        return line.Split(',').Select(value => value.Trim().Trim('"')).ToArray();
    }
}

/// <summary>
/// Represents an accent color option in settings.
/// </summary>
public sealed record ColorSwatch(string Name, string ColorHex)
{
    public Brush Brush { get; } = CreateBrush(ColorHex);

    private static SolidColorBrush CreateBrush(string colorHex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!;
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Represents an editable account row in settings.
/// </summary>
public partial class SettingsAccountRow(int id, string name, decimal balance, decimal openingBalance, decimal? creditLimit, string colorHex, string type) : ObservableObject
{
    public int Id { get; } = id;

    [ObservableProperty]
    private string name = name;

    [ObservableProperty]
    private decimal balance = balance;

    [ObservableProperty]
    private decimal openingBalance = openingBalance;

    [ObservableProperty]
    private decimal? creditLimit = creditLimit;

    [ObservableProperty]
    private string colorHex = colorHex;

    [ObservableProperty]
    private string type = type;
}

/// <summary>
/// Represents an editable category row in settings.
/// </summary>
public partial class SettingsCategoryRow(int id, string name, string colorHex, string iconCode, string type, int usageCount) : ObservableObject
{
    public int Id { get; } = id;

    [ObservableProperty]
    private string name = name;

    [ObservableProperty]
    private string colorHex = colorHex;

    [ObservableProperty]
    private string iconCode = iconCode;

    [ObservableProperty]
    private string type = type;

    [ObservableProperty]
    private int usageCount = usageCount;
}

/// <summary>
/// Represents one preview row from an imported CSV file.
/// </summary>
public sealed record CsvPreviewRow(string Column1, string Column2, string Column3, string Column4);

