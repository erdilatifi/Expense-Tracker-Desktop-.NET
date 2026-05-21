using System.Globalization;
using System.IO;
using FinanceTracker.Data;
using FinanceTracker.Models;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.SKCharts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace FinanceTracker.Services;

/// <summary>
/// Produces report aggregates and exports PDF report documents.
/// </summary>
public sealed class ReportService(FinanceDbContext dbContext)
{
    private static readonly CultureInfo CurrencyCulture = CultureInfo.GetCultureInfo("en-US");

    public List<(DateTime Month, decimal Income, decimal Expenses, decimal Savings)> GetCashflowByMonth(DateTime from, DateTime to)
    {
        var start = new DateTime(from.Year, from.Month, 1);
        var end = new DateTime(to.Year, to.Month, 1);
        var transactions = dbContext.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.Date >= start && transaction.Date < end.AddMonths(1))
            .ToList();

        return EnumerateMonths(start, end)
            .Select(month =>
            {
                var monthTransactions = transactions
                    .Where(transaction => transaction.Date >= month && transaction.Date < month.AddMonths(1))
                    .ToList();
                var income = monthTransactions.Where(transaction => transaction.Type == TransactionType.Income).Sum(transaction => transaction.Amount);
                var expenses = monthTransactions.Where(transaction => transaction.Type == TransactionType.Expense).Sum(transaction => transaction.Amount);
                return (month, income, expenses, income - expenses);
            })
            .ToList();
    }

    public List<(string Category, decimal Amount)> GetSpendByCategory(DateTime from, DateTime to)
    {
        var transactions = dbContext.Transactions
            .AsNoTracking()
            .Include(transaction => transaction.Category)
            .Where(transaction => transaction.Date >= from.Date && transaction.Date <= to.Date && transaction.Type == TransactionType.Expense)
            .ToList();

        return transactions
            .GroupBy(transaction => transaction.Category == null ? "Uncategorized" : transaction.Category.Name)
            .Select(group => new ValueTuple<string, decimal>(group.Key, group.Sum(transaction => transaction.Amount)))
            .OrderByDescending(item => item.Item2)
            .ToList();
    }

    public Dictionary<(DateTime Week, string Category), decimal> GetWeeklyHeatmap(DateTime from, DateTime to)
    {
        var transactions = dbContext.Transactions
            .AsNoTracking()
            .Include(transaction => transaction.Category)
            .Where(transaction => transaction.Date >= from.Date && transaction.Date <= to.Date && transaction.Type == TransactionType.Expense)
            .ToList();

        return transactions
            .GroupBy(transaction => (Week: StartOfWeek(transaction.Date), Category: transaction.Category?.Name ?? "Uncategorized"))
            .ToDictionary(group => group.Key, group => group.Sum(transaction => transaction.Amount));
    }

    public Task ExportPdfAsync(ReportData data, string path)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var chartImage = RenderCashflowChart(data.Cashflow);

        return Task.Run(() =>
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(28);
                    page.DefaultTextStyle(text => text.FontSize(10).FontColor("#F1F5F9"));
                    page.PageColor("#0D0F14");

                    page.Content().Column(column =>
                    {
                        column.Spacing(18);
                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Column(header =>
                            {
                                header.Item().Text("FinanceTracker Report").FontSize(22).SemiBold().FontColor("#F1F5F9");
                                header.Item().Text($"{data.From:MMM d, yyyy} to {data.To:MMM d, yyyy}").FontSize(10).FontColor("#64748B");
                            });
                            row.ConstantItem(120).AlignRight().Text(DateTime.Now.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)).FontColor("#64748B");
                        });

                        column.Item().Background("#151820").Border(1).BorderColor("#2D3748").Padding(14).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            AddHeaderCell(table, "Month");
                            AddHeaderCell(table, "Income");
                            AddHeaderCell(table, "Expenses");
                            AddHeaderCell(table, "Savings");

                            foreach (var row in data.Cashflow)
                            {
                                AddBodyCell(table, row.Month.ToString("MMM yyyy", CultureInfo.CurrentCulture));
                                AddBodyCell(table, FormatMoney(row.Income));
                                AddBodyCell(table, FormatMoney(row.Expenses));
                                AddBodyCell(table, FormatMoney(row.Savings));
                            }
                        });

                        column.Item().Text("Cashflow chart").FontSize(13).SemiBold().FontColor("#F1F5F9");
                        column.Item().Height(210).Image(chartImage).FitArea();
                    });
                });
            }).GeneratePdf(path);
        });
    }

    private static IEnumerable<DateTime> EnumerateMonths(DateTime start, DateTime end)
    {
        for (var cursor = start; cursor <= end; cursor = cursor.AddMonths(1))
        {
            yield return cursor;
        }
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-offset);
    }

    private static byte[] RenderCashflowChart(IReadOnlyList<CashflowPoint> cashflow)
    {
        var labels = cashflow.Select(point => point.Month.ToString("MMM", CultureInfo.CurrentCulture)).ToArray();
        var income = cashflow.Select(point => (double)point.Income / 1000d).ToArray();
        var expenses = cashflow.Select(point => (double)point.Expenses / 1000d).ToArray();

        var chart = new SKCartesianChart
        {
            Width = 900,
            Height = 360,
            Background = SKColor.Parse("#151820"),
            Series =
            [
                new LineSeries<double>
                {
                    Name = "Income",
                    Values = income,
                    Stroke = new SolidColorPaint(SKColor.Parse("#A78BFA"), 3),
                    GeometryFill = new SolidColorPaint(SKColor.Parse("#A78BFA")),
                    GeometryStroke = new SolidColorPaint(SKColor.Parse("#A78BFA"), 2),
                    Fill = new SolidColorPaint(new SKColor(167, 139, 250, 42))
                },
                new LineSeries<double>
                {
                    Name = "Expenses",
                    Values = expenses,
                    Stroke = new SolidColorPaint(SKColor.Parse("#7C3AED"), 3),
                    GeometryFill = new SolidColorPaint(SKColor.Parse("#7C3AED")),
                    GeometryStroke = new SolidColorPaint(SKColor.Parse("#7C3AED"), 2),
                    Fill = new SolidColorPaint(new SKColor(124, 58, 237, 42))
                }
            ],
            XAxes =
            [
                new Axis
                {
                    Labels = labels,
                    LabelsPaint = new SolidColorPaint(SKColor.Parse("#64748B")),
                    SeparatorsPaint = null,
                    TextSize = 12
                }
            ],
            YAxes =
            [
                new Axis
                {
                    Labeler = value => $"${value:0}k",
                    LabelsPaint = new SolidColorPaint(SKColor.Parse("#64748B")),
                    SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#1E293B"), 1),
                    TextSize = 12,
                    MinLimit = 0
                }
            ]
        };

        using var stream = new MemoryStream();
        chart.SaveImage(stream, SKEncodedImageFormat.Png, 95);
        return stream.ToArray();
    }

    private static void AddHeaderCell(TableDescriptor table, string text)
    {
        table.Cell().Background("#1E2130").Padding(8).Text(text).SemiBold().FontColor("#64748B");
    }

    private static void AddBodyCell(TableDescriptor table, string text)
    {
        table.Cell().BorderBottom(1).BorderColor("#1E293B").Padding(8).Text(text).FontColor("#F1F5F9");
    }

    private static string FormatMoney(decimal amount) => amount.ToString("C2", CurrencyCulture);
}

/// <summary>
/// Contains all data needed to render an exported report.
/// </summary>
public sealed record ReportData(
    DateTime From,
    DateTime To,
    IReadOnlyList<CashflowPoint> Cashflow,
    IReadOnlyList<CategorySpendPoint> CategorySpend,
    IReadOnlyList<FlaggedTransactionReportItem> FlaggedTransactions);

/// <summary>
/// Represents one monthly cashflow point.
/// </summary>
public sealed record CashflowPoint(DateTime Month, decimal Income, decimal Expenses, decimal Savings);

/// <summary>
/// Represents spending by category for reports.
/// </summary>
public sealed record CategorySpendPoint(string Category, decimal Amount, string ColorHex);

/// <summary>
/// Represents a flagged transaction in an exported report.
/// </summary>
public sealed record FlaggedTransactionReportItem(string Date, string Description, string Account, string Category, string Amount, string Status);
