using System.Globalization;
using Planner.App.Models;

namespace Planner.App.Services;

public class ToolCommandRouter : IDisposable
{
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
            "update_goal" => await UpdateGoalAsync(command.Args),
            "delete_goal" => await DeleteGoalAsync(command.Args),
            "mark_goal_completed" => await MarkGoalCompletedAsync(command.Args),
            "unmark_goal_completed" => await UnmarkGoalCompletedAsync(command.Args),
            "create_reminder" => await CreateReminderAsync(command.Args),
            "update_reminder" => await UpdateReminderAsync(command.Args),
            "delete_reminder" => await DeleteReminderAsync(command.Args),
            "mark_reminder_completed" => await MarkReminderCompletedAsync(command.Args),
            "unmark_reminder_completed" => await UnmarkReminderCompletedAsync(command.Args),
            "archive_goal" => await ArchiveGoalAsync(command.Args, archive: true),
            "unarchive_goal" => await ArchiveGoalAsync(command.Args, archive: false),
            "save_savings_snapshot" => await SaveSavingsSnapshotAsync(command.Args),
            "inspect_savings_snapshots" => await InspectSavingsSnapshotsAsync(command.Args),
            "create_transaction" => await CreateTransactionAsync(command.Args, context),
            "update_transaction" => await UpdateTransactionAsync(command.Args, context),
            "delete_transaction" => await DeleteTransactionAsync(command.Args, context),
            "transfer_between_savings" => await TransferBetweenSavingsAsync(command.Args, context),
            "create_finance_category" => await CreateFinanceCategoryAsync(command.Args),
            "update_finance_category" => await UpdateFinanceCategoryAsync(command.Args),
            "delete_finance_category" => await DeleteFinanceCategoryAsync(command.Args),
            "create_savings_category" => await CreateSavingsCategoryAsync(command.Args),
            "update_savings_category" => await UpdateSavingsCategoryAsync(command.Args),
            "delete_savings_category" => await DeleteSavingsCategoryAsync(command.Args),
            "create_savings_account" => await CreateSavingsAccountAsync(command.Args),
            "create_savings_entry" => await CreateSavingsAccountAsync(command.Args),
            "update_savings_account" => await UpdateSavingsAccountAsync(command.Args),
            "update_savings_entry" => await UpdateSavingsAccountAsync(command.Args),
            "delete_savings_account" => await DeleteSavingsAccountAsync(command.Args),
            "delete_savings_entry" => await DeleteSavingsAccountAsync(command.Args),
            "save_period_note" => await SavePeriodNoteAsync(command.Args),
            "inspect_goals" => await InspectGoalsAsync(command.Args),
            "inspect_reminders" => await InspectRemindersAsync(command.Args),
            "inspect_finances" => await InspectFinancesAsync(command.Args),
            "inspect_exchange_rates" => await InspectExchangeRatesAsync(command.Args),
            "get_exchange_rates" => await InspectExchangeRatesAsync(command.Args),
            "inspect_reports" => await InspectReportsAsync(command.Args),
            "generate_report" => await GenerateReportAsync(command.Args),
            "open_graphical_report" => await OpenGraphicalReportAsync(command.Args),
            "create_graphical_report" => await OpenGraphicalReportAsync(command.Args),
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
            Category = ParseGoalCategory(args),
            Type = ParseGoalType(args),
            RecurrenceKind = ParseRecurrenceKind(args),
            IntervalDays = ParseInt(args, "intervalDays", 1, 1, 365),
            RecurrenceDays = ParseWeekdayMask(args),
            TargetCount = ParseInt(args, "targetCount", 1, 1, 9999),
            StartDate = ParseDate(args, "startDate") ?? DateTime.Today
        };
        using var planner = new PlannerService();
        await planner.AddGoalAsync(goal);
        return new AssistantToolResult(true, $"Создана цель: #{goal.Id} {goal.Title}");
    }

    private async Task<AssistantToolResult> UpdateGoalAsync(Dictionary<string, string> args)
    {
        if (!TryParseIntArg(args, "goalId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для изменения цели нужен goalId.");

        using var planner = new PlannerService();
        var goal = (await planner.GetGoalsAsync(includeArchived: true)).FirstOrDefault(x => x.Id == id);
        if (goal == null) return new AssistantToolResult(false, $"Цель #{id} не найдена.");

        if (args.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
            goal.Title = title.Trim();
        if (args.ContainsKey("description"))
            goal.Description = string.IsNullOrWhiteSpace(args["description"]) ? null : args["description"].Trim();
        if (args.ContainsKey("targetCount"))
            goal.TargetCount = ParseInt(args, "targetCount", goal.TargetCount <= 0 ? 1 : goal.TargetCount, 1, 9999);
        if (args.ContainsKey("type"))
            goal.Type = ParseGoalType(args, goal.Type);
        if (args.ContainsKey("category"))
            goal.Category = ParseGoalCategory(args, goal.Category);
        if (args.ContainsKey("startDate"))
            goal.StartDate = ParseDate(args, "startDate") ?? goal.StartDate;
        if (args.ContainsKey("recurrenceKind"))
            goal.RecurrenceKind = ParseRecurrenceKind(args, goal.RecurrenceKind);
        if (args.ContainsKey("intervalDays"))
            goal.IntervalDays = ParseInt(args, "intervalDays", goal.IntervalDays <= 0 ? 1 : goal.IntervalDays, 1, 365);
        if (args.ContainsKey("weekdays") || args.ContainsKey("recurrenceDays"))
            goal.RecurrenceDays = ParseWeekdayMask(args);
        if (args.TryGetValue("isArchived", out var archived) && bool.TryParse(archived, out var isArchived))
            goal.IsArchived = isArchived;

        await planner.UpdateGoalAsync(goal);
        return new AssistantToolResult(true, $"Цель обновлена: {goal.Title}");
    }

    private async Task<AssistantToolResult> DeleteGoalAsync(Dictionary<string, string> args)
    {
        if (!TryParseIntArg(args, "goalId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для удаления цели нужен goalId.");
        using var planner = new PlannerService();
        await planner.DeleteGoalByIdAsync(id);
        return new AssistantToolResult(true, $"Цель #{id} удалена.");
    }

    private async Task<AssistantToolResult> MarkGoalCompletedAsync(Dictionary<string, string> args)
    {
        if (!TryParseIntArg(args, "goalId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для отметки цели нужен goalId.");
        var date = ParseDate(args, "date") ?? DateTime.Today;
        var count = ParseInt(args, "count", 1, 1, 9999);
        using var planner = new PlannerService();
        var goal = (await planner.GetGoalsAsync(includeArchived: true)).FirstOrDefault(x => x.Id == id);
        if (goal == null) return new AssistantToolResult(false, $"Цель #{id} не найдена.");
        await planner.MarkGoalCompleteAsync(id, date, count);
        GoalCompletionNotificationService.Publish(id, date, true);
        return new AssistantToolResult(true, $"Отмечено выполнение цели: {goal.Title} ({date:dd.MM.yyyy}).");
    }

    private async Task<AssistantToolResult> UnmarkGoalCompletedAsync(Dictionary<string, string> args)
    {
        if (!TryParseIntArg(args, "goalId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для снятия выполнения цели нужен goalId.");
        var date = ParseDate(args, "date") ?? DateTime.Today;
        using var planner = new PlannerService();
        await planner.UnmarkGoalCompletionAsync(id, date);
        GoalCompletionNotificationService.Publish(id, date, false);
        return new AssistantToolResult(true, $"Выполнение цели #{id} снято за {date:dd.MM.yyyy}.");
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
            ActiveFrom = ParseTime(args, "activeFrom"),
            ActiveTo = ParseTime(args, "activeTo"),
            IsEnabled = true
        };
        using var planner = new PlannerService();
        await planner.AddReminderAsync(reminder);
        return new AssistantToolResult(true, $"Создано напоминание: #{reminder.Id} {reminder.Title} ({interval} мин).");
    }

    private async Task<AssistantToolResult> UpdateReminderAsync(Dictionary<string, string> args)
    {
        if (!TryParseIntArg(args, "reminderId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для изменения напоминания нужен reminderId.");
        using var planner = new PlannerService();
        var reminder = await planner.GetReminderByIdAsync(id);
        if (reminder == null) return new AssistantToolResult(false, $"Напоминание #{id} не найдено.");
        if (args.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
            reminder.Title = title.Trim();
        if (args.ContainsKey("intervalMinutes"))
            reminder.IntervalMinutes = ParseInt(args, "intervalMinutes", reminder.IntervalMinutes <= 0 ? 60 : reminder.IntervalMinutes, 1, 10080);
        if (args.ContainsKey("activeFrom"))
            reminder.ActiveFrom = ParseTime(args, "activeFrom");
        if (args.ContainsKey("activeTo"))
            reminder.ActiveTo = ParseTime(args, "activeTo");
        if (args.TryGetValue("isEnabled", out var enabled) && bool.TryParse(enabled, out var isEnabled))
            reminder.IsEnabled = isEnabled;
        await planner.UpdateReminderAsync(reminder);
        return new AssistantToolResult(true, $"Напоминание обновлено: {reminder.Title}");
    }

    private async Task<AssistantToolResult> DeleteReminderAsync(Dictionary<string, string> args)
    {
        if (!TryParseIntArg(args, "reminderId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для удаления напоминания нужен reminderId.");
        using var planner = new PlannerService();
        await planner.DeleteReminderByIdAsync(id);
        return new AssistantToolResult(true, $"Напоминание #{id} удалено.");
    }

    private async Task<AssistantToolResult> MarkReminderCompletedAsync(Dictionary<string, string> args)
    {
        if (!TryParseIntArg(args, "reminderId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для отметки напоминания нужен reminderId.");
        var date = ParseDate(args, "date") ?? DateTime.Today;
        var time = ParseTime(args, "time") ?? TimeOnly.FromDateTime(DateTime.Now);
        var slot = date.Date.Add(time.ToTimeSpan());
        using var planner = new PlannerService();
        var changed = await planner.SetReminderSlotCompletedAsync(id, slot, true);
        if (changed)
            ReminderCompletionNotificationService.Publish(id, slot, true, 1);
        return new AssistantToolResult(true, $"Напоминание #{id} отмечено выполненным ({slot:dd.MM HH:mm}).");
    }

    private async Task<AssistantToolResult> UnmarkReminderCompletedAsync(Dictionary<string, string> args)
    {
        if (!TryParseIntArg(args, "reminderId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для снятия отметки напоминания нужен reminderId.");
        var date = ParseDate(args, "date") ?? DateTime.Today;
        var time = ParseTime(args, "time") ?? TimeOnly.FromDateTime(DateTime.Now);
        var slot = date.Date.Add(time.ToTimeSpan());
        using var planner = new PlannerService();
        var changed = await planner.SetReminderSlotCompletedAsync(id, slot, false);
        if (changed)
            ReminderCompletionNotificationService.Publish(id, slot, false, 1);
        return new AssistantToolResult(true, $"Отметка напоминания #{id} снята ({slot:dd.MM HH:mm}).");
    }

    private async Task<AssistantToolResult> ArchiveGoalAsync(Dictionary<string, string> args, bool archive)
    {
        if (!TryParseIntArg(args, "goalId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, archive
                ? "Для архивации цели нужен goalId."
                : "Для восстановления цели нужен goalId.");
        using var planner = new PlannerService();
        var goal = (await planner.GetGoalsAsync(includeArchived: true)).FirstOrDefault(x => x.Id == id);
        if (goal == null) return new AssistantToolResult(false, $"Цель #{id} не найдена.");
        if (goal.IsArchived == archive)
            return new AssistantToolResult(true, archive
                ? $"Цель «{goal.Title}» уже в архиве."
                : $"Цель «{goal.Title}» уже активна.");
        goal.IsArchived = archive;
        await planner.UpdateGoalAsync(goal);
        return new AssistantToolResult(true, archive
            ? $"Цель «{goal.Title}» отправлена в архив."
            : $"Цель «{goal.Title}» восстановлена из архива.");
    }

    private async Task<AssistantToolResult> SaveSavingsSnapshotAsync(Dictionary<string, string> args)
    {
        var today = DateTime.Today;
        var year = ParseInt(args, "year", today.Year, 2000, 2100);
        var month = ParseInt(args, "month", today.Month, 1, 12);

        decimal totalUah;
        if (TryParseDecimalArg(args, "totalUah", out var explicitTotal) && explicitTotal >= 0)
        {
            totalUah = explicitTotal;
        }
        else
        {
            using var planner = new PlannerService();
            var savings = await planner.GetSavingsEntriesAsync();
            var rates = await new ExchangeRateService().GetDailyRatesAsync();
            decimal sum = 0;
            foreach (var entry in savings)
            {
                if (entry.Currency.Equals(CurrencyInfo.UAH, StringComparison.OrdinalIgnoreCase))
                {
                    sum += entry.Balance;
                    continue;
                }
                if (rates != null &&
                    ExchangeRateService.ConvertWithRates(entry.Balance, entry.Currency, CurrencyInfo.UAH, rates) is { } converted)
                {
                    sum += converted;
                }
            }
            totalUah = sum;
        }

        using var saver = new PlannerService();
        await saver.SaveSavingsSnapshotAsync(year, month, totalUah);
        FinanceDataChangedNotificationService.Publish("save_savings_snapshot", new DateTime(year, month, 1));
        return new AssistantToolResult(true, $"Срез сбережений сохранён: {year}-{month:00}, {totalUah:N2} UAH.");
    }

    private async Task<AssistantToolResult> InspectSavingsSnapshotsAsync(Dictionary<string, string> args)
    {
        var limit = ParseInt(args, "limit", 24, 1, 240);
        using var planner = new PlannerService();
        var snapshots = (await planner.GetSavingsMonthlySnapshotsAsync())
            .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .Take(limit)
            .ToList();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Срезы сбережений: {snapshots.Count}");
        foreach (var s in snapshots)
            sb.AppendLine($"- {s.Year}-{s.Month:00}: {s.TotalAmountUah:N2} UAH (записано {s.RecordedAt:yyyy-MM-dd HH:mm})");
        return new AssistantToolResult(true, sb.ToString().Trim());
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
            Date = ParseDate(args, "date") ?? DateTime.Today,
            CategoryId = categoryId,
            Note = args.TryGetValue("note", out var n) ? n : null
        };

        using var planner = new PlannerService();
        await planner.AddTransactionAsync(tx);
        await planner.AddDeltaToSavingsBalanceAsync(savingsEntryId, transactionType == TransactionType.Income ? amount : -amount);
        FinanceDataChangedNotificationService.Publish("create_transaction", tx.Date);
        return new AssistantToolResult(true, $"Финансовая операция создана: #{tx.Id}.");
    }

    private async Task<AssistantToolResult> UpdateTransactionAsync(Dictionary<string, string> args, AssistantToolExecutionContext context)
    {
        if (!context.UserConfirmedFinance && !IsConfirmed(args))
            return new AssistantToolResult(false, "Изменение финансовой операции отменено или не подтверждено.");
        if (!TryParseIntArg(args, "transactionId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для изменения операции нужен transactionId.");

        decimal? amount = null;
        if (args.ContainsKey("amount"))
        {
            if (!TryParseDecimalArg(args, "amount", out var parsedAmount) || parsedAmount <= 0)
                return new AssistantToolResult(false, "Для операции нужна положительная сумма amount.");
            amount = parsedAmount;
        }

        int? categoryId = null;
        if (args.ContainsKey("categoryId"))
        {
            if (!TryParseIntArg(args, "categoryId", out var parsedCategoryId))
                return new AssistantToolResult(false, "categoryId должен быть числом.");
            categoryId = parsedCategoryId;
        }

        var date = ParseDate(args, "date");
        var currency = args.TryGetValue("currency", out var c) && !string.IsNullOrWhiteSpace(c) ? c.Trim().ToUpperInvariant() : null;
        var updateNote = args.ContainsKey("note");
        var note = updateNote ? args["note"] : null;

        if (amount == null && categoryId == null && date == null && currency == null && !updateNote)
            return new AssistantToolResult(false, "Не указано, что изменить в операции.");

        using var planner = new PlannerService();
        var ok = await planner.UpdateTransactionAsync(id, amount, categoryId, date, currency, note, updateNote);
        if (ok)
            FinanceDataChangedNotificationService.Publish("update_transaction", date ?? DateTime.Today);
        return ok
            ? new AssistantToolResult(true, $"Финансовая операция #{id} обновлена.")
            : new AssistantToolResult(false, $"Финансовая операция #{id} не найдена.");
    }

    private async Task<AssistantToolResult> DeleteTransactionAsync(Dictionary<string, string> args, AssistantToolExecutionContext context)
    {
        if (!context.UserConfirmedFinance && !IsConfirmed(args))
            return new AssistantToolResult(false, "Удаление финансовой операции отменено или не подтверждено.");
        if (!TryParseIntArg(args, "transactionId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для удаления операции нужен transactionId.");

        using var planner = new PlannerService();
        var transaction = await planner.GetTransactionByIdAsync(id);
        if (transaction == null)
            return new AssistantToolResult(false, $"Финансовая операция #{id} не найдена.");

        await planner.DeleteTransactionByIdAsync(id);
        FinanceDataChangedNotificationService.Publish("delete_transaction", transaction.Date);
        return new AssistantToolResult(true, $"Финансовая операция #{id} удалена.");
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

        using var planner = new PlannerService();
        await planner.TransferBetweenSavingsAsync(fromId, -amount, toId, amount);
        FinanceDataChangedNotificationService.Publish("transfer_between_savings");
        return new AssistantToolResult(true, "Перевод между счетами выполнен.");
    }

    private async Task<AssistantToolResult> CreateFinanceCategoryAsync(Dictionary<string, string> args)
    {
        if (!args.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return new AssistantToolResult(false, "Для категории нужен name.");
        using var planner = new PlannerService();
        var category = await planner.AddFinanceCategoryAsync(new FinanceCategory
        {
            Name = name.Trim(),
            Type = ParseTransactionType(args)
        });
        FinanceDataChangedNotificationService.Publish("create_finance_category");
        return new AssistantToolResult(true, $"Финансовая категория создана: #{category.Id} {category.Name}");
    }

    private async Task<AssistantToolResult> UpdateFinanceCategoryAsync(Dictionary<string, string> args)
    {
        if (!TryParseIntArg(args, "categoryId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для изменения категории нужен categoryId.");
        if (!args.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return new AssistantToolResult(false, "Для изменения категории нужен name.");
        using var planner = new PlannerService();
        await planner.UpdateFinanceCategoryAsync(id, name.Trim());
        FinanceDataChangedNotificationService.Publish("update_finance_category");
        return new AssistantToolResult(true, $"Финансовая категория #{id} обновлена.");
    }

    private async Task<AssistantToolResult> DeleteFinanceCategoryAsync(Dictionary<string, string> args)
    {
        if (!TryParseIntArg(args, "categoryId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для удаления категории нужен categoryId.");
        using var planner = new PlannerService();
        var category = (await planner.GetFinanceCategoriesAsync()).FirstOrDefault(x => x.Id == id);
        if (category == null) return new AssistantToolResult(false, $"Финансовая категория #{id} не найдена.");
        await planner.DeleteFinanceCategoryAsync(category);
        FinanceDataChangedNotificationService.Publish("delete_finance_category");
        return new AssistantToolResult(true, $"Финансовая категория удалена: {category.Name}");
    }

    private async Task<AssistantToolResult> CreateSavingsCategoryAsync(Dictionary<string, string> args)
    {
        if (!args.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return new AssistantToolResult(false, "Для категории сбережений нужен name.");
        using var planner = new PlannerService();
        var category = await planner.AddSavingsCategoryAsync(new SavingsCategory { Name = name.Trim() });
        FinanceDataChangedNotificationService.Publish("create_savings_category");
        return new AssistantToolResult(true, $"Категория сбережений создана: #{category.Id} {category.Name}");
    }

    private async Task<AssistantToolResult> UpdateSavingsCategoryAsync(Dictionary<string, string> args)
    {
        if (!TryParseIntArg(args, "categoryId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для изменения категории сбережений нужен categoryId.");
        if (!args.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return new AssistantToolResult(false, "Для изменения категории сбережений нужен name.");
        using var planner = new PlannerService();
        await planner.UpdateSavingsCategoryAsync(id, name.Trim());
        FinanceDataChangedNotificationService.Publish("update_savings_category");
        return new AssistantToolResult(true, $"Категория сбережений #{id} обновлена.");
    }

    private async Task<AssistantToolResult> DeleteSavingsCategoryAsync(Dictionary<string, string> args)
    {
        if (!TryParseIntArg(args, "categoryId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для удаления категории сбережений нужен categoryId.");
        using var planner = new PlannerService();
        var (ok, error) = await planner.TryDeleteSavingsCategoryAsync(id);
        if (ok)
            FinanceDataChangedNotificationService.Publish("delete_savings_category");
        return ok
            ? new AssistantToolResult(true, $"Категория сбережений #{id} удалена.")
            : new AssistantToolResult(false, error ?? "Не удалось удалить категорию сбережений.");
    }

    private async Task<AssistantToolResult> CreateSavingsAccountAsync(Dictionary<string, string> args)
    {
        if (!args.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return new AssistantToolResult(false, "Для счёта нужен name.");
        if (!TryParseIntArg(args, "categoryId", out var categoryId) && !TryParseIntArg(args, "savingsCategoryId", out categoryId))
            return new AssistantToolResult(false, "Для счёта нужен categoryId.");
        var balance = ParseDecimal(args, "balance", 0);
        var currency = args.TryGetValue("currency", out var c) && !string.IsNullOrWhiteSpace(c) ? c.Trim().ToUpperInvariant() : CurrencyInfo.UAH;
        using var planner = new PlannerService();
        var account = await planner.AddSavingsEntryAsync(new SavingsEntry
        {
            Name = name.Trim(),
            SavingsCategoryId = categoryId,
            Balance = balance,
            Currency = currency
        });
        FinanceDataChangedNotificationService.Publish("create_savings_account");
        return new AssistantToolResult(true, $"Счёт создан: #{account.Id} {account.Name}");
    }

    private async Task<AssistantToolResult> UpdateSavingsAccountAsync(Dictionary<string, string> args)
    {
        if (!TryParseIntArg(args, "savingsEntryId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для изменения счёта нужен savingsEntryId.");
        string? name = args.TryGetValue("name", out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : null;
        decimal? balance = args.ContainsKey("balance") ? ParseDecimal(args, "balance", 0) : null;
        using var planner = new PlannerService();
        await planner.UpdateSavingsEntryAsync(id, name, balance);
        FinanceDataChangedNotificationService.Publish("update_savings_account");
        return new AssistantToolResult(true, $"Счёт #{id} обновлён.");
    }

    private async Task<AssistantToolResult> DeleteSavingsAccountAsync(Dictionary<string, string> args)
    {
        if (!TryParseIntArg(args, "savingsEntryId", out var id) && !TryParseIntArg(args, "id", out id))
            return new AssistantToolResult(false, "Для удаления счёта нужен savingsEntryId.");
        using var planner = new PlannerService();
        await planner.DeleteSavingsEntryByIdAsync(id);
        FinanceDataChangedNotificationService.Publish("delete_savings_account");
        return new AssistantToolResult(true, $"Счёт #{id} удалён.");
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
                "week" or "weekly" => NotePeriodKind.Week,
                "month" => NotePeriodKind.Month,
                _ => NotePeriodKind.Day
            };
        }
        var date = ParseDate(args, "date") ?? DateTime.Today;
        var periodStart = kind switch
        {
            NotePeriodKind.Week => GetWeekStart(date),
            NotePeriodKind.Month => new DateTime(date.Year, date.Month, 1),
            _ => date.Date
        };
        using var planner = new PlannerService();
        await planner.SavePeriodNoteAsync(kind, periodStart, text ?? "");
        return new AssistantToolResult(true, "Заметка сохранена.");
    }

    private async Task<AssistantToolResult> GenerateReportAsync(Dictionary<string, string> args)
    {
        var kind = args.TryGetValue("kind", out var rawKind) ? rawKind.Trim().ToLowerInvariant() : "day";
        var domain = ResolveReportDomain(args);
        using var generator = new ReportGenerator();
        var body = kind switch
        {
            "month" or "monthly" => await GenerateMonthlyReportAsync(args, generator, domain),
            "week" or "weekly" => await GenerateWeeklyReportAsync(args, generator, domain),
            _ => await GenerateDailyReportAsync(args, generator, domain)
        };

        var reportKind = kind is "month" or "monthly"
            ? AssistantReportPeriodKind.Month
            : kind is "week" or "weekly"
                ? AssistantReportPeriodKind.Week
                : AssistantReportPeriodKind.Day;
        var periodStart = ResolveReportPeriodStart(args, reportKind);
        var repo = new AssistantRepositoryService();
        await repo.SaveReportAsync(reportKind, periodStart, body);
        return new AssistantToolResult(true, body);
    }

    private async Task<AssistantToolResult> OpenGraphicalReportAsync(Dictionary<string, string> args)
    {
        var kindText = args.TryGetValue("kind", out var rawKind) ? rawKind.Trim().ToLowerInvariant() : "month";
        var reportKind = kindText is "week" or "weekly"
            ? AssistantReportPeriodKind.Week
            : kindText is "day" or "daily"
                ? AssistantReportPeriodKind.Day
                : AssistantReportPeriodKind.Month;
        var periodStart = ResolveReportPeriodStart(args, reportKind);
        var domain = ResolveReportDomain(args);
        var service = new GraphicalReportService();
        var message = await service.OpenAsync(domain, reportKind, periodStart, ResolveTargetCurrency(args));
        return new AssistantToolResult(true, message);
    }

    private async Task<AssistantToolResult> InspectGoalsAsync(Dictionary<string, string> args)
    {
        var kind = args.TryGetValue("kind", out var rawKind) ? rawKind.Trim().ToLowerInvariant() : "month";
        using var generator = new ReportGenerator();
        if (kind is "day" or "daily")
            return new AssistantToolResult(true, await generator.BuildDailyGoalsReportAsync(ParseDate(args, "date") ?? DateTime.Today));
        if (kind is "week" or "weekly")
            return new AssistantToolResult(true, await generator.BuildWeeklyGoalsReportAsync(ParseDate(args, "date") ?? DateTime.Today));
        if (kind is "month" or "monthly")
            return new AssistantToolResult(true, await GenerateMonthlyReportAsync(args, generator, "goals"));

        using var planner = new PlannerService();
        var includeArchived = ParseBool(args, "includeArchived", true);
        var periodGoals = await planner.GetPeriodGoalsAsync(includeArchived);
        var recurringGoals = await planner.GetRecurringGoalsAsync(includeArchived);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Цели в базе");
        sb.AppendLine($"- Периодные: {periodGoals.Count}");
        foreach (var goal in periodGoals.OrderBy(x => x.Type).ThenBy(x => x.StartDate ?? x.CreatedAt).ThenBy(x => x.Id))
        {
            var start = (goal.StartDate ?? goal.CreatedAt).Date;
            var end = goal.Type switch
            {
                GoalType.Weekly => GetWeekStart(start).AddDays(6),
                GoalType.Monthly => new DateTime(start.Year, start.Month, 1).AddMonths(1).AddDays(-1),
                _ => start
            };
            var current = await planner.GetGoalCompletionCountAsync(goal.Id, start, end);
            sb.AppendLine($"  - id={goal.Id}; title=\"{goal.Title}\"; type={goal.Type}; progress={current}/{Math.Max(1, goal.TargetCount)}; period={start:yyyy-MM-dd}..{end:yyyy-MM-dd}; archived={goal.IsArchived}");
        }

        sb.AppendLine($"- Регулярные: {recurringGoals.Count}");
        foreach (var goal in recurringGoals.OrderBy(x => x.Id))
            sb.AppendLine($"  - id={goal.Id}; title=\"{goal.Title}\"; recurrence={goal.RecurrenceKind}; intervalDays={goal.IntervalDays}; weekdaysMask={goal.RecurrenceDays}; start={(goal.StartDate ?? goal.CreatedAt):yyyy-MM-dd}; archived={goal.IsArchived}");

        return new AssistantToolResult(true, sb.ToString().Trim());
    }

    private async Task<AssistantToolResult> InspectRemindersAsync(Dictionary<string, string> args)
    {
        var date = ParseDate(args, "date");
        var year = ParseInt(args, "year", date?.Year ?? DateTime.Today.Year, 2000, 2100);
        var month = ParseInt(args, "month", date?.Month ?? DateTime.Today.Month, 1, 12);
        var includeDisabled = ParseBool(args, "includeDisabled", true);
        using var planner = new PlannerService();
        var reminders = includeDisabled
            ? await planner.GetAllRemindersAsync()
            : await planner.GetRemindersAsync();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Напоминания в базе ({month:00}.{year})");
        sb.AppendLine($"- Всего: {reminders.Count}");
        foreach (var reminder in reminders.OrderBy(x => x.Id))
        {
            var (completed, total) = await planner.GetReminderMonthlyProgressAsync(reminder.Id, year, month);
            sb.AppendLine($"  - id={reminder.Id}; enabled={reminder.IsEnabled}; title=\"{reminder.Title}\"; intervalMinutes={reminder.IntervalMinutes}; active={FormatTime(reminder.ActiveFrom)}..{FormatTime(reminder.ActiveTo)}; monthProgress={completed}/{total}; created={reminder.CreatedAt:yyyy-MM-dd}");
        }

        return new AssistantToolResult(true, sb.ToString().Trim());
    }

    private async Task<AssistantToolResult> InspectFinancesAsync(Dictionary<string, string> args)
    {
        var kind = args.TryGetValue("kind", out var rawKind) ? rawKind.Trim().ToLowerInvariant() : "month";
        var targetCurrency = ResolveTargetCurrency(args);
        using var generator = new ReportGenerator();
        if (kind is "day" or "daily")
            return new AssistantToolResult(true, await generator.BuildDailyFinanceReportAsync(ParseDate(args, "date") ?? DateTime.Today, targetCurrency));
        if (kind is "week" or "weekly")
            return new AssistantToolResult(true, await generator.BuildWeeklyFinanceReportAsync(ParseDate(args, "date") ?? DateTime.Today, targetCurrency));
        if (kind is "month" or "monthly")
            return new AssistantToolResult(true, await GenerateMonthlyReportAsync(args, generator, "finance"));

        using var planner = new PlannerService();
        var categories = await planner.GetFinanceCategoriesAsync();
        var savingsCategories = await planner.GetSavingsCategoriesAsync();
        var savings = await planner.GetSavingsEntriesAsync();
        var today = DateTime.Today;
        var transactions = await planner.GetTransactionsAsync(new DateTime(today.Year, 1, 1), today.AddDays(1));
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Финансы в базе");
        sb.AppendLine($"- Категории операций: {categories.Count}");
        foreach (var category in categories)
            sb.AppendLine($"  - id={category.Id}; type={category.Type}; name=\"{category.Name}\"");
        sb.AppendLine($"- Категории счетов: {savingsCategories.Count}");
        foreach (var category in savingsCategories)
            sb.AppendLine($"  - id={category.Id}; name=\"{category.Name}\"");
        sb.AppendLine($"- Счета: {savings.Count}");
        foreach (var account in savings)
            sb.AppendLine($"  - id={account.Id}; categoryId={account.SavingsCategoryId}; name=\"{account.Name}\"; balance={account.Balance:N2}; currency={account.Currency}");
        sb.AppendLine($"- Операции с начала {today.Year}: {transactions.Count}");
        foreach (var tx in transactions)
            sb.AppendLine($"  - id={tx.Id}; date={tx.Date:yyyy-MM-dd}; type={tx.Category.Type}; categoryId={tx.CategoryId}; category=\"{tx.Category.Name}\"; amount={tx.Amount:N2}; currency={tx.Currency}; note=\"{tx.Note}\"");
        return new AssistantToolResult(true, sb.ToString().Trim());
    }

    private static async Task<AssistantToolResult> InspectExchangeRatesAsync(Dictionary<string, string> args)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var service = new ExchangeRateService();
        var rates = await service.GetDailyRatesAsync(cts.Token);
        if (rates == null)
            return new AssistantToolResult(false, "Не удалось получить курс валют НБУ. Проверьте интернет или попробуйте позже.");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Курсы валют НБУ на {rates.Date}");
        sb.AppendLine($"- SEK→UAH: 1 SEK = {rates.SekToUah:N4} UAH");
        if (rates.UsdToUah.HasValue)
            sb.AppendLine($"- USD→UAH: 1 USD = {rates.UsdToUah.Value:N4} UAH");
        if (rates.UsdToSek.HasValue)
            sb.AppendLine($"- USD→SEK: 1 USD = {rates.UsdToSek.Value:N4} SEK");
        if (rates.EurToSek.HasValue)
            sb.AppendLine($"- EUR→SEK: 1 EUR = {rates.EurToSek.Value:N4} SEK");

        if (args.TryGetValue("amount", out var amountText) &&
            decimal.TryParse(amountText, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) &&
            amount >= 0 &&
            args.TryGetValue("fromCurrency", out var from) &&
            args.TryGetValue("toCurrency", out var to))
        {
            var converted = ExchangeRateService.ConvertWithRates(
                amount,
                NormalizeCurrency(from),
                NormalizeCurrency(to),
                rates);
            sb.AppendLine(converted.HasValue
                ? $"- Конвертация: {amount:N2} {NormalizeCurrency(from)} = {converted.Value:N2} {NormalizeCurrency(to)}"
                : $"- Конвертация {NormalizeCurrency(from)}→{NormalizeCurrency(to)} недоступна по текущим курсам.");
        }

        return new AssistantToolResult(true, sb.ToString().Trim());
    }

    private async Task<AssistantToolResult> InspectReportsAsync(Dictionary<string, string> args)
    {
        var limit = ParseInt(args, "limit", 20, 1, 200);
        var repo = new AssistantRepositoryService();
        var reports = await repo.GetRecentReportsAsync(limit);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Сохраненные отчеты: {reports.Count}");
        foreach (var report in reports)
            sb.AppendLine($"- id={report.Id}; kind={report.Kind}; periodStart={report.PeriodStart:yyyy-MM-dd}; createdAt={report.CreatedAt:yyyy-MM-dd HH:mm}; body=\"{report.Body}\"");
        return new AssistantToolResult(true, sb.ToString().Trim());
    }

    private static async Task<string> GenerateDailyReportAsync(Dictionary<string, string> args, ReportGenerator generator, string domain)
    {
        var date = ParseDate(args, "date") ?? DateTime.Today;
        var targetCurrency = ResolveTargetCurrency(args);
        return domain switch
        {
            "finance" => await generator.BuildDailyFinanceReportAsync(date, targetCurrency),
            "goals" => await generator.BuildDailyGoalsReportAsync(date),
            _ => await generator.BuildDailyReportAsync(date)
        };
    }

    private static async Task<string> GenerateWeeklyReportAsync(Dictionary<string, string> args, ReportGenerator generator, string domain)
    {
        var date = ParseDate(args, "date") ?? DateTime.Today;
        var targetCurrency = ResolveTargetCurrency(args);
        return domain switch
        {
            "finance" => await generator.BuildWeeklyFinanceReportAsync(date, targetCurrency),
            "goals" => await generator.BuildWeeklyGoalsReportAsync(date),
            _ => await generator.BuildWeeklyReportAsync(date)
        };
    }

    private static async Task<string> GenerateMonthlyReportAsync(Dictionary<string, string> args, ReportGenerator generator, string domain)
    {
        var date = ParseDate(args, "date");
        var year = ParseInt(args, "year", date?.Year ?? DateTime.Today.Year, 2000, 2100);
        var month = ParseInt(args, "month", date?.Month ?? DateTime.Today.Month, 1, 12);
        var targetCurrency = ResolveTargetCurrency(args);
        return domain switch
        {
            "finance" => await generator.BuildMonthlyFinanceReportAsync(year, month, targetCurrency),
            "goals" => await generator.BuildMonthlyGoalsReportAsync(year, month),
            "reminders" => await generator.BuildMonthlyRemindersReportAsync(year, month),
            _ => await generator.BuildMonthlyReportAsync(year, month)
        };
    }

    private static string ResolveReportDomain(Dictionary<string, string> args)
    {
        if (!args.TryGetValue("domain", out var raw) || string.IsNullOrWhiteSpace(raw))
            return "general";

        var value = raw.Trim().ToLowerInvariant().Replace('ё', 'е');
        return value switch
        {
            "finance" or "financial" or "money" or "финансы" or "финансовый" or "деньги" => "finance",
            "goals" or "goal" or "цели" or "цель" => "goals",
            "reminders" or "reminder" or "напоминания" or "напоминание" => "reminders",
            _ => "general"
        };
    }

    private static string NormalizeCurrency(string? value)
    {
        var v = (value ?? "").Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(v) ? CurrencyInfo.UAH : v;
    }

    private static string ResolveTargetCurrency(Dictionary<string, string> args)
    {
        var raw =
            args.TryGetValue("targetCurrency", out var targetCurrency) ? targetCurrency :
            args.TryGetValue("displayCurrency", out var displayCurrency) ? displayCurrency :
            args.TryGetValue("currency", out var currency) ? currency :
            CurrencyInfo.UAH;
        var normalized = NormalizeCurrency(raw);
        return CurrencyInfo.DisplayCurrencies.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized
            : CurrencyInfo.UAH;
    }

    private static DateTime ResolveReportPeriodStart(Dictionary<string, string> args, AssistantReportPeriodKind kind)
    {
        var date = ParseDate(args, "date") ?? DateTime.Today;
        return kind switch
        {
            AssistantReportPeriodKind.Month => new DateTime(
                ParseInt(args, "year", date.Year, 2000, 2100),
                ParseInt(args, "month", date.Month, 1, 12),
                1),
            AssistantReportPeriodKind.Week => date.Date.AddDays(-((7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7)),
            _ => date.Date
        };
    }

    private static int ParseInt(Dictionary<string, string> args, string key, int fallback, int min, int max)
    {
        if (!args.TryGetValue(key, out var raw) || !int.TryParse(raw, out var value))
            return fallback;
        return Math.Clamp(value, min, max);
    }

    private static bool ParseBool(Dictionary<string, string> args, string key, bool fallback)
    {
        if (!args.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return fallback;
        if (bool.TryParse(raw, out var value))
            return value;
        return raw.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ => fallback
        };
    }

    private static bool TryParseIntArg(Dictionary<string, string> args, string key, out int value)
    {
        value = 0;
        return args.TryGetValue(key, out var raw) && int.TryParse(raw, out value);
    }

    private static decimal ParseDecimal(Dictionary<string, string> args, string key, decimal fallback)
    {
        if (!args.TryGetValue(key, out var raw) || !decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            return fallback;
        return value;
    }

    private static bool TryParseDecimalArg(Dictionary<string, string> args, string key, out decimal value)
    {
        value = 0;
        return args.TryGetValue(key, out var raw) &&
               decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static DateTime? ParseDate(Dictionary<string, string> args, string key)
    {
        if (!args.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;
        if (DateTime.TryParse(raw, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.AssumeLocal, out var date) ||
            DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date))
            return date.Date;
        return null;
    }

    private static TimeOnly? ParseTime(Dictionary<string, string> args, string key)
    {
        if (!args.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;
        return TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ||
               TimeOnly.TryParse(raw, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out value)
            ? value
            : null;
    }

    private static GoalCategory ParseGoalCategory(Dictionary<string, string> args, GoalCategory fallback = GoalCategory.Period)
    {
        if (!args.TryGetValue("category", out var raw)) return fallback;
        return raw.Trim().ToLowerInvariant() switch
        {
            "recurring" or "repeat" or "регулярная" or "повторяющаяся" => GoalCategory.Recurring,
            "period" or "период" => GoalCategory.Period,
            _ => fallback
        };
    }

    private static GoalType ParseGoalType(Dictionary<string, string> args, GoalType fallback = GoalType.Daily)
    {
        if (!args.TryGetValue("type", out var raw)) return fallback;
        return raw.Trim().ToLowerInvariant() switch
        {
            "week" or "weekly" or "неделя" or "недельная" => GoalType.Weekly,
            "month" or "monthly" or "месяц" or "месячная" => GoalType.Monthly,
            _ => GoalType.Daily
        };
    }

    private static RecurrenceKind ParseRecurrenceKind(Dictionary<string, string> args, RecurrenceKind fallback = RecurrenceKind.EveryDay)
    {
        if (!args.TryGetValue("recurrenceKind", out var raw)) return fallback;
        return raw.Trim().ToLowerInvariant() switch
        {
            "everyndays" or "every_n_days" or "interval" => RecurrenceKind.EveryNDays,
            "specificdaysofweek" or "weekdays" or "days_of_week" => RecurrenceKind.SpecificDaysOfWeek,
            "everyday" or "daily" => RecurrenceKind.EveryDay,
            _ => fallback
        };
    }

    private static int ParseWeekdayMask(Dictionary<string, string> args)
    {
        if (args.TryGetValue("recurrenceDays", out var rawMask) && int.TryParse(rawMask, out var mask))
            return mask;
        if (!args.TryGetValue("weekdays", out var raw) || string.IsNullOrWhiteSpace(raw))
            return 0;
        var result = 0;
        foreach (var part in raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result |= part.ToLowerInvariant() switch
            {
                "sun" or "sunday" or "вс" or "воскресенье" => 1 << 0,
                "mon" or "monday" or "пн" or "понедельник" => 1 << 1,
                "tue" or "tuesday" or "вт" or "вторник" => 1 << 2,
                "wed" or "wednesday" or "ср" or "среда" => 1 << 3,
                "thu" or "thursday" or "чт" or "четверг" => 1 << 4,
                "fri" or "friday" or "пт" or "пятница" => 1 << 5,
                "sat" or "saturday" or "сб" or "суббота" => 1 << 6,
                _ => 0
            };
        }
        return result;
    }

    private static DateTime GetWeekStart(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-diff);
    }

    private static string FormatTime(TimeOnly? value)
    {
        return (value ?? new TimeOnly(0, 0)).ToString("HH\\:mm", CultureInfo.InvariantCulture);
    }

    private static string Trim(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }

    private static TransactionType ParseTransactionType(Dictionary<string, string> args)
    {
        if (args.TryGetValue("type", out var t) && t != null)
        {
            var type = t.Trim().ToLowerInvariant();
            if (type is "income" or "доход" or "прибыль" or "зачисление")
                return TransactionType.Income;
        }
        return TransactionType.Expense;
    }

    private static bool IsConfirmed(Dictionary<string, string> args)
    {
        return args.TryGetValue("confirm", out var confirm) &&
               (string.Equals(confirm, "true", StringComparison.OrdinalIgnoreCase) || confirm == "1");
    }

    public void Dispose()
    {
    }
}
