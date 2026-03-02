namespace Planner.App.Models;

public class SavingsMonthlySnapshot
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal TotalAmountUah { get; set; }
    public DateTime RecordedAt { get; set; }
}
