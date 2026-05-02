namespace Planner.App.Models;

public class AssistantConversation
{
    public int Id { get; set; }
    public string Title { get; set; } = "Основной чат";
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<AssistantMessage> Messages { get; set; } = new List<AssistantMessage>();
}
