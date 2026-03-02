using System.Windows;
using Planner.App.Models;

namespace Planner.App.Views;

public partial class ReminderPopupWindow : Window
{
    private readonly int _reminderId;
    private readonly DateTime _slotDateTime;
    private readonly Action<int, DateTime, bool>? _onClosed;

    public ReminderPopupWindow(Reminder reminder, DateTime slotDateTime, Action<int, DateTime, bool>? onClosed = null)
    {
        InitializeComponent();
        _reminderId = reminder.Id;
        _slotDateTime = slotDateTime;
        _onClosed = onClosed;
        TitleText.Text = reminder.Title;
        TimeText.Text = $"Время: {slotDateTime:HH:mm} · каждые {reminder.IntervalMinutes} мин";
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        _onClosed?.Invoke(_reminderId, _slotDateTime, true);
        DialogResult = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DialogResult != true)
            _onClosed?.Invoke(_reminderId, _slotDateTime, false);
        base.OnClosed(e);
    }
}
