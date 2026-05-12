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
    private bool _isTickRunning;

    public ReminderPopupService(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public void Start()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Stop()
    {
        if (_timer != null)
        {
            _timer.Tick -= OnTick;
            _timer.Stop();
            _timer = null;
        }
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_isTickRunning) return;
        _isTickRunning = true;
        try
        {
            var dueSlots = await Task.Run(async () =>
            {
                using var svc = new PlannerService();
                var list = await svc.GetDueReminderSlotsAsync(DateTime.Now);
                return list;
            });

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
                // Only show popups if the main window is visible to avoid performance issues
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow == null || !mainWindow.IsVisible) return;

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
                            using var s = new PlannerService();
                            var changed = await s.SetReminderSlotCompletedAsync(reminderId, slotDt, true);
                            if (changed)
                                ReminderCompletionNotificationService.Publish(reminderId, slotDt, true, 1);
                        });
                    }
                });
                    wnd.ShowDialog();
                }
            });
        }
        catch
        {
        }
        finally
        {
            _isTickRunning = false;
        }
    }
}
