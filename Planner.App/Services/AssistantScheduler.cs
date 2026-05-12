using System.Windows.Threading;

namespace Planner.App.Services;

public class AssistantScheduler : IDisposable
{
    private readonly ReportGenerator _reportGenerator = new();
    private readonly AssistantTelemetryService _telemetry = new();
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _timer;
    private bool _isTickRunning;

    public AssistantScheduler(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public void Start()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, _dispatcher)
        {
            Interval = TimeSpan.FromMinutes(30)
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_isTickRunning) return;
        _isTickRunning = true;
        try
        {
            await TickAsync();
        }
        finally
        {
            _isTickRunning = false;
        }
    }

    private async Task TickAsync()
    {
        try
        {
            var now = DateTime.Now;
            await Task.Run(() => _reportGenerator.SavePeriodicReportsAsync(now));
            await _telemetry.TrackAsync("assistant_scheduler_tick_ok");
        }
        catch (Exception ex)
        {
            await _telemetry.TrackAsync("assistant_scheduler_tick_error", ex.Message);
        }
    }

    public void Dispose()
    {
        if (_timer != null)
        {
            _timer.Tick -= OnTick;
            _timer.Stop();
            _timer = null;
        }
        _reportGenerator.Dispose();
    }
}
