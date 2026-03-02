namespace Planner.App.Models;

public class Goal
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public GoalCategory Category { get; set; } = GoalCategory.Period;
    public GoalType Type { get; set; }
    public RecurrenceKind RecurrenceKind { get; set; } = RecurrenceKind.EveryDay;
    public int IntervalDays { get; set; } = 1;
    public int RecurrenceDays { get; set; }
    public int TargetCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsArchived { get; set; }

    public ICollection<GoalCompletion> Completions { get; set; } = new List<GoalCompletion>();
}
