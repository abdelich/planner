namespace Planner.App.Models;

public class AssistantMemoryFact
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public double Confidence { get; set; } = 1.0;
    public DateTime UpdatedAt { get; set; }
}
