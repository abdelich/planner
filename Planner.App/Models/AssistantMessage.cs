namespace Planner.App.Models;

public class AssistantMessage
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public AssistantConversation Conversation { get; set; } = null!;
    public AssistantRole Role { get; set; }
    public string Content { get; set; } = "";
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
