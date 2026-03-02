namespace Planner.App.Models;

public class FinanceCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TransactionType Type { get; set; }
    public int SortOrder { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
