using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using Planner.App.Models;

namespace Planner.App.Services;

public class AssistantOrchestratorService : IDisposable
{
    private const int MaxStoredMessageChars = 8000;
    private const int MaxPromptItemChars = 1800;
    private const int MaxHistoryTurns = 24;
    private const int MaxMemoryFacts = 20;
    private const int MaxReplyChars = 12000;
    private const int MaxAgentSteps = 4;
    private const int MaxCommandsPerAgentStep = 6;
    private const int MaxToolResultChars = 50000;

    private readonly AssistantRepositoryService _repo = new();
    private readonly AssistantLocalSettingsService _settingsService = new();
    private readonly CloudLlmClient _llm = new();
    private readonly ToolCommandRouter _toolRouter = new();
    private readonly AssistantAgentCriticService _critic = new();
    private readonly AssistantTelemetryService _telemetry = new();

    /// <param name="confirmRiskyCommandAsync">Подтверждение финансовых команд (UI). Возвращает true, если пользователь согласен.</param>
    public async Task<(AssistantConversation Conversation, string Reply)> SendUserMessageAsync(
        string text,
        Func<AssistantToolCommand, string, Task<bool>>? confirmRiskyCommandAsync = null,
        CancellationToken ct = default)
    {
        text = TrimForStorage(text);
        var conversation = await _repo.GetOrCreateMainConversationAsync();
        await _repo.AddMessageAsync(conversation.Id, AssistantRole.User, text);
        await _telemetry.TrackAsync("assistant_user_message");

        if (text.Contains("меня зовут", StringComparison.OrdinalIgnoreCase))
            await _repo.UpsertMemoryFactAsync("user.name", text);
        if (text.Contains("моя цель", StringComparison.OrdinalIgnoreCase))
            await _repo.UpsertMemoryFactAsync("user.goal", text);

        var settings = _settingsService.GetEffectiveLlmSettings();
        var recentMessages = await _repo.GetRecentMessagesAsync(conversation.Id, MaxHistoryTurns);

        var memoryFacts = await _repo.GetMemoryFactsAsync(MaxMemoryFacts);
        var snapshot = await BuildContextSnapshotAsync(settings);
        var systemPrompt = BuildSystemPrompt(memoryFacts, snapshot);

        var agentTurns = recentMessages
            .Select(x => new AssistantChatTurn(x.Role, x.Content, x.CreatedAt))
            .ToList();
        var pendingCommands = (IReadOnlyList<AssistantToolCommand>)Array.Empty<AssistantToolCommand>();
        var executedCommandSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allCommandResults = new List<string>();
        var allAgentResultLines = new List<string>();
        var replyText = "";
        var stoppedByStepLimit = false;

        for (var step = 1; step <= MaxAgentSteps; step++)
        {
            if (pendingCommands.Count == 0)
            {
                try
                {
                    if (step > 1)
                    {
                        snapshot = await BuildContextSnapshotAsync(settings);
                        systemPrompt = BuildSystemPrompt(memoryFacts, snapshot);
                    }

                    var llmResponse = await _llm.GenerateAsync(settings, systemPrompt, agentTurns, ct);
                    replyText = llmResponse.ReplyText;
                    pendingCommands = llmResponse.Commands;
                    if (pendingCommands.Count == 0 &&
                        executedCommandSignatures.Count == 0 &&
                        await TryBuildRequiredActionCommandAsync(text, recentMessages) is { } forcedAction)
                    {
                        pendingCommands = new[] { forcedAction };
                        replyText = "Выполняю действие через инструмент.";
                        await _telemetry.TrackAsync("assistant_forced_action", forcedAction.Name);
                    }
                    else if (pendingCommands.Count == 0 &&
                        executedCommandSignatures.Count == 0 &&
                        TryBuildRequiredInspectionCommand(text, recentMessages, out var forcedInspection))
                    {
                        pendingCommands = new[] { forcedInspection };
                        replyText = "Проверяю данные в базе.";
                        await _telemetry.TrackAsync("assistant_forced_inspection", forcedInspection.Name);
                    }
                    await _telemetry.TrackAsync("assistant_llm_ok", $"step={step};commands={pendingCommands.Count}");
                }
                catch (Exception ex)
                {
                    await _telemetry.TrackAsync("assistant_llm_error", ex.Message);
                    replyText = allCommandResults.Count > 0
                        ? "Действия выполнены, но не удалось получить финальный ответ модели. Проверьте ключ/сеть, если нужен развернутый вывод."
                        : "Не удалось обратиться к модели. Проверьте API-ключ в настройках ассистента, переменную OPENAI_API_KEY или сеть.";
                    pendingCommands = Array.Empty<AssistantToolCommand>();
                }
            }

            if (pendingCommands.Count == 0)
                break;

            agentTurns.Add(new AssistantChatTurn(
                AssistantRole.Assistant,
                FormatAssistantStepForAgent(replyText, pendingCommands),
                DateTime.UtcNow));

            var batch = await ExecuteCommandsAsync(
                pendingCommands,
                text,
                confirmRiskyCommandAsync,
                executedCommandSignatures);
            allCommandResults.AddRange(batch.UserLines);
            allAgentResultLines.AddRange(batch.AgentLines);

            agentTurns.Add(new AssistantChatTurn(
                AssistantRole.System,
                BuildToolResultsTurn(step, batch.AgentLines),
                DateTime.UtcNow));

            pendingCommands = Array.Empty<AssistantToolCommand>();
            replyText = "";

            if (step == MaxAgentSteps)
                stoppedByStepLimit = true;
        }

        if (string.IsNullOrWhiteSpace(replyText))
        {
            replyText = allCommandResults.Count > 0
                ? "Готово, выполнил доступные действия."
                : "Не смог определить действие. Напишите, что именно нужно сделать: цель, напоминание, отчет, финансы или заметку.";
        }

        if (stoppedByStepLimit)
            replyText += "\n\nОстановился после нескольких шагов, чтобы не уйти в бесконечный цикл. Если нужно продолжить, напишите следующим сообщением.";

        var criticResult = _critic.Review(text, replyText, allCommandResults);
        if (allCommandResults.Count == 0 && allAgentResultLines.Count > 0)
            criticResult = _critic.Review(text, replyText, allAgentResultLines);
        if (!criticResult.Approved)
        {
            await _telemetry.TrackAsync("assistant_critic_rewrite", criticResult.Reason);
            replyText = criticResult.RevisedReply;
        }

        replyText = TrimForStorage(replyText, MaxReplyChars);
        await _repo.AddMessageAsync(conversation.Id, AssistantRole.Assistant, replyText);
        return (conversation, replyText);
    }

    private static bool IsFinanceRisk(AssistantToolCommand command)
    {
        return AssistantToolCatalog.RequiresConfirmation(command.Name);
    }

    private static string BuildFinanceConfirmationSummary(AssistantToolCommand command)
    {
        var n = command.Name.Trim().ToLowerInvariant();
        if (n == "create_transaction")
        {
            command.Args.TryGetValue("amount", out var amount);
            command.Args.TryGetValue("type", out var type);
            command.Args.TryGetValue("categoryId", out var cat);
            command.Args.TryGetValue("savingsEntryId", out var acc);
            command.Args.TryGetValue("currency", out var cur);
            return
                "Подтвердите финансовую операцию:\n" +
                $"  Сумма: {amount}\n" +
                $"  Тип: {type ?? "expense"}\n" +
                $"  Валюта: {cur ?? "—"}\n" +
                $"  Категория Id: {cat}\n" +
                $"  Счёт сбережений Id: {acc}\n\n" +
                "Выполнить?";
        }

        if (n == "transfer_between_savings")
        {
            command.Args.TryGetValue("amount", out var amount);
            command.Args.TryGetValue("fromSavingsEntryId", out var from);
            command.Args.TryGetValue("toSavingsEntryId", out var to);
            return
                "Подтвердите перевод между счетами:\n" +
                $"  Со счёта Id: {from}\n" +
                $"  На счёт Id: {to}\n" +
                $"  Сумма: {amount}\n\n" +
                "Выполнить?";
        }

        var args = command.Args.Count == 0
            ? "  без аргументов"
            : string.Join("\n", command.Args.Select(x => $"  {x.Key}: {x.Value}"));
        return
            "Подтвердите финансовое действие:\n" +
            $"  Команда: {command.Name}\n" +
            args + "\n\n" +
            "Выполнить?";
    }

