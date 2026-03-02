using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Services;

namespace Planner.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly PlannerService _service = new();

    [ObservableProperty] private int _goalsCompletedToday;
    [ObservableProperty] private int _goalsCompletedThisWeek;
    [ObservableProperty] private int _goalsCompletedThisMonth;
    [ObservableProperty] private int _remindersCompletedThisMonth;
    [ObservableProperty] private string _remindersMonthSummary = "";

    [ObservableProperty] private ObservableCollection<DayActivityItem> _lastDaysActivity = new();
    [ObservableProperty] private ObservableCollection<ReminderStatItem> _reminderStats = new();

    public DashboardViewModel()
    {
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var today = DateTime.Today;
        var weekStart = GetWeekStart(today);
        var weekEnd = weekStart.AddDays(6);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        goalsCompletedToday = await _service.GetGoalCompletionsCountForDateAsync(today);
        var (weekDays, weekTotal) = await _service.GetGoalStatsForRangeAsync(weekStart, weekEnd);
        var (monthDays, monthTotal) = await _service.GetGoalStatsForRangeAsync(monthStart, monthEnd);
        goalsCompletedThisWeek = weekTotal;
        goalsCompletedThisMonth = monthTotal;

        var year = today.Year;
        var month = today.Month;
        var remindersCount = await _service.GetReminderCompletionsCountForMonthAsync(year, month);
        var remStats = await _service.GetRemindersMonthlyStatsAsync(year, month);
        var totalSlots = remStats.Sum(x => x.Total);
        var remindersSummary = totalSlots > 0
            ? $"Напоминания: {remindersCount} из {totalSlots} слотов за месяц"
            : "Напоминания: нет данных за месяц";

        var byDay = await _service.GetGoalCompletionsByDayAsync(14);
        var maxCount = byDay.Count > 0 ? Math.Max(1, byDay.Max(x => x.Count)) : 1;
        var dayItems = byDay.Select(x => new DayActivityItem(x.Date, x.Count, maxCount)).ToList();
        var reminderItems = remStats.Select(x => new ReminderStatItem(x.Title, x.Completed, x.Total)).ToList();

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            GoalsCompletedToday = goalsCompletedToday;
            GoalsCompletedThisWeek = goalsCompletedThisWeek;
            GoalsCompletedThisMonth = goalsCompletedThisMonth;
            RemindersCompletedThisMonth = remindersCount;
            RemindersMonthSummary = remindersSummary;
            LastDaysActivity.Clear();
            foreach (var item in dayItems) LastDaysActivity.Add(item);
            ReminderStats.Clear();
            foreach (var item in reminderItems) ReminderStats.Add(item);
        });
    }

    private int goalsCompletedToday;
    private int goalsCompletedThisWeek;
    private int goalsCompletedThisMonth;

    private static DateTime GetWeekStart(DateTime d)
    {
        var diff = (7 + (d.DayOfWeek - DayOfWeek.Monday)) % 7;
        return d.AddDays(-diff).Date;
    }
}

public class DayActivityItem
{
    public DateTime Date { get; }
    public string DateText { get; }
    public int Count { get; }
    public double Percent { get; }

    public DayActivityItem(DateTime date, int count, int maxCountForScale = 1)
    {
        Date = date;
        Count = count;
        var max = Math.Max(1, maxCountForScale);
        Percent = Math.Min(100, 100.0 * count / max);
        var ru = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        DateText = date.Date == DateTime.Today ? "Сегодня" : date.ToString("ddd, d MMM", ru);
    }
}

public class ReminderStatItem
{
    public string Title { get; }
    public int Completed { get; }
    public int Total { get; }
    public string Summary => Total > 0 ? $"{Completed} из {Total}" : "—";
    public double Percent => Total > 0 ? Math.Min(100, 100.0 * Completed / Total) : 0;

    public ReminderStatItem(string title, int completed, int total)
    {
        Title = title;
        Completed = completed;
        Total = total;
    }
}
