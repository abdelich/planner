using CommunityToolkit.Mvvm.ComponentModel;

namespace Planner.App.ViewModels;

public partial class ReminderSlotViewModel : ObservableObject
{
    private readonly RemindersViewModel _parent;
    private bool _suppressSave;

    public int ReminderId { get; }
    public DateTime SlotDateTime { get; }
    public string TimeText => SlotDateTime.ToString("HH:mm");

    [ObservableProperty] private bool _completed;

    public ReminderSlotViewModel(int reminderId, DateTime slotDateTime, bool completed, RemindersViewModel parent)
    {
        ReminderId = reminderId;
        _parent = parent;
        SlotDateTime = Services.ReminderCompletionNotificationService.NormalizeSlot(slotDateTime);
        _completed = completed;
    }

    partial void OnCompletedChanged(bool value)
    {
        if (!_suppressSave)
            _parent.MarkSlotCompleted(ReminderId, SlotDateTime, value);
    }

    internal void SetCompletedFromExternal(bool completed)
    {
        if (Completed == completed) return;
        _suppressSave = true;
        try
        {
            Completed = completed;
        }
        finally
        {
            _suppressSave = false;
        }
    }
}
