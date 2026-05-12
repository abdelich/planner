# Planner

Desktop app for goals (day/week/month), recurring reminders with checkboxes, finance tracking with FX, and an **AI assistant** with **voice input** that can act on your data through a typed tool catalog. Built with .NET 8 and WPF.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10/11 (uses WPF, system tray, and Windows Speech)
- (Optional) OpenAI API key — for the AI assistant and voice transcription

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

In **Settings → Launch with Windows**. It registers the app in the Windows startup.

## What it does

- **Goals** — Daily, weekly, or monthly; progress bar; recurring (every day, every N days, or specific weekdays).
- **Reminders** — Interval in minutes (e.g. every hour); today’s slots as checkboxes; monthly progress.
- **Finance** — Income and expenses by category. Currencies: **UAH / SEK / USD**. You pick the currency per transaction. Stats can be filtered by currency and by type (all / income / expense). **Conversion** uses the **National Bank of Ukraine (NBU)** API; “Update rate” refreshes and shows totals in the chosen currency. Savings accounts grouped by category with monthly snapshots.
- **Reports** — Day / week / month, plus dedicated finance / goals / reminders reports. Graphical (chart) reports for the dashboard and each domain.
- **AI Assistant** — Chat panel that runs a multi-step agent loop. Reads your goals/reminders/finance as context, then calls typed tools to make changes: `create_goal`, `update_goal`, `mark_goal_completed`, `create_reminder`, `create_transaction`, `transfer_between_savings`, `save_savings_snapshot`, `generate_report`, `open_graphical_report`, `inspect_*`, and more. Risky finance operations require explicit UI confirmation. A built-in **critic** rejects answers that claim or promise actions without an actual tool call.
- **Voice Input** — Global hotkey (default `Ctrl+Alt+Space`, configurable in Settings) opens a recording window. Audio is captured via NAudio and transcribed via OpenAI Whisper, then handled by the assistant. Optional **text-to-speech** plays the assistant’s reply via Windows Speech.

## Settings (sidebar → Настройки)

- **OpenAI API key** — stored encrypted via Windows DPAPI in `%LocalAppData%\Planner`. You can also set it via the `OPENAI_API_KEY` environment variable (the env var takes priority).
- **Model / Endpoint** — defaults: `gpt-4o-mini` and the OpenAI Chat Completions endpoint. You can point at an OpenAI-compatible endpoint.
- **Data scopes** — opt in / out of feeding finance / goals / reminders into the assistant context.
- **Voice hotkey** — capture a global hotkey for one-shot voice input.
- **Microphone** — pick a specific input device (or auto-detect).
- **Speak responses** — toggle TTS readout.
- **Launch with Windows** — adds/removes the startup entry.

## App icon

The icon (orange circle with checkmark) is in `Planner.App/app.ico` and is embedded in the exe. To regenerate it:

```bash
dotnet run --project IconGenerator -- Planner.App\app.ico
```

## Data

SQLite database path:

`%LocalAppData%\Planner\planner.db`

Assistant settings (encrypted API key, voice hotkey, etc.):

`%LocalAppData%\Planner\assistant.settings.json`

Diagnostics log:

`%LocalAppData%\Planner\assistant.diagnostics.log`

## Stack

- .NET 8, WPF
- SQLite, Entity Framework Core 8
- CommunityToolkit.Mvvm
- NAudio (microphone capture)
- System.Speech (TTS)
- OpenAI Chat Completions + Whisper transcription
- National Bank of Ukraine FX API
