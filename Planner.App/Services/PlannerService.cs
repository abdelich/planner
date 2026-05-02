using Microsoft.EntityFrameworkCore;
using Planner.App.Data;
using Planner.App.Models;

namespace Planner.App.Services;

public class PlannerService : IDisposable
{
    private readonly PlannerDbContext _db = new();

    public async Task EnsureDbAsync()
    {
        await _db.Database.EnsureCreatedAsync();
    }

    public void EnsureDb()
    {
        _db.Database.EnsureCreated();
    }

    public async Task<List<Goal>> GetGoalsAsync(bool includeArchived = false)
    {
        var q = _db.Goals.AsNoTracking();
        if (!includeArchived)
            q = q.Where(g => !g.IsArchived);
        return await q.OrderByDescending(g => g.CreatedAt).ToListAsync();
    }

    public async Task<List<Goal>> GetPeriodGoalsAsync(bool includeArchived = false)
    {
        var q = _db.Goals.AsNoTracking().Where(g => g.Category == GoalCategory.Period);
        if (!includeArchived)
            q = q.Where(g => !g.IsArchived);
        return await q.OrderByDescending(g => g.CreatedAt).ToListAsync();
    }

    public async Task<List<Goal>> GetRecurringGoalsAsync(bool includeArchived = false)
    {
        var q = _db.Goals.AsNoTracking().Where(g => g.Category == GoalCategory.Recurring);
        if (!includeArchived)
            q = q.Where(g => !g.IsArchived);
        var list = await q.ToListAsync();
        return list.OrderBy(g => g.RecurrenceKind)
            .ThenBy(g => g.RecurrenceKind == RecurrenceKind.EveryNDays ? g.IntervalDays : 999)
            .ThenByDescending(g => g.CreatedAt)
            .ToList();
    }

    public static bool IsRecurringGoalDueOn(Goal g, DateTime date)
    {
        if (g.Category != GoalCategory.Recurring) return false;
        var d = date.Date;
        return g.RecurrenceKind switch
        {
            RecurrenceKind.EveryDay => true,
            RecurrenceKind.EveryNDays => g.IntervalDays > 0 && IsDueEveryNDays(g, d),
            RecurrenceKind.SpecificDaysOfWeek => g.RecurrenceDays != 0 && ((1 << (int)d.DayOfWeek) & g.RecurrenceDays) != 0,
            _ => false
        };
    }

    private static bool IsDueEveryNDays(Goal g, DateTime date)
    {
        var start = (g.StartDate ?? g.CreatedAt).Date;
        var days = (int)(date - start).TotalDays;
        return days >= 0 && days % g.IntervalDays == 0;
    }

    public async Task<Goal> AddGoalAsync(Goal goal)
    {
        goal.CreatedAt = DateTime.UtcNow;
        _db.Goals.Add(goal);
        await _db.SaveChangesAsync();
        return goal;
    }

