using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Planner.App.Services;

public sealed class VoiceHotkeyService : IDisposable
{
    private const int HotkeyId = 0x504C;
    private const int WmHotkey = 0x0312;

    private HwndSource? _source;
    private IntPtr _handle;
    private bool _registered;

    public event EventHandler? Pressed;

    public bool Register(Window window, string hotkey, out string error)
    {
        error = "";
        Unregister();

        if (!TryParseHotkey(hotkey, out var modifiers, out var key, out error))
            return false;

        _handle = new WindowInteropHelper(window).Handle;
        if (_handle == IntPtr.Zero)
        {
            error = "Окно еще не готово для регистрации горячей клавиши.";
            return false;
        }

        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
        _registered = RegisterHotKey(_handle, HotkeyId, modifiers, key);
        if (!_registered)
        {
            error = "Не удалось зарегистрировать горячую клавишу. Возможно, она уже занята другой программой.";
            _source?.RemoveHook(WndProc);
            _source = null;
        }

        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    private void Unregister()
    {
        if (_registered && _handle != IntPtr.Zero)
            UnregisterHotKey(_handle, HotkeyId);
        _registered = false;
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    public void Dispose()
    {
        Unregister();
    }

    private static bool TryParseHotkey(string value, out uint modifiers, out uint key, out string error)
    {
        modifiers = 0;
        key = 0;
        error = "";

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Горячая клавиша пустая.";
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "Горячая клавиша пустая.";
            return false;
        }

        foreach (var part in parts[..^1])
        {
            modifiers |= (uint)(part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => 0x0002,
                "alt" => 0x0001,
                "shift" => 0x0004,
                "win" or "windows" => 0x0008,
                _ => 0
            });
        }

        if (modifiers == 0)
        {
            error = "Добавьте модификатор: Ctrl, Alt, Shift или Win.";
            return false;
        }

        var keyText = parts[^1];
        if (string.Equals(keyText, "Space", StringComparison.OrdinalIgnoreCase))
            key = (uint)System.Windows.Forms.Keys.Space;
        else if (Enum.TryParse<System.Windows.Forms.Keys>(keyText, true, out var parsedKey))
            key = (uint)parsedKey;
        else if (keyText.Length == 1)
            key = (uint)char.ToUpperInvariant(keyText[0]);

        if (key == 0)
        {
            error = $"Не понял клавишу: {keyText}.";
            return false;
        }

        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
