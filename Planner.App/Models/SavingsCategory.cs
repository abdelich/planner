namespace Planner.App.Models;

public class SavingsCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public ICollection<SavingsEntry> Entries { get; set; } = new List<SavingsEntry>();
}
