namespace Planner.App.Services;

public static class GoalCompletionNotificationService
{
    public static event Action<GoalCompletionChangedEvent>? CompletionChanged;

    public static void Publish(int goalId, DateTime date, bool completed)
    {
        CompletionChanged?.Invoke(new GoalCompletionChangedEvent(goalId, date.Date, completed));
    }
}

public sealed record GoalCompletionChangedEvent(
    int GoalId,
    DateTime Date,
    bool Completed);
