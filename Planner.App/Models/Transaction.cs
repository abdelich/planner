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

    /// <summary>Счет сбережений, по которому прошла операция. null — у старых операций, созданных до привязки.</summary>
    public int? SavingsEntryId { get; set; }

    /// <summary>
    /// Изменение баланса счета в валюте счета (со знаком), уже примененное этой операцией.
    /// Хранится, чтобы удаление и правка откатывали ровно то, что списали, без повторной
    /// конвертации по изменившемуся курсу.
    /// </summary>
    public decimal SavingsDelta { get; set; }
}
