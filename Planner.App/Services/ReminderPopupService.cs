using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Planner.App.Models;
using Planner.App.Views;

namespace Planner.App.Services;

public class ReminderPopupService
{
    private readonly Dispatcher _dispatcher;
    private readonly HashSet<(int ReminderId, DateTime Slot)> _shownSlots = new();
    private DispatcherTimer? _timer;

    public ReminderPopupService(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public void Start()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        List<(Reminder Reminder, DateTime Slot)>? dueSlots = null;
        try
        {
            dueSlots = await Task.Run(async () =>
            {
                var svc = new PlannerService();
                var list = await svc.GetDueReminderSlotsAsync(DateTime.Now);
                return list;
            });
        }
        catch
        {
            return;
        }

        var today = DateTime.Today;
        lock (_shownSlots)
        {
            var toRemove = _shownSlots.Where(s => s.Slot.Date < today).ToList();
            foreach (var x in toRemove)
                _shownSlots.Remove(x);
        }

        if (dueSlots.Count == 0) return;

        _dispatcher.Invoke(() =>
        {
            foreach (var (reminder, slot) in dueSlots)
            {
                lock (_shownSlots)
                {
                    if (_shownSlots.Contains((reminder.Id, slot)))
                        continue;
                    _shownSlots.Add((reminder.Id, slot));
                }

                var wnd = new ReminderPopupWindow(reminder, slot, (reminderId, slotDt, completed) =>
                {
                    if (completed)
                    {
                        _ = Task.Run(async () =>
                        {
                            var s = new PlannerService();
                            await s.SetReminderSlotCompletedAsync(reminderId, slotDt, true);
                        });
                    }
                });
                wnd.ShowDialog();
            }
        });
    }
}
