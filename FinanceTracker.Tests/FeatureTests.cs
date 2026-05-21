using FinanceTracker.Models;
using FinanceTracker.Services;
using Xunit;

namespace FinanceTracker.Tests;

public class FeatureTests
{
    [Theory]
    [InlineData(RecurrenceFrequency.Daily)]
    [InlineData(RecurrenceFrequency.Weekly)]
    [InlineData(RecurrenceFrequency.Monthly)]
    [InlineData(RecurrenceFrequency.Yearly)]
    public void RecurringSchedule_AdvancesFromDate(RecurrenceFrequency frequency)
    {
        var start = new DateTime(2026, 5, 1);
        var next = RecurringScheduleHelper.NextFrom(start, frequency);
        Assert.True(next > start);
    }

    [Fact]
    public void AppSettings_ConvertsCurrencyToBase()
    {
        var settings = new AppSettingsService();
        settings.Save(new AppSettingsState("USD", "MMM d, yyyy", "Monday", "#7C3AED", 14, 220));

        var eurAmount = settings.ConvertToBaseCurrency(100m, "EUR");
        Assert.True(eurAmount > 100m);
    }

    [Fact]
    public void AppSettings_CreateCurrencyCulture_ReturnsExpected()
    {
        var gbp = AppSettingsService.CreateCurrencyCulture("GBP");
        Assert.Equal("en-GB", gbp.Name);
    }
}
