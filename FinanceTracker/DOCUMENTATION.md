# FinanceTracker — Full Application Documentation

> Last updated: May 2026  
> Build target: `.NET 8 / Windows (WPF)` · Database: `SQLite`

---

## Table of Contents

1. [Tech Stack](#1-tech-stack)
2. [Project Structure](#2-project-structure)
3. [Architecture Overview](#3-architecture-overview)
4. [Database Schema](#4-database-schema)
5. [Data Services](#5-data-services)
6. [ViewModels & Data Flow](#6-viewmodels--data-flow)
7. [Views & Features](#7-views--features)
   - [Dashboard](#dashboard)
   - [Transactions](#transactions)
   - [Recurring](#recurring)
   - [Budgets](#budgets)
   - [Reports](#reports)
   - [Accounts](#accounts)
   - [Settings](#settings)
8. [Cross-Cutting Concerns](#8-cross-cutting-concerns)
9. [Seeded Demo Data](#9-seeded-demo-data)
10. [Build & Run](#10-build--run)
11. [Known Behaviors & Edge Cases](#11-known-behaviors--edge-cases)

---

## 1. Tech Stack

| Layer | Technology | Version |
|---|---|---|
| UI Framework | WPF (Windows Presentation Foundation) | .NET 8 |
| Language | C# | 12 |
| MVVM Toolkit | CommunityToolkit.Mvvm | 8.4.2 |
| ORM | Entity Framework Core | 8.x |
| Database | SQLite (via Microsoft.EntityFrameworkCore.Sqlite) | 8.x |
| Charts | LiveChartsCore.SkiaSharpView.WPF | 2.0.4 |
| PDF Export | QuestPDF | 2026.5.0 |
| JSON Serialization | Newtonsoft.Json | 13.0.4 |
| DI Container | Microsoft.Extensions.DependencyInjection | .NET 8 built-in |
| Icon Font | Segoe Fluent Icons / Segoe MDL2 Assets | Windows built-in |

---

## 2. Project Structure

```
FinanceTracker/
├── App.xaml / App.xaml.cs          # Application entry point, DI setup, accent color apply
├── MainWindow.xaml / .xaml.cs      # Shell: sidebar navigation, notifications popup
├── GlobalUsings.cs                 # Global using directives
│
├── Models/                         # EF Core domain entities
│   ├── Account.cs                  # Account + AccountType enum
│   ├── Transaction.cs              # Transaction + TransactionType enum
│   ├── Category.cs                 # Category + CategoryType enum
│   ├── Budget.cs                   # Monthly budget limit per category
│   └── RecurringRule.cs            # Recurring schedule + RecurrenceFrequency enum
│
├── Data/
│   ├── FinanceDbContext.cs         # EF DbContext, table config, database path
│   └── Migrations/                 # EF migration history (4 migrations)
│
├── Services/
│   ├── AppSettingsService.cs       # Load/save appsettings.json, currency formatting
│   ├── DatabaseService.cs          # DB initialization + demo data seed
│   ├── RecurringService.cs         # Auto-materialize due recurring rules at startup
│   ├── ReportService.cs            # Cashflow/spend aggregates + QuestPDF export
│   ├── BudgetAlertService.cs       # Toast/notification when budget ≥ 80% used
│   ├── NotificationService.cs      # In-app notification feed (bell button)
│   ├── InputDialogService.cs       # Shared text-input dialog helper
│   ├── ErrorDialogService.cs       # Global unhandled exception dialog
│   └── RecurringScheduleHelper.cs  # Date arithmetic for recurring schedules
│
├── ViewModels/
│   ├── MainViewModel.cs            # Navigation host; calls LoadAsync on every view switch
│   ├── DashboardViewModel.cs
│   ├── TransactionsViewModel.cs
│   ├── AccountsViewModel.cs
│   ├── BudgetViewModel.cs
│   ├── ReportsViewModel.cs
│   ├── RecurringViewModel.cs
│   └── SettingsViewModel.cs
│
├── Views/
│   ├── DashboardView.xaml
│   ├── TransactionsView.xaml
│   ├── AccountsView.xaml
│   ├── BudgetView.xaml
│   ├── ReportsView.xaml
│   ├── RecurringView.xaml
│   ├── SettingsView.xaml
│   └── Dialogs/
│       ├── AddTransactionDialog.xaml    # Add/edit transaction modal
│       └── (other dialogs)
│
├── Themes/
│   ├── DarkTheme.xaml               # All color brushes, base styles (BaseCard, DarkButton, etc.)
│   └── ControlStyles.xaml           # Extended control templates
│
├── Converters/
│   ├── VisibilityConverter.cs       # bool/int/null → Visibility (supports Invert param)
│   └── StringEqualityConverter.cs   # IMultiValueConverter: two equal strings → Visible
│
└── appsettings.json                  # Persisted user preferences (auto-generated)
```

---

## 3. Architecture Overview

### Pattern: MVVM with CommunityToolkit source generators

- **ViewModels** use `[ObservableProperty]`, `[RelayCommand]`, and `partial` methods from CommunityToolkit.Mvvm — no boilerplate `INotifyPropertyChanged` code.
- **Views** bind exclusively via `{Binding}` — no code-behind logic except for WPF-specific event wiring (popup open/close, DataGrid selection forwarding).
- **Navigation** is handled by `MainViewModel.CurrentPage`, which is a typed VM instance. `MainWindow.xaml` uses `DataTemplate` mappings to automatically render the correct `UserControl`.
- **DI** is configured in `App.xaml.cs` using `Microsoft.Extensions.DependencyInjection`. `MainViewModel` and `MainWindow` are singletons; DB-scoped services are `AddScoped`.

### Data refresh strategy

Every time the user navigates to a page, `MainViewModel` calls `await vm.LoadAsync()` on that page's ViewModel. This ensures data is always fresh without manual refresh buttons. Individual pages also call `LoadAsync()` after any mutation (add, edit, delete).

### Account balance recalculation

`Account.Balance` is a **derived field** recomputed from scratch after every mutation:

```
Balance = OpeningBalance
        + SUM(Income transactions for this account)
        - SUM(Expense transactions for this account)
        - SUM(Transfer-out transactions for this account)
        + SUM(Transfer-in transactions targeting this account)
```

This computation runs in `TransactionsViewModel.RecalculateAccountBalancesAsync()` (called after every add/edit/delete) and in `RecurringService.ProcessDueRulesAsync()` (called at startup).

---

## 4. Database Schema

### `Accounts`

| Column | Type | Notes |
|---|---|---|
| Id | int PK | Auto-increment |
| Name | text | Display name |
| Balance | decimal | Recomputed on every transaction mutation |
| OpeningBalance | decimal | Starting balance at account creation |
| CreditLimit | decimal? | Optional, for credit cards |
| Currency | text | ISO 4217 code, default `"USD"` |
| Type | int | `AccountType` enum: Checking / Savings / CreditCard / Cash / Investment |
| ColorHex | text | Hex color for UI (e.g. `#A78BFA`) |

### `Categories`

| Column | Type | Notes |
|---|---|---|
| Id | int PK | |
| Name | text | |
| IconCode | text | Material/MDL2 icon key (e.g. `"home"`, `"restaurant"`) |
| ColorHex | text | |
| Type | int | `CategoryType` enum: Income / Expense |
| IsDefault | bool | True for seeded system categories |

### `Transactions`

| Column | Type | Notes |
|---|---|---|
| Id | int PK | |
| Amount | decimal | Always positive; sign derived from `Type` |
| Date | datetime | |
| Description | text | |
| Notes | text? | Optional extended notes |
| ReceiptPath | text? | Path to attached receipt file |
| CategoryId | int FK | → Categories |
| AccountId | int FK | → Accounts (source account) |
| TargetAccountId | int? FK | → Accounts (for Transfer type only) |
| Type | int | `TransactionType` enum: Income / Expense / Transfer |
| IsRecurring | bool | True when generated by a recurring rule |
| RecurringRuleId | int? FK | → RecurringRules |

### `Budgets`

| Column | Type | Notes |
|---|---|---|
| Id | int PK | |
| CategoryId | int FK | → Categories |
| MonthlyLimit | decimal | Budget cap for the given month/year |
| Month | int | 1–12 |
| Year | int | |

### `RecurringRules`

| Column | Type | Notes |
|---|---|---|
| Id | int PK | |
| TransactionId | int FK | Template transaction to clone |
| Frequency | int | `RecurrenceFrequency` enum: Daily / Weekly / Monthly / Yearly |
| NextOccurrence | datetime | Next date to materialize |
| IsPaused | bool | When true, rule is skipped during startup processing |

---

## 5. Data Services

### `AppSettingsService`

Reads/writes `appsettings.json` in the app's base directory. Stores:

- `Currency` — ISO code (e.g. `"USD"`, `"EUR"`)
- `DateFormat` — display pattern (e.g. `"MMM d, yyyy"`)
- `FirstDayOfWeek` — `"Monday"` / `"Sunday"` / `"Saturday"`
- `AccentColor` — hex color for the app's purple accent (e.g. `"#7C3AED"`)
- `FontSize` — base font size (double)
- `SidebarWidth` — sidebar pixel width (double)

Provides `CurrencyCulture` (CultureInfo), `FormatMoney(decimal)`, and `SumAccountsInBaseCurrency()` for multi-currency totaling.

### `DatabaseService`

Runs EF Core migrations on startup, then seeds demo data if the `Accounts` table is empty. Seeded demo data includes 3 accounts, 8 expense categories, 3 income categories, 6 months of transactions, 5 budget limits, and 3 recurring rules.

### `RecurringService`

Called once at startup. Queries all non-paused `RecurringRules` where `NextOccurrence <= today`, creates a new `Transaction` for each, advances `NextOccurrence` past today, then recalculates affected account balances. Shows an in-app notification if any rules fired.

### `ReportService`

- `GetCashflowByMonth(from, to)` — aggregates income/expenses/savings per calendar month.
- `GetSpendByCategory(from, to)` — totals expense amounts grouped by category name.
- `GetWeeklyHeatmap(from, to)` — groups expense amounts by (week-start, category) for the heatmap.
- `ExportPdfAsync(data, path)` — renders a dark-themed A4 PDF with a cashflow summary table and a SkiaSharp line chart using QuestPDF Community license.

### `BudgetAlertService`

After each new expense transaction is saved, evaluates the category's monthly budget. If the spend ratio reaches ≥ 80%, it either posts to `NotificationService` (if injected) or shows an animated slide-in popup toast in the bottom-right corner of the window. The toast auto-dismisses after 4 seconds.

### `NotificationService`

Singleton in-memory feed of `NotificationItem` objects. The bell button in the sidebar shows a pink badge with the unread count. `NotificationKind` determines the icon glyph and accent color (BudgetWarning = purple, RecurringTransactions = teal, General = muted).

---

## 6. ViewModels & Data Flow

### `MainViewModel`

- Holds singleton instances of all page ViewModels.
- `NavigateTo(string tag)` sets `CurrentPage` and calls `LoadAsync()` on the target VM.
- `MainWindow` listens to `ListBox.SelectionChanged` and calls `NavigateTo`.

### `DashboardViewModel`

`LoadAsync()` queries all accounts and all transactions, then computes:

- **Total Balance** — sum of all `Account.Balance` (currency-converted if `AppSettingsService` is available).
- **Monthly Income / Expenses** — filtered to current calendar month.
- **Savings Rate** — `(income - expenses) / income * 100`, clamped 0–100.
- **Month-over-month deltas** — each KPI compared to the prior calendar month, formatted as `↑ X.X% vs last month`.
- **Cashflow chart** — 6-month rolling income vs expense line chart.
- **Net Worth trend** — 6-month cumulative balance line chart using `OpeningBalance + all prior transactions`.
- **Category pie chart** — current-month expense breakdown, top 6 categories.
- **Recent transactions** — 5 most recent, each showing `TypeLabel` (Income/Expense/Transfer) with color coding.

### `TransactionsViewModel`

Full CRUD for transactions with:

- Paginated list (configurable page size)
- Filters: text search, date range, category (multi-select popup), account (ComboBox), type (Income/Expense/Transfer pills)
- `SaveAsync()` — add or edit, then calls `RecalculateAccountBalancesAsync()` on all affected accounts
- `DeleteTransactionAsync()` / `DeleteSelectedAsync()` — with confirmation dialog; both recalculate balances
- CSV export via `SaveFileDialog`
- Inline budget limit display per transaction row (category budget cap)
- Recurring rule auto-sync: if `IsRecurring` is checked, creates or updates a `RecurringRule` linked to the saved transaction

### `AccountsViewModel`

- Displays account tiles (name, type, balance, sparkline chart of last 5 balance points, monthly delta text)
- Shows transaction history for the selected account, filtered to that account's `AccountId`
- Pagination: configurable `PageSize`, `CurrentPage`, `TotalPages` with Previous/Next commands
- `TrendText` is computed from real transactions: sums all current-month transactions for the account and formats as `↑ +$X,XXX.XX this month` or `↓ -$X,XXX.XX this month`
- Add Account creates a blank record in DB and reloads

### `BudgetViewModel`

- Header shows aggregate **Spent** and **Remaining** across all budgets in the selected date range.
- Range selector: 1D / 1W / 1M (default) / 1Y — each recalculates the cumulative spending line chart.
- **Budget chart** — cumulative daily spend vs flat budget limit line.
- **By Account** — `ProgressBar` per account showing percentage of total expenses, derived from real transaction grouping.
- **Overspent Categories** — categories at ≥ 80% of their monthly limit, with a 5-dot risk indicator; only populated when real data exists.
- **Budget Cards** — one card per `Budget` record, showing actual spend vs limit, icon from category's `IconCode` (mapped to Segoe MDL2 glyph), progress bar, and days remaining.
- **Manage Budgets dialog** — `OpenBudgetDialogAsync` loads all expense categories and current month budgets; `SaveBudgetsAsync` upserts/removes budget records; `CopyLastMonthBudgetsAsync` copies prior month's limits.

### `ReportsViewModel`

- Date range preset buttons (This month / Last month / Last 3 months / This year / Custom)
- Custom date range via two `DatePicker` controls
- Account filter: multi-select ComboBox with `ToggleAccountCommand`
- **Period summary table** — 6 rows of monthly income/expenses/savings
- **Avg daily spend**, **Month-end forecast**, **YoY comparison** text summaries
- **Top Categories bar chart** — horizontal bars per category
- **Weekly Heatmap** — 6 category rows × 12 week columns; intensity shaded by spend amount
- **Status Overview donut chart** — 3 slices: "Open" (≥ $500 expenses), "Reviewing" (recurring expenses), "Cleared" (remainder)
- **Flagged Transactions table** — expenses ≥ $150 or recurring, status derived from amount
- `ExportPdfAsync` — opens `SaveFileDialog` for `.pdf`, calls `ReportService.ExportPdfAsync()`
- `ExportAllTransactionsCsvAsync` — all transactions to `.csv`
- `ExportCsvCommand` — flagged transactions to `.csv`

### `RecurringViewModel`

- Displays upcoming recurring transactions from `RecurringRules` joined to their template transaction
- Shows: next occurrence date, description, category, account, frequency, amount
- **Pause/Resume** — toggles `RecurringRule.IsPaused`
- **Skip** — advances `NextOccurrence` by one frequency period without creating a transaction
- **Remove** — deletes the rule and disconnects its template transaction's `IsRecurring` flag
- Action buttons are disabled for sample/placeholder items (when `RuleId == 0`)

### `SettingsViewModel`

#### General tab
- **Currency** — ComboBox list of currencies; change calls `PersistSettings()` + `AppSettingsService.Save()`
- **Date format** — ComboBox; change persists immediately
- **First day of week** — ComboBox
- **Accent color swatches** — 8 color options; selecting one calls `App.ApplyAccentColor()` to update `AccentPurpleBrush`, `AccentPurpleDimBrush`, `AccentPurpleHoverBrush` in `Application.Current.Resources` live, and persists to `appsettings.json`

#### Accounts tab
- Inline-editable DataGrid for all accounts (name, type, color, opening balance)
- Add / Delete / Save commands backed by EF CRUD

#### Categories tab
- Inline-editable DataGrid for all categories (name, icon, color, type)
- Add / Delete / Save commands backed by EF CRUD

#### Data tab
- **Import CSV** — 5-step wizard: pick file → map columns → preview → confirm → import
- **Backup Database** — `SaveFileDialog` for `.db` file; copies the SQLite file
- **Restore Database** — `OpenFileDialog` for `.db`; overwrites current DB and restarts data load
- **Clear All Data** — confirmation dialog; removes all transactions, budgets, recurring rules, resets balances to opening balance

---

## 7. Views & Features

### Dashboard

The landing page. All numbers update on every navigation visit.

| Element | Data source |
|---|---|
| Total Balance card | Sum of all `Account.Balance` (currency-converted) |
| Monthly Income card | Sum of Income transactions in current month |
| Monthly Expenses card | Sum of Expense transactions in current month |
| Savings Rate gauge | `(income - expenses) / income × 100` |
| All delta badges | Compared to same metric in prior calendar month |
| Cashflow chart | 6-month rolling income vs expenses line chart |
| Spending by Category pie | Top 6 expense categories in current month |
| Net Worth trend chart | 6-month cumulative balance from opening + transactions |
| Recent Transactions list | 5 most recent, with Income/Expense/Transfer type badge |

### Transactions

Full ledger view with real-time filters.

**Filters available:**
- Free-text search (matches description)
- From / To date range
- Category multi-select popup
- Account dropdown
- Type toggle buttons (Income / Expense / Transfer)

**Actions:**
- Add Transaction — modal dialog with amount, type, description, date, category, account, notes, receipt path, recurrence toggle
- Edit Transaction — pre-fills dialog with existing values
- Duplicate Transaction — clones row into new dialog
- Delete Transaction — confirmation; balance recalculated
- Bulk delete — select multiple rows; confirmation
- Export CSV — all filtered transactions

### Recurring

Manages recurring payment schedules. Rules are auto-executed at every app startup.

**Columns:** Next date · Description · Category · Account · Frequency · Amount

**Actions per row (disabled for sample/placeholder items):**
- Pause / Resume
- Skip next occurrence
- Remove rule

### Budgets

Per-category monthly budget tracking with live progress.

**Header:** Aggregate spent vs remaining for the current period.

**Range selector:** 1D / 1W / 1M / 1Y — changes the time window for the spending chart.

**Budget Cards:** One card per active budget. Shows:
- Category icon (Segoe MDL2 glyph from category `IconCode`)
- Spent / Limit amounts
- Progress bar (purple → red when overspent)
- Days remaining in month

**By Account panel:** Progress bar per account showing its share of total expenses.

**Overspent Categories panel:** Categories at ≥ 80% of budget, with a 5-dot risk indicator.

**Manage Budgets dialog:** Set monthly limits for any expense category. Limits can be copied from the prior month.

### Reports

Analytical reporting over any date range.

| Component | Description |
|---|---|
| Period summary table | 6 rows: monthly income, expenses, savings, savings % |
| Stats line | Avg daily spend · Month-end forecast · YoY comparison |
| Top Categories chart | Horizontal bar chart, categories ranked by spend |
| Weekly Heatmap | 6 categories × 12 weeks, shaded by spend intensity |
| Status Overview donut | Open / Reviewing / Cleared transaction distribution |
| Flagged Transactions | High-value or recurring expenses with status column |
| Export PDF | Dark-themed A4 PDF with cashflow table + SkiaSharp chart |
| Export CSV | Flagged transactions / all transactions |

### Accounts

Per-account transaction history with sparkline balance trends.

**Account tiles:** Icon + name + balance + sparkline (last 5 balance points) + monthly delta (e.g. `↑ +$1,240 this month`).

**Transaction grid:** Shows all transactions for the selected account, paginated.

**Pagination:** Previous/Next page buttons with page indicator.

**Add Account:** Creates a new blank checking account with $0 balance.

### Settings

User preferences and data management.

**Sections:** General · Accounts · Categories · Data · About

| Section | Capabilities |
|---|---|
| General | Currency, date format, first day of week, accent color (8 swatches with live preview) |
| Accounts | Add, edit, delete accounts; set opening balance, type, color |
| Categories | Add, edit, delete categories; set icon, color, type |
| Data | CSV import wizard, DB backup, DB restore, clear all data |

---

## 8. Cross-Cutting Concerns

### Accent Color System

`App.ApplyAccentColor(hexColor)` updates three entries in `Application.Current.Resources`:

| Resource key | Role |
|---|---|
| `AccentPurpleBrush` | Primary accent (buttons, active nav item, progress bars) |
| `AccentPurpleDimBrush` | Dim background tint (selected rows, hover overlays) |
| `AccentPurpleHoverBrush` | Hover state for accent buttons |

Called at: startup (from persisted setting) and on swatch selection in Settings.

### Error Handling

- WPF `DispatcherUnhandledException` → `ErrorDialogService.Show()` — shows a clean dialog with the exception message and stack trace.
- `TaskScheduler.UnobservedTaskException` → same handler.
- All async commands silently guard with `if (dbContext is null) return` for design-time safety.

### Sample / Placeholder Data

All ViewModels have a parameterless constructor that loads representative sample data. This is used:

1. **At design time** in the WPF designer.
2. **At runtime as a fallback** when the DB returns no rows (e.g., first launch before seed completes, or after clearing all data).

Sample data is always replaced by real data the moment `LoadAsync()` finds actual DB records.

### Currency Formatting

All money display goes through `AppSettingsService.CurrencyCulture`. When currency is changed in Settings, all ViewModels pick up the new culture on the next `LoadAsync()` call.

---

## 9. Seeded Demo Data

`DatabaseService` seeds the following on first launch (when `Accounts` table is empty):

**Accounts (3):**
- Checking — $12,480.25 (Opening: $10,000)
- Savings — $8,320.00 (Opening: $7,500)
- Travel Card — –$1,480.75 (Opening: $0, CreditCard type)

**Categories (11):**
- Expense: Housing, Food, Transport, Health, Entertainment, Subscriptions, Shopping
- Income: Salary, Freelance, Investment Returns

**Transactions:** ~40 transactions spread across 6 months covering all accounts and categories.

**Budgets (5):** Monthly limits for Housing ($2,000), Food ($800), Transport ($400), Entertainment ($300), Subscriptions ($150).

**Recurring Rules (3):**
- Netflix — Monthly, $15.99 (Expense / Subscriptions)
- Rent — Monthly, $1,850.00 (Expense / Housing)
- Train Pass — Monthly, $78.00 (Expense / Transport)

---

## 10. Build & Run

### Prerequisites

- .NET 8 SDK
- Windows 10 or later (WPF is Windows-only)

### Run

```powershell
dotnet run --project FinanceTracker\FinanceTracker.csproj
```

### Build only

```powershell
dotnet build FinanceTracker\FinanceTracker.csproj
```

### Publish (self-contained, single file)

```powershell
dotnet publish FinanceTracker\FinanceTracker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### Database location

SQLite file is created at `%LOCALAPPDATA%\FinanceTracker\finance.db` (resolved from `FinanceDbContext.DatabasePath`).

### Settings file

`appsettings.json` is created next to the executable on first launch.

### EF Core Migrations

To add a new migration:

```powershell
dotnet ef migrations add <MigrationName> --project FinanceTracker
dotnet ef database update --project FinanceTracker
```

---

## 11. Known Behaviors & Edge Cases

| Behavior | Detail |
|---|---|
| **Accent color uses StaticResource** | Most brushes bind via `{StaticResource}`. `ApplyAccentColor()` updates the resource BEFORE `MainWindow.Show()` at startup, so static bindings pick up the saved color. Live changes in Settings use `DynamicResource`-equivalent runtime dictionary writes — existing open windows update immediately. |
| **Balance recalculation on every mutation** | `Account.Balance` is recalculated from raw transactions every time any transaction is added, edited, or deleted. This is correct but O(n) in transaction count per account. For large datasets, consider a running-total index. |
| **Recurring rules fire once per startup** | `RecurringService.ProcessDueRulesAsync()` runs once when the app starts. If the app is not opened for multiple periods, all missed occurrences fire at once on next launch and the rule is advanced past today. |
| **Empty Budget page** | When no budgets exist in the DB (e.g., fresh start before any budgets are created), the page shows empty state. Use "Manage Budgets" to set limits. |
| **Category legend on Dashboard** | Shows only the top 6 expense categories for the current month. Categories with $0 spend in the current month do not appear. |
| **Multi-currency accounts** | Individual accounts can have different `Currency` values. Dashboard total balance converts using `AppSettingsService.SumAccountsInBaseCurrency()`. Transaction amounts are stored in their native account currency — no currency conversion happens on individual transactions. |
| **CSV Import wizard** | Supports mapping arbitrary CSV columns to Description, Amount, Date, Category, and Account fields. New categories/accounts are created if the mapped value doesn't match an existing one. |
| **Receipt attachments** | `ReceiptPath` stores a file path string. No file is copied into the app's storage — it links to the original path. If the file is moved or deleted, the link breaks silently. |
| **Design-time sample data** | The WPF designer renders sample data from parameterless VM constructors. No DB access occurs at design time. |
