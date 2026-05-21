namespace FinanceTracker.Models;

public enum CategoryType
{
    Income,
    Expense
}

public class Category
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string IconCode { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;

    public CategoryType Type { get; set; }

    public bool IsDefault { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

    public ICollection<Budget> Budgets { get; set; } = new List<Budget>();
}
