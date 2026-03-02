using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Models;
using Planner.App.Services;

namespace Planner.App.ViewModels;

public partial class GoalItemViewModel : ObservableObject
{
    private readonly PlannerService _service;
    private readonly Action _onChanged;

    public Goal Goal { get; }
    public string FrequencyText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private (int Current, int Target, string Label) _progress;
    [ObservableProperty] private bool _isCompletedToday;

    public DateTime? PeriodDate { get; init; }
    public bool IsPastPeriod { get; init; }

    public double ProgressPercent => Progress.Target > 0 ? Math.Min(100, 100.0 * Progress.Current / Progress.Target) : 0;
    public string ProgressText => $"{Progress.Current} / {Progress.Target} ({Progress.Label})";

    public string StatusText => IsPastPeriod && Progress.Current < Progress.Target ? "Не выполнено" : "";

    public bool CanMarkComplete => !IsPastPeriod;

    public GoalItemViewModel(Goal goal, (int current, int target, string label) progress, string frequencyText, PlannerService service, Action onChanged)
    {
        Goal = goal;
        _progress = progress;
        FrequencyText = frequencyText ?? "";
        _service = service;
        _onChanged = onChanged;
    }

    private bool MarkCompleteCanExecute() => CanMarkComplete;

    [RelayCommand(CanExecute = nameof(MarkCompleteCanExecute))]
    private async Task MarkComplete()
    {
        var date = PeriodDate ?? DateTime.Today;
        var completed = await _service.IsGoalCompletedForDateAsync(Goal.Id, date);
        if (completed)
            await _service.UnmarkGoalCompletionAsync(Goal.Id, date);
        else
            await _service.MarkGoalCompleteAsync(Goal.Id, date, 1);
        _onChanged();
    }
}
