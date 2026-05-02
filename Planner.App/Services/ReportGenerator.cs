using System.Text;
using Planner.App.Models;

namespace Planner.App.Services;

public class ReportGenerator : IDisposable
{
    private readonly PlannerService _planner = new();
    private readonly AssistantRepositoryService _repo = new();

    public async Task<string> BuildDailyReportAsync(DateTime day)
    {
        var d = day.Date;
        var next = d.AddDays(1);
        var transactions = await _planner.GetTransactionsAsync(d, next);
        var remindersDone = await _planner.GetReminderCompletionsCountForMonthAsync(d.Year, d.Month);
        var goalsDone = await _planner.GetGoalCompletionsCountForDateAsync(d);
        var sb = new StringBuilder();
        sb.AppendLine($"Отчет за день ({d:dd.MM.yyyy})");
        sb.AppendLine($"- Выполнено целей: {goalsDone}");
        sb.AppendLine($"- Отметок по напоминаниям в месяце: {remindersDone}");
        sb.AppendLine($"- Операций за день: {transactions.Count}");
        if (transactions.Count > 0)
        {
            var income = transactions.Where(x => x.Category.Type == TransactionType.Income).Sum(x => x.Amount);
            var expense = transactions.Where(x => x.Category.Type == TransactionType.Expense).Sum(x => x.Amount);
            sb.AppendLine($"- Финансы: доход {income:N2}, расход {expense:N2}, баланс {income - expense:N2}");
        }
        return sb.ToString().Trim();
    }

    public async Task<string> BuildWeeklyReportAsync(DateTime anyDayInWeek)
    {
        var from = anyDayInWeek.Date.AddDays(-(int)anyDayInWeek.DayOfWeek + (int)DayOfWeek.Monday);
        var to = from.AddDays(7);
        var tx = await _planner.GetTransactionsAsync(from, to);
        var goalStats = await _planner.GetGoalStatsForRangeAsync(from, to.AddDays(-1));
        var sb = new StringBuilder();
        sb.AppendLine($"Отчет за неделю ({from:dd.MM} - {to.AddDays(-1):dd.MM})");
        sb.AppendLine($"- Дней с выполнениями целей: {goalStats.DaysWithCompletions}");
        sb.AppendLine($"- Всего выполнений целей: {goalStats.TotalCompletions}");
        sb.AppendLine($"- Финансовых операций: {tx.Count}");
        if (tx.Count > 0)
        {
            var topExpense = tx.Where(x => x.Category.Type == TransactionType.Expense)
                .GroupBy(x => x.Category.Name)
                .OrderByDescending(g => g.Sum(x => x.Amount))
                .FirstOrDefault();
            if (topExpense != null)
                sb.AppendLine($"- Топ расход: {topExpense.Key} ({topExpense.Sum(x => x.Amount):N2})");
        }
        return sb.ToString().Trim();
    }

    public async Task<string> BuildMonthlyReportAsync(int year, int month)
    {
        var stats = await _planner.GetFinanceMonthStatsAsync(year, month);
        var reminderDone = await _planner.GetReminderCompletionsCountForMonthAsync(year, month);
        var reminders = await _planner.GetRemindersMonthlyStatsAsync(year, month);
        var sb = new StringBuilder();
        sb.AppendLine($"Отчет за месяц ({month:00}.{year})");
        sb.AppendLine($"- Доход: {stats.Income:N2}");
        sb.AppendLine($"- Расход: {stats.Expenses:N2}");
        sb.AppendLine($"- Маржа: {stats.Margin:N2}");
        sb.AppendLine($"- Выполнено напоминаний: {reminderDone}");
        if (reminders.Count > 0)
        {
            var weak = reminders.OrderBy(x => x.Total == 0 ? 1.0 : (double)x.Completed / x.Total).First();
            sb.AppendLine($"- Внимание: низкая дисциплина по «{weak.Title}» ({weak.Completed}/{weak.Total})");
        }
        return sb.ToString().Trim();
    }

    public async Task SavePeriodicReportsAsync(DateTime nowLocal)
    {
        var day = nowLocal.Date;
        var weekStart = day.AddDays(-(int)day.DayOfWeek + (int)DayOfWeek.Monday);
        var monthStart = new DateTime(day.Year, day.Month, 1);
        await _repo.SaveReportAsync(AssistantReportPeriodKind.Day, day, await BuildDailyReportAsync(day));
        await _repo.SaveReportAsync(AssistantReportPeriodKind.Week, weekStart, await BuildWeeklyReportAsync(day));
        await _repo.SaveReportAsync(AssistantReportPeriodKind.Month, monthStart, await BuildMonthlyReportAsync(day.Year, day.Month));
    }

    public void Dispose()
    {
        _planner.Dispose();
    }
}
