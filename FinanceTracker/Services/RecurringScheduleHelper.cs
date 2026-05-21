using FinanceTracker.Models;

namespace FinanceTracker.Services;

public static class RecurringScheduleHelper
{
    public static DateTime NextFrom(DateTime date, RecurrenceFrequency frequency) =>
        frequency switch
        {
            RecurrenceFrequency.Daily => date.AddDays(1),
            RecurrenceFrequency.Weekly => date.AddDays(7),
            RecurrenceFrequency.Monthly => date.AddMonths(1),
            RecurrenceFrequency.Yearly => date.AddYears(1),
            _ => date.AddMonths(1)
        };
}
