using System.IO;
using FinanceTracker.Models;

namespace FinanceTracker.Data;

public class FinanceDbContext : DbContext
{
    public FinanceDbContext()
    {
    }

    public FinanceDbContext(DbContextOptions<FinanceDbContext> options)
        : base(options)
    {
    }

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Budget> Budgets => Set<Budget>();

    public DbSet<RecurringRule> RecurringRules => Set<RecurringRule>();

    public static string DatabasePath
    {
        get
        {
            var databaseDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FinanceTracker");

            Directory.CreateDirectory(databaseDirectory);

            return Path.Combine(databaseDirectory, "finance.db");
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={DatabasePath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.Property(transaction => transaction.Amount).HasPrecision(18, 2);
            entity.Property(transaction => transaction.Description).HasMaxLength(250);
            entity.Property(transaction => transaction.Notes).HasMaxLength(1000);
            entity.Property(transaction => transaction.ReceiptPath).HasMaxLength(500);

            entity
                .HasOne(transaction => transaction.Category)
                .WithMany(category => category.Transactions)
                .HasForeignKey(transaction => transaction.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasOne(transaction => transaction.Account)
                .WithMany(account => account.Transactions)
                .HasForeignKey(transaction => transaction.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasOne(transaction => transaction.TargetAccount)
                .WithMany()
                .HasForeignKey(transaction => transaction.TargetAccountId)
                .OnDelete(DeleteBehavior.SetNull);

            entity
                .HasOne(transaction => transaction.RecurringRule)
                .WithMany()
                .HasForeignKey(transaction => transaction.RecurringRuleId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.Property(category => category.Name).HasMaxLength(100);
            entity.Property(category => category.IconCode).HasMaxLength(100);
            entity.Property(category => category.ColorHex).HasMaxLength(7);
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.Property(account => account.Balance).HasPrecision(18, 2);
            entity.Property(account => account.OpeningBalance).HasPrecision(18, 2);
            entity.Property(account => account.CreditLimit).HasPrecision(18, 2);
            entity.Property(account => account.Name).HasMaxLength(100);
            entity.Property(account => account.Currency).HasMaxLength(3).HasDefaultValue("USD");
            entity.Property(account => account.ColorHex).HasMaxLength(7);
        });

        modelBuilder.Entity<Budget>(entity =>
        {
            entity.Property(budget => budget.MonthlyLimit).HasPrecision(18, 2);

            entity
                .HasOne(budget => budget.Category)
                .WithMany(category => category.Budgets)
                .HasForeignKey(budget => budget.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecurringRule>(entity =>
        {
            entity
                .HasOne(rule => rule.Transaction)
                .WithOne()
                .HasForeignKey<RecurringRule>(rule => rule.TransactionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Income", IconCode = "payments", ColorHex = "#06B6D4", Type = CategoryType.Income, IsDefault = true },
            new Category { Id = 2, Name = "Housing", IconCode = "home", ColorHex = "#7C3AED", Type = CategoryType.Expense, IsDefault = true },
            new Category { Id = 3, Name = "Food", IconCode = "restaurant", ColorHex = "#E879A0", Type = CategoryType.Expense, IsDefault = true },
            new Category { Id = 4, Name = "Transport", IconCode = "directions_car", ColorHex = "#F59E0B", Type = CategoryType.Expense, IsDefault = true },
            new Category { Id = 5, Name = "Health", IconCode = "local_hospital", ColorHex = "#10B981", Type = CategoryType.Expense, IsDefault = true },
            new Category { Id = 6, Name = "Entertainment", IconCode = "movie", ColorHex = "#3B82F6", Type = CategoryType.Expense, IsDefault = true },
            new Category { Id = 7, Name = "Subscriptions", IconCode = "subscriptions", ColorHex = "#EF4444", Type = CategoryType.Expense, IsDefault = true },
            new Category { Id = 8, Name = "Shopping", IconCode = "shopping_bag", ColorHex = "#8B5CF6", Type = CategoryType.Expense, IsDefault = true });

        modelBuilder.Entity<Account>().HasData(
            new Account { Id = 1, Name = "Checking", Balance = 0m, Currency = "USD", Type = AccountType.Checking, ColorHex = "#1D9E75" },
            new Account { Id = 2, Name = "Savings", Balance = 0m, Currency = "USD", Type = AccountType.Savings, ColorHex = "#534AB7" });
    }

    public override int SaveChanges()
    {
        var result = base.SaveChanges();
        
        var accounts = Accounts.ToList();
        foreach (var account in accounts)
        {
            var income = (decimal)Transactions
                .Where(t => t.AccountId == account.Id && t.Type == TransactionType.Income)
                .Sum(t => (double)t.Amount);

            var expense = (decimal)Transactions
                .Where(t => t.AccountId == account.Id && t.Type == TransactionType.Expense)
                .Sum(t => (double)t.Amount);

            var transferOut = (decimal)Transactions
                .Where(t => t.AccountId == account.Id && t.Type == TransactionType.Transfer)
                .Sum(t => (double)t.Amount);

            var transferIn = (decimal)Transactions
                .Where(t => t.TargetAccountId == account.Id && t.Type == TransactionType.Transfer)
                .Sum(t => (double)t.Amount);

            account.Balance = account.OpeningBalance + income - expense - transferOut + transferIn;
        }
        
        base.SaveChanges();
        return result;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await base.SaveChangesAsync(cancellationToken);
        
        var accounts = await Accounts.ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            var income = (decimal)await Transactions
                .Where(t => t.AccountId == account.Id && t.Type == TransactionType.Income)
                .SumAsync(t => (double)t.Amount, cancellationToken);

            var expense = (decimal)await Transactions
                .Where(t => t.AccountId == account.Id && t.Type == TransactionType.Expense)
                .SumAsync(t => (double)t.Amount, cancellationToken);

            var transferOut = (decimal)await Transactions
                .Where(t => t.AccountId == account.Id && t.Type == TransactionType.Transfer)
                .SumAsync(t => (double)t.Amount, cancellationToken);

            var transferIn = (decimal)await Transactions
                .Where(t => t.TargetAccountId == account.Id && t.Type == TransactionType.Transfer)
                .SumAsync(t => (double)t.Amount, cancellationToken);

            account.Balance = account.OpeningBalance + income - expense - transferOut + transferIn;
        }
        
        await base.SaveChangesAsync(cancellationToken);
        return result;
    }
}
