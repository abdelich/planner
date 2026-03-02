using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Planner.App.Models;

namespace Planner.App.ViewModels;

public partial class ReminderItemViewModel : ObservableObject
{
    public Reminder Reminder { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthProgressPercent))]
    [NotifyPropertyChangedFor(nameof(MonthProgressText))]
    private int _monthCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthProgressPercent))]
    [NotifyPropertyChangedFor(nameof(MonthProgressText))]
    private int _monthTotal;

    [ObservableProperty] private ObservableCollection<ReminderSlotViewModel> _todaySlots = new();

    public double MonthProgressPercent => MonthTotal > 0 ? Math.Min(100, 100.0 * MonthCompleted / MonthTotal) : 0;
    public string MonthProgressText => $"{MonthCompleted} / {MonthTotal} за месяц";

    public ReminderItemViewModel(Reminder reminder, int monthCompleted, int monthTotal, List<ReminderSlotViewModel> todaySlots)
    {
        Reminder = reminder;
        _monthCompleted = monthCompleted;
        _monthTotal = monthTotal;
        TodaySlots = new ObservableCollection<ReminderSlotViewModel>(todaySlots);
    }
}
