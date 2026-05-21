using FinanceTracker.Data;
using FinanceTracker.Models;

namespace FinanceTracker.Services;

/// <summary>
/// Materializes due recurring rules into dated transactions during application startup.
/// </summary>
public sealed class RecurringService(FinanceDbContext dbContext, NotificationService notificationService)
{
    public async Task ProcessDueRulesAsync()
    {
        var today = DateTime.Today;
        var dueRules = await dbContext.RecurringRules
            .Include(rule => rule.Transaction)
            .Where(rule => !rule.IsPaused && rule.NextOccurrence <= today)
            .ToListAsync();

        var added = 0;
        foreach (var rule in dueRules)
        {
            if (rule.Transaction is null)
            {
                continue;
            }

            dbContext.Transactions.Add(new Transaction
            {
                Amount = rule.Transaction.Amount,
                Date = today,
                Description = rule.Transaction.Description,
                Notes = rule.Transaction.Notes,
                CategoryId = rule.Transaction.CategoryId,
                AccountId = rule.Transaction.AccountId,
                TargetAccountId = rule.Transaction.TargetAccountId,
                Type = rule.Transaction.Type,
                IsRecurring = true,
                RecurringRuleId = rule.Id
            });

            rule.NextOccurrence = AdvancePastToday(rule.NextOccurrence, rule.Frequency, today);
            added++;
        }

        if (added == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync();

        var affectedIds = dueRules
            .Where(r => r.Transaction is not null)
            .SelectMany(r => new[] { r.Transaction!.AccountId, r.Transaction.TargetAccountId ?? 0 })
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        foreach (var id in affectedIds)
        {
            var account = await dbContext.Accounts.FindAsync(id);
            if (account is null) continue;
            var income  = (decimal)await dbContext.Transactions.Where(t => t.AccountId == id && t.Type == TransactionType.Income).SumAsync(t => (double)t.Amount);
            var expense = (decimal)await dbContext.Transactions.Where(t => t.AccountId == id && t.Type == TransactionType.Expense).SumAsync(t => (double)t.Amount);
            var txOut   = (decimal)await dbContext.Transactions.Where(t => t.AccountId == id && t.Type == TransactionType.Transfer).SumAsync(t => (double)t.Amount);
            var txIn    = (decimal)await dbContext.Transactions.Where(t => t.TargetAccountId == id && t.Type == TransactionType.Transfer).SumAsync(t => (double)t.Amount);
            account.Balance = account.OpeningBalance + income - expense - txOut + txIn;
        }

        await dbContext.SaveChangesAsync();
        notificationService.Add($"{added} recurring transaction{(added == 1 ? string.Empty : "s")} added", NotificationKind.RecurringTransactions);
    }

    private static DateTime AdvancePastToday(DateTime nextOccurrence, RecurrenceFrequency frequency, DateTime today)
    {
        var next = AddFrequency(nextOccurrence, frequency);
        while (next <= today)
        {
            next = AddFrequency(next, frequency);
        }

        return next;
    }

    private static DateTime AddFrequency(DateTime date, RecurrenceFrequency frequency)
    {
        return frequency switch
        {
            RecurrenceFrequency.Daily => date.AddDays(1),
            RecurrenceFrequency.Weekly => date.AddDays(7),
            RecurrenceFrequency.Monthly => date.AddMonths(1),
            RecurrenceFrequency.Yearly => date.AddYears(1),
            _ => date.AddMonths(1)
        };
    }
}
