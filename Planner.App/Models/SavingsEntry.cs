namespace Planner.App.Models;

public class SavingsEntry
{
    public int Id { get; set; }
    public int SavingsCategoryId { get; set; }
    public SavingsCategory SavingsCategory { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "UAH";
    public decimal Balance { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}
