using System.Reflection;

namespace Planner.App.Services;

public sealed class AssistantSpeechSynthesisService
{
    private const int MaxSpokenChars = 3500;

    public Task SpeakAsync(string text, CancellationToken ct = default)
    {
        var spoken = PrepareText(text);
        if (string.IsNullOrWhiteSpace(spoken))
            return Task.CompletedTask;

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var sapiType = Type.GetTypeFromProgID("SAPI.SpVoice")
                ?? throw new InvalidOperationException("Windows SAPI voice engine is not available.");
            var voice = Activator.CreateInstance(sapiType)
                ?? throw new InvalidOperationException("Не удалось создать Windows SAPI voice engine.");
            sapiType.InvokeMember(
                "Speak",
                BindingFlags.InvokeMethod,
                null,
                voice,
                new object[] { spoken, 0 });
        }, ct);
    }

    private static string PrepareText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var cleaned = text
            .Replace("Действия:", "", StringComparison.OrdinalIgnoreCase)
            .Replace("```", "");
        return cleaned.Length <= MaxSpokenChars
            ? cleaned.Trim()
            : cleaned[..MaxSpokenChars].Trim();
    }
}
