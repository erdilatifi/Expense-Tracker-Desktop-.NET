# FinanceTracker

FinanceTracker is a dark, polished Windows desktop app for tracking personal finances, budgets, accounts, reports, recurring transactions, and CSV/PDF exports.

![FinanceTracker screenshot placeholder](docs/screenshot-placeholder.png)

## Requirements

- .NET 8 SDK
- Windows 10 or newer

## Build And Run

```powershell
dotnet restore
dotnet build
dotnet run
```

## Features

- Dashboard KPIs with animated number count-up, charts, and recent transactions
- Transaction filtering, add-transaction dialog, recurring transaction marking, and CSV export
- Budget tracking with category risk indicators and warning notifications
- Reports with charts, heatmaps, flagged transaction review, PDF export, and CSV export
- Account overview with balances, sparklines, and recent account activity
- Settings for appearance, local data maintenance, CSV import preview, backup, and restore
- Startup recurring transaction processing with toast and notification center entries
- Global keyboard shortcuts for navigation, add transaction, CSV export, and overlay dismissal
- Custom dark error dialog for unhandled UI and background task exceptions

## Credits

- LiveCharts2
- CommunityToolkit.Mvvm
- QuestPDF
