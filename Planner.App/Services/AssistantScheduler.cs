using System.Windows.Threading;

namespace Planner.App.Services;

public class AssistantScheduler : IDisposable
{
    private readonly ReportGenerator _reportGenerator = new();
    private readonly AssistantRepositoryService _repo = new();
    private readonly AssistantTelemetryService _telemetry = new();
    private DispatcherTimer? _timer;

    public void Start()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMinutes(30)
        };
        _timer.Tick += async (_, _) => await TickAsync();
        // Delay the first tick to avoid running heavy DB work immediately on app start
        Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => _timer.Start());
    }

    private async Task TickAsync()
    {
        try
        {
            var now = DateTime.Now;
            await Task.Run(() => _reportGenerator.SavePeriodicReportsAsync(now));
            if (now.Hour is >= 20 and <= 22)
            {
                await Task.Run(async () =>
                {
                    var conv = await _repo.GetOrCreateMainConversationAsync();
                    await _repo.AddMessageAsync(
                        conv.Id,
                        Models.AssistantRole.Assistant,
                        "Вечерний check-in: как прошел день по целям и что улучшим завтра?");
                });
            }
            await _telemetry.TrackAsync("assistant_scheduler_tick_ok");
        }
        catch (Exception ex)
        {
            await _telemetry.TrackAsync("assistant_scheduler_tick_error", ex.Message);
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        _reportGenerator.Dispose();
    }
}