    public async Task UpdateGoalAsync(Goal goal)
    {
        _db.Goals.Update(goal);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteGoalAsync(Goal goal)
    {
        await DeleteGoalByIdAsync(goal.Id);
    }

    public async Task DeleteGoalByIdAsync(int goalId)
    {
        await _db.GoalCompletions.Where(c => c.GoalId == goalId).ExecuteDeleteAsync();
        var goal = await _db.Goals.FindAsync(goalId);
        if (goal != null)
        {
            _db.Goals.Remove(goal);
            await _db.SaveChangesAsync();
        }
    }

    public async Task MarkGoalCompleteAsync(int goalId, DateTime date, int count = 1)
    {
        var existing = await _db.GoalCompletions
            .FirstOrDefaultAsync(c => c.GoalId == goalId && c.Date.Date == date.Date);
        if (existing != null)
        {
            existing.Count = Math.Max(existing.Count, count);
        }
        else
        {
            _db.GoalCompletions.Add(new GoalCompletion { GoalId = goalId, Date = date.Date, Count = count });
        }
        await _db.SaveChangesAsync();
    }

    public async Task<int> GetGoalCompletionCountAsync(int goalId, DateTime from, DateTime to)
    {
        return await _db.GoalCompletions
            .Where(c => c.GoalId == goalId && c.Date >= from && c.Date <= to)
            .SumAsync(c => c.Count);
    }

    public async Task<bool> IsGoalCompletedForDateAsync(int goalId, DateTime date)
    {
        return await _db.GoalCompletions
            .AnyAsync(c => c.GoalId == goalId && c.Date.Date == date.Date);
    }

    public async Task UnmarkGoalCompletionAsync(int goalId, DateTime date)
    {
        var toRemove = await _db.GoalCompletions
            .Where(c => c.GoalId == goalId && c.Date.Date == date.Date)
            .ToListAsync();
        _db.GoalCompletions.RemoveRange(toRemove);
        await _db.SaveChangesAsync();
    }

    public async Task<List<Reminder>> GetRemindersAsync()
    {
        return await _db.Reminders.AsNoTracking()
            .Where(r => r.IsEnabled)
            .OrderBy(r => r.Id)
            .ToListAsync();
    }

    public async Task<List<Reminder>> GetAllRemindersAsync()
    {
        return await _db.Reminders.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
    }

    public async Task<Reminder?> GetReminderByIdAsync(int id)
    {
        return await _db.Reminders.FindAsync(id);
    }

    public async Task<Reminder> AddReminderAsync(Reminder reminder)
    {
        reminder.CreatedAt = DateTime.UtcNow;
        _db.Reminders.Add(reminder);
        await _db.SaveChangesAsync();
        return reminder;
    }

    public async Task UpdateReminderAsync(Reminder reminder)
    {
        _db.Reminders.Update(reminder);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteReminderAsync(Reminder reminder)
    {
        await DeleteReminderByIdAsync(reminder.Id);
    }

    public async Task DeleteReminderByIdAsync(int reminderId)
    {
        await _db.ReminderCompletions.Where(c => c.ReminderId == reminderId).ExecuteDeleteAsync();
        var reminder = await _db.Reminders.FindAsync(reminderId);
        if (reminder != null)
        {
            _db.Reminders.Remove(reminder);
            await _db.SaveChangesAsync();
        }
    }

    public async Task SetReminderSlotCompletedAsync(int reminderId, DateTime slotDateTime, bool completed)
    {
        var slot = slotDateTime.Date.Add(new TimeSpan(0, slotDateTime.Hour, slotDateTime.Minute, 0));
        var existing = await _db.ReminderCompletions
            .FirstOrDefaultAsync(c => c.ReminderId == reminderId && c.SlotDateTime == slot);
        if (existing != null)
        {
            existing.Completed = completed;
        }
        else if (completed)
        {
            _db.ReminderCompletions.Add(new ReminderCompletion { ReminderId = reminderId, SlotDateTime = slot, Completed = true });
        }
        await _db.SaveChangesAsync();
    }

    public async Task<bool> IsReminderSlotCompletedAsync(int reminderId, DateTime slotDateTime)
    {
        var slot = slotDateTime.Date.Add(new TimeSpan(0, slotDateTime.Hour, slotDateTime.Minute, 0));
        return await _db.ReminderCompletions
            .AnyAsync(c => c.ReminderId == reminderId && c.SlotDateTime == slot && c.Completed);
    }

    public async Task<(int Completed, int Total)> GetReminderMonthlyProgressAsync(int reminderId, int year, int month)
    {
        var reminder = await _db.Reminders.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reminderId);
        if (reminder == null) return (0, 0);

        var from = new DateTime(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var totalSlots = CountSlotsInRange(reminder, from, to);
        var completed = await _db.ReminderCompletions
            .CountAsync(c => c.ReminderId == reminderId && c.Completed &&
                c.SlotDateTime >= from && c.SlotDateTime < from.AddMonths(1));
        return (completed, totalSlots);
    }

    private static int CountSlotsInRange(Reminder r, DateTime from, DateTime to)
    {
        var interval = r.IntervalMinutes < 1 ? 60 : r.IntervalMinutes;
        int count = 0;
        var start = r.ActiveFrom ?? TimeOnly.MinValue;
        var end = r.ActiveTo ?? new TimeOnly(23, 59);
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
        {
            var t = start;
            while (t <= end)
            {
                count++;
                t = t.Add(TimeSpan.FromMinutes(interval));
                if (t <= start) break;
            }
        }
        return count;
    }

    public async Task<List<ReminderCompletion>> GetReminderCompletionsForDayAsync(int reminderId, DateTime date)
    {
        var start = date.Date;
        var end = start.AddDays(1);
        return await _db.ReminderCompletions
            .AsNoTracking()
            .Where(c => c.ReminderId == reminderId && c.SlotDateTime >= start && c.SlotDateTime < end)
            .ToListAsync();
    }

    public async Task<List<(Reminder Reminder, DateTime Slot)>> GetDueReminderSlotsAsync(DateTime now)
    {
        var reminders = await _db.Reminders.AsNoTracking().Where(r => r.IsEnabled).ToListAsync();
        var result = new List<(Reminder, DateTime)>();
        var today = now.Date;
        foreach (var r in reminders)
        {
            var interval = r.IntervalMinutes < 1 ? 60 : r.IntervalMinutes;
            var from = r.ActiveFrom ?? new TimeOnly(0, 0);
            var to = r.ActiveTo ?? new TimeOnly(23, 59);
            var totalMins = (int)(now - today).TotalMinutes;
            var slotMins = (totalMins / interval) * interval;
            var slotTime = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(slotMins));
            if (slotTime < from || slotTime > to) continue;
            var slotDateTime = today.AddMinutes(slotMins);
            var completed = await IsReminderSlotCompletedAsync(r.Id, slotDateTime);
            if (!completed)
                result.Add((r, slotDateTime));
        }
        return result;
    }

    public async Task<int> GetGoalCompletionsCountForDateAsync(DateTime date)
    {
        var start = date.Date;
        var end = start.AddDays(1);
        return await _db.GoalCompletions
            .Where(c => c.Date >= start && c.Date < end)
            .SumAsync(c => c.Count);
    }

    public async Task<(int DaysWithCompletions, int TotalCompletions)> GetGoalStatsForRangeAsync(DateTime from, DateTime to)
    {
        var fromDate = from.Date;
        var toDate = to.Date;
        var days = await _db.GoalCompletions
            .Where(c => c.Date >= fromDate && c.Date <= toDate)
            .GroupBy(c => c.Date)
            .CountAsync();
        var total = await _db.GoalCompletions
            .Where(c => c.Date >= fromDate && c.Date <= toDate)
            .SumAsync(c => c.Count);
        return (days, total);
    }

    public async Task<List<(DateTime Date, int Count)>> GetGoalCompletionsByDayAsync(int lastDays = 14)
    {
        var from = DateTime.Today.AddDays(-lastDays);
        var list = await _db.GoalCompletions
            .Where(c => c.Date >= from)
            .GroupBy(c => c.Date)
            .Select(g => new { g.Key, Sum = g.Sum(c => c.Count) })
            .ToListAsync();
        var dict = list.ToDictionary(x => x.Key.Date, x => x.Sum);
        var result = new List<(DateTime, int)>();
        for (var i = lastDays - 1; i >= 0; i--)
        {
            var d = DateTime.Today.AddDays(-i);
            result.Add((d, dict.TryGetValue(d, out var n) ? n : 0));
        }
        return result;
    }

    public async Task<List<(int ReminderId, string Title, int Completed, int Total)>> GetRemindersMonthlyStatsAsync(int year, int month)
    {
        var reminders = await _db.Reminders.AsNoTracking().Where(r => r.IsEnabled).OrderBy(r => r.Id).ToListAsync();
        var from = new DateTime(year, month, 1);
        var to = from.AddMonths(1);
        var result = new List<(int, string, int, int)>();
        foreach (var r in reminders)
        {
            var total = CountSlotsInRange(r, from, to.AddDays(-1));
            var completed = await _db.ReminderCompletions
                .CountAsync(c => c.ReminderId == r.Id && c.Completed && c.SlotDateTime >= from && c.SlotDateTime < to);
            result.Add((r.Id, r.Title ?? "", completed, total));
        }
        return result;
    }

    public async Task<int> GetReminderCompletionsCountForMonthAsync(int year, int month)
    {
        var from = new DateTime(year, month, 1);
        var to = from.AddMonths(1);
        return await _db.ReminderCompletions
            .CountAsync(c => c.Completed && c.SlotDateTime >= from && c.SlotDateTime < to);
    }

    public async Task<List<FinanceCategory>> GetFinanceCategoriesAsync(TransactionType? type = null)
    {
        var q = _db.FinanceCategories.AsNoTracking();
        if (type.HasValue)
            q = q.Where(c => c.Type == type.Value);
        return await q.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync();
    }

    public async Task<FinanceCategory> AddFinanceCategoryAsync(FinanceCategory category)
    {
        _db.FinanceCategories.Add(category);
        await _db.SaveChangesAsync();
        return category;
    }

    public async Task UpdateFinanceCategoryAsync(int id, string name)
    {
        var c = await _db.FinanceCategories.FindAsync(id);
        if (c == null) return;
        c.Name = name?.Trim() ?? "";
        await _db.SaveChangesAsync();
    }

    public async Task DeleteFinanceCategoryAsync(FinanceCategory category)
    {
        await _db.Transactions.Where(t => t.CategoryId == category.Id).ExecuteDeleteAsync();
        _db.FinanceCategories.Remove(category);
        await _db.SaveChangesAsync();
    }

    /// <param name="toExclusive">Первая дата после диапазона (как <c>firstOfMonth.AddMonths(1)</c>).</param>
    public async Task<List<Transaction>> GetTransactionsAsync(DateTime from, DateTime toExclusive, string? currencyFilter = null, StatsFilterType? typeFilter = null)
    {
        var fromDate = from.Date;
        var endExclusive = toExclusive.Date;
        var q = _db.Transactions
            .AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.Date >= fromDate && t.Date < endExclusive);
        if (!string.IsNullOrEmpty(currencyFilter))
            q = q.Where(t => t.Currency == currencyFilter);
        var list = await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToListAsync();
        if (typeFilter == StatsFilterType.IncomeOnly)
            list = list.Where(t => t.Category.Type == TransactionType.Income).ToList();
        else if (typeFilter == StatsFilterType.ExpenseOnly)
            list = list.Where(t => t.Category.Type == TransactionType.Expense).ToList();
        return list;
    }

