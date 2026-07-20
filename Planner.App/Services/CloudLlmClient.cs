using System.IO;
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

        var requestJson = BuildRequestJson(settings, systemPrompt, turns);

        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
                req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (res.Content.Headers.ContentLength is > MaxResponseBodyChars)
                    throw new InvalidOperationException("LLM response is too large.");
                var body = await res.Content.ReadAsStringAsync(ct);
                if (body.Length > MaxResponseBodyChars)
                    throw new InvalidOperationException("LLM response is too large.");
                if (res.IsSuccessStatusCode)
                    return ParseAssistantResponse(body);

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

    private static string BuildRequestJson(
        AssistantLlmSettings settings,
        string systemPrompt,
        IReadOnlyList<AssistantChatTurn> turns)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", string.IsNullOrWhiteSpace(settings.Model) ? "gpt-4o-mini" : settings.Model);
            writer.WriteNumber("temperature", 0.2);
            writer.WriteNumber("max_tokens", MaxOutputTokens);
            writer.WriteString("tool_choice", "auto");
            writer.WritePropertyName("tools");
            using (var toolsDoc = JsonDocument.Parse(AssistantToolCatalog.BuildOpenAiToolsJson()))
                toolsDoc.RootElement.WriteTo(writer);

            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            WriteSystemMessage(writer, systemPrompt);
            foreach (var turn in turns)
                WriteTurnMessage(writer, turn);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSystemMessage(Utf8JsonWriter writer, string systemPrompt)
    {
        writer.WriteStartObject();
        writer.WriteString("role", "system");
        writer.WriteString("content", TrimContent(systemPrompt, MaxSystemPromptChars));
        writer.WriteEndObject();
    }

    private static void WriteTurnMessage(Utf8JsonWriter writer, AssistantChatTurn turn)
    {
        writer.WriteStartObject();
        switch (turn.Role)
        {
            case AssistantRole.User:
                writer.WriteString("role", "user");
                writer.WriteString("content", TrimContent(turn.Content, MaxTurnContentChars));
                break;
            case AssistantRole.System:
                writer.WriteString("role", "system");
                writer.WriteString("content", TrimContent(turn.Content, MaxTurnContentChars));
                break;
            case AssistantRole.Tool:
                writer.WriteString("role", "tool");
                writer.WriteString("tool_call_id", turn.ToolCallId ?? "");
                writer.WriteString("content", TrimContent(turn.Content, MaxTurnContentChars));
                break;
            default:
                writer.WriteString("role", "assistant");
                writer.WriteString("content", TrimContent(turn.Content, MaxTurnContentChars));
                if (turn.ToolCalls != null && turn.ToolCalls.Count > 0)
                {
                    writer.WriteStartArray("tool_calls");
                    foreach (var call in turn.ToolCalls)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("id", call.ToolCallId ?? "");
                        writer.WriteString("type", "function");
                        writer.WriteStartObject("function");
                        writer.WriteString("name", call.Name);
                        writer.WriteString("arguments", string.IsNullOrEmpty(call.RawArgumentsJson)
                            ? SerializeArgs(call.Args)
                            : call.RawArgumentsJson);
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                break;
        }
        writer.WriteEndObject();
    }

    private static string SerializeArgs(Dictionary<string, string> args)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var kv in args)
                writer.WriteString(kv.Key, kv.Value);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static AssistantLlmResponse ParseAssistantResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new AssistantLlmResponse("Пустой ответ модели. Попробуйте еще раз.", Array.Empty<AssistantToolCommand>());

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return new AssistantLlmResponse("Пустой ответ модели.", Array.Empty<AssistantToolCommand>());

        var message = choices[0].GetProperty("message");
        var reply = message.TryGetProperty("content", out var contentNode) && contentNode.ValueKind == JsonValueKind.String
            ? contentNode.GetString() ?? ""
            : "";

        var commands = new List<AssistantToolCommand>();
        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in toolCalls.EnumerateArray())
            {
                if (!call.TryGetProperty("function", out var function))
                    continue;
                var name = function.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                var argsJson = function.TryGetProperty("arguments", out var argsNode) && argsNode.ValueKind == JsonValueKind.String
                    ? argsNode.GetString() ?? "{}"
                    : "{}";
                var id = call.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? "" : "";

                var command = new AssistantToolCommand
                {
                    Name = name.Trim(),
                    ToolCallId = id,
                    RawArgumentsJson = argsJson
                };
                PopulateArgsFromJson(command, argsJson);
                commands.Add(command);
            }
        }

        return new AssistantLlmResponse(TrimContent(reply, MaxAssistantReplyChars), commands);
    }

    private static void PopulateArgsFromJson(AssistantToolCommand command, string argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
            return;
        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return;
            foreach (var property in doc.RootElement.EnumerateObject())
                command.Args[property.Name] = JsonValueToString(property.Value);
        }
        catch
        {
            // Malformed arguments — leave args empty; validator will reject.
        }
    }

    private static string JsonValueToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.Null => "",
            _ => value.ToString()
        };
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