    private async Task<string> BuildContextSnapshotAsync(AssistantLlmSettings settings)
    {
        using var planner = new PlannerService();
        var sb = new StringBuilder();
        var today = DateTime.Today;
        var weekStart = GetWeekStart(today);
        var weekEnd = weekStart.AddDays(6);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var previousMonthStart = monthStart.AddMonths(-1);
        var previousMonthEnd = monthStart.AddDays(-1);

        sb.AppendLine($"CurrentLocalDate: {today:yyyy-MM-dd}");
        sb.AppendLine($"CurrentWeek: {weekStart:yyyy-MM-dd}..{weekEnd:yyyy-MM-dd}");
        sb.AppendLine($"CurrentMonth: {monthStart:yyyy-MM-dd}..{monthEnd:yyyy-MM-dd}");
        sb.AppendLine($"PreviousMonth: {previousMonthStart:yyyy-MM-dd}..{previousMonthEnd:yyyy-MM-dd}");

        if (settings.AllowGoalsData)
        {
            await AppendGoalsContextAsync(sb, planner, today, weekStart, weekEnd, monthStart, monthEnd);
        }
        if (settings.AllowRemindersData)
        {
            var reminders = await planner.GetAllRemindersAsync();
            sb.AppendLine();
            sb.AppendLine($"RemindersAll: count={reminders.Count}");
            foreach (var r in reminders)
            {
                var (completed, total) = await planner.GetReminderMonthlyProgressAsync(r.Id, today.Year, today.Month);
                sb.AppendLine($"- id={r.Id}; enabled={r.IsEnabled}; title=\"{TrimForPrompt(r.Title)}\"; intervalMinutes={r.IntervalMinutes}; active={FormatTimeWindow(r.ActiveFrom, r.ActiveTo)}; monthProgress={completed}/{total}; created={r.CreatedAt:yyyy-MM-dd}");
            }
        }
        if (settings.AllowFinanceData)
        {
            sb.AppendLine();
            var categories = await planner.GetFinanceCategoriesAsync();
            sb.AppendLine($"FinanceCategoriesAll: count={categories.Count}");
            foreach (var c in categories)
                sb.AppendLine($"- id={c.Id}; type={c.Type}; name=\"{TrimForPrompt(c.Name)}\"");

            var yearStart = new DateTime(today.Year, 1, 1);
            var tx = await planner.GetTransactionsAsync(yearStart, today.AddDays(1));
            sb.AppendLine($"TransactionsCurrentYearThroughToday: count={tx.Count}");
            foreach (var t in tx)
                sb.AppendLine($"- id={t.Id}; date={t.Date:yyyy-MM-dd}; type={t.Category.Type}; categoryId={t.CategoryId}; category=\"{TrimForPrompt(t.Category.Name)}\"; amount={t.Amount:N2}; currency={t.Currency}; note=\"{TrimForPrompt(t.Note, 240)}\"");

            var savingsCategories = await planner.GetSavingsCategoriesAsync();
            sb.AppendLine($"SavingsCategoriesAll: count={savingsCategories.Count}");
            foreach (var c in savingsCategories)
                sb.AppendLine($"- id={c.Id}; name=\"{TrimForPrompt(c.Name)}\"");

            var savings = await planner.GetSavingsEntriesAsync();
            sb.AppendLine($"SavingsAccountsAll: count={savings.Count}");
            foreach (var s in savings)
                sb.AppendLine($"- id={s.Id}; categoryId={s.SavingsCategoryId}; category=\"{TrimForPrompt(s.SavingsCategory.Name)}\"; name=\"{TrimForPrompt(s.Name)}\"; balance={s.Balance:N2}; currency={s.Currency}");
        }

        var reports = await _repo.GetRecentReportsAsync(5);
        sb.AppendLine();
        sb.AppendLine($"ReportsRecent: count={reports.Count}");
        foreach (var report in reports)
            sb.AppendLine($"- id={report.Id}; kind={report.Kind}; periodStart={report.PeriodStart:yyyy-MM-dd}; body=\"{TrimForPrompt(report.Body, 500)}\"");

        return sb.ToString().Trim();
    }

    private static async Task AppendGoalsContextAsync(
        StringBuilder sb,
        PlannerService planner,
        DateTime today,
        DateTime weekStart,
        DateTime weekEnd,
        DateTime monthStart,
        DateTime monthEnd)
    {
        var periodGoals = await planner.GetPeriodGoalsAsync();
        var recurringGoals = await planner.GetRecurringGoalsAsync();

        var dayNote = await planner.GetPeriodNoteTextAsync(NotePeriodKind.Day, today) ?? "";
        var weekNote = await planner.GetPeriodNoteTextAsync(NotePeriodKind.Week, weekStart) ?? "";
        var monthNote = await planner.GetPeriodNoteTextAsync(NotePeriodKind.Month, monthStart) ?? "";

        sb.AppendLine();
        sb.AppendLine($"GoalPeriodNotes: day=\"{TrimForPrompt(dayNote)}\"; week=\"{TrimForPrompt(weekNote)}\"; month=\"{TrimForPrompt(monthNote)}\"");

        var todayPeriodGoals = periodGoals
            .Where(g => MatchesDailyPeriod(g, today))
            .OrderBy(g => g.CreatedAt)
            .ToList();
        var todayRecurringGoals = recurringGoals
            .Where(g => PlannerService.IsRecurringGoalDueOn(g, today))
            .OrderBy(g => g.CreatedAt)
            .ToList();

        sb.AppendLine($"GoalsDueToday: count={todayPeriodGoals.Count + todayRecurringGoals.Count}");
        foreach (var g in todayPeriodGoals)
        {
            var current = await planner.GetGoalCompletionCountAsync(g.Id, today, today);
            sb.AppendLine(FormatPeriodGoal(g, "day", today, today, current));
        }
        foreach (var g in todayRecurringGoals)
        {
            var completed = await planner.IsGoalCompletedForDateAsync(g.Id, today);
            sb.AppendLine(FormatRecurringGoal(g, dueToday: true, completedToday: completed));
        }

        var weekGoals = periodGoals
            .Where(g => MatchesWeeklyPeriod(g, weekStart))
            .OrderBy(g => g.CreatedAt)
            .ToList();
        sb.AppendLine($"GoalsThisWeek: count={weekGoals.Count}");
        foreach (var g in weekGoals)
        {
            var current = await planner.GetGoalCompletionCountAsync(g.Id, weekStart, weekEnd);
            sb.AppendLine(FormatPeriodGoal(g, "week", weekStart, weekEnd, current));
        }

        var monthGoals = periodGoals
            .Where(g => MatchesMonthlyPeriod(g, monthStart))
            .OrderBy(g => g.CreatedAt)
            .ToList();
        sb.AppendLine($"GoalsThisMonth: count={monthGoals.Count}");
        foreach (var g in monthGoals)
        {
            var current = await planner.GetGoalCompletionCountAsync(g.Id, monthStart, monthEnd);
            sb.AppendLine(FormatPeriodGoal(g, "month", monthStart, monthEnd, current));
        }

        sb.AppendLine($"AllPeriodGoalsActive: count={periodGoals.Count}");
        foreach (var g in periodGoals.OrderBy(g => g.Type).ThenBy(g => PeriodAnchor(g)).ThenBy(g => g.CreatedAt))
        {
            var start = PeriodAnchor(g);
            var end = g.Type switch
            {
                GoalType.Weekly => GetWeekStart(start).AddDays(6),
                GoalType.Monthly => new DateTime(start.Year, start.Month, 1).AddMonths(1).AddDays(-1),
                _ => start
            };
            sb.AppendLine(FormatPeriodGoal(g, GoalTypeText(g.Type), start, end, current: null));
        }

        sb.AppendLine($"AllRecurringGoalsActive: count={recurringGoals.Count}");
        foreach (var g in recurringGoals.OrderBy(g => g.CreatedAt))
        {
            var dueToday = PlannerService.IsRecurringGoalDueOn(g, today);
            var completedToday = dueToday && await planner.IsGoalCompletedForDateAsync(g.Id, today);
            sb.AppendLine(FormatRecurringGoal(g, dueToday, completedToday));
        }
    }

    private static string BuildSystemPrompt(List<AssistantMemoryFact> memoryFacts, string contextSnapshot)
    {
        var memoryLines = memoryFacts.Count == 0
            ? "No memory facts yet."
            : string.Join("\n", memoryFacts.Select(m => $"- {TrimForPrompt(m.Key, 120)}: {TrimForPrompt(m.Value)}"));
        return
            "You are a personal life-planner copilot inside a desktop app.\n" +
            "Answer in Russian, concise and practical.\n" +
            "Use Current app context as the source of truth.\n" +
            "Work as a supervised production agent: planner chooses the next tool call, executor runs tools, critic checks facts before the final reply.\n" +
            "Every user-facing answer must come from this agent loop. There are no local shortcut answers in normal mode.\n" +
            "If the user asks for goals for today/day, answer from GoalsDueToday only.\n" +
            "If the user asks for weekly goals, use GoalsThisWeek; for monthly goals, use GoalsThisMonth.\n" +
            "Do not mix all goals into day/week/month answers unless the user asks for all goals.\n" +
            "You are running in a multi-step agent loop. The app may execute your commands, then send TOOL RESULTS back to you.\n" +
            "After TOOL RESULTS, inspect what actually happened and either return the next needed commands or a final Russian answer with no commands.\n" +
            $"Use at most {MaxCommandsPerAgentStep} commands per step and prefer the smallest reliable sequence.\n" +
            "Do not repeat commands that TOOL RESULTS already say were executed successfully.\n" +
            "If ids or required details are missing, ask a short clarifying question instead of inventing ids.\n" +
            "For any question about app data, database state, previous periods, goals, reminders, finances, reports, notes, categories, accounts, or transactions, use an inspect_* or report tool before the final answer.\n" +
            "For exchange-rate questions or finance calculations that require conversion, use inspect_exchange_rates/get_exchange_rates before the final answer.\n" +
            "Do not answer database/status questions from chat history alone; chat history is only conversation context, not the source of truth.\n" +
            "Prefer complete database-backed answers. If the compact context is not enough, call the relevant inspect_* tool and rely on TOOL RESULTS.\n" +
            "When the user asks you to change app data, return JSON commands so the app can execute them.\n" +
            "Never say that you created, updated, deleted, completed, saved, moved, or generated something unless you also return the matching command.\n" +
            "Never promise a future action either. If you write any of: 'добавлю', 'создам', 'сохраню', 'обновлю', 'удалю', 'отмечу', 'сформирую', 'выполняю', 'делаю', 'сейчас', 'подождите', 'минутку', 'I will', 'I'll', 'let me', 'one moment' — you MUST also include the matching tool call in commands[] in the SAME response. Otherwise ask a short clarifying question, or just answer in past tense after the action is actually done.\n" +
            "If you already have enough information to act (title, kind, recurrence, period, ids), DO NOT ask 'wait a moment' — emit the command immediately in commands[] and write a short confirmation in 'reply'.\n" +
            "Never show raw command JSON to the user as the final answer. Put tool calls only into commands[].\n" +
            "Always return either plain Russian text for ordinary conversation, or this exact JSON envelope for agent actions:\n" +
            "{\n" +
            "  \"reply\":\"human answer\",\n" +
            "  \"commands\":[{\"name\":\"create_goal\",\"args\":{\"title\":\"...\",\"targetCount\":\"1\"}}]\n" +
            "}\n" +
            "Use exact ids from context for update/delete/complete commands.\n" +
            "For report requests always call generate_report instead of answering from memory. Set kind=day/week/month and date or year/month.\n" +
            "For graphical/visual/chart report requests call open_graphical_report. Set kind=day/week/month and domain=finance/goals/reminders/general.\n" +
            "For financial reports use domain=finance. For goal reports use domain=goals. For reminder reports use domain=reminders. Otherwise use domain=general.\n" +
            "For financial reports and graphical finance reports, set targetCurrency=UAH/SEK/USD when the user asks for a report currency; if not specified, the tool defaults to UAH and aggregates categories after conversion.\n" +
            "If the user says a month name like April/апрель without a year, resolve it as the most recent matching month not after CurrentLocalDate.\n" +
            "Never add or subtract money across different currencies yourself; use finance report tools or exchange-rate tools for conversion-backed totals.\n" +
            "Only include commands when user explicitly asks to execute something.\n" +
            "For create_transaction use real categoryId and savingsEntryId from context (Savings: id=...).\n" +
            "Do not set confirm in args; the user confirms in the app UI.\n\n" +
            AssistantToolCatalog.RenderForPrompt() + "\n\n" +
            "Memory facts:\n" +
            memoryLines + "\n\n" +
            "Current app context:\n" +
            TrimForPrompt(contextSnapshot, 80000);
    }

