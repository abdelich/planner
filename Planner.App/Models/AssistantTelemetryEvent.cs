namespace Planner.App.Models;

public class AssistantTelemetryEvent
{
    public int Id { get; set; }
    public string EventType { get; set; } = "";
    public string? Payload { get; set; }
    public DateTime CreatedAt { get; set; }
}
