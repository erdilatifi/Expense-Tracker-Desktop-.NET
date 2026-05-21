using System.Globalization;
using FinanceTracker.Data;
using FinanceTracker.Models;
using FinanceTracker.Services;

namespace FinanceTracker.ViewModels;

public partial class RecurringViewModel : ObservableObject
{
    private readonly FinanceDbContext? dbContext;
    private readonly AppSettingsService? appSettings;

    public ObservableCollection<RecurringListItem> UpcomingItems { get; } = [];

    private CultureInfo CurrencyCulture => appSettings?.CurrencyCulture ?? CultureInfo.GetCultureInfo("en-US");

    public RecurringViewModel()
    {
    }

    public RecurringViewModel(FinanceDbContext dbContext, AppSettingsService? appSettings = null)
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

        var horizon = DateTime.Today.AddDays(30);
        var rules = await dbContext.RecurringRules
            .AsNoTracking()
            .Include(r => r.Transaction)
            .ThenInclude(t => t!.Category)
            .Include(r => r.Transaction)
            .ThenInclude(t => t!.Account)
            .Where(r => r.NextOccurrence <= horizon)
            .OrderBy(r => r.NextOccurrence)
            .ToListAsync();

        UpcomingItems.Clear();
        foreach (var rule in rules)
        {
            if (rule.Transaction is null)
            {
                continue;
            }

            UpcomingItems.Add(new RecurringListItem(
                rule.Id,
                rule.Transaction.Description,
                rule.Transaction.Category?.Name ?? "—",
                rule.Transaction.Account?.Name ?? "—",
                rule.NextOccurrence,
                rule.NextOccurrence.ToString("MMM d, yyyy", CultureInfo.CurrentCulture),
                rule.Frequency.ToString(),
                rule.IsPaused,
                rule.Transaction.Amount.ToString("C2", CurrencyCulture)));
        }

        if (UpcomingItems.Count == 0)
        {
            LoadSampleItems();
        }
    }

    private void LoadSampleItems()
    {
        var today = DateTime.Today;
        UpcomingItems.Add(new RecurringListItem(0, "Rent payment", "Housing", "Checking",
            new DateTime(today.Year, today.Month, 1).AddMonths(1),
            new DateTime(today.Year, today.Month, 1).AddMonths(1).ToString("MMM d, yyyy", CultureInfo.CurrentCulture),
            "Monthly", false, "$1,850.00"));
        UpcomingItems.Add(new RecurringListItem(0, "Streaming services", "Subscriptions", "Checking",
            today.AddDays(7),
            today.AddDays(7).ToString("MMM d, yyyy", CultureInfo.CurrentCulture),
            "Monthly", false, "$42.99"));
        UpcomingItems.Add(new RecurringListItem(0, "Train pass", "Transport", "Checking",
            today.AddDays(12),
            today.AddDays(12).ToString("MMM d, yyyy", CultureInfo.CurrentCulture),
            "Monthly", false, "$78.00"));
        UpcomingItems.Add(new RecurringListItem(0, "Internet bill", "Subscriptions", "Checking",
            today.AddDays(18),
            today.AddDays(18).ToString("MMM d, yyyy", CultureInfo.CurrentCulture),
            "Monthly", false, "$59.99"));
        UpcomingItems.Add(new RecurringListItem(0, "Gym membership", "Health", "Checking",
            today.AddDays(22),
            today.AddDays(22).ToString("MMM d, yyyy", CultureInfo.CurrentCulture),
            "Monthly", true, "$39.99"));
    }

    [RelayCommand]
    private async Task TogglePauseAsync(RecurringListItem item)
    {
        if (dbContext is null)
        {
            return;
        }

        var rule = await dbContext.RecurringRules.FindAsync(item.RuleId);
        if (rule is null)
        {
            return;
        }

        rule.IsPaused = !rule.IsPaused;
        await dbContext.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SkipNextAsync(RecurringListItem item)
    {
        if (dbContext is null)
        {
            return;
        }

        var rule = await dbContext.RecurringRules.FindAsync(item.RuleId);
        if (rule is null)
        {
            return;
        }

        rule.NextOccurrence = RecurringScheduleHelper.NextFrom(rule.NextOccurrence, rule.Frequency);
        await dbContext.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(RecurringListItem item)
    {
        if (dbContext is null)
        {
            return;
        }

        var rule = await dbContext.RecurringRules.FindAsync(item.RuleId);
        if (rule is not null)
        {
            dbContext.RecurringRules.Remove(rule);
            await dbContext.SaveChangesAsync();
        }

        await LoadAsync();
    }
}

public sealed record RecurringListItem(
    int RuleId,
    string Description,
    string CategoryName,
    string AccountName,
    DateTime NextOccurrence,
    string NextOccurrenceText,
    string Frequency,
    bool IsPaused,
    string AmountText)
{
    public bool IsFromDb => RuleId > 0;
}
