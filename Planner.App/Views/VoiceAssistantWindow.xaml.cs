using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Planner.App.Services;

namespace Planner.App.Views;

public partial class VoiceAssistantWindow : Window
{
    private readonly AssistantVoiceSettings _settings;
    private readonly VoiceRecorderService _recorder = new();
    private readonly OpenAiAudioTranscriptionService _transcription = new();
    private readonly AssistantSpeechSynthesisService _speech = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch _watch = new();
    private bool _isRecording;
    private bool _isProcessing;
    private string? _recordingPath;

    public VoiceAssistantWindow(AssistantVoiceSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
        _timer.Tick += (_, _) => RecordingText.Text = _isRecording
            ? $"Слушаю: {_watch.Elapsed:mm\\:ss}"
            : RecordingText.Text;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _recorder.LevelChanged += OnRecorderLevelChanged;
            _recorder.Start(_settings.MicrophoneDeviceNumber);
            _isRecording = true;
            _watch.Restart();
            _timer.Start();
            StatusText.Text = "Говорите, затем остановите запись.";
            StopButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ShowError("Не удалось начать запись: " + ex.Message);
        }
    }

    private void OnRecorderLevelChanged(double level)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            LevelBar.Value = Math.Clamp(level * 140, 0, 100);
            MicPulse.Opacity = 0.12 + Math.Clamp(level, 0, 1) * 0.36;
            MicPulse.Width = 54 + Math.Clamp(level, 0, 1) * 30;
            MicPulse.Height = MicPulse.Width;
        }));
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await StopAndSendAsync();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing)
            return;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async Task StopAndSendAsync()
    {
        if (_isProcessing)
            return;

        _isProcessing = true;
        StopButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        BusyBar.Visibility = Visibility.Visible;

        try
        {
            if (_isRecording)
            {
                _isRecording = false;
                _timer.Stop();
                _watch.Stop();
                _recordingPath = await _recorder.StopAsync();
            }

            StatusText.Text = "Распознаю речь...";
            var text = await _transcription.TranscribeAsync(_recordingPath ?? "");
            TranscriptText.Text = text;
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Не получилось распознать речь. Попробуйте сказать чуть громче или ближе к микрофону.");

            StatusText.Text = "Отправляю ассистенту...";
            using var orchestrator = new AssistantOrchestratorService();
            var result = await orchestrator.SendUserMessageAsync(
                text,
                async (_, summary) =>
                {
                    return await Dispatcher.InvokeAsync(() =>
                        System.Windows.MessageBox.Show(
                            this,
                            summary,
                            "Planner — подтверждение",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question) == MessageBoxResult.Yes);
                });

            AssistantReplyText.Text = result.Reply;
            AssistantConversationChangedNotificationService.Publish(result.Conversation.Id);

            if (_settings.SpeakAssistantResponses)
            {
                StatusText.Text = "Озвучиваю ответ...";
                await _speech.SpeakAsync(result.Reply);
            }

            StatusText.Text = "Готово.";
            CloseButton.Visibility = Visibility.Visible;
            StopButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            BusyBar.Visibility = Visibility.Collapsed;
            _isProcessing = false;
            CleanupRecording();
        }
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StopButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        BusyBar.Visibility = Visibility.Collapsed;
        _isProcessing = false;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        try
        {
            if (_isRecording)
                _recordingPath = _recorder.StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort cleanup.
        }

        _recorder.LevelChanged -= OnRecorderLevelChanged;
        _recorder.Dispose();
        _transcription.Dispose();
        CleanupRecording();
    }

    private void CleanupRecording()
    {
        if (string.IsNullOrWhiteSpace(_recordingPath))
            return;
        try
        {
            if (File.Exists(_recordingPath))
                File.Delete(_recordingPath);
        }
        catch
        {
            // Temp file cleanup can wait for the OS if the file is still locked.
        }
        finally
        {
            _recordingPath = null;
        }
    }
}
