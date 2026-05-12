using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Planner.App.Models;

namespace Planner.App.Services;

public class CloudLlmClient : IDisposable
{
    private const int MaxTurnContentChars = 50000;
    private const int MaxSystemPromptChars = 120000;
    private const int MaxResponseBodyChars = 240000;
    private const int MaxAssistantReplyChars = 12000;
    private const int MaxOutputTokens = 3000;

    private readonly HttpClient _http = new();

    public async Task<AssistantLlmResponse> GenerateAsync(
        AssistantLlmSettings settings,
        string systemPrompt,
        IReadOnlyList<AssistantChatTurn> turns,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return new AssistantLlmResponse(
                "API ключ не задан. Задайте переменную окружения OPENAI_API_KEY или сохраните ключ в настройках ассистента.",
                Array.Empty<AssistantToolCommand>());
        }

        var endpoint = string.IsNullOrWhiteSpace(settings.Endpoint)
            ? "https://api.openai.com/v1/chat/completions"
            : settings.Endpoint.Trim();

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(settings.Model) ? "gpt-4o-mini" : settings.Model,
            temperature = 0.2,
            max_tokens = MaxOutputTokens,
            messages = BuildMessages(systemPrompt, turns)
        };

        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (res.Content.Headers.ContentLength is > MaxResponseBodyChars)
                    throw new InvalidOperationException("LLM response is too large.");
                var body = await res.Content.ReadAsStringAsync(ct);
                if (body.Length > MaxResponseBodyChars)
                    throw new InvalidOperationException("LLM response is too large.");
                if (res.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(body);
                    var content = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString() ?? "";
                    return ParseAssistantResponse(content);
                }

                var code = (int)res.StatusCode;
                if ((code == 429 || code is >= 500 and <= 599) && attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(400 * (attempt + 1)), ct);
                    continue;
                }

                throw new InvalidOperationException($"LLM request failed: {(int)res.StatusCode} {res.ReasonPhrase}. {TrimContent(body, 1500)}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && attempt < 2)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(400 * (attempt + 1)), ct);
            }
        }

        throw last ?? new InvalidOperationException("LLM request failed after retries.");
    }

    private static object[] BuildMessages(string systemPrompt, IReadOnlyList<AssistantChatTurn> turns)
    {
        var list = new List<object> { new { role = "system", content = TrimContent(systemPrompt, MaxSystemPromptChars) } };
        foreach (var t in turns)
        {
            list.Add(new
            {
                role = t.Role switch
                {
                    AssistantRole.User => "user",
                    AssistantRole.System => "system",
                    _ => "assistant"
                },
                content = TrimContent(t.Content, MaxTurnContentChars)
            });
        }
        return list.ToArray();
    }

    private static AssistantLlmResponse ParseAssistantResponse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new AssistantLlmResponse("Пустой ответ модели. Попробуйте еще раз.", Array.Empty<AssistantToolCommand>());

        // Expected optional JSON envelope:
        // {"reply":"...","commands":[{"name":"create_goal","args":{"title":"...","targetCount":"1"}}]}
        var original = content.Trim();
        var trimmed = ExtractJsonEnvelope(original);
        if (trimmed.Length > MaxAssistantReplyChars)
            trimmed = TrimContent(trimmed, MaxAssistantReplyChars);
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            return new AssistantLlmResponse(trimmed, Array.Empty<AssistantToolCommand>());

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var commands = new List<AssistantToolCommand>();
            if (doc.RootElement.TryGetProperty("commands", out var commandsNode) && commandsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var cmdNode in commandsNode.EnumerateArray())
                {
                    if (TryParseCommandObject(cmdNode, out var cmd))
                        commands.Add(cmd);
                }
            }

            if (commands.Count == 0 && TryParseCommandObject(doc.RootElement, out var singleCommand))
                commands.Add(singleCommand);

            var reply = doc.RootElement.TryGetProperty("reply", out var replyNode)
                ? replyNode.GetString() ?? ""
                : commands.Count > 0
                    ? "Выполняю действие."
                    : trimmed;
            reply = TrimContent(reply, MaxAssistantReplyChars);
            return new AssistantLlmResponse(reply, commands);
        }
        catch
        {
            return new AssistantLlmResponse(TrimContent(original, MaxAssistantReplyChars), Array.Empty<AssistantToolCommand>());
        }
    }

    private static bool TryParseCommandObject(JsonElement node, out AssistantToolCommand command)
    {
        command = new AssistantToolCommand();
        if (node.ValueKind != JsonValueKind.Object)
            return false;

        string name = "";
        if (node.TryGetProperty("name", out var nameNode))
            name = nameNode.GetString() ?? "";
        else if (node.TryGetProperty("command", out var commandNode))
            name = commandNode.GetString() ?? "";
        else if (node.TryGetProperty("tool", out var toolNode))
            name = toolNode.GetString() ?? "";

        JsonElement argsNode = node;
        if (node.TryGetProperty("args", out var explicitArgs) && explicitArgs.ValueKind == JsonValueKind.Object)
            argsNode = explicitArgs;
        else if (string.IsNullOrWhiteSpace(name) &&
                 node.TryGetProperty("amount", out _) &&
                 node.TryGetProperty("categoryId", out _) &&
                 node.TryGetProperty("savingsEntryId", out _))
        {
            name = "create_transaction";
        }

        if (string.IsNullOrWhiteSpace(name) || !AssistantToolCatalog.IsKnown(name))
            return false;

        command.Name = name.Trim();
        foreach (var p in argsNode.EnumerateObject())
        {
            if (p.NameEquals("name") || p.NameEquals("command") || p.NameEquals("tool") || p.NameEquals("reply") || p.NameEquals("commands"))
                continue;
            command.Args[p.Name] = JsonValueToString(p.Value);
        }

        return command.Args.Count > 0 || name.StartsWith("inspect_", StringComparison.OrdinalIgnoreCase);
    }

    private static string JsonValueToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.ToString(),
            _ => value.ToString()
        };
    }

    private static string ExtractJsonEnvelope(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd >= 0 && lastFence > firstLineEnd)
                trimmed = trimmed[(firstLineEnd + 1)..lastFence].Trim();
        }

        if (trimmed.StartsWith("{", StringComparison.Ordinal))
            return trimmed;

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start
            ? trimmed[start..(end + 1)].Trim()
            : trimmed;
    }

    private static string TrimContent(string? value, int maxChars)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars
            ? trimmed
            : trimmed[..maxChars] + "\n\n[Сокращено, чтобы не раздувать память приложения.]";
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
