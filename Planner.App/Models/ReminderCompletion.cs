namespace Planner.App.Models;

public class ReminderCompletion
{
    public int Id { get; set; }
    public int ReminderId { get; set; }
    public Reminder Reminder { get; set; } = null!;
    public DateTime SlotDateTime { get; set; }
    public bool Completed { get; set; }
}
