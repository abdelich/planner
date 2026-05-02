using System.Globalization;
using Planner.App.Models;

namespace Planner.App.Services;

public class ToolCommandRouter
{
    private readonly PlannerService _planner = new();

    public Task<AssistantToolResult> ExecuteAsync(AssistantToolCommand command)
        => ExecuteAsync(command, null);

    public async Task<AssistantToolResult> ExecuteAsync(AssistantToolCommand command, AssistantToolExecutionContext? context)
    {
        if (command == null || string.IsNullOrWhiteSpace(command.Name))
            return new AssistantToolResult(false, "Команда пустая.");
        context ??= new AssistantToolExecutionContext();

        return command.Name.Trim().ToLowerInvariant() switch
        {
            "create_goal" => await CreateGoalAsync(command.Args),
            "create_reminder" => await CreateReminderAsync(command.Args),
            "create_transaction" => await CreateTransactionAsync(command.Args, context),
            "transfer_between_savings" => await TransferBetweenSavingsAsync(command.Args, context),
            "save_period_note" => await SavePeriodNoteAsync(command.Args),
            _ => new AssistantToolResult(false, $"Неизвестная команда: {command.Name}")
        };
    }

    private async Task<AssistantToolResult> CreateGoalAsync(Dictionary<string, string> args)
    {
        if (!args.TryGetValue("title", out var title) || string.IsNullOrWhiteSpace(title))
            return new AssistantToolResult(false, "Для цели нужен title.");
        var goal = new Goal
        {
            Title = title.Trim(),
            Description = args.TryGetValue("description", out var desc) ? desc : null,
            Category = GoalCategory.Period,
            Type = GoalType.Daily,
            TargetCount = ParseInt(args, "targetCount", 1, 1, 9999)
        };
        await _planner.AddGoalAsync(goal);
        return new AssistantToolResult(true, $"Создана цель: {goal.Title}");
    }

    private async Task<AssistantToolResult> CreateReminderAsync(Dictionary<string, string> args)
    {
        if (!args.TryGetValue("title", out var title) || string.IsNullOrWhiteSpace(title))
            return new AssistantToolResult(false, "Для напоминания нужен title.");
        var interval = ParseInt(args, "intervalMinutes", 60, 1, 10080);
        var reminder = new Reminder
        {
            Title = title.Trim(),
            IntervalMinutes = interval,
            IsEnabled = true
        };
        await _planner.AddReminderAsync(reminder);
        return new AssistantToolResult(true, $"Создано напоминание: {reminder.Title} ({interval} мин).");
    }

    private async Task<AssistantToolResult> CreateTransactionAsync(Dictionary<string, string> args, AssistantToolExecutionContext context)
    {
        if (!context.UserConfirmedFinance && !IsConfirmed(args))
            return new AssistantToolResult(false, "Финансовая операция отменена или не подтверждена.");
        if (!args.TryGetValue("amount", out var amountText) || !decimal.TryParse(amountText, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return new AssistantToolResult(false, "Для операции нужна положительная сумма amount.");
        if (!args.TryGetValue("categoryId", out var categoryIdText) || !int.TryParse(categoryIdText, out var categoryId))
            return new AssistantToolResult(false, "Для операции нужен categoryId.");
        if (!args.TryGetValue("savingsEntryId", out var savingsIdText) || !int.TryParse(savingsIdText, out var savingsEntryId))
            return new AssistantToolResult(false, "Для операции нужен savingsEntryId.");

        var currency = args.TryGetValue("currency", out var c) && !string.IsNullOrWhiteSpace(c) ? c.Trim().ToUpperInvariant() : CurrencyInfo.SEK;
        var transactionType = ParseTransactionType(args);

        var tx = new Transaction
        {
            Amount = amount,
            Currency = currency,
            Date = DateTime.Today,
            CategoryId = categoryId,
            Note = args.TryGetValue("note", out var n) ? n : null
        };

        await _planner.AddTransactionAsync(tx);
        await _planner.AddDeltaToSavingsBalanceAsync(savingsEntryId, transactionType == TransactionType.Income ? amount : -amount);
        return new AssistantToolResult(true, "Финансовая операция создана.");
    }

    private async Task<AssistantToolResult> TransferBetweenSavingsAsync(Dictionary<string, string> args, AssistantToolExecutionContext context)
    {
        if (!context.UserConfirmedFinance && !IsConfirmed(args))
            return new AssistantToolResult(false, "Перевод отменён или не подтверждён.");
        if (!args.TryGetValue("fromSavingsEntryId", out var fromText) || !int.TryParse(fromText, out var fromId))
            return new AssistantToolResult(false, "Нужен fromSavingsEntryId.");
        if (!args.TryGetValue("toSavingsEntryId", out var toText) || !int.TryParse(toText, out var toId))
            return new AssistantToolResult(false, "Нужен toSavingsEntryId.");
        if (!args.TryGetValue("amount", out var amountText) || !decimal.TryParse(amountText, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return new AssistantToolResult(false, "Нужна сумма amount > 0.");
        if (fromId == toId)
            return new AssistantToolResult(false, "Счета перевода должны быть разными.");

        await _planner.TransferBetweenSavingsAsync(fromId, -amount, toId, amount);
        return new AssistantToolResult(true, "Перевод между счетами выполнен.");
    }

    private async Task<AssistantToolResult> SavePeriodNoteAsync(Dictionary<string, string> args)
    {
        if (!args.TryGetValue("text", out var text))
            return new AssistantToolResult(false, "Для заметки нужен text.");
        var kind = NotePeriodKind.Day;
        if (args.TryGetValue("kind", out var kindText))
        {
            kind = kindText?.Trim().ToLowerInvariant() switch
            {
                "month" => NotePeriodKind.Month,
                _ => NotePeriodKind.Day
            };
        }
        await _planner.SavePeriodNoteAsync(kind, DateTime.Today, text ?? "");
        return new AssistantToolResult(true, "Заметка сохранена.");
    }

    private static int ParseInt(Dictionary<string, string> args, string key, int fallback, int min, int max)
    {
        if (!args.TryGetValue(key, out var raw) || !int.TryParse(raw, out var value))
            return fallback;
        return Math.Clamp(value, min, max);
    }

    private static TransactionType ParseTransactionType(Dictionary<string, string> args)
    {
        if (args.TryGetValue("type", out var t) && t != null && t.Equals("income", StringComparison.OrdinalIgnoreCase))
            return TransactionType.Income;
        return TransactionType.Expense;
    }

    private static bool IsConfirmed(Dictionary<string, string> args)
    {
        return args.TryGetValue("confirm", out var confirm) &&
               (string.Equals(confirm, "true", StringComparison.OrdinalIgnoreCase) || confirm == "1");
    }
}
