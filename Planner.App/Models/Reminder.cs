namespace Planner.App.Models;

public class Reminder
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int IntervalMinutes { get; set; }
    public TimeOnly? ActiveFrom { get; set; }
    public TimeOnly? ActiveTo { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public ICollection<ReminderCompletion> Completions { get; set; } = new List<ReminderCompletion>();
}
