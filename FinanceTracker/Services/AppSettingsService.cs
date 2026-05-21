using System.Globalization;
using System.IO;
using System.Text.Json;

namespace FinanceTracker.Services;

/// <summary>
/// Loads and persists user preferences and exposes formatting culture.
/// </summary>
public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public string Currency { get; private set; } = "USD";

    public string DateFormat { get; private set; } = "MMM d, yyyy";

    public string FirstDayOfWeek { get; private set; } = "Monday";

    public string AccentColor { get; private set; } = "#7C3AED";

    public double FontSize { get; private set; } = 14;

    public double SidebarWidth { get; private set; } = 220;

    public CultureInfo CurrencyCulture => CreateCurrencyCulture(Currency);

    public void Load()
    {
        if (!File.Exists(settingsPath))
        {
            return;
        }

        var settings = JsonSerializer.Deserialize<AppSettingsState>(File.ReadAllText(settingsPath), JsonOptions);
        if (settings is null)
        {
            return;
        }

        Currency = settings.Currency;
        DateFormat = settings.DateFormat;
        FirstDayOfWeek = settings.FirstDayOfWeek;
        AccentColor = settings.AccentColor;
        FontSize = settings.FontSize;
        SidebarWidth = settings.SidebarWidth;
    }

    public void Save(AppSettingsState state)
    {
        Currency = state.Currency;
        DateFormat = state.DateFormat;
        FirstDayOfWeek = state.FirstDayOfWeek;
        AccentColor = state.AccentColor;
        FontSize = state.FontSize;
        SidebarWidth = state.SidebarWidth;
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(state, JsonOptions));
    }

    public AppSettingsState Snapshot() =>
        new(Currency, DateFormat, FirstDayOfWeek, AccentColor, FontSize, SidebarWidth);

    public string FormatMoney(decimal amount, string format = "C2") =>
        amount.ToString(format, CurrencyCulture);

    public string FormatDate(DateTime date) =>
        date.ToString(DateFormat, CultureInfo.CurrentCulture);

    private static readonly Dictionary<string, decimal> ExchangeRatesToUsd = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = 1m,
        ["EUR"] = 1.08m,
        ["GBP"] = 1.27m,
        ["CAD"] = 0.74m,
        ["AUD"] = 0.66m,
        ["JPY"] = 0.0067m,
        ["CHF"] = 1.12m
    };

    public decimal ConvertToBaseCurrency(decimal amount, string fromCurrency)
    {
        if (!ExchangeRatesToUsd.TryGetValue(fromCurrency, out var fromRate))
        {
            fromRate = 1m;
        }

        if (!ExchangeRatesToUsd.TryGetValue(Currency, out var toRate))
        {
            toRate = 1m;
        }

        var usd = amount * fromRate;
        return toRate == 0m ? usd : usd / toRate;
    }

    public decimal SumAccountsInBaseCurrency(IEnumerable<(decimal Balance, string Currency)> accounts) =>
        accounts.Sum(a => ConvertToBaseCurrency(a.Balance, a.Currency));

    public static CultureInfo CreateCurrencyCulture(string currencyCode) =>
        currencyCode.ToUpperInvariant() switch
        {
            "EUR" => CultureInfo.GetCultureInfo("de-DE"),
            "GBP" => CultureInfo.GetCultureInfo("en-GB"),
            "CAD" => CultureInfo.GetCultureInfo("en-CA"),
            "AUD" => CultureInfo.GetCultureInfo("en-AU"),
            "JPY" => CultureInfo.GetCultureInfo("ja-JP"),
            "CHF" => CultureInfo.GetCultureInfo("de-CH"),
            _ => CultureInfo.GetCultureInfo("en-US")
        };
}

public sealed record AppSettingsState(
    string Currency,
    string DateFormat,
    string FirstDayOfWeek,
    string AccentColor,
    double FontSize,
    double SidebarWidth);
