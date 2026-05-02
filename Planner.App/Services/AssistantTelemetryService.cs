using Planner.App.Data;
using Planner.App.Models;

namespace Planner.App.Services;

public class AssistantTelemetryService
{
    public async Task TrackAsync(string eventType, string? payload = null)
    {
        try
        {
            await using var db = new PlannerDbContext();
            db.AssistantTelemetryEvents.Add(new AssistantTelemetryEvent
            {
                EventType = eventType ?? "",
                Payload = payload,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        catch
        {
        }
    }
}
