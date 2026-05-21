using FinanceTracker.Data;
using FinanceTracker.Models;

namespace FinanceTracker.Services;

/// <summary>
/// Initializes and migrates the local FinanceTracker database, then seeds realistic demo data.
/// </summary>
public class DatabaseService(FinanceDbContext dbContext)
{
    public async Task InitializeAsync()
    {
        await dbContext.Database.MigrateAsync();
        await SeedDataAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Entry point
    // ─────────────────────────────────────────────────────────────────────────

    private async Task SeedDataAsync()
    {
        if (await dbContext.Transactions.AnyAsync())
            return;

        await EnsureExtraCategoriesAsync();
        await EnsureExtraAccountsAsync();

        var cats = (await dbContext.Categories.ToListAsync()).ToDictionary(c => c.Name);
        var accs = (await dbContext.Accounts.ToListAsync()).ToDictionary(a => a.Name);

        await SeedBudgetsAsync(cats);
        await SeedTransactionsAndRulesAsync(cats, accs);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Extra categories (beyond the 8 in HasData)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task EnsureExtraCategoriesAsync()
    {
        var existing = (await dbContext.Categories.ToListAsync())
            .Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = new List<Category>();

        void Ensure(string name, string icon, string color, CategoryType type)
        {
            if (!existing.Contains(name))
                toAdd.Add(new Category { Name = name, IconCode = icon, ColorHex = color, Type = type, IsDefault = true });
        }

        Ensure("Freelance",          "work",          "#8B5CF6", CategoryType.Income);
        Ensure("Investment Returns", "savings",        "#059669", CategoryType.Income);
        Ensure("Dining Out",         "coffee",         "#F97316", CategoryType.Expense);
        Ensure("Travel",             "flight",         "#0EA5E9", CategoryType.Expense);
        Ensure("Utilities",          "bolt",           "#64748B", CategoryType.Expense);
        Ensure("Personal Care",      "child",          "#EC4899", CategoryType.Expense);
        Ensure("Gym & Fitness",      "fitness",        "#14B8A6", CategoryType.Expense);
        Ensure("Insurance",          "local_hospital", "#6366F1", CategoryType.Expense);

        if (toAdd.Count > 0)
        {
            await dbContext.Categories.AddRangeAsync(toAdd);
            await dbContext.SaveChangesAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Extra accounts (beyond Checking + Savings in HasData)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task EnsureExtraAccountsAsync()
    {
        var existing = (await dbContext.Accounts.ToListAsync())
            .Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = new List<Account>();

        void Ensure(string name, AccountType type, string color, decimal opening, decimal? creditLimit = null)
        {
            if (!existing.Contains(name))
                toAdd.Add(new Account
                {
                    Name = name, Balance = opening, OpeningBalance = opening,
                    Currency = "USD", Type = type, ColorHex = color, CreditLimit = creditLimit
                });
        }

        Ensure("Credit Card", AccountType.CreditCard, "#DC2626", 0m,      5000m);
        Ensure("Cash",        AccountType.Cash,        "#D97706", 2000m);
        Ensure("Investment",  AccountType.Investment,  "#2563EB", 5000m);

        if (toAdd.Count > 0)
        {
            await dbContext.Accounts.AddRangeAsync(toAdd);
            await dbContext.SaveChangesAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Budgets — 12 categories × 6 months
    // ─────────────────────────────────────────────────────────────────────────

    private async Task SeedBudgetsAsync(Dictionary<string, Category> cats)
    {
        var today = DateTime.Today;

        (string Cat, decimal Limit)[] limits =
        [
            ("Housing",      2000m),
            ("Food",          700m),
            ("Dining Out",    600m),
            ("Transport",     300m),
            ("Health",        200m),
            ("Entertainment", 400m),
            ("Subscriptions", 150m),
            ("Shopping",      800m),
            ("Utilities",     360m),
            ("Gym & Fitness",  60m),
            ("Personal Care", 120m),
            ("Insurance",     200m),
        ];

        var budgets = new List<Budget>();
        for (int mo = -5; mo <= 0; mo++)
        {
            var d = today.AddMonths(mo);
            foreach (var (cat, limit) in limits)
            {
                if (!cats.ContainsKey(cat)) continue;
                budgets.Add(new Budget { CategoryId = cats[cat].Id, MonthlyLimit = limit, Month = d.Month, Year = d.Year });
            }
        }

        await dbContext.Budgets.AddRangeAsync(budgets);
        await dbContext.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Transactions (~220 entries over 6 months) + 7 recurring rules
    // ─────────────────────────────────────────────────────────────────────────

    private async Task SeedTransactionsAndRulesAsync(
        Dictionary<string, Category> cats,
        Dictionary<string, Account>  accs)
    {
        var today = DateTime.Today;
        var rng   = new Random(42);
        var txns  = new List<Transaction>();

        int C(string name) => cats[name].Id;
        int A(string name) => accs[name].Id;
        DateTime D(int y, int mn, int day) =>
            new(y, mn, Math.Clamp(day, 1, DateTime.DaysInMonth(y, mn)));

        for (int mo = -5; mo <= 0; mo++)
        {
            var m      = today.AddMonths(mo);
            int y      = m.Year;
            int mn     = m.Month;
            int maxDay = mo == 0 ? today.Day : DateTime.DaysInMonth(y, mn);

            // ── INCOME ──────────────────────────────────────────────────────

            txns.Add(Tx(3800m, D(y, mn, 1), "Salary — first half",  C("Income"), A("Checking"), TransactionType.Income));
            if (15 <= maxDay)
                txns.Add(Tx(3800m, D(y, mn, 15), "Salary — second half", C("Income"), A("Checking"), TransactionType.Income));

            // Freelance — 4 of 6 months
            if (mo is -5 or -3 or -2 or 0)
            {
                var fl = Math.Round(900m + (decimal)(rng.NextDouble() * 1300), 0);
                txns.Add(Tx(fl, D(y, mn, rng.Next(5, Math.Min(18, maxDay) + 1)),
                    "Freelance — client payment", C("Freelance"), A("Checking"), TransactionType.Income));
            }

            // Investment dividend — every month on the 28th
            if (28 <= maxDay)
            {
                var div = Math.Round(75m + (decimal)(rng.NextDouble() * 125), 2);
                txns.Add(Tx(div, D(y, mn, 28), "Investment dividend", C("Investment Returns"), A("Investment"), TransactionType.Income));
            }

            // ── FIXED MONTHLY EXPENSES ───────────────────────────────────────

            txns.Add(Tx(1850m,  D(y, mn, 1), "Rent payment",              C("Housing"),      A("Checking"), TransactionType.Expense));
            if ( 3 <= maxDay) txns.Add(Tx(45m,    D(y, mn,  3), "Gym membership",            C("Gym & Fitness"), A("Checking"),  TransactionType.Expense));
            if ( 5 <= maxDay) txns.Add(Tx(79.99m, D(y, mn,  5), "Internet — fiber plan",     C("Utilities"),     A("Checking"),  TransactionType.Expense));
            if ( 8 <= maxDay) txns.Add(Tx(55m,    D(y, mn,  8), "Phone bill",                C("Utilities"),     A("Checking"),  TransactionType.Expense));
            if (10 <= maxDay)
            {
                txns.Add(Tx(15.99m, D(y, mn, 10), "Netflix",        C("Subscriptions"), A("Checking"), TransactionType.Expense));
                txns.Add(Tx( 9.99m, D(y, mn, 10), "Spotify",        C("Subscriptions"), A("Checking"), TransactionType.Expense));
                txns.Add(Tx( 2.99m, D(y, mn, 10), "iCloud storage", C("Subscriptions"), A("Checking"), TransactionType.Expense));
            }
            if (12 <= maxDay) txns.Add(Tx(78m,   D(y, mn, 12), "Monthly transit pass",       C("Transport"),  A("Checking"), TransactionType.Expense));
            if (15 <= maxDay) txns.Add(Tx(189m,  D(y, mn, 15), "Health & renter insurance",  C("Insurance"),  A("Checking"), TransactionType.Expense));

            // Electric bill varies by season
            if (17 <= maxDay)
            {
                var elec = Math.Round(86m + (decimal)(rng.NextDouble() * 59), 2);
                txns.Add(Tx(elec, D(y, mn, 17), "Electric bill", C("Utilities"), A("Checking"), TransactionType.Expense));
            }

            // ── GROCERIES (4 weekly shops) ───────────────────────────────────

            for (int w = 0; w < 4; w++)
            {
                int gd = 2 + w * 7 + rng.Next(0, 3);
                if (gd > maxDay) break;
                var gAmt = Math.Round(92m + (decimal)(rng.NextDouble() * 73), 2);
                string gDesc = rng.Next(3) switch
                {
                    0 => "Whole Foods Market",
                    1 => "Trader Joe's",
                    _ => "Local grocery store"
                };
                txns.Add(Tx(gAmt, D(y, mn, gd), gDesc, C("Food"), A("Checking"), TransactionType.Expense));
            }

            // ── DINING OUT (7–10 visits) ─────────────────────────────────────

            int dCount = rng.Next(7, 11);
            for (int i = 0; i < dCount; i++)
            {
                var dAmt = Math.Round(24m + (decimal)(rng.NextDouble() * 56), 2);
                string dDesc = rng.Next(6) switch
                {
                    0 => "Sushi & ramen bar",
                    1 => "Italian bistro",
                    2 => "Morning coffee & pastry",
                    3 => "Thai takeaway",
                    4 => "Burger & craft beer bar",
                    _ => "Mexican cantina"
                };
                int dAcc = rng.Next(2) == 0 ? A("Credit Card") : A("Cash");
                txns.Add(Tx(dAmt, D(y, mn, rng.Next(1, maxDay + 1)), dDesc, C("Dining Out"), dAcc, TransactionType.Expense));
            }

            // ── TRANSPORT — gas (1–2 fill-ups) ───────────────────────────────

            for (int i = 0; i < rng.Next(1, 3); i++)
            {
                var gAmt = Math.Round(52m + (decimal)(rng.NextDouble() * 33), 2);
                txns.Add(Tx(gAmt, D(y, mn, rng.Next(1, maxDay + 1)), "Gas station", C("Transport"), A("Credit Card"), TransactionType.Expense));
            }

            // ── SHOPPING (2–4 orders) ────────────────────────────────────────

            for (int i = 0; i < rng.Next(2, 5); i++)
            {
                var sAmt = Math.Round(38m + (decimal)(rng.NextDouble() * 182), 2);
                string sDesc = rng.Next(5) switch
                {
                    0 => "Amazon order",
                    1 => "Target run",
                    2 => "ZARA clothing",
                    3 => "Home goods & décor",
                    _ => "Electronics & gadgets"
                };
                txns.Add(Tx(sAmt, D(y, mn, rng.Next(1, maxDay + 1)), sDesc, C("Shopping"), A("Credit Card"), TransactionType.Expense));
            }

            // ── PERSONAL CARE (~2 of 3 months) ───────────────────────────────

            if (rng.Next(3) != 0)
            {
                var pAmt = Math.Round(48m + (decimal)(rng.NextDouble() * 52), 2);
                string pDesc = rng.Next(3) switch { 0 => "Hair salon", 1 => "Spa & wellness", _ => "Pharmacy & beauty" };
                txns.Add(Tx(pAmt, D(y, mn, rng.Next(4, maxDay + 1)), pDesc, C("Personal Care"), A("Cash"), TransactionType.Expense));
            }

            // ── HEALTH (~1 of 3 months) ───────────────────────────────────────

            if (rng.Next(3) == 0)
            {
                var hAmt = Math.Round(35m + (decimal)(rng.NextDouble() * 95), 2);
                txns.Add(Tx(hAmt, D(y, mn, rng.Next(1, maxDay + 1)), "Doctor / pharmacy visit", C("Health"), A("Checking"), TransactionType.Expense));
            }

            // ── ENTERTAINMENT (1–3 outings) ───────────────────────────────────

            for (int i = 0; i < rng.Next(1, 4); i++)
            {
                var eAmt = Math.Round(28m + (decimal)(rng.NextDouble() * 52), 2);
                string eDesc = rng.Next(4) switch
                {
                    0 => "Cinema tickets",
                    1 => "Live concert",
                    2 => "Museum & exhibition",
                    _ => "Bowling & arcade night"
                };
                int eAcc = rng.Next(2) == 0 ? A("Credit Card") : A("Cash");
                txns.Add(Tx(eAmt, D(y, mn, rng.Next(1, maxDay + 1)), eDesc, C("Entertainment"), eAcc, TransactionType.Expense));
            }

            // ── TRAVEL (months -4 and -1 only) ───────────────────────────────

            if (mo is -4 or -1)
            {
                var fAmt = Math.Round(245m + (decimal)(rng.NextDouble() * 355), 2);
                var hAmt = Math.Round(130m + (decimal)(rng.NextDouble() * 210), 2);
                txns.Add(Tx(fAmt, D(y, mn, rng.Next(5, maxDay + 1)), "Flight booking",        C("Travel"), A("Credit Card"), TransactionType.Expense));
                txns.Add(Tx(hAmt, D(y, mn, rng.Next(5, maxDay + 1)), "Hotel & accommodation", C("Travel"), A("Credit Card"), TransactionType.Expense));
            }

            // ── TRANSFERS ────────────────────────────────────────────────────

            if (20 <= maxDay)
                txns.Add(Tx(1000m, D(y, mn, 20), "Transfer to savings",      C("Income"), A("Checking"), TransactionType.Transfer, A("Savings")));
            if (22 <= maxDay)
                txns.Add(Tx(800m,  D(y, mn, 22), "Credit card payment",      C("Income"), A("Checking"), TransactionType.Transfer, A("Credit Card")));
            if (24 <= maxDay)
                txns.Add(Tx(500m,  D(y, mn, 24), "Investment contribution",  C("Income"), A("Checking"), TransactionType.Transfer, A("Investment")));
        }

        await dbContext.Transactions.AddRangeAsync(txns);
        await dbContext.SaveChangesAsync();

        // ── RECURRING RULES ───────────────────────────────────────────────────

        var saved = await dbContext.Transactions.ToListAsync();

        (string Desc, string CatName, RecurrenceFrequency Freq, int Day)[] ruleDefs =
        [
            ("Rent payment",          "Housing",       RecurrenceFrequency.Monthly, 1),
            ("Netflix",               "Subscriptions", RecurrenceFrequency.Monthly, 10),
            ("Spotify",               "Subscriptions", RecurrenceFrequency.Monthly, 10),
            ("iCloud storage",        "Subscriptions", RecurrenceFrequency.Monthly, 10),
            ("Gym membership",        "Gym & Fitness", RecurrenceFrequency.Monthly, 3),
            ("Internet — fiber plan", "Utilities",     RecurrenceFrequency.Monthly, 5),
            ("Phone bill",            "Utilities",     RecurrenceFrequency.Monthly, 8),
        ];

        var rules = new List<RecurringRule>();
        foreach (var (desc, catName, freq, day) in ruleDefs)
        {
            if (!cats.ContainsKey(catName)) continue;
            var template = saved.FirstOrDefault(t => t.Description == desc && t.CategoryId == cats[catName].Id);
            if (template is null) continue;

            var next = new DateTime(today.Year, today.Month,
                Math.Min(day, DateTime.DaysInMonth(today.Year, today.Month)));
            if (next <= today) next = next.AddMonths(1);

            rules.Add(new RecurringRule { TransactionId = template.Id, Frequency = freq, NextOccurrence = next, IsPaused = false });
        }

        if (rules.Count > 0)
        {
            await dbContext.RecurringRules.AddRangeAsync(rules);
            await dbContext.SaveChangesAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static Transaction Tx(
        decimal amount, DateTime date, string description,
        int categoryId, int accountId, TransactionType type,
        int? targetAccountId = null) =>
        new()
        {
            Amount          = amount,
            Date            = date,
            Description     = description,
            CategoryId      = categoryId,
            AccountId       = accountId,
            Type            = type,
            TargetAccountId = targetAccountId,
            IsRecurring     = false
        };
}
