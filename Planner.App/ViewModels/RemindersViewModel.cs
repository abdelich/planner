using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Planner.App.Models;
using Planner.App.Services;

namespace Planner.App.ViewModels;

public partial class RemindersViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<ReminderItemViewModel> _reminders = new();
    [ObservableProperty] private bool _showEmptyMessage = true;
    [ObservableProperty] private bool _isLoadingReminders;
    [ObservableProperty] private string _loadError = string.Empty;
    [ObservableProperty] private string _newReminderTitle = string.Empty;
    [ObservableProperty] private string _newReminderIntervalMinutes = "60";
    [ObservableProperty] private bool _isAddPanelOpen;
    [ObservableProperty] private bool _isEditPanelOpen;
    [ObservableProperty] private string _editReminderTitle = string.Empty;
    [ObservableProperty] private string _editReminderIntervalMinutes = "60";
    [ObservableProperty] private int _selectedYear;
    [ObservableProperty] private int _selectedMonth;

    private ReminderItemViewModel? _editReminderItem;
    private bool _isLoading;
    private Dispatcher? _dispatcher;

    public void SetDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public RemindersViewModel()
    {
        var now = DateTime.Now;
        _selectedYear = now.Year;
        _selectedMonth = now.Month;
        Reminders.CollectionChanged += (_, _) => ShowEmptyMessage = Reminders.Count == 0;
    }

    public void StartLoad()
    {
        LoadError = string.Empty;
        if (_isLoading) return;
        _isLoading = true;
        IsLoadingReminders = true;
        var year = SelectedYear;
        var month = SelectedMonth;
        var vm = this;
        var dispatcher = _dispatcher ?? System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _ = Task.Run(async () =>
        {
            try
            {
                var minimalItems = await vm.BuildMinimalItemsInBackground();
                dispatcher.Invoke(() =>
                {
                    try
                    {
                        vm.Reminders.Clear();
                        foreach (var item in minimalItems)
                            vm.Reminders.Add(item);
                        vm.ShowEmptyMessage = vm.Reminders.Count == 0;
                        vm.LoadError = string.Empty;
                    }
                    finally
                    {
                        vm._isLoading = false;
                        vm.IsLoadingReminders = false;
                    }
                });
                try
                {
                    var fullItems = await vm.BuildItemsInBackground(year, month);
                    dispatcher.Invoke(() =>
                    {
                        vm.Reminders.Clear();
                        foreach (var item in fullItems)
                            vm.Reminders.Add(item);
                        vm.ShowEmptyMessage = vm.Reminders.Count == 0;
                    });
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                dispatcher.Invoke(() =>
                {
                    try
                    {
                        vm.LoadError = "Ошибка загрузки: " + ex.Message;
                    }
                    finally
                    {
                        vm._isLoading = false;
                        vm.IsLoadingReminders = false;
                    }
                });
            }
        });
    }

    public void ReloadInBackground()
    {
        StartLoad();
    }

    private const int MaxSlotsPerDay = 48;

    private async Task<List<ReminderItemViewModel>> BuildMinimalItemsInBackground()
    {
        var svc = new PlannerService();
        await svc.EnsureDbAsync();
        var list = await svc.GetAllRemindersAsync();
        var result = new List<ReminderItemViewModel>();
        foreach (var r in list)
            result.Add(new ReminderItemViewModel(r, 0, 0, new List<ReminderSlotViewModel>()));
        return result;
    }

    private async Task<List<ReminderItemViewModel>> BuildItemsInBackground(int year, int month)
    {
        var svc = new PlannerService();
        await svc.EnsureDbAsync();
        var list = await svc.GetAllRemindersAsync();
        var result = new List<ReminderItemViewModel>();
        foreach (var r in list)
        {
            try
            {
                var (completed, total) = await svc.GetReminderMonthlyProgressAsync(r.Id, year, month);
                var daySlots = BuildTodaySlots(r, await svc.GetReminderCompletionsForDayAsync(r.Id, DateTime.Today));
                result.Add(new ReminderItemViewModel(r, completed, total, daySlots));
            }
            catch
            {
                result.Add(new ReminderItemViewModel(r, 0, 0, new List<ReminderSlotViewModel>()));
            }
        }
        return result;
    }

    private List<ReminderSlotViewModel> BuildTodaySlots(Reminder r, List<ReminderCompletion> completions)
    {
        var interval = r.IntervalMinutes < 1 ? 60 : r.IntervalMinutes;
        var today = DateTime.Today;
        var from = r.ActiveFrom ?? new TimeOnly(0, 0);
        var to = r.ActiveTo ?? new TimeOnly(23, 59);
        var completedSet = new HashSet<DateTime>();
        foreach (var c in completions)
            if (c.Completed)
                completedSet.Add(c.SlotDateTime);

        var slotTimes = new List<DateTime>();
        var t = from;
        while (t <= to)
        {
            slotTimes.Add(new DateTime(today.Year, today.Month, today.Day, t.Hour, t.Minute, 0));
            t = t.Add(TimeSpan.FromMinutes(interval));
        }

        if (slotTimes.Count > MaxSlotsPerDay)
        {
            var step = slotTimes.Count / MaxSlotsPerDay;
            var sampled = new List<DateTime>();
            for (var i = 0; i < slotTimes.Count && sampled.Count < MaxSlotsPerDay; i += step)
                sampled.Add(slotTimes[i]);
            slotTimes = sampled;
        }

        var slots = new List<ReminderSlotViewModel>();
        foreach (var slotDt in slotTimes)
            slots.Add(new ReminderSlotViewModel(r.Id, slotDt, completedSet.Contains(slotDt), this));
        return slots;
    }

    internal void MarkSlotCompleted(int reminderId, DateTime slotDateTime, bool completed)
    {
        Task.Run(async () =>
        {
            var svc = new PlannerService();
            await svc.SetReminderSlotCompletedAsync(reminderId, slotDateTime, completed);
        });
    }

    [RelayCommand]
    private void OpenAddPanel() => IsAddPanelOpen = true;

    [RelayCommand]
    private void CloseAddPanel() => IsAddPanelOpen = false;

    [RelayCommand]
    private void OpenEditPanel(ReminderItemViewModel? item)
    {
        if (item == null) return;
        _editReminderItem = item;
        EditReminderTitle = item.Reminder.Title;
        EditReminderIntervalMinutes = item.Reminder.IntervalMinutes.ToString();
        IsEditPanelOpen = true;
    }

    [RelayCommand]
    private void CloseEditPanel()
    {
        _editReminderItem = null;
        IsEditPanelOpen = false;
    }

    [RelayCommand]
    private void SaveEditReminder()
    {
        if (_editReminderItem == null) return;
        if (string.IsNullOrWhiteSpace(EditReminderTitle?.Trim())) return;
        if (!int.TryParse(EditReminderIntervalMinutes?.Trim(), out var interval) || interval < 1)
            interval = 60;
        if (interval > 60 * 24 * 7)
            interval = 60 * 24 * 7;

        var r = _editReminderItem.Reminder;
        r.Title = EditReminderTitle.Trim();
        r.IntervalMinutes = interval;
        var id = r.Id;
        var title = r.Title;
        var mins = r.IntervalMinutes;

        CloseEditPanel();

        Task.Run(async () =>
        {
            var svc = new PlannerService();
            var entity = await svc.GetReminderByIdAsync(id);
            if (entity != null)
            {
                entity.Title = title;
                entity.IntervalMinutes = mins;
                await svc.UpdateReminderAsync(entity);
            }
        });
        ReloadInBackground();
    }

    [RelayCommand]
    private void SaveNewReminder()
    {
        if (string.IsNullOrWhiteSpace(NewReminderTitle?.Trim()))
            return;

        if (!int.TryParse(NewReminderIntervalMinutes?.Trim(), out var interval) || interval < 1)
        {
            NewReminderIntervalMinutes = "60";
            return;
        }
        if (interval > 60 * 24 * 7)
        {
            NewReminderIntervalMinutes = "10080";
            return;
        }

        var title = NewReminderTitle.Trim();
        var mins = interval;

        NewReminderTitle = string.Empty;
        NewReminderIntervalMinutes = "60";
        IsAddPanelOpen = false;

        var dispatcher = _dispatcher ?? System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        var vm = this;

        Task.Run(async () =>
        {
            try
            {
                var svc = new PlannerService();
                var reminder = new Reminder
                {
                    Title = title,
                    IntervalMinutes = mins,
                    IsEnabled = true
                };
                await svc.AddReminderAsync(reminder);
                dispatcher.Invoke(() => vm.StartLoad());
            }
            catch (Exception ex)
            {
                dispatcher.Invoke(() => vm.LoadError = "Ошибка после добавления: " + ex.Message);
            }
        });
    }

    [RelayCommand]
    private void DeleteReminder(ReminderItemViewModel item)
    {
        var reminderId = item.Reminder.Id;
        Reminders.Remove(item);
        Task.Run(async () =>
        {
            var svc = new PlannerService();
            await svc.DeleteReminderByIdAsync(reminderId);
        });
    }

    [RelayCommand]
    private void RefreshList() => StartLoad();

    partial void OnSelectedYearChanged(int value) => StartLoad();
    partial void OnSelectedMonthChanged(int value) => StartLoad();
}
