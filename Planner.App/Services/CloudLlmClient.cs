using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Planner.App.Models;

namespace Planner.App.Services;

public class CloudLlmClient
{
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

                using var res = await _http.SendAsync(req, ct);
                var body = await res.Content.ReadAsStringAsync(ct);
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

                throw new InvalidOperationException($"LLM request failed: {(int)res.StatusCode} {res.ReasonPhrase}. {body}");
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
        var list = new List<object> { new { role = "system", content = systemPrompt } };
        foreach (var t in turns)
        {
            list.Add(new
            {
                role = t.Role == AssistantRole.User ? "user" : "assistant",
                content = t.Content
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
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            return new AssistantLlmResponse(content.Trim(), Array.Empty<AssistantToolCommand>());

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var reply = doc.RootElement.TryGetProperty("reply", out var replyNode)
                ? replyNode.GetString() ?? ""
                : content.Trim();
            var commands = new List<AssistantToolCommand>();
            if (doc.RootElement.TryGetProperty("commands", out var commandsNode) && commandsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var cmdNode in commandsNode.EnumerateArray())
                {
                    var cmd = new AssistantToolCommand
                    {
                        Name = cmdNode.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "" : ""
                    };
                    if (cmdNode.TryGetProperty("args", out var argsNode) && argsNode.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var p in argsNode.EnumerateObject())
                            cmd.Args[p.Name] = p.Value.ToString();
                    }
                    if (!string.IsNullOrWhiteSpace(cmd.Name))
                        commands.Add(cmd);
                }
            }
            return new AssistantLlmResponse(reply, commands);
        }
        catch
        {
            return new AssistantLlmResponse(content.Trim(), Array.Empty<AssistantToolCommand>());
        }
    }
}
