using Planner.App.Models;

namespace Planner.App.Services;

public record AssistantLlmSettings(
    string ApiKey,
    string Endpoint,
    string Model,
    bool AllowFinanceData,
    bool AllowGoalsData,
    bool AllowRemindersData);

public record AssistantChatTurn(
    AssistantRole Role,
    string Content,
    DateTime CreatedAt);

public record AssistantLlmResponse(
    string ReplyText,
    IReadOnlyList<AssistantToolCommand> Commands);

public class AssistantToolCommand
{
    public string Name { get; set; } = "";
    public Dictionary<string, string> Args { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public record AssistantToolResult(
    bool Success,
    string Message);

/// <summary>Контекст выполнения команд (подтверждение рискованных действий в UI).</summary>
public sealed class AssistantToolExecutionContext
{
    public bool UserConfirmedFinance { get; init; }
}
