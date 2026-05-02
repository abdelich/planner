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

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetupTrayIcon();
        _reminderPopupService = new ReminderPopupService();
        _reminderPopupService.Start();
        _assistantScheduler = new AssistantScheduler();
        _assistantScheduler.Start();
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
        _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add(exitItem);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isRealClose)
        {
            _reminderPopupService?.Stop();
            _assistantScheduler?.Dispose();
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
