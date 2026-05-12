using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Models;
using Planner.App.Services;

namespace Planner.App.ViewModels;

public partial class GoalsViewModel : ObservableObject
{
    private readonly PlannerService _service = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayPeriodText))]
    [NotifyPropertyChangedFor(nameof(ShowPeriodTypeAsLabel))]
    [NotifyPropertyChangedFor(nameof(ShowPeriodTypeComboBox))]
    [NotifyPropertyChangedFor(nameof(ShowPeriodNotes))]
    private int _selectedTabIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayPeriodText))]
    [NotifyPropertyChangedFor(nameof(PeriodNoteTitle))]
    private int _selectedPeriodSubTabIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayPeriodText))]
    private DateTime _selectedPeriodDate = DateTime.Today;

    [ObservableProperty] private ObservableCollection<GoalItemViewModel> _dayGoals = new();
    [ObservableProperty] private ObservableCollection<GoalItemViewModel> _weekGoals = new();
    [ObservableProperty] private ObservableCollection<GoalItemViewModel> _monthGoals = new();
    [ObservableProperty] private ObservableCollection<GoalItemViewModel> _recurringGoals = new();
    [ObservableProperty] private ObservableCollection<GoalItemViewModel> _todayDue = new();

    [ObservableProperty] private string _periodNoteText = "";

    public bool ShowPeriodNotes => SelectedTabIndex == 0;

    public string PeriodNoteTitle => SelectedPeriodSubTabIndex switch
    {
        0 => "Заметка за этот день",
        1 => "Заметка за эту неделю",
        2 => "Заметка за этот месяц",
        _ => "Заметка за этот день"
    };

    private static readonly System.Globalization.CultureInfo Ru = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");

    public string DisplayPeriodText => SelectedPeriodSubTabIndex switch
    {
        0 => GetDayHeaderText(SelectedPeriodDate),
        1 => $"{GetWeekStart(SelectedPeriodDate):d MMM} – {GetWeekEnd(SelectedPeriodDate):d MMM yyyy}",
        2 => SelectedPeriodDate.ToString("MMMM yyyy", Ru),
        _ => GetDayHeaderText(SelectedPeriodDate)
    };

    private static string GetDayHeaderText(DateTime d)
    {
        d = d.Date;
        var today = DateTime.Today;
        var dateStr = d.ToString("d MMM yyyy", Ru);
        if (d == today)
            return "Сегодня, " + dateStr;
        var dayNames = new[] { "воскресенье", "понедельник", "вторник", "среда", "четверг", "пятница", "суббота" };
        var dayName = dayNames[(int)d.DayOfWeek];
        return char.ToUpper(dayName[0], Ru) + dayName[1..] + ", " + dateStr;
    }

    public bool ShowPeriodTypeAsLabel => SelectedTabIndex == 0 && NewGoalCategory == GoalCategory.Period;
    public bool ShowPeriodTypeComboBox => SelectedTabIndex == 1 && NewGoalCategory == GoalCategory.Period;
    public string PeriodTypeLabel => NewGoalType switch { GoalType.Daily => "Цель на день", GoalType.Weekly => "Цель на неделю", GoalType.Monthly => "Цель на месяц", _ => "Период" };

    [ObservableProperty] private bool _isAddPanelOpen;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPeriodTypeAsLabel))]
    [NotifyPropertyChangedFor(nameof(ShowPeriodTypeComboBox))]
    private GoalCategory _newGoalCategory = GoalCategory.Period;
    [ObservableProperty] private string _newGoalTitle = string.Empty;
    [ObservableProperty] private string _newGoalDescription = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeriodTypeLabel))]
    private GoalType _newGoalType = GoalType.Daily;
    [ObservableProperty] private string _newGoalTargetCount = "1";
    [ObservableProperty] private RecurrenceKind _newGoalRecurrenceKind = RecurrenceKind.EveryDay;
    [ObservableProperty] private string _newGoalIntervalDays = "1";
    [ObservableProperty] private int _newGoalRecurrenceDays;
    [ObservableProperty] private bool _newGoalDaySun;
    [ObservableProperty] private bool _newGoalDayMon;
    [ObservableProperty] private bool _newGoalDayTue;
    [ObservableProperty] private bool _newGoalDayWed;
    [ObservableProperty] private bool _newGoalDayThu;
    [ObservableProperty] private bool _newGoalDayFri;
    [ObservableProperty] private bool _newGoalDaySat;

    public GoalsViewModel()
    {
        GoalCompletionNotificationService.CompletionChanged += OnGoalCompletionChanged;
        _ = LoadAsync();
    }

    private void OnGoalCompletionChanged(GoalCompletionChangedEvent change)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            _ = LoadAsync();
            return;
        }

        dispatcher.BeginInvoke(new Action(() => _ = LoadAsync()));
    }

    public async Task LoadAsync()
    {
        await LoadPeriodGoalsAsync();
        await LoadRecurringGoalsAsync();
        await LoadTodayDueAsync();
        await LoadPeriodNoteAsync();
    }

    private async Task LoadPeriodGoalsAsync()
    {
        var list = await _service.GetPeriodGoalsAsync();
        var dayItems = new List<GoalItemViewModel>();
        var weekItems = new List<GoalItemViewModel>();
        var monthItems = new List<GoalItemViewModel>();

        var today = DateTime.Today;
        var dayDate = SelectedPeriodDate.Date;
        var weekStart = GetWeekStart(SelectedPeriodDate);
        var weekEnd = GetWeekEnd(SelectedPeriodDate);
        var monthStart = new DateTime(SelectedPeriodDate.Year, SelectedPeriodDate.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        foreach (var g in list)
        {
            if (g.Type == GoalType.Daily)
            {
                if (!MatchesDailyPeriod(g, dayDate))
                    continue;
                var (current, target, label) = await GetPeriodProgressForDateAsync(g, dayDate, dayDate);
                var periodEnd = dayDate;
                var isPast = periodEnd < today;
                var periodDate = dayDate;
                var item = new GoalItemViewModel(g, (current, target, label), FrequencyText(g), _service, () => _ = LoadAsync())
                {
                    PeriodDate = periodDate,
                    IsPastPeriod = isPast
                };
                dayItems.Add(item);
            }
            else if (g.Type == GoalType.Weekly)
            {
                if (!MatchesWeeklyPeriod(g, weekStart))
                    continue;
                var (current, target, label) = await GetPeriodProgressForDateAsync(g, weekStart, weekEnd);
                var isPast = weekEnd < today;
                var periodDate = today >= weekStart && today <= weekEnd ? today : weekStart;
                var item = new GoalItemViewModel(g, (current, target, label), FrequencyText(g), _service, () => _ = LoadAsync())
                {
                    PeriodDate = periodDate,
                    IsPastPeriod = isPast
                };
                weekItems.Add(item);
            }
            else if (g.Type == GoalType.Monthly)
            {
                if (!MatchesMonthlyPeriod(g, monthStart))
                    continue;
                var (current, target, label) = await GetPeriodProgressForDateAsync(g, monthStart, monthEnd);
                var isPast = monthEnd < today;
                var periodDate = today >= monthStart && today <= monthEnd ? today : monthStart;
                var item = new GoalItemViewModel(g, (current, target, label), FrequencyText(g), _service, () => _ = LoadAsync())
                {
                    PeriodDate = periodDate,
                    IsPastPeriod = isPast
                };
                monthItems.Add(item);
            }
        }

        var recurringList = await _service.GetRecurringGoalsAsync();
        foreach (var g in recurringList.Where(g => PlannerService.IsRecurringGoalDueOn(g, dayDate)))
        {
            var progress = await GetRecurringProgressForDateAsync(g, dayDate);
            var isPast = dayDate < today;
            var item = new GoalItemViewModel(g, progress, FrequencyText(g), _service, () => _ = LoadAsync())
            {
                PeriodDate = dayDate,
                IsPastPeriod = isPast
            };
            dayItems.Add(item);
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            DayGoals.Clear();
            WeekGoals.Clear();
            MonthGoals.Clear();
            foreach (var item in dayItems) DayGoals.Add(item);
            foreach (var item in weekItems) WeekGoals.Add(item);
            foreach (var item in monthItems) MonthGoals.Add(item);
        });
    }

    private async Task<(int Current, int Target, string Label)> GetPeriodProgressForDateAsync(Goal g, DateTime from, DateTime to)
    {
        if (g.Category != GoalCategory.Period) return (0, 1, "");
        var target = g.TargetCount > 0 ? g.TargetCount : 1;
        var current = await _service.GetGoalCompletionCountAsync(g.Id, from, to);
        var label = g.Type switch { GoalType.Daily => "день", GoalType.Weekly => "неделя", GoalType.Monthly => "месяц", _ => "" };
        return (current, Math.Max(1, target), label);
    }

    private async Task LoadRecurringGoalsAsync()
    {
        var list = await _service.GetRecurringGoalsAsync();
        var items = new List<GoalItemViewModel>();
        foreach (var g in list)
        {
            var progress = await GetRecurringProgressAsync(g);
            items.Add(new GoalItemViewModel(g, progress, FrequencyText(g), _service, () => _ = LoadAsync()));
        }
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            RecurringGoals.Clear();
            foreach (var item in items)
                RecurringGoals.Add(item);
        });
    }

    private async Task LoadTodayDueAsync()
    {
        var today = DateTime.Today;
        var items = new List<GoalItemViewModel>();
        var periodList = await _service.GetPeriodGoalsAsync();
        foreach (var g in periodList.Where(g => g.Type == GoalType.Daily && MatchesDailyPeriod(g, today)))
        {
            var completed = await _service.IsGoalCompletedForDateAsync(g.Id, today);
            var progress = await GetPeriodProgressAsync(g);
            items.Add(new GoalItemViewModel(g, progress, FrequencyText(g), _service, () => _ = LoadAsync()) { IsCompletedToday = completed });
        }
        var recurringList = await _service.GetRecurringGoalsAsync();
        foreach (var g in recurringList.Where(g => PlannerService.IsRecurringGoalDueOn(g, today)))
        {
            var completed = await _service.IsGoalCompletedForDateAsync(g.Id, today);
            var progress = await GetRecurringProgressAsync(g);
            items.Add(new GoalItemViewModel(g, progress, FrequencyText(g), _service, () => _ = LoadAsync()) { IsCompletedToday = completed });
        }
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            TodayDue.Clear();
            foreach (var item in items)
                TodayDue.Add(item);
        });
    }

    private static string FrequencyText(Goal g)
    {
        if (g.Category == GoalCategory.Period)
            return g.Type switch { GoalType.Daily => "день", GoalType.Weekly => "неделя", GoalType.Monthly => "месяц", _ => "" };
        return g.RecurrenceKind switch
        {
            RecurrenceKind.EveryDay => "каждый день",
            RecurrenceKind.EveryNDays => g.IntervalDays > 0 ? $"каждые {g.IntervalDays} дн." : "каждые N дней",
            RecurrenceKind.SpecificDaysOfWeek => DaysOfWeekText(g.RecurrenceDays),
            _ => ""
        };
    }

    private static string DaysOfWeekText(int mask)
    {
        if (mask == 0) return "по дням недели";
        var list = new List<string>();
        for (var i = 1; i <= 7; i++)
        {
            var bitIndex = i % 7;
            if ((mask & (1 << bitIndex)) != 0)
                list.Add(bitIndex switch { 0 => "вс", 1 => "пн", 2 => "вт", 3 => "ср", 4 => "чт", 5 => "пт", 6 => "сб", _ => "" });
        }
        return string.Join(", ", list);
    }

    private async Task<(int Current, int Target, string Label)> GetPeriodProgressAsync(Goal g)
    {
        if (g.Category != GoalCategory.Period) return (0, 1, "");
        var today = DateTime.Today;
        var target = g.TargetCount > 0 ? g.TargetCount : 1;
        int current = g.Type switch
        {
            GoalType.Daily => await _service.GetGoalCompletionCountAsync(g.Id, today, today),
            GoalType.Weekly => await _service.GetGoalCompletionCountAsync(g.Id, GetWeekStart(today), GetWeekEnd(today)),
            GoalType.Monthly => await _service.GetGoalCompletionCountAsync(g.Id, new DateTime(today.Year, today.Month, 1), today),
            _ => 0
        };
        var label = g.Type switch { GoalType.Daily => "день", GoalType.Weekly => "неделя", GoalType.Monthly => "месяц", _ => "" };
        return (current, Math.Max(1, target), label);
    }

    private async Task<(int Current, int Target, string Label)> GetRecurringProgressAsync(Goal g)
    {
        var today = DateTime.Today;
        if (!PlannerService.IsRecurringGoalDueOn(g, today))
            return (0, 1, "след. раз: " + NextDueDayText(g));
        var completed = await _service.IsGoalCompletedForDateAsync(g.Id, today);
        return (completed ? 1 : 0, 1, "сегодня");
    }

    private static string NextDueDayText(Goal g)
    {
        if (g.RecurrenceKind != RecurrenceKind.SpecificDaysOfWeek || g.RecurrenceDays == 0)
            return "—";
        var days = new[] { "вс", "пн", "вт", "ср", "чт", "пт", "сб" };
        var today = (int)DateTime.Today.DayOfWeek;
        for (var i = 1; i <= 7; i++)
        {
            var dow = (today + i) % 7;
            if ((g.RecurrenceDays & (1 << dow)) != 0)
                return days[dow];
        }
        return "—";
    }

    private async Task<(int Current, int Target, string Label)> GetRecurringProgressForDateAsync(Goal g, DateTime date)
    {
        var completed = await _service.IsGoalCompletedForDateAsync(g.Id, date);
        var label = date.Date == DateTime.Today ? "сегодня" : "день";
        return (completed ? 1 : 0, 1, label);
    }

    private static DateTime GetWeekStart(DateTime d)
    {
        var diff = (7 + (d.DayOfWeek - DayOfWeek.Monday)) % 7;
        return d.AddDays(-diff).Date;
    }
    private static DateTime GetWeekEnd(DateTime d) => GetWeekStart(d).AddDays(6);

    private static DateTime PeriodAnchor(Goal g) => (g.StartDate ?? g.CreatedAt).Date;

    private static bool MatchesDailyPeriod(Goal g, DateTime dayDate) =>
        g.Category == GoalCategory.Period && g.Type == GoalType.Daily && PeriodAnchor(g) == dayDate.Date;

    private static bool MatchesWeeklyPeriod(Goal g, DateTime weekStart) =>
        g.Category == GoalCategory.Period && g.Type == GoalType.Weekly &&
        GetWeekStart(PeriodAnchor(g)) == weekStart.Date;

    private static bool MatchesMonthlyPeriod(Goal g, DateTime monthStart) =>
        g.Category == GoalCategory.Period && g.Type == GoalType.Monthly &&
        PeriodAnchor(g).Year == monthStart.Year && PeriodAnchor(g).Month == monthStart.Month;

    private (NotePeriodKind kind, DateTime periodStart) GetCurrentNotePeriod() =>
        SelectedPeriodSubTabIndex switch
        {
            0 => (NotePeriodKind.Day, SelectedPeriodDate.Date),
            1 => (NotePeriodKind.Week, GetWeekStart(SelectedPeriodDate)),
            2 => (NotePeriodKind.Month, new DateTime(SelectedPeriodDate.Year, SelectedPeriodDate.Month, 1)),
            _ => (NotePeriodKind.Day, SelectedPeriodDate.Date)
        };

    private async Task LoadPeriodNoteAsync()
    {
        if (SelectedTabIndex != 0)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => PeriodNoteText = "");
            return;
        }
        var (kind, start) = GetCurrentNotePeriod();
        var text = await _service.GetPeriodNoteTextAsync(kind, start) ?? "";
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => PeriodNoteText = text);
    }

    private async Task ReloadPeriodTabAsync()
    {
        await LoadPeriodGoalsAsync();
        await LoadPeriodNoteAsync();
    }

    partial void OnSelectedTabIndexChanged(int value) => _ = LoadAsync();
    partial void OnSelectedPeriodSubTabIndexChanged(int value) => _ = ReloadPeriodTabAsync();
    partial void OnSelectedPeriodDateChanged(DateTime value) => _ = ReloadPeriodTabAsync();

    [RelayCommand]
    private async Task ToggleTodayComplete(GoalItemViewModel item)
    {
        var completed = await _service.IsGoalCompletedForDateAsync(item.Goal.Id, DateTime.Today);
        if (completed)
            await _service.UnmarkGoalCompletionAsync(item.Goal.Id, DateTime.Today);
        else
            await _service.MarkGoalCompleteAsync(item.Goal.Id, DateTime.Today, 1);
        await LoadAsync();
    }

    [RelayCommand]
    private void PrevPeriod()
    {
        SelectedPeriodDate = SelectedPeriodSubTabIndex switch
        {
            0 => SelectedPeriodDate.AddDays(-1),
            1 => GetWeekStart(SelectedPeriodDate).AddDays(-7),
            2 => SelectedPeriodDate.AddMonths(-1),
            _ => SelectedPeriodDate.AddDays(-1)
        };
    }

    [RelayCommand]
    private void NextPeriod()
    {
        SelectedPeriodDate = SelectedPeriodSubTabIndex switch
        {
            0 => SelectedPeriodDate.AddDays(1),
            1 => GetWeekStart(SelectedPeriodDate).AddDays(7),
            2 => SelectedPeriodDate.AddMonths(1),
            _ => SelectedPeriodDate.AddDays(1)
        };
    }

    [RelayCommand]
    private void OpenAddPanel()
    {
        if (SelectedTabIndex == 0)
        {
            NewGoalCategory = GoalCategory.Period;
            NewGoalType = SelectedPeriodSubTabIndex switch { 0 => GoalType.Daily, 1 => GoalType.Weekly, 2 => GoalType.Monthly, _ => GoalType.Daily };
        }
        else
            NewGoalCategory = GoalCategory.Recurring;
        IsAddPanelOpen = true;
    }

    [RelayCommand]
    private void CloseAddPanel() => IsAddPanelOpen = false;

    [RelayCommand]
    private async Task SavePeriodNote()
    {
        if (SelectedTabIndex != 0) return;
        var (kind, start) = GetCurrentNotePeriod();
        await _service.SavePeriodNoteAsync(kind, start, PeriodNoteText ?? "");
    }

    [RelayCommand]
    private async Task SaveNewGoal()
    {
        if (string.IsNullOrWhiteSpace(NewGoalTitle?.Trim()))
            return;

        if (!int.TryParse(NewGoalTargetCount?.Trim(), out var targetCount) || targetCount < 1)
        {
            NewGoalTargetCount = "1";
            return;
        }
        if (targetCount > 1000)
        {
            NewGoalTargetCount = "1000";
            return;
        }

        int intervalDays = 1;
        if (NewGoalCategory == GoalCategory.Recurring && NewGoalRecurrenceKind == RecurrenceKind.EveryNDays)
        {
            if (!int.TryParse(NewGoalIntervalDays?.Trim(), out intervalDays) || intervalDays < 1)
            {
                NewGoalIntervalDays = "1";
                return;
            }
            if (intervalDays > 365)
            {
                NewGoalIntervalDays = "365";
                return;
            }
        }

        var recurrenceDays = 0;
        if (NewGoalCategory == GoalCategory.Recurring && NewGoalRecurrenceKind == RecurrenceKind.SpecificDaysOfWeek)
        {
            if (NewGoalDaySun) recurrenceDays |= 1;
            if (NewGoalDayMon) recurrenceDays |= 2;
            if (NewGoalDayTue) recurrenceDays |= 4;
            if (NewGoalDayWed) recurrenceDays |= 8;
            if (NewGoalDayThu) recurrenceDays |= 16;
            if (NewGoalDayFri) recurrenceDays |= 32;
            if (NewGoalDaySat) recurrenceDays |= 64;
            if (recurrenceDays == 0)
                return;
        }

        DateTime? periodStart = null;
        if (NewGoalCategory == GoalCategory.Period)
        {
            periodStart = NewGoalType switch
            {
                GoalType.Daily => SelectedPeriodDate.Date,
                GoalType.Weekly => GetWeekStart(SelectedPeriodDate),
                GoalType.Monthly => new DateTime(SelectedPeriodDate.Year, SelectedPeriodDate.Month, 1),
                _ => SelectedPeriodDate.Date
            };
        }

        var goal = new Goal
        {
            Category = NewGoalCategory,
            Title = NewGoalTitle.Trim(),
            Description = string.IsNullOrWhiteSpace(NewGoalDescription) ? null : NewGoalDescription.Trim(),
            Type = NewGoalType,
            RecurrenceKind = NewGoalRecurrenceKind,
            IntervalDays = intervalDays,
            RecurrenceDays = recurrenceDays,
            TargetCount = targetCount,
            StartDate = periodStart
        };
        try
        {
            await _service.AddGoalAsync(goal);
        }
        catch (Exception)
        {
            return;
        }
        ResetNewGoalForm();
        IsAddPanelOpen = false;
        try
        {
            await LoadAsync();
        }
        catch (Exception)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { });
        }
    }

    private void ResetNewGoalForm()
    {
        NewGoalTitle = string.Empty;
        NewGoalDescription = string.Empty;
        NewGoalTargetCount = "1";
        NewGoalIntervalDays = "1";
        NewGoalRecurrenceDays = 0;
        NewGoalDaySun = NewGoalDayMon = NewGoalDayTue = NewGoalDayWed = NewGoalDayThu = NewGoalDayFri = NewGoalDaySat = false;
    }

    [RelayCommand]
    private async Task DeleteGoal(GoalItemViewModel item)
    {
        var goalId = item.Goal.Id;
        var category = item.Goal.Category;
        var type = item.Goal.Type;
        await Task.Run(async () =>
        {
            using var service = new PlannerService();
            await service.DeleteGoalByIdAsync(goalId);
        });
        if (category == GoalCategory.Period)
        {
            if (type == GoalType.Daily) DayGoals.Remove(item);
            else if (type == GoalType.Weekly) WeekGoals.Remove(item);
            else if (type == GoalType.Monthly) MonthGoals.Remove(item);
        }
        else
        {
            DayGoals.Remove(item);
            var recurringItem = RecurringGoals.FirstOrDefault(x => x.Goal.Id == goalId);
            if (recurringItem != null) RecurringGoals.Remove(recurringItem);
        }
        var todayItem = TodayDue.FirstOrDefault(x => x.Goal.Id == goalId);
        if (todayItem != null) TodayDue.Remove(todayItem);
    }
}
