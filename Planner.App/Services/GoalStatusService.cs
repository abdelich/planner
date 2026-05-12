using Planner.App.Models;

namespace Planner.App.Services;

public sealed class GoalStatusService : IDisposable
{
    private readonly PlannerService _planner = new();

    public async Task<List<GoalPeriodStatus>> GetPeriodGoalStatusesForMonthAsync(int year, int month, bool includeArchived = true)
    {
        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var goals = await _planner.GetPeriodGoalsAsync(includeArchived);
        var result = new List<GoalPeriodStatus>();

        foreach (var goal in goals)
        {
            var anchor = PeriodAnchor(goal);
            DateTime periodStart;
            DateTime periodEnd;
            string scope;

            switch (goal.Type)
            {
                case GoalType.Weekly:
                    periodStart = GetWeekStart(anchor);
                    periodEnd = periodStart.AddDays(6);
                    scope = "week";
                    break;
                case GoalType.Monthly:
                    periodStart = new DateTime(anchor.Year, anchor.Month, 1);
                    periodEnd = periodStart.AddMonths(1).AddDays(-1);
                    scope = "month";
                    break;
                default:
                    periodStart = anchor.Date;
                    periodEnd = anchor.Date;
                    scope = "day";
                    break;
            }

            if (periodEnd < monthStart || periodStart > monthEnd)
                continue;

            var current = await _planner.GetGoalCompletionCountAsync(goal.Id, periodStart, periodEnd);
            result.Add(new GoalPeriodStatus(
                goal,
                scope,
                periodStart,
                periodEnd,
                current,
                Math.Max(1, goal.TargetCount)));
        }

        return result
            .OrderBy(x => x.PeriodStart)
            .ThenBy(x => x.ScopeOrder)
            .ThenBy(x => x.Goal.CreatedAt)
            .ToList();
    }

    public async Task<List<RecurringGoalMonthStatus>> GetRecurringGoalStatusesForMonthAsync(int year, int month, bool includeArchived = true)
    {
        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var goals = await _planner.GetRecurringGoalsAsync(includeArchived);
        var result = new List<RecurringGoalMonthStatus>();

        foreach (var goal in goals)
        {
            var dueDays = CountRecurringDueDays(goal, monthStart, monthEnd);
            var completions = await _planner.GetGoalCompletionCountAsync(goal.Id, monthStart, monthEnd);
            if (dueDays == 0 && completions == 0)
                continue;

            result.Add(new RecurringGoalMonthStatus(goal, dueDays, completions));
        }

        return result
            .OrderByDescending(x => x.Completions)
            .ThenBy(x => x.Goal.Title)
            .ToList();
    }

    private static int CountRecurringDueDays(Goal goal, DateTime from, DateTime to)
    {
        var count = 0;
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            if (PlannerService.IsRecurringGoalDueOn(goal, day))
                count++;
        }
        return count;
    }

    private static DateTime GetWeekStart(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-diff);
    }

    private static DateTime PeriodAnchor(Goal goal)
    {
        return (goal.StartDate ?? goal.CreatedAt).Date;
    }

    public void Dispose()
    {
        _planner.Dispose();
    }
}

public sealed record GoalPeriodStatus(
    Goal Goal,
    string Scope,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int Current,
    int Target)
{
    public bool IsComplete => Current >= Target;

    public int ScopeOrder => Scope switch
    {
        "day" => 0,
        "week" => 1,
        "month" => 2,
        _ => 3
    };

    public string ScopeText => Scope switch
    {
        "day" => "день",
        "week" => "неделя",
        "month" => "месяц",
        _ => Scope
    };
}

public sealed record RecurringGoalMonthStatus(
    Goal Goal,
    int DueDays,
    int Completions);
