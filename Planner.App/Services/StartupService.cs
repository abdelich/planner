using Microsoft.Win32;

namespace Planner.App.Services;

/// <summary>Добавление приложения в автозагрузку Windows.</summary>
public static class StartupService
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Planner";

    public static bool IsRunAtStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            var path = key?.GetValue(AppName) as string;
            return !string.IsNullOrEmpty(path);
        }
        catch
        {
            return false;
        }
    }

    public static void SetRunAtStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            if (enable)
                key.SetValue(AppName, Environment.ProcessPath ?? AppContext.BaseDirectory + "Planner.App.exe");
            else
                key.DeleteValue(AppName, false);
        }
        catch { /* ignore */ }
    }
}
