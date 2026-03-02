using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Planner.App.ViewModels;

public partial class ReminderSlotViewModel : ObservableObject
{
    private readonly RemindersViewModel _parent;

    public int ReminderId { get; }
    public DateTime SlotDateTime { get; }
    public string TimeText => SlotDateTime.ToString("HH:mm");

    [ObservableProperty] private bool _completed;

    public ReminderSlotViewModel(int reminderId, DateTime slotDateTime, bool completed, RemindersViewModel parent)
    {
        ReminderId = reminderId;
        SlotDateTime = slotDateTime;
        Completed = completed;
        _parent = parent;
    }

    [RelayCommand]
    private void Toggle()
    {
        Completed = !Completed;
        _parent.MarkSlotCompleted(ReminderId, SlotDateTime, Completed);
    }
}
