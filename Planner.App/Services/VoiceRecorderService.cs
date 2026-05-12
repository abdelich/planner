using System.IO;
using NAudio.Wave;

namespace Planner.App.Services;

public sealed class VoiceRecorderService : IDisposable
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _path;
    private bool _isRecording;
    private TaskCompletionSource<bool>? _stoppedTcs;

    public event Action<double>? LevelChanged;

    public void Start(int deviceNumber)
    {
        if (_isRecording)
            throw new InvalidOperationException("Запись уже идет.");

        var resolvedDevice = VoiceMicrophoneService.ResolveDeviceNumber(deviceNumber);
        if (resolvedDevice < 0)
            throw new InvalidOperationException("Микрофон не найден.");

        _path = Path.Combine(Path.GetTempPath(), $"planner-voice-{Guid.NewGuid():N}.wav");
        _waveIn = new WaveInEvent
        {
            DeviceNumber = resolvedDevice,
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 80
        };
        _writer = new WaveFileWriter(_path, _waveIn.WaveFormat);
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;
        _stoppedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _isRecording = true;
        _waveIn.StartRecording();
    }

    public async Task<string> StopAsync()
    {
        if (!_isRecording)
            return _path ?? "";

        _isRecording = false;
        var tcs = _stoppedTcs;
        _waveIn?.StopRecording();
        _writer?.Flush();
        if (tcs != null)
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            if (completed != tcs.Task)
                ForceCloseHandles();
        }
        return _path ?? "";
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        LevelChanged?.Invoke(CalculateLevel(e.Buffer, e.BytesRecorded));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        ForceCloseHandles();
        _stoppedTcs?.TrySetResult(true);
    }

    private void ForceCloseHandles()
    {
        var writer = _writer;
        _writer = null;
        try { writer?.Dispose(); } catch { /* best-effort */ }

        var waveIn = _waveIn;
        if (waveIn != null)
        {
            _waveIn = null;
            try
            {
                waveIn.DataAvailable -= OnDataAvailable;
                waveIn.RecordingStopped -= OnRecordingStopped;
                waveIn.Dispose();
            }
            catch
            {
                // best-effort
            }
        }
    }

    private static double CalculateLevel(byte[] buffer, int bytesRecorded)
    {
        var max = 0;
        for (var i = 0; i + 1 < bytesRecorded; i += 2)
        {
            var sample = Math.Abs(BitConverter.ToInt16(buffer, i));
            if (sample > max)
                max = sample;
        }

        return Math.Clamp(max / 32768.0, 0, 1);
    }

    public void Dispose()
    {
        try
        {
            if (_isRecording)
            {
                _isRecording = false;
                _waveIn?.StopRecording();
            }
        }
        catch
        {
            // Best-effort cleanup.
        }

        ForceCloseHandles();
    }
}
