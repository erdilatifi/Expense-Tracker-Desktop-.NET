namespace FinanceTracker.Models;

public enum TransactionType
{
    Income,
    Expense,
    Transfer
}

public class Transaction
{
    public int Id { get; set; }

    public decimal Amount { get; set; }

    public DateTime Date { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string? ReceiptPath { get; set; }

    public int CategoryId { get; set; }

    public int AccountId { get; set; }

    public int? TargetAccountId { get; set; }

    public TransactionType Type { get; set; }

    public bool IsRecurring { get; set; }

    public int? RecurringRuleId { get; set; }

    public Category? Category { get; set; }

    public Account? Account { get; set; }

    public Account? TargetAccount { get; set; }

    public RecurringRule? RecurringRule { get; set; }
}
