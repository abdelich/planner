namespace Planner.App.Models;

public class AssistantReport
{
    public int Id { get; set; }
    public AssistantReportPeriodKind Kind { get; set; }
    public DateTime PeriodStart { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
