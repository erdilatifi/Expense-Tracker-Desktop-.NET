namespace FinanceTracker.Models;

public enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly
}

public class RecurringRule
{
    public int Id { get; set; }

    public int TransactionId { get; set; }

    public RecurrenceFrequency Frequency { get; set; }

    public DateTime NextOccurrence { get; set; }

    public bool IsPaused { get; set; }

    public Transaction? Transaction { get; set; }
}
