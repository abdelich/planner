using System.Windows;
using System.Windows.Forms;
using Planner.App.Services;

namespace Planner.App;

public partial class MainWindow : Window
{
    private NotifyIcon? _notifyIcon;
    private bool _isRealClose;
    private ReminderPopupService? _reminderPopupService;
    private AssistantScheduler? _assistantScheduler;
    private VoiceHotkeyService? _voiceHotkeyService;
    private bool _voiceWindowOpen;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AssistantDiagnosticsService.LogMemory("main-window-loaded");
        SetupTrayIcon();
        _reminderPopupService = new ReminderPopupService();
        _reminderPopupService.Start();
        _assistantScheduler = new AssistantScheduler();
        _assistantScheduler.Start();
        _voiceHotkeyService = new VoiceHotkeyService();
        _voiceHotkeyService.Pressed += OnVoiceHotkeyPressed;
        AssistantLocalSettingsService.SettingsChanged += OnAssistantSettingsChanged;
        RegisterVoiceHotkey();
        AssistantDiagnosticsService.LogMemory("main-window-services-started");
    }

    private void SetupTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconService.CreateTrayIcon(),
            Text = "Planner — цели и напоминания",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        var openItem = new ToolStripMenuItem("Открыть");
        openItem.Click += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) =>
        {
            _isRealClose = true;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            Close();
            ((System.Windows.Application)System.Windows.Application.Current).Shutdown();
        };

        _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.Add(openItem);
        var voiceItem = new ToolStripMenuItem("Голосовой ввод");
        voiceItem.Click += async (_, _) => await ShowVoiceAssistantAsync();
        _notifyIcon.ContextMenuStrip.Items.Add(voiceItem);
        _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add(exitItem);
    }

    private void OnAssistantSettingsChanged()
    {
        Dispatcher.BeginInvoke(new Action(RegisterVoiceHotkey));
    }

    private void RegisterVoiceHotkey()
    {
        if (_voiceHotkeyService == null)
            return;

        var settings = new AssistantLocalSettingsService().GetVoiceSettings();
        if (!_voiceHotkeyService.Register(this, settings.Hotkey, out var error))
            AssistantDiagnosticsService.LogMemory("voice-hotkey-register-failed", error);
        else
            AssistantDiagnosticsService.LogMemory("voice-hotkey-registered", settings.Hotkey);
    }

    private void OnVoiceHotkeyPressed(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(async () => await ShowVoiceAssistantAsync()));
    }

    private Task ShowVoiceAssistantAsync()
    {
        if (_voiceWindowOpen)
            return Task.CompletedTask;

        _voiceWindowOpen = true;
        try
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            if (DataContext is ViewModels.MainViewModel vm)
                vm.NavigateCommand.Execute("Assistant");

            var settings = new AssistantLocalSettingsService().GetVoiceSettings();
            var window = new Views.VoiceAssistantWindow(settings)
            {
                Owner = this
            };
            window.ShowDialog();
        }
        finally
        {
            _voiceWindowOpen = false;
        }

        return Task.CompletedTask;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isRealClose)
        {
            AssistantLocalSettingsService.SettingsChanged -= OnAssistantSettingsChanged;
            _reminderPopupService?.Stop();
            _assistantScheduler?.Dispose();
            if (_voiceHotkeyService != null)
            {
                _voiceHotkeyService.Pressed -= OnVoiceHotkeyPressed;
                _voiceHotkeyService.Dispose();
                _voiceHotkeyService = null;
            }
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
