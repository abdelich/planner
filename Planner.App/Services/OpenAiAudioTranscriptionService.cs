using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Planner.App.Services;

public sealed class OpenAiAudioTranscriptionService : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly AssistantLocalSettingsService _settings = new();

    public async Task<string> TranscribeAsync(string wavPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath))
            throw new FileNotFoundException("Файл записи не найден.", wavPath);

        var settings = _settings.GetEffectiveLlmSettings();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Для распознавания речи нужен OpenAI API key в настройках ассистента.");

        using var req = new HttpRequestMessage(HttpMethod.Post, ResolveAudioEndpoint(settings.Endpoint));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("whisper-1"), "model");
        content.Add(new StringContent("ru"), "language");
        content.Add(new StringContent("json"), "response_format");

        var bytes = await File.ReadAllBytesAsync(wavPath, ct);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(file, "file", "voice.wav");
        req.Content = content;

        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Распознавание речи не удалось: {(int)res.StatusCode} {res.ReasonPhrase}. {Trim(body, 1200)}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("text", out var text)
            ? (text.GetString() ?? "").Trim()
            : "";
    }

    private static string ResolveAudioEndpoint(string chatEndpoint)
    {
        if (Uri.TryCreate(chatEndpoint, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath;
            var v1Index = path.IndexOf("/v1/", StringComparison.OrdinalIgnoreCase);
            if (v1Index >= 0)
            {
                var builder = new UriBuilder(uri)
                {
                    Path = path[..(v1Index + 4)] + "audio/transcriptions",
                    Query = ""
                };
                return builder.Uri.ToString();
            }
        }

        return "https://api.openai.com/v1/audio/transcriptions";
    }

    private static string Trim(string value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
