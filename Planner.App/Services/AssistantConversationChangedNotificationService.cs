namespace Planner.App.Services;

public static class AssistantConversationChangedNotificationService
{
    public static event Action<int>? Changed;

    public static void Publish(int conversationId)
    {
        Changed?.Invoke(conversationId);
    }
}
