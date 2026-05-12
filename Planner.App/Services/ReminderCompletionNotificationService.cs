namespace Planner.App.Services;

public static class ReminderCompletionNotificationService
{
    public static event Action<ReminderCompletionChangedEvent>? CompletionChanged;

    public static void Publish(int reminderId, DateTime slotDateTime, bool completed, int monthDelta)
    {
        var normalizedSlot = NormalizeSlot(slotDateTime);
        CompletionChanged?.Invoke(new ReminderCompletionChangedEvent(
            reminderId,
            normalizedSlot,
            completed,
            monthDelta));
    }

    public static DateTime NormalizeSlot(DateTime slotDateTime)
    {
        return slotDateTime.Date.Add(new TimeSpan(0, slotDateTime.Hour, slotDateTime.Minute, 0));
    }
}

public sealed record ReminderCompletionChangedEvent(
    int ReminderId,
    DateTime SlotDateTime,
    bool Completed,
    int MonthDelta);
