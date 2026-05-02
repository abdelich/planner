namespace Planner.App.Models;

public class PeriodNote
{
    public int Id { get; set; }
    public NotePeriodKind Kind { get; set; }
    public DateTime PeriodStart { get; set; }
    public string Text { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}