    private static string FormatPeriodGoal(Goal goal, string scope, DateTime periodStart, DateTime periodEnd, int? current)
    {
        var progress = current.HasValue ? $"; progress={current.Value}/{Math.Max(1, goal.TargetCount)}" : "";
        return
            $"- id={goal.Id}; scope={scope}; title=\"{TrimForPrompt(goal.Title)}\"; description=\"{TrimForPrompt(goal.Description, 300)}\"; target={Math.Max(1, goal.TargetCount)}{progress}; period={periodStart:yyyy-MM-dd}..{periodEnd:yyyy-MM-dd}; created={goal.CreatedAt:yyyy-MM-dd}";
    }

    private static string FormatRecurringGoal(Goal goal, bool dueToday, bool completedToday)
    {
        return
            $"- id={goal.Id}; scope=recurring; title=\"{TrimForPrompt(goal.Title)}\"; description=\"{TrimForPrompt(goal.Description, 300)}\"; recurrence=\"{RecurrenceText(goal)}\"; target=1; dueToday={dueToday}; completedToday={completedToday}; start={PeriodAnchor(goal):yyyy-MM-dd}; created={goal.CreatedAt:yyyy-MM-dd}";
    }

    private static string FormatTimeWindow(TimeOnly? from, TimeOnly? to)
    {
        return $"{(from ?? new TimeOnly(0, 0)):HH\\:mm}..{(to ?? new TimeOnly(23, 59)):HH\\:mm}";
    }

    private static string GoalTypeText(GoalType type)
    {
        return type switch
        {
            GoalType.Weekly => "week",
            GoalType.Monthly => "month",
            _ => "day"
        };
    }

    private static string RecurrenceText(Goal goal)
    {
        return goal.RecurrenceKind switch
        {
            RecurrenceKind.EveryDay => "every day",
            RecurrenceKind.EveryNDays => $"every {Math.Max(1, goal.IntervalDays)} days",
            RecurrenceKind.SpecificDaysOfWeek => "weekdays: " + DaysOfWeekText(goal.RecurrenceDays),
            _ => "unknown"
        };
    }

    private static string DaysOfWeekText(int mask)
    {
        if (mask == 0) return "none";
        var names = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        var selected = new List<string>();
        for (var i = 0; i < names.Length; i++)
        {
            if ((mask & (1 << i)) != 0)
                selected.Add(names[i]);
        }
        return string.Join(",", selected);
    }

