namespace Planner.App.Models;

public class GoalCompletion
{
    public int Id { get; set; }
    public int GoalId { get; set; }
    public Goal Goal { get; set; } = null!;
    public DateTime Date { get; set; }
    public int Count { get; set; } = 1;
    public string? Note { get; set; }
}
