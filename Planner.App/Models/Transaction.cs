namespace Planner.App.Models;

public class Transaction
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "SEK";
    public DateTime Date { get; set; }
    public int CategoryId { get; set; }
    public FinanceCategory Category { get; set; } = null!;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}