    private static DateTime GetWeekStart(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-diff);
    }

    private static DateTime PeriodAnchor(Goal goal)
    {
        return (goal.StartDate ?? goal.CreatedAt).Date;
    }

    private static bool MatchesDailyPeriod(Goal goal, DateTime day)
    {
        return goal.Category == GoalCategory.Period &&
               goal.Type == GoalType.Daily &&
               PeriodAnchor(goal) == day.Date;
    }

    private static bool MatchesWeeklyPeriod(Goal goal, DateTime weekStart)
    {
        return goal.Category == GoalCategory.Period &&
               goal.Type == GoalType.Weekly &&
               GetWeekStart(PeriodAnchor(goal)) == weekStart.Date;
    }

    private static bool MatchesMonthlyPeriod(Goal goal, DateTime monthStart)
    {
        var anchor = PeriodAnchor(goal);
        return goal.Category == GoalCategory.Period &&
               goal.Type == GoalType.Monthly &&
               anchor.Year == monthStart.Year &&
               anchor.Month == monthStart.Month;
    }

    private static List<AssistantToolCommand> ParseInlineCommands(string userText)
    {
        var list = new List<AssistantToolCommand>();
        if (string.IsNullOrWhiteSpace(userText)) return list;
        var text = userText.Trim();
        if (text.StartsWith("создай цель:", StringComparison.OrdinalIgnoreCase))
        {
            var title = text["создай цель:".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(title))
            {
                list.Add(new AssistantToolCommand
                {
                    Name = "create_goal",
                    Args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["title"] = title,
                        ["targetCount"] = "1"
                    }
                });
            }
        }
        return list;
    }

    private async Task<CommandExecutionBatch> ExecuteCommandsAsync(
        IReadOnlyList<AssistantToolCommand> commands,
        string originalUserText,
        Func<AssistantToolCommand, string, Task<bool>>? confirmRiskyCommandAsync,
        HashSet<string> executedCommandSignatures)
    {
        var userLines = new List<string>();
        var agentLines = new List<string>();
        foreach (var command in commands.Take(MaxCommandsPerAgentStep))
        {
            var validation = AssistantToolCatalog.Validate(command);
            if (!validation.Success)
            {
                var validationTask = await _repo.CreateTaskAsync(command?.Name ?? "invalid_command", originalUserText);
                await _repo.CompleteTaskAsync(validationTask.Id, false, validation.Message);
                userLines.Add($"{command?.Name ?? "command"}: ошибка валидации — {validation.Message}");
                agentLines.Add($"{command?.Name ?? "command"}: validation_failed; {validation.Message}");
                continue;
            }

            var signature = CommandSignature(command);
            if (!executedCommandSignatures.Add(signature))
            {
                var duplicateLine = $"{command.Name}: skipped duplicate command.";
                userLines.Add($"{command.Name}: пропущен повтор команды.");
                agentLines.Add(duplicateLine);
                continue;
            }

            var task = await _repo.CreateTaskAsync(command.Name, originalUserText);
            AssistantToolExecutionContext? ctx = null;
            if (IsFinanceRisk(command))
            {
                var summary = BuildFinanceConfirmationSummary(command);
                var ok = confirmRiskyCommandAsync != null && await confirmRiskyCommandAsync(command, summary);
                if (!ok)
                {
                    const string cancelled = "Отменено пользователем.";
                    await _repo.CompleteTaskAsync(task.Id, false, cancelled);
                    userLines.Add($"{command.Name}: отменено пользователем.");
                    agentLines.Add($"{command.Name}: failed; {cancelled}");
                    continue;
                }

                ctx = new AssistantToolExecutionContext { UserConfirmedFinance = true };
            }

            try
            {
                var result = await _toolRouter.ExecuteAsync(command, ctx);
                await _repo.CompleteTaskAsync(task.Id, result.Success, result.Message);
                if (!IsReadOnlyTool(command.Name) || !result.Success)
                {
                    var userMessage = IsReadOnlyTool(command.Name)
                        ? ReadOnlyToolUserMessage(command.Name, result.Success)
                        : TrimForPrompt(result.Message, 700);
                    userLines.Add($"{command.Name}: {(result.Success ? "выполнено" : "ошибка")} — {userMessage}");
                }
                agentLines.Add($"{command.Name}: {(result.Success ? "success" : "failed")}; {TrimForPrompt(result.Message, 30000)}");
            }
            catch (Exception ex)
            {
                await _repo.CompleteTaskAsync(task.Id, false, ex.Message);
                userLines.Add($"{command.Name}: ошибка — {TrimForPrompt(ex.Message, 700)}");
                agentLines.Add($"{command.Name}: failed; exception={TrimForPrompt(ex.Message, 1400)}");
            }
        }

        if (commands.Count > MaxCommandsPerAgentStep)
        {
            var skipped = commands.Count - MaxCommandsPerAgentStep;
            userLines.Add($"Пропущено команд сверх лимита: {skipped}.");
            agentLines.Add($"Skipped {skipped} commands because per-step command limit is {MaxCommandsPerAgentStep}.");
        }

        var batch = new CommandExecutionBatch(userLines, agentLines);
        AssistantDiagnosticsService.LogMemory("assistant-agent-tools", $"commands={commands.Count};results={agentLines.Count}");
        return batch;
    }

    private static string FormatAssistantStepForAgent(string replyText, IReadOnlyList<AssistantToolCommand> commands)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(replyText))
            sb.AppendLine(TrimForPrompt(replyText, 1200));
        sb.AppendLine("Requested commands:");
        foreach (var command in commands.Take(MaxCommandsPerAgentStep))
            sb.AppendLine("- " + CommandDebugText(command));
        return sb.ToString().Trim();
    }

    private static string BuildToolResultsTurn(int step, IReadOnlyList<string> resultLines)
    {
        var body = string.Join("\n", resultLines.Select(x => "- " + x));
        return TrimForPrompt(
            $"TOOL RESULTS step {step}:\n{body}\n\nDecide the next step. If the user's request is fully handled, return final JSON with reply and an empty commands array.",
            MaxToolResultChars);
    }

    private static string CommandSignature(AssistantToolCommand command)
    {
        var args = string.Join(";", command.Args
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Key}={x.Value}"));
        return $"{command.Name.Trim().ToLowerInvariant()}|{args}";
    }

    private static string CommandDebugText(AssistantToolCommand command)
    {
        var args = command.Args.Count == 0
            ? ""
            : " " + string.Join(", ", command.Args
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => $"{x.Key}={x.Value}"));
        return $"{command.Name}{args}";
    }

    private static bool IsReadOnlyTool(string name)
    {
        var n = (name ?? "").Trim().ToLowerInvariant();
        return n is "generate_report" or "inspect_goals" or "inspect_reminders" or "inspect_finances" or "inspect_reports";
    }

    private static string ReadOnlyToolUserMessage(string name, bool success)
    {
        if (!success)
            return "не удалось получить данные";

        return (name ?? "").Trim().ToLowerInvariant() switch
        {
            "generate_report" => "отчет сформирован и сохранен",
            "inspect_goals" => "данные по целям получены",
            "inspect_reminders" => "данные по напоминаниям получены",
            "inspect_finances" => "финансовые данные получены",
            "inspect_reports" => "данные по отчетам получены",
            _ => "данные получены"
        };
    }

    private static bool TryBuildRequiredInspectionCommand(string userText, IReadOnlyList<AssistantMessage> recentMessages, out AssistantToolCommand command)
    {
        command = new AssistantToolCommand();
        if (string.IsNullOrWhiteSpace(userText))
            return false;

        var normalized = NormalizeForMatch(userText);
        if (LooksLikeExchangeRateQuestion(normalized))
        {
            command = new AssistantToolCommand
            {
                Name = "inspect_exchange_rates",
                Args = BuildExchangeRateArgs(userText, normalized)
            };
            return true;
        }

        if (TryParseReportRequest(userText, out var reportKind, out var reportPeriodStart, out var reportDomain))
        {
            var args = BuildPeriodArgs(reportKind, reportPeriodStart, reportDomain);
            AddTargetCurrencyArg(args, normalized);
            command = new AssistantToolCommand
            {
                Name = "generate_report",
                Args = args
            };
            return true;
        }

        if (!LooksLikeDatabaseQuestion(normalized))
            return false;

        var hasExplicitPeriod = HasExplicitPeriod(normalized);
        var kind = ResolveInspectionKind(normalized, out var periodStart);
        if (MentionsFinance(normalized))
        {
            if (!hasExplicitPeriod &&
                normalized.Contains("марж", StringComparison.Ordinal) &&
                TryResolveRecentFinanceReportPeriod(recentMessages, out var recentKind, out var recentPeriodStart))
            {
                kind = recentKind;
                periodStart = recentPeriodStart;
            }

            var args = BuildPeriodArgs(kind, periodStart);
            AddTargetCurrencyArg(args, normalized);
            command = new AssistantToolCommand { Name = "inspect_finances", Args = args };
            return true;
        }

        if (MentionsReminder(normalized))
        {
            command = new AssistantToolCommand { Name = "inspect_reminders", Args = BuildPeriodArgs(kind == AssistantReportPeriodKind.Day ? AssistantReportPeriodKind.Month : kind, periodStart) };
            command.Args["includeDisabled"] = "true";
            return true;
        }

        if (MentionsGoal(normalized))
        {
            command = new AssistantToolCommand { Name = "inspect_goals", Args = BuildPeriodArgs(kind, periodStart) };
            command.Args["includeArchived"] = "true";
            return true;
        }

        if (normalized.Contains("отчет", StringComparison.Ordinal) || normalized.Contains("отчёт", StringComparison.Ordinal))
        {
            command = new AssistantToolCommand
            {
                Name = "inspect_reports",
                Args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["limit"] = "10" }
            };
            return true;
        }

        return false;
    }

    private static async Task<AssistantToolCommand?> TryBuildRequiredActionCommandAsync(string userText, IReadOnlyList<AssistantMessage> recentMessages)
    {
        var normalized = NormalizeForMatch(userText);
        if (LooksLikeGraphicalReportIntent(normalized))
            return BuildGraphicalReportCommand(userText, normalized);

        if (TryBuildCreateGoalCommand(userText, normalized, recentMessages, out var goalCommand))
            return goalCommand;

        if (TryBuildCreateReminderCommand(userText, normalized, recentMessages, out var reminderCommand))
            return reminderCommand;

        if (TryBuildSavePeriodNoteCommand(userText, normalized, out var noteCommand))
            return noteCommand;

        if (await TryBuildDeleteGoalCommandAsync(normalized) is { } deleteGoalCommand)
            return deleteGoalCommand;

        if (!LooksLikeExpenseTransactionIntent(normalized))
            return null;

        if (!TryParseMoneyAmount(userText, out var amount))
            return null;

        using var planner = new PlannerService();
        var categories = await planner.GetFinanceCategoriesAsync(TransactionType.Expense);
        var savings = await planner.GetSavingsEntriesAsync();
        var category = FindBestFinanceCategory(normalized, categories);
        var account = FindBestSavingsAccount(normalized, savings);
        if (category == null || account == null)
            return null;

        return new AssistantToolCommand
        {
            Name = "create_transaction",
            Args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["amount"] = amount.ToString("0.##", CultureInfo.InvariantCulture),
                ["type"] = "expense",
                ["categoryId"] = category.Id.ToString(CultureInfo.InvariantCulture),
                ["savingsEntryId"] = account.Id.ToString(CultureInfo.InvariantCulture),
                ["currency"] = DetectCurrency(userText, account.Currency),
                ["date"] = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["note"] = category.Name
            }
        };
    }

    private static bool TryBuildCreateGoalCommand(
        string userText,
        string normalized,
        IReadOnlyList<AssistantMessage> recentMessages,
        out AssistantToolCommand command)
    {
        command = new AssistantToolCommand();
        var (intentText, intentNormalized) = ResolveCreateGoalIntent(userText, normalized, recentMessages);
        if (intentText == null)
            return false;

        var title = ExtractCreateGoalTitle(intentText, recentMessages);
        if (string.IsNullOrWhiteSpace(title))
            return false;

        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = title
        };

        var description = ExtractCreateGoalDescription(userText, normalized, recentMessages, title);
        if (!string.IsNullOrWhiteSpace(description))
            args["description"] = description;

        if (LooksLikeRecurringGoalIntent(intentNormalized, normalized))
        {
            args["category"] = "recurring";
            if (TryParseEveryNDays(intentNormalized + " " + normalized, out var n))
            {
                args["recurrenceKind"] = "every_n_days";
                args["intervalDays"] = n.ToString(CultureInfo.InvariantCulture);
            }
            else if (TryParseWeekdayList(intentNormalized + " " + normalized, out var weekdays))
            {
                args["recurrenceKind"] = "weekdays";
                args["weekdays"] = weekdays;
            }
            else
            {
                args["recurrenceKind"] = "everyday";
            }
        }
        else
        {
            args["category"] = "period";
            args["type"] = LooksLikeMonthlyPeriodGoal(intentNormalized + " " + normalized)
                ? "month"
                : LooksLikeWeeklyPeriodGoal(intentNormalized + " " + normalized)
                    ? "week"
                    : "day";
        }

        if (TryParseTargetCount(intentNormalized + " " + normalized, out var target))
            args["targetCount"] = target.ToString(CultureInfo.InvariantCulture);
        else
            args["targetCount"] = "1";

        command = new AssistantToolCommand { Name = "create_goal", Args = args };
        return true;
    }

    private static (string? IntentText, string IntentNormalized) ResolveCreateGoalIntent(
        string userText,
        string normalized,
        IReadOnlyList<AssistantMessage> recentMessages)
    {
        if (LooksLikeCreateGoalIntent(normalized))
            return (userText, normalized);

        if (!LooksLikeGoalClarification(normalized))
            return (null, "");

        var earlier = recentMessages
            .Where(m => m.Role == AssistantRole.User && !string.Equals(m.Content?.Trim(), userText.Trim(), StringComparison.Ordinal))
            .Reverse()
            .Take(4)
            .FirstOrDefault(m => LooksLikeCreateGoalIntent(NormalizeForMatch(m.Content)));
        if (earlier == null)
            return (null, "");

        return (earlier.Content, NormalizeForMatch(earlier.Content));
    }

    private static bool LooksLikeCreateGoalIntent(string normalized)
    {
        var hasVerb =
            normalized.Contains(" создай ", StringComparison.Ordinal) ||
            normalized.Contains(" создать ", StringComparison.Ordinal) ||
            normalized.Contains(" добавь ", StringComparison.Ordinal) ||
            normalized.Contains(" добавить ", StringComparison.Ordinal) ||
            normalized.Contains(" заведи ", StringComparison.Ordinal) ||
            normalized.Contains(" поставь ", StringComparison.Ordinal) ||
            normalized.Contains(" запланируй ", StringComparison.Ordinal);
        var padded = $" {normalized} ";
        var hasNoun =
            padded.Contains(" цел", StringComparison.Ordinal) ||
            padded.Contains(" задач", StringComparison.Ordinal) ||
            padded.Contains(" привычк", StringComparison.Ordinal);
        return hasVerb && hasNoun;
    }

    private static bool LooksLikeGoalClarification(string normalized)
    {
        return normalized.Contains("каждый день", StringComparison.Ordinal) ||
               normalized.Contains("каждую неделю", StringComparison.Ordinal) ||
               normalized.Contains("каждый месяц", StringComparison.Ordinal) ||
               normalized.Contains("каждые ", StringComparison.Ordinal) ||
               normalized.Contains(" описание ", StringComparison.Ordinal) ||
               normalized.Contains("такое же как", StringComparison.Ordinal);
    }

    private static string ExtractCreateGoalTitle(string intentText, IReadOnlyList<AssistantMessage> recentMessages)
    {
        var fromIntent = ExtractQuotedOrNamedTitle(intentText);
        if (!string.IsNullOrWhiteSpace(fromIntent))
            return fromIntent;

        foreach (var message in recentMessages.Reverse().Take(6))
        {
            if (message.Role != AssistantRole.User) continue;
            var candidate = ExtractQuotedOrNamedTitle(message.Content);
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;
        }
        return "";
    }

    private static string ExtractQuotedOrNamedTitle(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var quoted = Regex.Match(text, "[«\"']([^«»\"']+)[»\"']");
        if (quoted.Success)
            return quoted.Groups[1].Value.Trim();

        var afterName = Regex.Match(text, @"(?:названием|называется|называть|именем|задач[ауие]|цел[ьюие])\s+(?:с\s+названием\s+)?(?:под\s+названием\s+)?([\p{L}\d\-]+(?:\s+[\p{L}\d\-]+){0,5})", RegexOptions.IgnoreCase);
        if (afterName.Success)
            return afterName.Groups[1].Value.Trim();

        return "";
    }

    private static string ExtractCreateGoalDescription(
        string userText,
        string normalized,
        IReadOnlyList<AssistantMessage> recentMessages,
        string title)
    {
        if (normalized.Contains("такое же как", StringComparison.Ordinal) ||
            normalized.Contains("такое же", StringComparison.Ordinal))
            return title;

        var match = Regex.Match(userText, @"описание[:\s]+(?:такое же как и название,?\s*)?(.+?)(?:\.|$)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var raw = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(raw))
                return raw;
        }

        foreach (var message in recentMessages.Reverse().Where(m => m.Role == AssistantRole.User).Take(4))
        {
            var m = Regex.Match(message.Content ?? "", @"описание[:\s]+([^.]+)", RegexOptions.IgnoreCase);
            if (m.Success)
                return m.Groups[1].Value.Trim();
        }
        return "";
    }

    private static bool LooksLikeRecurringGoalIntent(string intentNormalized, string normalized)
    {
        var combined = intentNormalized + " " + normalized;
        return combined.Contains("повтор", StringComparison.Ordinal) ||
               combined.Contains("регуляр", StringComparison.Ordinal) ||
               combined.Contains("привычк", StringComparison.Ordinal) ||
               combined.Contains("каждый день", StringComparison.Ordinal) ||
               combined.Contains("каждые ", StringComparison.Ordinal) ||
               combined.Contains("ежедневн", StringComparison.Ordinal) ||
               combined.Contains("по будн", StringComparison.Ordinal) ||
               combined.Contains("по выходн", StringComparison.Ordinal);
    }

    private static bool LooksLikeMonthlyPeriodGoal(string combined)
    {
        return combined.Contains(" этот месяц", StringComparison.Ordinal) ||
               combined.Contains(" на месяц", StringComparison.Ordinal) ||
               combined.Contains(" в этом месяц", StringComparison.Ordinal);
    }

    private static bool LooksLikeWeeklyPeriodGoal(string combined)
    {
        return combined.Contains(" эту неделю", StringComparison.Ordinal) ||
               combined.Contains(" на неделю", StringComparison.Ordinal) ||
               combined.Contains(" в этой недел", StringComparison.Ordinal);
    }

    private static bool TryParseEveryNDays(string normalized, out int n)
    {
        n = 0;
        var match = Regex.Match(normalized, @"каждые\s+(\d{1,3})\s*д");
        if (!match.Success)
            return false;
        return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n is > 0 and <= 365;
    }

    private static bool TryParseWeekdayList(string normalized, out string weekdays)
    {
        weekdays = "";
        var map = new (string Word, string Code)[]
        {
            ("понедельник", "mon"), ("вторник", "tue"), ("среду", "wed"), ("среда", "wed"),
            ("четверг", "thu"), ("пятницу", "fri"), ("пятница", "fri"),
            ("субботу", "sat"), ("суббота", "sat"), ("воскресенье", "sun")
        };
        var found = new List<string>();
        foreach (var (word, code) in map)
        {
            if (normalized.Contains(word, StringComparison.Ordinal) && !found.Contains(code))
                found.Add(code);
        }
        if (found.Count == 0)
            return false;
        weekdays = string.Join(",", found);
        return true;
    }

    private static bool TryParseTargetCount(string normalized, out int count)
    {
        count = 0;
        var match = Regex.Match(normalized, @"(?:цель|план|сделать|раз)\s*[:=]?\s*(\d{1,3})|целевое\s+количество[:\s]+(\d{1,3})|(\d{1,3})\s+раз");
        for (var i = 1; i < match.Groups.Count; i++)
        {
            if (match.Groups[i].Success && int.TryParse(match.Groups[i].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) && count > 0)
                return true;
        }
        return false;
    }

    private static bool TryBuildCreateReminderCommand(
        string userText,
        string normalized,
        IReadOnlyList<AssistantMessage> recentMessages,
        out AssistantToolCommand command)
    {
        command = new AssistantToolCommand();
        if (!LooksLikeCreateReminderIntent(normalized))
            return false;

        var title = ExtractQuotedOrNamedTitle(userText);
        if (string.IsNullOrWhiteSpace(title))
        {
            foreach (var msg in recentMessages.Reverse().Where(m => m.Role == AssistantRole.User).Take(4))
            {
                title = ExtractQuotedOrNamedTitle(msg.Content);
                if (!string.IsNullOrWhiteSpace(title))
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(title))
            return false;

        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["title"] = title };
        if (TryParseIntervalMinutes(normalized, out var interval))
            args["intervalMinutes"] = interval.ToString(CultureInfo.InvariantCulture);
        if (TryParseTimeRange(userText, out var from, out var to))
        {
            args["activeFrom"] = from;
            args["activeTo"] = to;
        }
        command = new AssistantToolCommand { Name = "create_reminder", Args = args };
        return true;
    }

    private static bool LooksLikeCreateReminderIntent(string normalized)
    {
        var hasVerb =
            normalized.Contains(" создай ", StringComparison.Ordinal) ||
            normalized.Contains(" добавь ", StringComparison.Ordinal) ||
            normalized.Contains(" заведи ", StringComparison.Ordinal) ||
            normalized.Contains(" поставь ", StringComparison.Ordinal);
        return hasVerb && normalized.Contains("напомин", StringComparison.Ordinal);
    }

    private static bool TryParseIntervalMinutes(string normalized, out int minutes)
    {
        minutes = 0;
        var match = Regex.Match(normalized, @"кажд(?:ые|ый|ую)?\s+(\d{1,3})\s*(минут|мин|час|ч)");
        if (!match.Success)
            return false;
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return false;
        var unit = match.Groups[2].Value;
        minutes = unit.StartsWith("час", StringComparison.Ordinal) || unit == "ч" ? value * 60 : value;
        return minutes > 0 && minutes <= 10080;
    }

    private static bool TryParseTimeRange(string text, out string from, out string to)
    {
        from = "";
        to = "";
        var match = Regex.Match(text, @"с\s+(\d{1,2})(?::(\d{2}))?\s+до\s+(\d{1,2})(?::(\d{2}))?", RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;
        var fromHour = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var fromMin = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        var toHour = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var toMin = match.Groups[4].Success ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : 0;
        if (fromHour is < 0 or > 23 || toHour is < 0 or > 23)
            return false;
        from = $"{fromHour:D2}:{fromMin:D2}";
        to = $"{toHour:D2}:{toMin:D2}";
        return true;
    }

    private static bool TryBuildSavePeriodNoteCommand(string userText, string normalized, out AssistantToolCommand command)
    {
        command = new AssistantToolCommand();
        if (!normalized.Contains(" заметк", StringComparison.Ordinal) ||
            !(normalized.Contains(" сохрани", StringComparison.Ordinal) ||
              normalized.Contains(" запиши", StringComparison.Ordinal) ||
              normalized.Contains(" добавь", StringComparison.Ordinal)))
            return false;

        var match = Regex.Match(userText, @"заметку?[:\s]+(.+)$", RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;
        var text = match.Groups[1].Value.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["text"] = text };
        if (normalized.Contains("на неделю", StringComparison.Ordinal) || normalized.Contains("эту неделю", StringComparison.Ordinal))
            args["kind"] = "week";
        else if (normalized.Contains("на месяц", StringComparison.Ordinal) || normalized.Contains("этот месяц", StringComparison.Ordinal))
            args["kind"] = "month";
        else
            args["kind"] = "day";

        command = new AssistantToolCommand { Name = "save_period_note", Args = args };
        return true;
    }

    private static async Task<AssistantToolCommand?> TryBuildDeleteGoalCommandAsync(string normalized)
    {
        if (!(normalized.Contains(" удали ", StringComparison.Ordinal) ||
              normalized.Contains(" удалить ", StringComparison.Ordinal) ||
              normalized.Contains(" убери ", StringComparison.Ordinal) ||
              normalized.Contains(" убрать ", StringComparison.Ordinal)))
            return null;
        if (!(normalized.Contains(" цел", StringComparison.Ordinal) || normalized.Contains(" задач", StringComparison.Ordinal)))
            return null;

        using var planner = new PlannerService();
        var goals = await planner.GetGoalsAsync(includeArchived: false);
        var best = goals
            .Select(g => new { Goal = g, Score = ScoreGoalTitle(normalized, g.Title, 100, 18) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();
        if (best == null)
            return null;
        return new AssistantToolCommand
        {
            Name = "delete_goal",
            Args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["goalId"] = best.Goal.Id.ToString(CultureInfo.InvariantCulture)
            }
        };
    }

    private static bool LooksLikeGraphicalReportIntent(string normalized)
    {
        return (normalized.Contains("граф", StringComparison.Ordinal) ||
                normalized.Contains("диаграм", StringComparison.Ordinal) ||
                normalized.Contains("визуаль", StringComparison.Ordinal)) &&
               (normalized.Contains("отчет", StringComparison.Ordinal) ||
                normalized.Contains("отчёт", StringComparison.Ordinal) ||
                normalized.Contains("статист", StringComparison.Ordinal));
    }

    private static AssistantToolCommand BuildGraphicalReportCommand(string userText, string normalized)
    {
        string domain;
        if (MentionsFinance(normalized))
            domain = "finance";
        else if (MentionsReminder(normalized))
            domain = "reminders";
        else if (MentionsGoal(normalized))
            domain = "goals";
        else
            domain = TryParseReportRequest(userText, out _, out _, out var parsedDomain) ? parsedDomain : "general";

        var kind = ResolveInspectionKind(normalized, out var periodStart);
        var args = BuildPeriodArgs(kind, periodStart, domain);
        AddTargetCurrencyArg(args, normalized);
        return new AssistantToolCommand
        {
            Name = "open_graphical_report",
            Args = args
        };
    }

    private static bool LooksLikeExpenseTransactionIntent(string normalized)
    {
        return normalized.Contains("потрат", StringComparison.Ordinal) ||
               normalized.Contains("купил", StringComparison.Ordinal) ||
               normalized.Contains("купила", StringComparison.Ordinal) ||
               normalized.Contains("оплат", StringComparison.Ordinal) ||
               normalized.Contains("заплат", StringComparison.Ordinal) ||
               normalized.Contains("расход", StringComparison.Ordinal);
    }

    private static bool TryParseMoneyAmount(string text, out decimal amount)
    {
        amount = 0;
        var match = Regex.Match(text, @"(?<!\d)(\d+(?:[\s.,]\d{1,2})?)(?!\d)");
        if (!match.Success)
            return false;

        var raw = match.Groups[1].Value.Replace(" ", "").Replace(',', '.');
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out amount) && amount > 0;
    }

    private static FinanceCategory? FindBestFinanceCategory(string normalizedText, IReadOnlyList<FinanceCategory> categories)
    {
        return categories
            .Select(category => new { Category = category, Score = ScoreLooseNameMatch(normalizedText, category.Name) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Category.Name.Length)
            .Select(x => x.Category)
            .FirstOrDefault();
    }

    private static SavingsEntry? FindBestSavingsAccount(string normalizedText, IReadOnlyList<SavingsEntry> accounts)
    {
        return accounts
            .Select(account => new { Account = account, Score = ScoreSavingsAccountMatch(normalizedText, account.Name) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Account.Name.Length)
            .Select(x => x.Account)
            .FirstOrDefault();
    }

    private static int ScoreSavingsAccountMatch(string normalizedText, string name)
    {
        var score = ScoreLooseNameMatch(normalizedText, name);
        if (normalizedText.Contains("монобанк", StringComparison.Ordinal) &&
            NormalizeForMatch(name).Contains("mono", StringComparison.Ordinal))
            score += 120;
        if (normalizedText.Contains("моно", StringComparison.Ordinal) &&
            NormalizeForMatch(name).Contains("mono", StringComparison.Ordinal))
            score += 80;
        return score;
    }

    private static int ScoreLooseNameMatch(string normalizedText, string name)
    {
        var normalizedName = NormalizeForMatch(name);
        if (string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(normalizedName))
            return 0;

        var score = normalizedText.Contains(normalizedName, StringComparison.Ordinal) ? 100 : 0;
        foreach (var token in normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length >= 3))
        {
            if (normalizedText.Contains(token, StringComparison.Ordinal))
                score += 20;
        }
        return score;
    }

    private static string DetectCurrency(string text, string fallback)
    {
        var normalized = NormalizeForMatch(text);
        if (normalized.Contains("грн", StringComparison.Ordinal) ||
            normalized.Contains("uah", StringComparison.Ordinal) ||
            normalized.Contains("грив", StringComparison.Ordinal))
            return CurrencyInfo.UAH;
        if (normalized.Contains("sek", StringComparison.Ordinal) ||
            normalized.Contains("крон", StringComparison.Ordinal))
            return CurrencyInfo.SEK;
        if (normalized.Contains("usd", StringComparison.Ordinal) ||
            normalized.Contains("дол", StringComparison.Ordinal))
            return CurrencyInfo.USD;
        if (normalized.Contains("eur", StringComparison.Ordinal) ||
            normalized.Contains("евро", StringComparison.Ordinal))
            return "EUR";
        return string.IsNullOrWhiteSpace(fallback) ? CurrencyInfo.UAH : fallback.Trim().ToUpperInvariant();
    }

    private static Dictionary<string, string> BuildPeriodArgs(AssistantReportPeriodKind kind, DateTime periodStart, string? domain = null)
    {
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        switch (kind)
        {
            case AssistantReportPeriodKind.Month:
                args["kind"] = "month";
                args["year"] = periodStart.Year.ToString(CultureInfo.InvariantCulture);
                args["month"] = periodStart.Month.ToString(CultureInfo.InvariantCulture);
                break;
            case AssistantReportPeriodKind.Week:
                args["kind"] = "week";
                args["date"] = periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                break;
            default:
                args["kind"] = "day";
                args["date"] = periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                break;
        }

        if (!string.IsNullOrWhiteSpace(domain))
            args["domain"] = domain;
        return args;
    }

    private static void AddTargetCurrencyArg(Dictionary<string, string> args, string normalized)
    {
        var currency = ExtractCurrencies(normalized)
            .FirstOrDefault(x => x is CurrencyInfo.UAH or CurrencyInfo.SEK or CurrencyInfo.USD);
        if (!string.IsNullOrWhiteSpace(currency))
            args["targetCurrency"] = currency;
    }

    private static bool LooksLikeExchangeRateQuestion(string normalized)
    {
        return normalized.Contains("курс", StringComparison.Ordinal) ||
               normalized.Contains("конверт", StringComparison.Ordinal) ||
               normalized.Contains("переведи", StringComparison.Ordinal) ||
               normalized.Contains("сколько будет", StringComparison.Ordinal);
    }

    private static Dictionary<string, string> BuildExchangeRateArgs(string userText, string normalized)
    {
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (TryParseMoneyAmount(userText, out var amount))
            args["amount"] = amount.ToString("0.##", CultureInfo.InvariantCulture);

        var currencies = ExtractCurrencies(normalized);
        if (currencies.Count > 0)
            args["fromCurrency"] = currencies[0];
        if (currencies.Count > 1)
            args["toCurrency"] = currencies[1];
        return args;
    }

    private static List<string> ExtractCurrencies(string normalized)
    {
        var result = new List<string>();
        void Add(string code)
        {
            if (!result.Contains(code, StringComparer.OrdinalIgnoreCase))
                result.Add(code);
        }

        if (normalized.Contains("sek", StringComparison.Ordinal) || normalized.Contains("крон", StringComparison.Ordinal))
            Add(CurrencyInfo.SEK);
        if (normalized.Contains("uah", StringComparison.Ordinal) || normalized.Contains("грн", StringComparison.Ordinal) || normalized.Contains("грив", StringComparison.Ordinal))
            Add(CurrencyInfo.UAH);
        if (normalized.Contains("usd", StringComparison.Ordinal) || normalized.Contains("дол", StringComparison.Ordinal))
            Add(CurrencyInfo.USD);
        if (normalized.Contains("eur", StringComparison.Ordinal) || normalized.Contains("евро", StringComparison.Ordinal))
            Add("EUR");
        return result;
    }

    private static AssistantReportPeriodKind ResolveInspectionKind(string normalized, out DateTime periodStart)
    {
        var today = DateTime.Today;
        var month = TryParseRussianMonth(normalized);
        var year = TryParseYear(normalized);
        var padded = $" {normalized} ";
        if (month.HasValue || padded.Contains(" месяц ", StringComparison.Ordinal) || padded.Contains(" меся", StringComparison.Ordinal))
        {
            var resolvedMonth = month ?? today.Month;
            var resolvedYear = year ?? (resolvedMonth > today.Month ? today.Year - 1 : today.Year);
            periodStart = new DateTime(resolvedYear, resolvedMonth, 1);
            return AssistantReportPeriodKind.Month;
        }

        if (padded.Contains(" недел", StringComparison.Ordinal))
        {
            periodStart = GetWeekStart(ResolveRelativeDate(normalized) ?? today);
            return AssistantReportPeriodKind.Week;
        }

        periodStart = (ResolveRelativeDate(normalized) ?? today).Date;
        return AssistantReportPeriodKind.Day;
    }

    private static bool HasExplicitPeriod(string normalized)
    {
        var padded = $" {normalized} ";
        return TryParseRussianMonth(normalized).HasValue ||
               TryParseYear(normalized).HasValue ||
               padded.Contains(" месяц ", StringComparison.Ordinal) ||
               padded.Contains(" меся", StringComparison.Ordinal) ||
               padded.Contains(" недел", StringComparison.Ordinal) ||
               padded.Contains(" сегодня ", StringComparison.Ordinal) ||
               padded.Contains(" вчера ", StringComparison.Ordinal);
    }

    private static bool TryResolveRecentFinanceReportPeriod(
        IReadOnlyList<AssistantMessage> recentMessages,
        out AssistantReportPeriodKind kind,
        out DateTime periodStart)
    {
        kind = AssistantReportPeriodKind.Day;
        periodStart = DateTime.Today;

        foreach (var message in recentMessages.Reverse().Take(10))
        {
            var content = message.Content ?? "";
            var normalized = NormalizeForMatch(content);
            if (!MentionsFinance(normalized) && !normalized.Contains("марж", StringComparison.Ordinal))
                continue;

            var monthMatch = Regex.Match(content, @"\((?<month>\d{2})\.(?<year>\d{4})\)");
            if (monthMatch.Success &&
                int.TryParse(monthMatch.Groups["month"].Value, out var month) &&
                int.TryParse(monthMatch.Groups["year"].Value, out var year) &&
                month is >= 1 and <= 12)
            {
                kind = AssistantReportPeriodKind.Month;
                periodStart = new DateTime(year, month, 1);
                return true;
            }

            if (TryParseReportRequest(content, out var parsedKind, out var parsedStart, out var parsedDomain) &&
                parsedDomain == "finance")
            {
                kind = parsedKind;
                periodStart = parsedStart;
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeDatabaseQuestion(string normalized)
    {
        var padded = $" {normalized} ";
        return padded.Contains(" ? ", StringComparison.Ordinal) ||
               padded.Contains(" какие ", StringComparison.Ordinal) ||
               padded.Contains(" какой ", StringComparison.Ordinal) ||
               padded.Contains(" какая ", StringComparison.Ordinal) ||
               padded.Contains(" сколько ", StringComparison.Ordinal) ||
               padded.Contains(" покажи ", StringComparison.Ordinal) ||
               padded.Contains(" список ", StringComparison.Ordinal) ||
               padded.Contains(" статус ", StringComparison.Ordinal) ||
               padded.Contains(" что у меня ", StringComparison.Ordinal) ||
               padded.Contains(" есть ли ", StringComparison.Ordinal) ||
               normalized.Contains("выполнил", StringComparison.Ordinal) ||
               normalized.Contains("выполнены", StringComparison.Ordinal);
    }

    private static bool MentionsFinance(string normalized)
    {
        return normalized.Contains("финанс", StringComparison.Ordinal) ||
               normalized.Contains("деньг", StringComparison.Ordinal) ||
               normalized.Contains("доход", StringComparison.Ordinal) ||
               normalized.Contains("расход", StringComparison.Ordinal) ||
               normalized.Contains("трат", StringComparison.Ordinal) ||
               normalized.Contains("операци", StringComparison.Ordinal) ||
               normalized.Contains("марж", StringComparison.Ordinal) ||
               normalized.Contains("баланс", StringComparison.Ordinal) ||
               normalized.Contains("валют", StringComparison.Ordinal) ||
               normalized.Contains("счет", StringComparison.Ordinal) ||
               normalized.Contains("счёт", StringComparison.Ordinal);
    }

    private static bool MentionsReminder(string normalized)
    {
        return normalized.Contains("напомин", StringComparison.Ordinal);
    }

    private static bool MentionsGoal(string normalized)
    {
        return normalized.Contains("цел", StringComparison.Ordinal) ||
               normalized.Contains("выполнил", StringComparison.Ordinal) ||
               normalized.Contains("выполнены", StringComparison.Ordinal);
    }

    private async Task<string?> TryHandleLocalIntentAsync(string userText, IReadOnlyList<AssistantMessage> recentMessages)
    {
        if (LooksLikeGoalCompletionQuestion(userText))
            return await TryAnswerGoalCompletionQuestionAsync(userText, recentMessages);

        if (LooksLikeGoalCompletionIntent(userText))
            return await TryCompleteGoalFromIntentAsync(userText, recentMessages);

        return null;
    }

    private async Task<string?> TryAnswerGoalCompletionQuestionAsync(string userText, IReadOnlyList<AssistantMessage> recentMessages)
    {
        if (TryResolveGoalStatusMonth(userText, recentMessages, out var monthStart))
            return await BuildMonthGoalStatusAnswerAsync(monthStart, recentMessages);

        return null;
    }

    private static bool LooksLikeGoalCompletionQuestion(string userText)
    {
        var normalized = NormalizeForMatch(userText);
        if (!normalized.Contains("выполн", StringComparison.Ordinal) &&
            !normalized.Contains("сделал", StringComparison.Ordinal) &&
            !normalized.Contains("сделала", StringComparison.Ordinal))
            return false;

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return userText.Contains('?') ||
               words.Contains("ли") ||
               normalized.Contains("выполнены ли", StringComparison.Ordinal) ||
               normalized.Contains("выполнена ли", StringComparison.Ordinal) ||
               normalized.Contains("выполнено ли", StringComparison.Ordinal);
    }

    private async Task<string> BuildMonthGoalStatusAnswerAsync(DateTime monthStart, IReadOnlyList<AssistantMessage> recentMessages)
    {
        using var statusService = new GoalStatusService();
        var statuses = await statusService.GetPeriodGoalStatusesForMonthAsync(monthStart.Year, monthStart.Month);
        var hintedTitles = ExtractRecentNumberedGoalTitles(recentMessages);
        if (hintedTitles.Count > 0)
        {
            var filtered = statuses
                .Where(status => hintedTitles.Any(hint => IsSameGoalHint(hint, status.Goal.Title)))
                .ToList();
            if (filtered.Count > 0)
                statuses = filtered;
        }

        if (statuses.Count == 0)
            return $"За {MonthName(monthStart)} не нашел периодных целей в базе.";

        var completed = statuses.Count(x => x.IsComplete);
        var prefix = completed == statuses.Count
            ? "Да, все эти цели выполнены."
            : completed == 0
                ? "Нет, эти цели не выполнены."
                : $"Частично: выполнено {completed} из {statuses.Count}.";

        var lines = statuses.Select(status =>
        {
            var mark = status.IsComplete ? "выполнено" : "не выполнено";
            return $"- {status.Goal.Title} ({status.ScopeText}): {status.Current}/{status.Target}, {mark}";
        });

        return $"{prefix}\n\nСтатус за {MonthName(monthStart)}:\n{string.Join("\n", lines)}";
    }

    private static bool TryResolveGoalStatusMonth(string userText, IReadOnlyList<AssistantMessage> recentMessages, out DateTime monthStart)
    {
        var normalized = NormalizeForMatch(userText);
        var month = TryParseRussianMonth(normalized);
        var year = TryParseYear(normalized);

        if (!month.HasValue)
        {
            foreach (var message in recentMessages.Reverse().Take(6))
            {
                var content = NormalizeForMatch(message.Content);
                month = TryParseRussianMonth(content);
                year ??= TryParseYear(content);
                if (month.HasValue)
                    break;
            }
        }

        if (!month.HasValue)
        {
            monthStart = default;
            return false;
        }

        var today = DateTime.Today;
        var resolvedYear = year ?? (month.Value > today.Month ? today.Year - 1 : today.Year);
        monthStart = new DateTime(resolvedYear, month.Value, 1);
        return true;
    }

    private static List<string> ExtractRecentNumberedGoalTitles(IReadOnlyList<AssistantMessage> recentMessages)
    {
        var result = new List<string>();
        foreach (var message in recentMessages.Reverse().Where(x => x.Role == AssistantRole.Assistant).Take(3))
        {
            foreach (var line in (message.Content ?? "").Split('\n'))
            {
                var match = Regex.Match(line.Trim(), @"^\d+\.\s+(.+)$");
                if (!match.Success)
                    continue;

                var title = Regex.Replace(match.Groups[1].Value, @"\s*\(.+\)\s*$", "").Trim();
                if (!string.IsNullOrWhiteSpace(title))
                    result.Add(title);
            }

            if (result.Count > 0)
                break;
        }

        return result;
    }

    private static bool IsSameGoalHint(string hint, string title)
    {
        return ScoreGoalTitle(NormalizeForMatch(hint), title, 100, 20) > 0 ||
               ScoreGoalTitle(NormalizeForMatch(title), hint, 100, 20) > 0;
    }

    private static string MonthName(DateTime monthStart)
    {
        return monthStart.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static bool LooksLikeCompoundActionRequest(string userText)
    {
        var normalized = NormalizeForMatch(userText);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var hasConnector =
            normalized.Contains(" и ", StringComparison.Ordinal) ||
            normalized.Contains(" потом ", StringComparison.Ordinal) ||
            normalized.Contains(" затем ", StringComparison.Ordinal) ||
            normalized.Contains(" после этого ", StringComparison.Ordinal) ||
            normalized.Contains(" также ", StringComparison.Ordinal) ||
            normalized.Contains(" еще ", StringComparison.Ordinal);
        if (!hasConnector)
            return false;

        var hints = new[]
        {
            "отчет", "отчёт", "выполн", "готово", "сделал", "сделала", "создай", "создать",
            "добавь", "удали", "измени", "отмет", "напом", "финанс", "переведи", "запиши",
            "сохрани", "цель", "напоминание", "транзакц", "категор", "счет", "счёт"
        };

        return hints.Count(h => normalized.Contains(h, StringComparison.Ordinal)) >= 2;
    }

    private async Task<string> GenerateAndSaveReportAsync(AssistantReportPeriodKind kind, DateTime periodStart, string domain, string requestText)
    {
        var task = await _repo.CreateTaskAsync("generate_report", requestText);
        try
        {
            using var generator = new ReportGenerator();
            var targetCurrency = ExtractCurrencies(NormalizeForMatch(requestText))
                .FirstOrDefault(x => x is CurrencyInfo.UAH or CurrencyInfo.SEK or CurrencyInfo.USD) ?? CurrencyInfo.UAH;
            var body = kind switch
            {
                AssistantReportPeriodKind.Month => domain switch
                {
                    "finance" => await generator.BuildMonthlyFinanceReportAsync(periodStart.Year, periodStart.Month, targetCurrency),
                    "goals" => await generator.BuildMonthlyGoalsReportAsync(periodStart.Year, periodStart.Month),
                    "reminders" => await generator.BuildMonthlyRemindersReportAsync(periodStart.Year, periodStart.Month),
                    _ => await generator.BuildMonthlyReportAsync(periodStart.Year, periodStart.Month)
                },
                AssistantReportPeriodKind.Week => domain switch
                {
                    "finance" => await generator.BuildWeeklyFinanceReportAsync(periodStart, targetCurrency),
                    "goals" => await generator.BuildWeeklyGoalsReportAsync(periodStart),
                    _ => await generator.BuildWeeklyReportAsync(periodStart)
                },
                _ => domain switch
                {
                    "finance" => await generator.BuildDailyFinanceReportAsync(periodStart, targetCurrency),
                    "goals" => await generator.BuildDailyGoalsReportAsync(periodStart),
                    _ => await generator.BuildDailyReportAsync(periodStart)
                }
            };

            await _repo.SaveReportAsync(kind, periodStart, body);
            await _repo.CompleteTaskAsync(task.Id, true, $"Отчет сохранен: {kind}, {periodStart:yyyy-MM-dd}, domain={domain}");
            return body;
        }
        catch (Exception ex)
        {
            await _repo.CompleteTaskAsync(task.Id, false, ex.Message);
            return "Не смог сформировать отчет: " + ex.Message;
        }
    }

    private async Task<string> TryCompleteGoalFromIntentAsync(string userText, IReadOnlyList<AssistantMessage> recentMessages)
    {
        var today = DateTime.Today;
        using var planner = new PlannerService();
        var candidates = await BuildGoalCompletionCandidatesAsync(planner, today);
        if (candidates.Count == 0)
            return "Не нашел активных целей на сегодня, эту неделю или этот месяц. Уточните название цели, если нужно отметить цель из другого периода.";

        var match = ResolveGoalCandidate(userText, recentMessages, candidates);
        if (match == null)
        {
            var open = candidates.Where(x => !x.IsCompleted).ToList();
            if (open.Count == 1)
                match = open[0];
        }

        if (match == null)
        {
            var lines = candidates
                .Where(x => !x.IsCompleted)
                .Take(8)
                .Select((x, i) => $"{i + 1}. {x.Goal.Title} ({ScopeText(x.Scope)})");
            return "Какую цель отметить выполненной?\n" + string.Join("\n", lines);
        }

        if (match.IsCompleted)
            return $"Цель «{match.Goal.Title}» уже отмечена выполненной.";

        var task = await _repo.CreateTaskAsync("mark_goal_completed", userText);
        try
        {
            var countToSet = Math.Max(1, match.Target);
            await planner.MarkGoalCompleteAsync(match.Goal.Id, today, countToSet);
            GoalCompletionNotificationService.Publish(match.Goal.Id, today, true);
            await _repo.CompleteTaskAsync(task.Id, true, $"Отмечена цель #{match.Goal.Id}: {match.Goal.Title}");
            return $"Готово, отметил цель «{match.Goal.Title}» выполненной.";
        }
        catch (Exception ex)
        {
            await _repo.CompleteTaskAsync(task.Id, false, ex.Message);
            return "Не смог отметить цель выполненной: " + ex.Message;
        }
    }

    private static async Task<List<GoalCompletionCandidate>> BuildGoalCompletionCandidatesAsync(PlannerService planner, DateTime today)
    {
        var result = new List<GoalCompletionCandidate>();
        var periodGoals = await planner.GetPeriodGoalsAsync();
        var recurringGoals = await planner.GetRecurringGoalsAsync();
        var weekStart = GetWeekStart(today);
        var weekEnd = weekStart.AddDays(6);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        foreach (var goal in periodGoals)
        {
            DateTime from;
            DateTime to;
            string scope;
            if (MatchesDailyPeriod(goal, today))
            {
                from = to = today;
                scope = "day";
            }
            else if (MatchesWeeklyPeriod(goal, weekStart))
            {
                from = weekStart;
                to = weekEnd;
                scope = "week";
            }
            else if (MatchesMonthlyPeriod(goal, monthStart))
            {
                from = monthStart;
                to = monthEnd;
                scope = "month";
            }
            else
            {
                continue;
            }

            var target = Math.Max(1, goal.TargetCount);
            var current = await planner.GetGoalCompletionCountAsync(goal.Id, from, to);
            result.Add(new GoalCompletionCandidate(goal, scope, from, to, current, target, current >= target));
        }

        foreach (var goal in recurringGoals.Where(g => PlannerService.IsRecurringGoalDueOn(g, today)))
        {
            var completed = await planner.IsGoalCompletedForDateAsync(goal.Id, today);
            result.Add(new GoalCompletionCandidate(goal, "recurring", today, today, completed ? 1 : 0, 1, completed));
        }

        return result
            .OrderBy(x => x.IsCompleted)
            .ThenBy(x => x.Scope == "day" ? 0 : x.Scope == "recurring" ? 1 : x.Scope == "week" ? 2 : 3)
            .ThenBy(x => x.Goal.CreatedAt)
            .ToList();
    }

    private static GoalCompletionCandidate? ResolveGoalCandidate(
        string userText,
        IReadOnlyList<AssistantMessage> recentMessages,
        IReadOnlyList<GoalCompletionCandidate> candidates)
    {
        var direct = NormalizeForMatch(userText);
        var previousUserText = string.Join(" ", recentMessages
            .Where(x => x.Role == AssistantRole.User && !string.Equals(x.Content?.Trim(), userText.Trim(), StringComparison.Ordinal))
            .Reverse()
            .Take(3)
            .Select(x => x.Content));
        var assistantContext = string.Join(" ", recentMessages
            .Where(x => x.Role == AssistantRole.Assistant)
            .Reverse()
            .Take(2)
            .Select(x => x.Content));
        var previousUser = NormalizeForMatch(previousUserText);
        var assistant = NormalizeForMatch(assistantContext);

        var scored = candidates
            .Select(x => new
            {
                Candidate = x,
                Score =
                    ScoreGoalTitle(direct, x.Goal.Title, fullTitleScore: 100, tokenScore: 12) +
                    ScoreGoalTitle(previousUser, x.Goal.Title, fullTitleScore: 80, tokenScore: 18) +
                    ScoreGoalTitle(assistant, x.Goal.Title, fullTitleScore: 20, tokenScore: 3) +
                    (direct.Contains($" {x.Goal.Id} ", StringComparison.Ordinal) || direct.Contains($" id {x.Goal.Id} ", StringComparison.Ordinal) ? 40 : 0)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.IsCompleted)
            .ToList();

        if (scored.Count == 0)
            return null;

        var best = scored[0];
        if (scored.Count > 1 && scored[1].Score == best.Score)
            return null;

        return best.Candidate;
    }

    private static int ScoreGoalTitle(string normalizedText, string title, int fullTitleScore, int tokenScore)
    {
        if (string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(title))
            return 0;

        var normalizedTitle = NormalizeForMatch(title);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
            return 0;

        var paddedText = $" {normalizedText} ";
        var score = paddedText.Contains($" {normalizedTitle} ", StringComparison.Ordinal) ? fullTitleScore : 0;
        foreach (var token in normalizedTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length >= 4))
        {
            if (paddedText.Contains($" {token} ", StringComparison.Ordinal))
                score += tokenScore;
        }
        return score;
    }

    private static bool LooksLikeGoalCompletionIntent(string userText)
    {
        if (LooksLikeGoalCompletionQuestion(userText))
            return false;

        var words = NormalizeForMatch(userText).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return false;

        if (words.Any(w => w is "выполнил" or "выполнила" or "выполнено" or "сделал" or "сделала" or "готово" or "закрыл" or "закрыла"))
            return true;

        return words.Any(w => w.StartsWith("отмет", StringComparison.Ordinal)) &&
               words.Any(w => w.StartsWith("выполн", StringComparison.Ordinal));
    }

    private static bool TryParseReportRequest(string userText, out AssistantReportPeriodKind kind, out DateTime periodStart, out string domain)
    {
        var normalized = NormalizeForMatch(userText);
        kind = AssistantReportPeriodKind.Day;
        periodStart = DateTime.Today;
        domain = "general";

        if (!ContainsAnyWord(normalized, "отчет", "отчёт"))
            return false;

        domain = DetectReportDomain(normalized);
        var today = DateTime.Today;
        var month = TryParseRussianMonth(normalized);
        var year = TryParseYear(normalized);
        var padded = $" {normalized} ";
        if (month.HasValue || padded.Contains(" месяц ", StringComparison.Ordinal) || padded.Contains(" меся", StringComparison.Ordinal))
        {
            var resolvedMonth = month ?? today.Month;
            var resolvedYear = year ?? (resolvedMonth > today.Month ? today.Year - 1 : today.Year);
            kind = AssistantReportPeriodKind.Month;
            periodStart = new DateTime(resolvedYear, resolvedMonth, 1);
            return true;
        }

        if (padded.Contains(" недел", StringComparison.Ordinal))
        {
            kind = AssistantReportPeriodKind.Week;
            periodStart = GetWeekStart(ResolveRelativeDate(normalized) ?? today);
            return true;
        }

        kind = AssistantReportPeriodKind.Day;
        periodStart = (ResolveRelativeDate(normalized) ?? today).Date;
        return true;
    }

    private static string DetectReportDomain(string normalizedText)
    {
        if (normalizedText.Contains("финанс", StringComparison.Ordinal) ||
            normalizedText.Contains("деньг", StringComparison.Ordinal) ||
            normalizedText.Contains("доход", StringComparison.Ordinal) ||
            normalizedText.Contains("расход", StringComparison.Ordinal) ||
            normalizedText.Contains("трат", StringComparison.Ordinal) ||
            normalizedText.Contains("операци", StringComparison.Ordinal) ||
            normalizedText.Contains("марж", StringComparison.Ordinal) ||
            normalizedText.Contains("баланс", StringComparison.Ordinal) ||
            normalizedText.Contains("валют", StringComparison.Ordinal))
            return "finance";

        if (normalizedText.Contains("цел", StringComparison.Ordinal))
            return "goals";

        if (normalizedText.Contains("напомин", StringComparison.Ordinal))
            return "reminders";

        return "general";
    }

    private static DateTime? ResolveRelativeDate(string normalizedText)
    {
        var today = DateTime.Today;
        var padded = $" {normalizedText} ";
        if (padded.Contains(" вчера ", StringComparison.Ordinal))
            return today.AddDays(-1);
        if (padded.Contains(" сегодня ", StringComparison.Ordinal))
            return today;
        return null;
    }

    private static int? TryParseRussianMonth(string normalizedText)
    {
        var padded = $" {normalizedText} ";
        var monthForms = new Dictionary<int, string[]>
        {
            [1] = ["январь", "января", "январе", "январ"],
            [2] = ["февраль", "февраля", "феврале", "феврал"],
            [3] = ["март", "марта", "марте"],
            [4] = ["апрель", "апреля", "апреле", "апрел"],
            [5] = ["май", "мая", "мае"],
            [6] = ["июнь", "июня", "июне", "июн"],
            [7] = ["июль", "июля", "июле", "июл"],
            [8] = ["август", "августа", "августе"],
            [9] = ["сентябрь", "сентября", "сентябре", "сентябр"],
            [10] = ["октябрь", "октября", "октябре", "октябр"],
            [11] = ["ноябрь", "ноября", "ноябре", "ноябр"],
            [12] = ["декабрь", "декабря", "декабре", "декабр"]
        };

        foreach (var (month, forms) in monthForms)
        {
            if (forms.Any(form => padded.Contains($" {form} ", StringComparison.Ordinal)))
                return month;
        }

        return null;
    }

    private static int? TryParseYear(string normalizedText)
    {
        var match = Regex.Match(normalizedText, @"\b(20\d{2}|19\d{2})\b");
        return match.Success && int.TryParse(match.Value, out var year) ? year : null;
    }

    private static bool ContainsAnyWord(string normalizedText, params string[] words)
    {
        var padded = $" {normalizedText} ";
        return words.Any(word => padded.Contains($" {NormalizeForMatch(word)} ", StringComparison.Ordinal));
    }

    private static string NormalizeForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var sb = new StringBuilder(value.Length + 2);
        sb.Append(' ');
        foreach (var ch in value.Trim().ToLowerInvariant().Replace('ё', 'е'))
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }
        sb.Append(' ');
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static string ScopeText(string scope)
    {
        return scope switch
        {
            "day" => "день",
            "week" => "неделя",
            "month" => "месяц",
            "recurring" => "регулярная",
            _ => scope
        };
    }

    private sealed record GoalCompletionCandidate(
        Goal Goal,
        string Scope,
        DateTime From,
        DateTime To,
        int Current,
        int Target,
        bool IsCompleted);

    private sealed record CommandExecutionBatch(
        List<string> UserLines,
        List<string> AgentLines);

    private static string TrimForStorage(string? value, int maxChars = MaxStoredMessageChars)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "\n\n[Текст сокращен, чтобы ассистент не раздувал память приложения.]";
    }

    private static string TrimForPrompt(string? value, int maxChars = MaxPromptItemChars)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }

    public void Dispose()
    {
        _toolRouter.Dispose();
        _llm.Dispose();
    }
}
