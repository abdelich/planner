namespace Planner.App.Services;

public static class FinanceDataChangedNotificationService
{
    public static event Action<FinanceDataChangedEvent>? Changed;

    public static void Publish(string reason, DateTime? date = null)
    {
        Changed?.Invoke(new FinanceDataChangedEvent(reason, date ?? DateTime.Today));
    }
}

public sealed record FinanceDataChangedEvent(string Reason, DateTime Date);
