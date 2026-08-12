using System.Text;
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
            .Where(x => x.Role is AssistantRole.User or AssistantRole.Assistant)
            .Select(x => new AssistantChatTurn(x.Role, x.Content, x.CreatedAt))
            .ToList();
        var executedCommandSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allCommandResults = new List<string>();
        var allAgentResultLines = new List<string>();
        var replyText = "";
        var stoppedByStepLimit = false;

        for (var step = 1; step <= MaxAgentSteps; step++)
        {
            IReadOnlyList<AssistantToolCommand> commands;
            try
            {
                if (step > 1)
                {
                    snapshot = await BuildContextSnapshotAsync(settings);
                    systemPrompt = BuildSystemPrompt(memoryFacts, snapshot);
                }

                var llmResponse = await _llm.GenerateAsync(settings, systemPrompt, agentTurns, ct);
                replyText = llmResponse.ReplyText;
                commands = llmResponse.Commands;
                await _telemetry.TrackAsync("assistant_llm_ok", $"step={step};commands={commands.Count}");
            }
            catch (Exception ex)
            {
                await _telemetry.TrackAsync("assistant_llm_error", ex.Message);
                AssistantDiagnosticsService.LogMemory("assistant-llm-error", ex.ToString());
                var details = TrimForPrompt(ex.Message, 1200);
                replyText = allCommandResults.Count > 0
                    ? $"Действия выполнены, но модель вернула ошибку:\n{details}"
                    : $"Ошибка LLM:\n{details}";
                commands = Array.Empty<AssistantToolCommand>();
                break;
            }

            if (commands.Count == 0)
                break;

            agentTurns.Add(new AssistantChatTurn(
                AssistantRole.Assistant,
                replyText ?? "",
                DateTime.UtcNow,
                ToolCalls: commands));

            var batch = await ExecuteCommandsAsync(
                commands,
                text,
                confirmRiskyCommandAsync,
                executedCommandSignatures);
            allCommandResults.AddRange(batch.UserLines);
            allAgentResultLines.AddRange(batch.AgentLines);

            foreach (var toolTurn in batch.ToolTurns)
                agentTurns.Add(toolTurn);

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

        var openEnded = await planner.GetOpenEndedGoalStatesAsync(periodGoals);

        var todayPeriodGoals = periodGoals
            .Where(g => MatchesDailyPeriod(g, today, openEnded))
            .OrderBy(g => g.CreatedAt)
            .ToList();
        var todayRecurringGoals = recurringGoals
            .Where(g => PlannerService.IsRecurringGoalDueOn(g, today))
            .OrderBy(g => g.CreatedAt)
            .ToList();

        sb.AppendLine($"GoalsDueToday: count={todayPeriodGoals.Count + todayRecurringGoals.Count}");
        foreach (var g in todayPeriodGoals)
        {
            var current = await GetPeriodGoalProgressAsync(planner, g, today, today, openEnded);
            sb.AppendLine(FormatPeriodGoal(g, "day", today, today, current, openEnded));
        }
        foreach (var g in todayRecurringGoals)
        {
            var completed = await planner.IsGoalCompletedForDateAsync(g.Id, today);
            sb.AppendLine(FormatRecurringGoal(g, dueToday: true, completedToday: completed));
        }

        var weekGoals = periodGoals
            .Where(g => MatchesWeeklyPeriod(g, weekStart, openEnded))
            .OrderBy(g => g.CreatedAt)
            .ToList();
        sb.AppendLine($"GoalsThisWeek: count={weekGoals.Count}");
        foreach (var g in weekGoals)
        {
            var current = await GetPeriodGoalProgressAsync(planner, g, weekStart, weekEnd, openEnded);
            sb.AppendLine(FormatPeriodGoal(g, "week", weekStart, weekEnd, current, openEnded));
        }

        var monthGoals = periodGoals
            .Where(g => MatchesMonthlyPeriod(g, monthStart, openEnded))
            .OrderBy(g => g.CreatedAt)
            .ToList();
        sb.AppendLine($"GoalsThisMonth: count={monthGoals.Count}");
        foreach (var g in monthGoals)
        {
            var current = await GetPeriodGoalProgressAsync(planner, g, monthStart, monthEnd, openEnded);
            sb.AppendLine(FormatPeriodGoal(g, "month", monthStart, monthEnd, current, openEnded));
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
            sb.AppendLine(FormatPeriodGoal(g, GoalTypeText(g.Type), start, end, current: null, openEnded));
        }

        sb.AppendLine($"AllRecurringGoalsActive: count={recurringGoals.Count}");
        foreach (var g in recurringGoals.OrderBy(g => g.CreatedAt))
        {
            var dueToday = PlannerService.IsRecurringGoalDueOn(g, today);
            var completedToday = dueToday && await planner.IsGoalCompletedForDateAsync(g.Id, today);
            sb.AppendLine(FormatRecurringGoal(g, dueToday, completedToday));
        }
    }

    /// <summary>Прогресс бессрочной цели считается за всё время, обычной — за её период.</summary>
    private static async Task<int> GetPeriodGoalProgressAsync(
        PlannerService planner,
        Goal goal,
        DateTime from,
        DateTime to,
        OpenEndedGoalStateMap openEnded)
    {
        if (goal.IsOpenEnded && openEnded.Find(goal.Id) is { } state)
            return state.TotalCount;
        return await planner.GetGoalCompletionCountAsync(goal.Id, from, to);
    }

    private static string BuildSystemPrompt(List<AssistantMemoryFact> memoryFacts, string contextSnapshot)
    {
        var memoryLines = memoryFacts.Count == 0
            ? "No memory facts yet."
            : string.Join("\n", memoryFacts.Select(m => $"- {TrimForPrompt(m.Key, 120)}: {TrimForPrompt(m.Value)}"));
        return
            "You are a personal life-planner copilot inside a desktop app. Reply in Russian, concise and practical.\n" +
            "You have access to typed tools (OpenAI function calling). Decide between three options on every turn:\n" +
            "1) Call one or more tools — when the user wants something done or you need data.\n" +
            "2) Reply in plain Russian text with NO tool calls — when the action is complete or no action is needed.\n" +
            "3) Reply with a short clarifying question — only if the user's wording is genuinely ambiguous about WHAT to do or WHICH item to act on.\n" +
            "\n" +
            "Rules:\n" +
            "- If you have enough info to act, call the tool immediately. Do NOT write 'я добавлю', 'подождите', 'сейчас сделаю', 'I will', 'let me' — either call the tool now or ask a real clarifying question.\n" +
            "- Optional arguments have sensible defaults. Do NOT ask about them when the user did not bring them up. create_goal needs only title; defaults: category=period+type=daily unless user says 'каждый день/повторяй/привычка' (then category=recurring, recurrenceKind=everyday), targetCount=1, description=empty. create_reminder: intervalMinutes=60, activeFrom/activeTo unset means whole day.\n" +
            "- Open-ended goals: set isOpenEnded=true when the user says 'бессрочная', 'без срока', 'пока не выполню', 'висит до выполнения'. Such a goal shows up in the day, week AND month lists every period until it is completed once, and scope/type is ignored for it.\n" +
            "- For any question about app data (goals, reminders, finance, accounts, transactions, reports, notes, history, status), call an inspect_* tool first; never answer database/status questions from chat history alone.\n" +
            "- For report requests call generate_report; for graphical/chart requests call open_graphical_report. Pick domain=finance/goals/reminders/general accordingly.\n" +
            "- For finance numbers across currencies, do not add/subtract yourself — use inspect_exchange_rates or a finance report with targetCurrency.\n" +
            "- When the user asks for goals today/this week/this month, answer from GoalsDueToday / GoalsThisWeek / GoalsThisMonth only, not the full list.\n" +
            "- Use real ids from Current app context for update/delete/complete. Do not invent ids.\n" +
            "- Do not call confirm in args; the desktop UI prompts the user for risky finance ops.\n" +
            $"- Up to {MaxCommandsPerAgentStep} tool calls per step. After tool results come back, decide the next step or give the final Russian answer.\n" +
            "- Never claim 'создал/удалил/сохранил/обновил' something unless the matching tool call in this turn succeeded.\n" +
            "- If the user says a month like 'апрель/April' without a year, resolve it as the most recent past matching month.\n" +
            "\n" +
            "Memory facts:\n" +
            memoryLines + "\n\n" +
            "Current app context:\n" +
            TrimForPrompt(contextSnapshot, 80000);
    }

    private static string FormatPeriodGoal(Goal goal, string scope, DateTime periodStart, DateTime periodEnd, int? current, OpenEndedGoalStateMap openEnded)
    {
        var progress = current.HasValue ? $"; progress={current.Value}/{Math.Max(1, goal.TargetCount)}" : "";
        if (!goal.IsOpenEnded)
        {
            return
                $"- id={goal.Id}; scope={scope}; title=\"{TrimForPrompt(goal.Title)}\"; description=\"{TrimForPrompt(goal.Description, 300)}\"; target={Math.Max(1, goal.TargetCount)}{progress}; period={periodStart:yyyy-MM-dd}..{periodEnd:yyyy-MM-dd}; created={goal.CreatedAt:yyyy-MM-dd}";
        }

        var completed = openEnded.Find(goal.Id) is { IsComplete: true };
        return
            $"- id={goal.Id}; scope={scope}; openEnded=true; title=\"{TrimForPrompt(goal.Title)}\"; description=\"{TrimForPrompt(goal.Description, 300)}\"; target={Math.Max(1, goal.TargetCount)}{progress}; completed={completed}; since={PeriodAnchor(goal):yyyy-MM-dd}; created={goal.CreatedAt:yyyy-MM-dd}";
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

    private static bool MatchesDailyPeriod(Goal goal, DateTime day, OpenEndedGoalStateMap openEnded)
    {
        if (goal.Category != GoalCategory.Period)
            return false;
        if (goal.IsOpenEnded)
            return openEnded.IsVisibleIn(goal, day.Date, day.Date);
        return goal.Type == GoalType.Daily && PeriodAnchor(goal) == day.Date;
    }

    private static bool MatchesWeeklyPeriod(Goal goal, DateTime weekStart, OpenEndedGoalStateMap openEnded)
    {
        if (goal.Category != GoalCategory.Period)
            return false;
        if (goal.IsOpenEnded)
            return openEnded.IsVisibleIn(goal, weekStart.Date, weekStart.Date.AddDays(6));
        return goal.Type == GoalType.Weekly && GetWeekStart(PeriodAnchor(goal)) == weekStart.Date;
    }

    private static bool MatchesMonthlyPeriod(Goal goal, DateTime monthStart, OpenEndedGoalStateMap openEnded)
    {
        if (goal.Category != GoalCategory.Period)
            return false;
        if (goal.IsOpenEnded)
            return openEnded.IsVisibleIn(goal, monthStart.Date, monthStart.Date.AddMonths(1).AddDays(-1));
        var anchor = PeriodAnchor(goal);
        return goal.Type == GoalType.Monthly &&
               anchor.Year == monthStart.Year &&
               anchor.Month == monthStart.Month;
    }

    private async Task<CommandExecutionBatch> ExecuteCommandsAsync(
        IReadOnlyList<AssistantToolCommand> commands,
        string originalUserText,
        Func<AssistantToolCommand, string, Task<bool>>? confirmRiskyCommandAsync,
        HashSet<string> executedCommandSignatures)
    {
        var userLines = new List<string>();
        var agentLines = new List<string>();
        var toolTurns = new List<AssistantChatTurn>();
        var processed = 0;
        foreach (var command in commands)
        {
            if (processed >= MaxCommandsPerAgentStep)
                break;
            processed++;

            var validation = AssistantToolCatalog.Validate(command);
            if (!validation.Success)
            {
                var validationTask = await _repo.CreateTaskAsync(command?.Name ?? "invalid_command", originalUserText);
                await _repo.CompleteTaskAsync(validationTask.Id, false, validation.Message);
                userLines.Add($"{command?.Name ?? "command"}: ошибка валидации — {validation.Message}");
                agentLines.Add($"{command?.Name ?? "command"}: validation_failed; {validation.Message}");
                toolTurns.Add(BuildToolResultTurn(command, $"validation_failed: {validation.Message}"));
                continue;
            }

            var signature = CommandSignature(command);
            if (!executedCommandSignatures.Add(signature))
            {
                userLines.Add($"{command.Name}: пропущен повтор команды.");
                agentLines.Add($"{command.Name}: skipped_duplicate");
                toolTurns.Add(BuildToolResultTurn(command, "skipped_duplicate"));
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
                    toolTurns.Add(BuildToolResultTurn(command, $"failed: {cancelled}"));
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
                toolTurns.Add(BuildToolResultTurn(command, $"{(result.Success ? "success" : "failed")}: {TrimForPrompt(result.Message, MaxToolResultChars)}"));
            }
            catch (Exception ex)
            {
                await _repo.CompleteTaskAsync(task.Id, false, ex.Message);
                userLines.Add($"{command.Name}: ошибка — {TrimForPrompt(ex.Message, 700)}");
                agentLines.Add($"{command.Name}: failed; exception={TrimForPrompt(ex.Message, 1400)}");
                toolTurns.Add(BuildToolResultTurn(command, $"failed: exception {TrimForPrompt(ex.Message, 1400)}"));
            }
        }

        if (commands.Count > MaxCommandsPerAgentStep)
        {
            var skipped = commands.Count - MaxCommandsPerAgentStep;
            userLines.Add($"Пропущено команд сверх лимита: {skipped}.");
            agentLines.Add($"Skipped {skipped} commands because per-step command limit is {MaxCommandsPerAgentStep}.");
        }

        var batch = new CommandExecutionBatch(userLines, agentLines, toolTurns);
        AssistantDiagnosticsService.LogMemory("assistant-agent-tools", $"commands={commands.Count};results={agentLines.Count}");
        return batch;
    }

    private static AssistantChatTurn BuildToolResultTurn(AssistantToolCommand? command, string content)
    {
        return new AssistantChatTurn(
            AssistantRole.Tool,
            content,
            DateTime.UtcNow,
            ToolCallId: command?.ToolCallId ?? Guid.NewGuid().ToString("N"),
            ToolName: command?.Name);
    }

    private static string CommandSignature(AssistantToolCommand command)
    {
        var args = string.Join(";", command.Args
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Key}={x.Value}"));
        return $"{command.Name.Trim().ToLowerInvariant()}|{args}";
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

    private sealed record CommandExecutionBatch(
        List<string> UserLines,
        List<string> AgentLines,
        List<AssistantChatTurn> ToolTurns);

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
