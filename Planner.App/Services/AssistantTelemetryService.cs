using Planner.App.Data;
using Planner.App.Models;

namespace Planner.App.Services;

public class AssistantTelemetryService
{
    private const int MaxPayloadChars = 2000;

    public async Task TrackAsync(string eventType, string? payload = null)
    {
        try
        {
            await using var db = new PlannerDbContext();
            db.AssistantTelemetryEvents.Add(new AssistantTelemetryEvent
            {
                EventType = eventType ?? "",
                Payload = TrimPayload(payload),
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        catch
        {
        }
    }

    private static string? TrimPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return payload;
        var trimmed = payload.Trim();
        return trimmed.Length <= MaxPayloadChars
            ? trimmed
            : trimmed[..MaxPayloadChars] + "\n\n[Payload сокращен.]";
    }
}
