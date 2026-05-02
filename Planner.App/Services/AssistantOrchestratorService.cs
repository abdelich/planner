using System.Text;
using Planner.App.Models;

namespace Planner.App.Services;

public class AssistantOrchestratorService
{
    private readonly AssistantRepositoryService _repo = new();
    private readonly AssistantLocalSettingsService _settingsService = new();
    private readonly CloudLlmClient _llm = new();
    private readonly ToolCommandRouter _toolRouter = new();
    private readonly AssistantTelemetryService _telemetry = new();
    private readonly PlannerService _planner = new();

    /// <param name="confirmRiskyCommandAsync">Подтверждение финансовых команд (UI). Возвращает true, если пользователь согласен.</param>
    public async Task<(AssistantConversation Conversation, string Reply)> SendUserMessageAsync(
        string text,
        Func<AssistantToolCommand, string, Task<bool>>? confirmRiskyCommandAsync = null,
        CancellationToken ct = default)
    {
        var conversation = await _repo.GetOrCreateMainConversationAsync();
        await _repo.AddMessageAsync(conversation.Id, AssistantRole.User, text);
        await _telemetry.TrackAsync("assistant_user_message");

        if (text.Contains("меня зовут", StringComparison.OrdinalIgnoreCase))
            await _repo.UpsertMemoryFactAsync("user.name", text);
        if (text.Contains("моя цель", StringComparison.OrdinalIgnoreCase))
            await _repo.UpsertMemoryFactAsync("user.goal", text);

        var settings = _settingsService.GetEffectiveLlmSettings();
        var recentMessages = await _repo.GetRecentMessagesAsync(conversation.Id, 30);
        var memoryFacts = await _repo.GetMemoryFactsAsync(10);
        var snapshot = await BuildContextSnapshotAsync(settings);
        var systemPrompt = BuildSystemPrompt(memoryFacts, snapshot);

        string replyText;
        IReadOnlyList<AssistantToolCommand> commands;
        try
        {
            var llmResponse = await _llm.GenerateAsync(
                settings,
                systemPrompt,
                recentMessages.Select(x => new AssistantChatTurn(x.Role, x.Content, x.CreatedAt)).ToList(),
                ct);
            replyText = llmResponse.ReplyText;
            commands = llmResponse.Commands;
            await _telemetry.TrackAsync("assistant_llm_ok");
        }
        catch (Exception ex)
        {
            await _telemetry.TrackAsync("assistant_llm_error", ex.Message);
            replyText =
                "Не удалось обратиться к модели. Проверьте ключ (переменная OPENAI_API_KEY или настройки) и сеть. " +
                "Локально можно: «создай цель: …».";
            commands = Array.Empty<AssistantToolCommand>();
        }

        var explicitCommands = ParseInlineCommands(text);
        if (explicitCommands.Count > 0)
            commands = explicitCommands;

        var commandResults = new List<string>();
        foreach (var command in commands)
        {
            var task = await _repo.CreateTaskAsync(command.Name, text);
            AssistantToolExecutionContext? ctx = null;
            if (IsFinanceRisk(command))
            {
                var summary = BuildFinanceConfirmationSummary(command);
                var ok = confirmRiskyCommandAsync != null && await confirmRiskyCommandAsync(command, summary);
                if (!ok)
                {
                    await _repo.CompleteTaskAsync(task.Id, false, "Отменено пользователем.");
                    commandResults.Add("❌ Действие отменено.");
                    continue;
                }
                ctx = new AssistantToolExecutionContext { UserConfirmedFinance = true };
            }

            var result = await _toolRouter.ExecuteAsync(command, ctx);
            await _repo.CompleteTaskAsync(task.Id, result.Success, result.Message);
            commandResults.Add((result.Success ? "✅ " : "❌ ") + result.Message);
        }

        if (commandResults.Count > 0)
            replyText = $"{replyText}\n\nРезультаты действий:\n- {string.Join("\n- ", commandResults)}";

        await _repo.AddMessageAsync(conversation.Id, AssistantRole.Assistant, replyText);
        return (conversation, replyText);
    }

    private static bool IsFinanceRisk(AssistantToolCommand command)
    {
        var n = command.Name.Trim().ToLowerInvariant();
        return n is "create_transaction" or "transfer_between_savings";
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

        return "Подтвердить действие?";
    }

    private async Task<string> BuildContextSnapshotAsync(AssistantLlmSettings settings)
    {
        var sb = new StringBuilder();
        if (settings.AllowGoalsData)
        {
            var goals = await _planner.GetGoalsAsync();
            sb.AppendLine($"Goals: {goals.Count}");
            foreach (var g in goals.Take(8))
                sb.AppendLine($"- Goal: {g.Title} (Target={g.TargetCount})");
        }
        if (settings.AllowRemindersData)
        {
            var reminders = await _planner.GetRemindersAsync();
            sb.AppendLine($"Reminders: {reminders.Count}");
            foreach (var r in reminders.Take(8))
                sb.AppendLine($"- Reminder: {r.Title} every {r.IntervalMinutes} min");
        }
        if (settings.AllowFinanceData)
        {
            var from = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var tx = await _planner.GetTransactionsAsync(from, from.AddMonths(1));
            sb.AppendLine($"MonthTransactions: {tx.Count}");
            var savings = await _planner.GetSavingsEntriesAsync();
            sb.AppendLine($"SavingsAccounts: {savings.Count}");
            foreach (var s in savings.Take(8))
                sb.AppendLine($"- Savings: id={s.Id} {s.Name} {s.Balance:N2} {s.Currency}");
        }
        return sb.ToString().Trim();
    }

    private static string BuildSystemPrompt(List<AssistantMemoryFact> memoryFacts, string contextSnapshot)
    {
        var memoryLines = memoryFacts.Count == 0
            ? "No memory facts yet."
            : string.Join("\n", memoryFacts.Select(m => $"- {m.Key}: {m.Value}"));
        return
            "You are a personal life-planner copilot inside a desktop app.\n" +
            "Answer in Russian, concise and practical.\n" +
            "You can optionally return JSON:\n" +
            "{\n" +
            "  \"reply\":\"human answer\",\n" +
            "  \"commands\":[{\"name\":\"create_goal\",\"args\":{\"title\":\"...\",\"targetCount\":\"1\"}}]\n" +
            "}\n" +
            "Only include commands when user explicitly asks to execute something.\n" +
            "For create_transaction use real categoryId and savingsEntryId from context (Savings: id=...).\n" +
            "Do not set confirm in args; the user confirms in the app UI.\n\n" +
            "Memory facts:\n" +
            memoryLines + "\n\n" +
            "Current app context:\n" +
            contextSnapshot;
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
}
