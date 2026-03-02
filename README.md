# Planner

Desktop app for goals (day/week/month), recurring reminders with checkboxes, and basic finance tracking. Built with .NET 8 and WPF.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Run

From the repo root:

```bash
dotnet run --project Planner.App
```

Or from the app folder:

```bash
cd Planner.App
dotnet run
```

Note: plain `dotnet run` from the solution root won’t work because no startup project is set.

## Build a single EXE

```bash
cd Planner.App
dotnet publish -c Release
```

Output: `Planner.App\bin\Release\net8.0-windows\win-x64\publish\Planner.App.exe`. Copy the whole `publish` folder where you want and run the exe. You can create a shortcut (right‑click exe → Create shortcut) and pin it to the taskbar; the app icon (orange circle with checkmark) will show there.

## Start with Windows

In the app sidebar, turn on **“Launch with Windows”**. It registers itself in the system startup.

## What it does

- **Goals** — Daily, weekly, or monthly; progress bar; recurring by weekdays (e.g. Tuesdays only).
- **Reminders** — Interval in minutes (e.g. every hour); today’s slots as checkboxes; monthly progress.
- **Finance** — Income and expenses by category. Currencies: **SEK** and **UAH**. You pick the currency per transaction. Stats can be filtered by currency and by type (all / income only / expense only). **Conversion** uses the **National Bank of Ukraine (NBU)** API; use “Update rate” to refresh and see amounts in the chosen currency. More bank integrations may come later.

## App icon

The icon (orange circle with checkmark) is in `Planner.App/app.ico` and is embedded in the exe. To regenerate it:

```bash
dotnet run --project IconGenerator -- Planner.App\app.ico
```

## Data

SQLite database path:

`%LocalAppData%\Planner\planner.db`

## Stack

- .NET 8, WPF  
- SQLite, Entity Framework Core 8  
- CommunityToolkit.Mvvm  
