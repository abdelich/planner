using System.Text;
using Planner.App.Models;

namespace Planner.App.Services;

public class ReportGenerator : IDisposable
{
    private readonly PlannerService _planner = new();
    private readonly AssistantRepositoryService _repo = new();
    private readonly ExchangeRateService _exchange = new();

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
            AppendFinanceSummary(sb, transactions);
        }
        return sb.ToString().Trim();
    }

    public async Task<string> BuildWeeklyReportAsync(DateTime anyDayInWeek)
    {
        var from = GetWeekStart(anyDayInWeek);
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
            AppendFinanceSummary(sb, tx);
            var topExpense = tx.Where(x => x.Category.Type == TransactionType.Expense)
                .GroupBy(x => new { x.Category.Name, Currency = CurrencyKey(x.Currency) })
                .OrderByDescending(g => g.Sum(x => x.Amount))
                .FirstOrDefault();
            if (topExpense != null)
                sb.AppendLine($"- Топ расход: {topExpense.Key.Name} ({FormatMoney(topExpense.Sum(x => x.Amount), topExpense.Key.Currency)})");
        }
        return sb.ToString().Trim();
    }

    public async Task<string> BuildDailyFinanceReportAsync(DateTime day, string? targetCurrency = null)
    {
        var d = day.Date;
        return await BuildFinanceReportForRangeAsync(
            $"Финансовый отчет за день ({d:dd.MM.yyyy})",
            d,
            d.AddDays(1),
            includeSavingsSnapshot: false,
            targetCurrency);
    }

    public async Task<string> BuildWeeklyFinanceReportAsync(DateTime anyDayInWeek, string? targetCurrency = null)
    {
        var from = GetWeekStart(anyDayInWeek);
        return await BuildFinanceReportForRangeAsync(
            $"Финансовый отчет за неделю ({from:dd.MM} - {from.AddDays(6):dd.MM})",
            from,
            from.AddDays(7),
            includeSavingsSnapshot: false,
            targetCurrency);
    }

    public async Task<string> BuildMonthlyFinanceReportAsync(int year, int month, string? targetCurrency = null)
    {
        var from = new DateTime(year, month, 1);
        return await BuildFinanceReportForRangeAsync(
            $"Финансовый отчет за месяц ({month:00}.{year})",
            from,
            from.AddMonths(1),
            includeSavingsSnapshot: true,
            targetCurrency);
    }

    public async Task<string> BuildDailyGoalsReportAsync(DateTime day)
    {
        var d = day.Date;
        var goalsDone = await _planner.GetGoalCompletionsCountForDateAsync(d);
        var sb = new StringBuilder();
        sb.AppendLine($"Отчет по целям за день ({d:dd.MM.yyyy})");
        sb.AppendLine($"- Выполнений целей: {goalsDone}");
        return sb.ToString().Trim();
    }

    public async Task<string> BuildWeeklyGoalsReportAsync(DateTime anyDayInWeek)
    {
        var from = GetWeekStart(anyDayInWeek);
        var to = from.AddDays(6);
        var stats = await _planner.GetGoalStatsForRangeAsync(from, to);
        var sb = new StringBuilder();
        sb.AppendLine($"Отчет по целям за неделю ({from:dd.MM} - {to:dd.MM})");
        sb.AppendLine($"- Дней с отметками целей: {stats.DaysWithCompletions}");
        sb.AppendLine($"- Отметок выполнения целей всего: {stats.TotalCompletions}");
        return sb.ToString().Trim();
    }

    public async Task<string> BuildMonthlyGoalsReportAsync(int year, int month)
    {
        var from = new DateTime(year, month, 1);
        var toExclusive = from.AddMonths(1);
        var goalStats = await _planner.GetGoalStatsForRangeAsync(from, toExclusive.AddDays(-1));
        using var goalStatus = new GoalStatusService();
        var periodGoals = await goalStatus.GetPeriodGoalStatusesForMonthAsync(year, month);
        var recurringGoals = await goalStatus.GetRecurringGoalStatusesForMonthAsync(year, month);
        var completedPeriodGoals = periodGoals.Count(x => x.IsComplete);
        var recurringDone = recurringGoals.Sum(x => x.Completions);
        var recurringDue = recurringGoals.Sum(x => x.DueDays);

        var sb = new StringBuilder();
        sb.AppendLine($"Отчет по целям за месяц ({month:00}.{year})");
        sb.AppendLine($"- Периодные цели: {completedPeriodGoals}/{periodGoals.Count} выполнено");
        sb.AppendLine($"- Отметок выполнения целей всего: {goalStats.TotalCompletions}");
        sb.AppendLine($"- Дней с отметками целей: {goalStats.DaysWithCompletions}");
        if (recurringDue > 0 || recurringDone > 0)
            sb.AppendLine($"- Регулярные цели: {recurringDone}/{recurringDue} отметок");

        if (periodGoals.Count > 0)
        {
            sb.AppendLine("- Периодные цели:");
            foreach (var item in periodGoals)
            {
                var mark = item.IsComplete ? "выполнено" : "не выполнено";
                sb.AppendLine($"  - {item.Goal.Title} ({item.ScopeText}): {item.Current}/{item.Target}, {mark}");
            }
        }

        if (recurringGoals.Count > 0)
        {
            sb.AppendLine("- Регулярные цели:");
            foreach (var item in recurringGoals.Take(12))
                sb.AppendLine($"  - {item.Goal.Title}: {item.Completions}/{item.DueDays} отметок");
        }

        return sb.ToString().Trim();
    }

    public async Task<string> BuildMonthlyRemindersReportAsync(int year, int month)
    {
        var reminders = await _planner.GetRemindersMonthlyStatsAsync(year, month);
        var totalSlots = reminders.Sum(x => x.Total);
        var completedSlots = reminders.Sum(x => x.Completed);
        var sb = new StringBuilder();
        sb.AppendLine($"Отчет по напоминаниям за месяц ({month:00}.{year})");
        sb.AppendLine($"- Активных напоминаний: {reminders.Count}");
        sb.AppendLine($"- Выполнено слотов: {completedSlots}/{totalSlots}");

        if (reminders.Count == 0)
        {
            sb.AppendLine("- Активных напоминаний за этот месяц нет.");
            return sb.ToString().Trim();
        }

        sb.AppendLine("- Напоминания:");
        foreach (var item in reminders.OrderBy(x => x.Total == 0 ? 1.0 : (double)x.Completed / x.Total))
        {
            var ratio = item.Total == 0 ? 0 : (double)item.Completed / item.Total * 100;
            sb.AppendLine($"  - {item.Title}: {item.Completed}/{item.Total} ({ratio:N0}%)");
        }

        return sb.ToString().Trim();
    }

    public async Task<string> BuildMonthlyReportAsync(int year, int month)
    {
        var from = new DateTime(year, month, 1);
        var toExclusive = from.AddMonths(1);
        var transactions = await _planner.GetTransactionsAsync(from, toExclusive);
        var goalStats = await _planner.GetGoalStatsForRangeAsync(from, toExclusive.AddDays(-1));
        using var goalStatus = new GoalStatusService();
        var periodGoals = await goalStatus.GetPeriodGoalStatusesForMonthAsync(year, month);
        var recurringGoals = await goalStatus.GetRecurringGoalStatusesForMonthAsync(year, month);
        var completedPeriodGoals = periodGoals.Count(x => x.IsComplete);
        var recurringDone = recurringGoals.Sum(x => x.Completions);
        var recurringDue = recurringGoals.Sum(x => x.DueDays);
        var reminderDone = await _planner.GetReminderCompletionsCountForMonthAsync(year, month);
        var reminders = await _planner.GetRemindersMonthlyStatsAsync(year, month);
        var note = await _planner.GetPeriodNoteTextAsync(NotePeriodKind.Month, from) ?? "";
        var sb = new StringBuilder();
        sb.AppendLine($"Отчет за месяц ({month:00}.{year})");
        sb.AppendLine($"- Периодные цели: {completedPeriodGoals}/{periodGoals.Count} выполнено");
        sb.AppendLine($"- Отметок выполнения целей всего: {goalStats.TotalCompletions}");
        sb.AppendLine($"- Дней с отметками целей: {goalStats.DaysWithCompletions}");
        if (recurringDue > 0 || recurringDone > 0)
            sb.AppendLine($"- Регулярные цели: {recurringDone}/{recurringDue} отметок");
        if (periodGoals.Count > 0)
        {
            sb.AppendLine("- Периодные цели:");
            foreach (var item in periodGoals)
            {
                var mark = item.IsComplete ? "выполнено" : "не выполнено";
                sb.AppendLine($"  - {item.Goal.Title} ({item.ScopeText}): {item.Current}/{item.Target}, {mark}");
            }
        }
        if (recurringGoals.Count > 0)
        {
            sb.AppendLine("- Регулярные цели:");
            foreach (var item in recurringGoals.Take(8))
                sb.AppendLine($"  - {item.Goal.Title}: {item.Completions}/{item.DueDays} отметок");
        }
        if (!string.IsNullOrWhiteSpace(note))
            sb.AppendLine($"- Заметка месяца: {note.Trim()}");
        AppendFinanceSummary(sb, transactions);
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
        var weekStart = GetWeekStart(day);
        var monthStart = new DateTime(day.Year, day.Month, 1);
        await _repo.SaveReportAsync(AssistantReportPeriodKind.Day, day, await BuildDailyReportAsync(day));
        await _repo.SaveReportAsync(AssistantReportPeriodKind.Week, weekStart, await BuildWeeklyReportAsync(day));
        await _repo.SaveReportAsync(AssistantReportPeriodKind.Month, monthStart, await BuildMonthlyReportAsync(day.Year, day.Month));
    }

    private static DateTime GetWeekStart(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-diff);
    }

    private async Task<string> BuildFinanceReportForRangeAsync(
        string title,
        DateTime from,
        DateTime toExclusive,
        bool includeSavingsSnapshot,
        string? targetCurrency)
    {
        var transactions = await _planner.GetTransactionsAsync(from, toExclusive);
        var reportCurrency = NormalizeReportCurrency(targetCurrency);
        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine($"- Валюта отчета: {reportCurrency}");
        sb.AppendLine($"- Операций: {transactions.Count}");

        if (transactions.Count == 0)
        {
            sb.AppendLine($"- Доход: {FormatMoney(0, reportCurrency)}");
            sb.AppendLine($"- Расход: {FormatMoney(0, reportCurrency)}");
            sb.AppendLine($"- Маржа: {FormatMoney(0, reportCurrency)}");
            await AppendSavingsSnapshotAsync(sb, includeSavingsSnapshot);
            return sb.ToString().Trim();
        }

        var converted = await ConvertTransactionsAsync(transactions, reportCurrency);
        if (converted == null)
        {
            sb.AppendLine($"- Не удалось привести операции к {reportCurrency}: курс недоступен. Показываю суммы по валютам отдельно.");
            AppendUnconvertedFinanceBreakdown(sb, transactions);
            AppendCategoryBlock(sb, "Доходы по категориям", transactions, TransactionType.Income);
            AppendCategoryBlock(sb, "Расходы по категориям", transactions, TransactionType.Expense);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(converted.RatesDate))
                sb.AppendLine($"- Курс конвертации: НБУ на {converted.RatesDate}");

            var income = converted.Transactions.Where(x => x.Source.Category.Type == TransactionType.Income).Sum(x => x.Amount);
            var expense = converted.Transactions.Where(x => x.Source.Category.Type == TransactionType.Expense).Sum(x => x.Amount);
            sb.AppendLine($"- Доход: {FormatMoney(income, reportCurrency)}");
            sb.AppendLine($"- Расход: {FormatMoney(expense, reportCurrency)}");
            sb.AppendLine($"- Маржа: {FormatMoney(income - expense, reportCurrency)}");

            AppendConvertedCategoryBlock(sb, "Доходы по категориям", converted.Transactions, TransactionType.Income, reportCurrency);
            AppendConvertedCategoryBlock(sb, "Расходы по категориям", converted.Transactions, TransactionType.Expense, reportCurrency);

            var largestExpenses = converted.Transactions
                .Where(x => x.Source.Category.Type == TransactionType.Expense)
                .OrderByDescending(x => x.Amount)
                .Take(5)
                .ToList();
            if (largestExpenses.Count > 0)
            {
                sb.AppendLine("- Крупные расходы:");
                foreach (var tx in largestExpenses)
                    sb.AppendLine($"  - {tx.Source.Date:dd.MM}: {tx.Source.Category.Name}, {FormatMoney(tx.Amount, reportCurrency)}{FormatOriginalAmount(tx.Source, reportCurrency)}{FormatNote(tx.Source.Note)}");
            }
        }

        await AppendSavingsSnapshotAsync(sb, includeSavingsSnapshot);
        return sb.ToString().Trim();
    }

    private async Task<ConvertedFinanceSnapshot?> ConvertTransactionsAsync(
        IReadOnlyList<Transaction> transactions,
        string targetCurrency)
    {
        var needsRates = transactions.Any(x => CurrencyKey(x.Currency) != targetCurrency);
        DailyRates? rates = null;
        if (needsRates)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            rates = await _exchange.GetDailyRatesAsync(cts.Token);
            if (rates == null)
                return null;
        }

        var rows = new List<ConvertedFinanceTransaction>(transactions.Count);
        foreach (var tx in transactions)
        {
            var sourceCurrency = CurrencyKey(tx.Currency);
            var amount = sourceCurrency == targetCurrency
                ? tx.Amount
                : ExchangeRateService.ConvertWithRates(tx.Amount, sourceCurrency, targetCurrency, rates!);
            if (!amount.HasValue)
                return null;

            rows.Add(new ConvertedFinanceTransaction(tx, amount.Value));
        }

        return new ConvertedFinanceSnapshot(rows, rates?.Date);
    }

    private static void AppendUnconvertedFinanceBreakdown(StringBuilder sb, IReadOnlyList<Transaction> transactions)
    {
        var byCurrency = transactions
            .GroupBy(x => CurrencyKey(x.Currency))
            .OrderBy(g => g.Key)
            .ToList();

        if (byCurrency.Count == 1)
        {
            var currency = byCurrency[0].Key;
            var income = byCurrency[0].Where(x => x.Category.Type == TransactionType.Income).Sum(x => x.Amount);
            var expense = byCurrency[0].Where(x => x.Category.Type == TransactionType.Expense).Sum(x => x.Amount);
            sb.AppendLine($"- Доход: {FormatMoney(income, currency)}");
            sb.AppendLine($"- Расход: {FormatMoney(expense, currency)}");
            sb.AppendLine($"- Маржа: {FormatMoney(income - expense, currency)}");
            return;
        }

        sb.AppendLine("- Валют несколько, суммы показаны отдельно:");
        foreach (var group in byCurrency)
        {
            var income = group.Where(x => x.Category.Type == TransactionType.Income).Sum(x => x.Amount);
            var expense = group.Where(x => x.Category.Type == TransactionType.Expense).Sum(x => x.Amount);
            sb.AppendLine($"  - {group.Key}: доход {income:N2}, расход {expense:N2}, маржа {income - expense:N2}");
        }
        sb.AppendLine("- Итоговая маржа: не считается без конвертации валют");
    }

    private async Task AppendSavingsSnapshotAsync(StringBuilder sb, bool includeSavingsSnapshot)
    {
        if (!includeSavingsSnapshot)
            return;

        var savings = await _planner.GetSavingsEntriesAsync();
        if (savings.Count == 0)
            return;

        sb.AppendLine("- Балансы счетов сейчас:");
        foreach (var group in savings.GroupBy(x => CurrencyKey(x.Currency)).OrderBy(x => x.Key))
            sb.AppendLine($"  - {group.Key}: {group.Sum(x => x.Balance):N2}");
    }

    private static void AppendCategoryBlock(
        StringBuilder sb,
        string title,
        IReadOnlyList<Transaction> transactions,
        TransactionType type)
    {
        var rows = transactions
            .Where(x => x.Category.Type == type)
            .GroupBy(x => new
            {
                x.Category.Name,
                Currency = CurrencyKey(x.Currency)
            })
            .Select(g => new { g.Key.Name, g.Key.Currency, Sum = g.Sum(x => x.Amount) })
            .OrderByDescending(x => x.Sum)
            .Take(8)
            .ToList();

        if (rows.Count == 0)
            return;

        sb.AppendLine($"- {title}:");
        foreach (var row in rows)
            sb.AppendLine($"  - {row.Name}: {FormatMoney(row.Sum, row.Currency)}");
    }

    private static void AppendConvertedCategoryBlock(
        StringBuilder sb,
        string title,
        IReadOnlyList<ConvertedFinanceTransaction> transactions,
        TransactionType type,
        string currency)
    {
        var rows = transactions
            .Where(x => x.Source.Category.Type == type)
            .GroupBy(x => x.Source.Category.Name)
            .Select(g => new { Name = g.Key, Sum = g.Sum(x => x.Amount) })
            .OrderByDescending(x => x.Sum)
            .Take(8)
            .ToList();

        if (rows.Count == 0)
            return;

        sb.AppendLine($"- {title}:");
        foreach (var row in rows)
            sb.AppendLine($"  - {row.Name}: {FormatMoney(row.Sum, currency)}");
    }

    private static string FormatMoney(decimal amount, string? currency)
    {
        var suffix = string.IsNullOrWhiteSpace(currency) ? "" : $" {currency.Trim().ToUpperInvariant()}";
        return $"{amount:N2}{suffix}";
    }

    private static void AppendFinanceSummary(StringBuilder sb, IReadOnlyList<Transaction> transactions)
    {
        if (transactions.Count == 0)
        {
            sb.AppendLine("- Финансы: операций нет");
            return;
        }

        var byCurrency = transactions
            .GroupBy(x => CurrencyKey(x.Currency))
            .OrderBy(x => x.Key)
            .ToList();

        if (byCurrency.Count == 1)
        {
            var currency = byCurrency[0].Key;
            var income = byCurrency[0].Where(x => x.Category.Type == TransactionType.Income).Sum(x => x.Amount);
            var expense = byCurrency[0].Where(x => x.Category.Type == TransactionType.Expense).Sum(x => x.Amount);
            sb.AppendLine($"- Доход: {FormatMoney(income, currency)}");
            sb.AppendLine($"- Расход: {FormatMoney(expense, currency)}");
            sb.AppendLine($"- Маржа: {FormatMoney(income - expense, currency)}");
            return;
        }

        sb.AppendLine("- Финансы по валютам:");
        foreach (var group in byCurrency)
        {
            var income = group.Where(x => x.Category.Type == TransactionType.Income).Sum(x => x.Amount);
            var expense = group.Where(x => x.Category.Type == TransactionType.Expense).Sum(x => x.Amount);
            sb.AppendLine($"  - {group.Key}: доход {income:N2}, расход {expense:N2}, маржа {income - expense:N2}");
        }
        sb.AppendLine("- Итоговая маржа: не считается без конвертации валют");
    }

    private static string CurrencyKey(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency) ? CurrencyInfo.UAH : currency.Trim().ToUpperInvariant();
    }

    private static string NormalizeReportCurrency(string? currency)
    {
        var value = CurrencyKey(currency);
        return CurrencyInfo.DisplayCurrencies.Contains(value, StringComparer.OrdinalIgnoreCase)
            ? value
            : CurrencyInfo.UAH;
    }

    private static string FormatOriginalAmount(Transaction tx, string reportCurrency)
    {
        var sourceCurrency = CurrencyKey(tx.Currency);
        return sourceCurrency == reportCurrency ? "" : $" (исходно {FormatMoney(tx.Amount, sourceCurrency)})";
    }

    private static string FormatNote(string? note)
    {
        return string.IsNullOrWhiteSpace(note) ? "" : $" ({note.Trim()})";
    }

    public void Dispose()
    {
        _planner.Dispose();
    }

    private sealed record ConvertedFinanceSnapshot(
        IReadOnlyList<ConvertedFinanceTransaction> Transactions,
        string? RatesDate);

    private sealed record ConvertedFinanceTransaction(Transaction Source, decimal Amount);
}