    public async Task<Transaction> AddTransactionAsync(Transaction transaction)
    {
        transaction.CreatedAt = DateTime.UtcNow;
        transaction.Category = null!;
        _db.Transactions.Add(transaction);
        await _db.SaveChangesAsync();
        return transaction;
    }

    public async Task DeleteTransactionAsync(Transaction transaction)
    {
        await DeleteTransactionByIdAsync(transaction.Id);
    }

    public async Task DeleteTransactionByIdAsync(int transactionId)
    {
        var t = await _db.Transactions.FindAsync(transactionId);
        if (t != null)
        {
            _db.Transactions.Remove(t);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<FinanceMonthStats> GetFinanceMonthStatsAsync(int year, int month, string? currencyFilter = null, StatsFilterType statsFilter = StatsFilterType.All)
    {
        var from = new DateTime(year, month, 1);
        var to = from.AddMonths(1);
        var q = _db.Transactions
            .AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.Date >= from && t.Date < to);
        if (!string.IsNullOrEmpty(currencyFilter))
            q = q.Where(t => t.Currency == currencyFilter);
        var list = await q.ToListAsync();
        if (statsFilter == StatsFilterType.IncomeOnly)
            list = list.Where(t => t.Category.Type == TransactionType.Income).ToList();
        else if (statsFilter == StatsFilterType.ExpenseOnly)
            list = list.Where(t => t.Category.Type == TransactionType.Expense).ToList();
        var income = list.Where(t => t.Category.Type == TransactionType.Income).Sum(t => t.Amount);
        var expenses = list.Where(t => t.Category.Type == TransactionType.Expense).Sum(t => t.Amount);
        var byCategory = list
            .GroupBy(t => new { t.Category.Id, t.Category.Name, t.Category.Type })
            .Select(g => new CategorySum(g.Key.Name, g.Key.Type, g.Sum(t => t.Amount)))
            .OrderByDescending(x => x.Sum)
            .ToList();
        return new FinanceMonthStats(income, expenses, income - expenses, byCategory);
    }

    public async Task<List<SavingsCategory>> GetSavingsCategoriesAsync()
    {
        return await _db.SavingsCategories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<SavingsCategory> AddSavingsCategoryAsync(SavingsCategory category)
    {
        _db.SavingsCategories.Add(category);
        await _db.SaveChangesAsync();
        return category;
    }

    public async Task UpdateSavingsCategoryAsync(int id, string name)
    {
        var c = await _db.SavingsCategories.FindAsync(id);
        if (c == null) return;
        c.Name = name;
        await _db.SaveChangesAsync();
    }

    public async Task<(bool Ok, string? Error)> TryDeleteSavingsCategoryAsync(int id)
    {
        var hasEntries = await _db.SavingsEntries.AnyAsync(e => e.SavingsCategoryId == id);
        if (hasEntries)
            return (false, "В категории есть счета. Сначала удалите или переназначьте их.");
        var c = await _db.SavingsCategories.FindAsync(id);
        if (c == null) return (true, null);
        _db.SavingsCategories.Remove(c);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<SavingsEntry>> GetSavingsEntriesAsync()
    {
        return await _db.SavingsEntries
            .AsNoTracking()
            .Include(e => e.SavingsCategory)
            .OrderBy(e => e.SavingsCategory.SortOrder)
            .ThenBy(e => e.SortOrder)
            .ThenBy(e => e.Name)
            .ToListAsync();
    }

    public async Task<SavingsEntry> AddSavingsEntryAsync(SavingsEntry entry)
    {
        entry.CreatedAt = DateTime.UtcNow;
        _db.SavingsEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task UpdateSavingsEntryAsync(int id, string? name, decimal? balance)
    {
        var e = await _db.SavingsEntries.FindAsync(id);
        if (e == null) return;
        if (name != null) e.Name = name;
        if (balance.HasValue) e.Balance = balance.Value;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteSavingsEntryByIdAsync(int id)
    {
        var e = await _db.SavingsEntries.FindAsync(id);
        if (e != null)
        {
            _db.SavingsEntries.Remove(e);
            await _db.SaveChangesAsync();
        }
    }

    public async Task AddDeltaToSavingsBalanceAsync(int savingsEntryId, decimal delta)
    {
        var e = await _db.SavingsEntries.FindAsync(savingsEntryId);
        if (e == null) return;
        e.Balance += delta;
        await _db.SaveChangesAsync();
    }

    public async Task TransferBetweenSavingsAsync(int fromSavingsEntryId, decimal fromDelta, int toSavingsEntryId, decimal toDelta)
    {
        if (fromSavingsEntryId == toSavingsEntryId) return;

        var from = await _db.SavingsEntries.FindAsync(fromSavingsEntryId);
        var to = await _db.SavingsEntries.FindAsync(toSavingsEntryId);
        if (from == null || to == null) return;

        from.Balance += fromDelta;
        to.Balance += toDelta;
        await _db.SaveChangesAsync();
    }

    public async Task SaveSavingsSnapshotAsync(int year, int month, decimal totalUah)
    {
        var existing = await _db.SavingsMonthlySnapshots
            .FirstOrDefaultAsync(s => s.Year == year && s.Month == month);
        if (existing != null)
        {
            existing.TotalAmountUah = totalUah;
            existing.RecordedAt = DateTime.UtcNow;
        }
        else
        {
            _db.SavingsMonthlySnapshots.Add(new SavingsMonthlySnapshot
            {
                Year = year,
                Month = month,
                TotalAmountUah = totalUah,
                RecordedAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync();
    }

    public async Task<List<SavingsMonthlySnapshot>> GetSavingsMonthlySnapshotsAsync()
    {
        return await _db.SavingsMonthlySnapshots
            .AsNoTracking()
            .OrderBy(s => s.Year).ThenBy(s => s.Month)
            .ToListAsync();
    }

    public static DateTime NormalizePeriodNoteStart(NotePeriodKind kind, DateTime periodStart)
    {
        var d = periodStart.Date;
        return kind == NotePeriodKind.Month
            ? new DateTime(d.Year, d.Month, 1)
            : d;
    }

    public async Task<string?> GetPeriodNoteTextAsync(NotePeriodKind kind, DateTime periodStart)
    {
        var key = NormalizePeriodNoteStart(kind, periodStart);
        var n = await _db.PeriodNotes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Kind == kind && x.PeriodStart == key);
        return n?.Text;
    }

    public async Task SavePeriodNoteAsync(NotePeriodKind kind, DateTime periodStart, string text)
    {
        var key = NormalizePeriodNoteStart(kind, periodStart);
        var existing = await _db.PeriodNotes
            .FirstOrDefaultAsync(x => x.Kind == kind && x.PeriodStart == key);
        var trimmed = text ?? "";
        if (existing != null)
        {
            existing.Text = trimmed;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.PeriodNotes.Add(new PeriodNote
            {
                Kind = kind,
                PeriodStart = key,
                Text = trimmed,
                UpdatedAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync();
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}

public enum StatsFilterType { All, IncomeOnly, ExpenseOnly }

public record FinanceMonthStats(
    decimal Income,
    decimal Expenses,
    decimal Margin,
    List<CategorySum> ByCategory);

public record CategorySum(string CategoryName, TransactionType Type, decimal Sum);
