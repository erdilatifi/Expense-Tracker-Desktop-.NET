namespace FinanceTracker.Models;

public enum AccountType
{
    Checking,
    Savings,
    CreditCard,
    Cash,
    Investment
}

public class Account
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Balance { get; set; }

    public decimal OpeningBalance { get; set; }

    public decimal? CreditLimit { get; set; }

    public string Currency { get; set; } = "USD";

    public AccountType Type { get; set; }

    public string ColorHex { get; set; } = string.Empty;

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
