namespace Planner.App.Models;

public class AssistantTask
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";
    public string RequestText { get; set; } = "";
    public AssistantTaskStatus Status { get; set; } = AssistantTaskStatus.Pending;
    public string? ResultText { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
