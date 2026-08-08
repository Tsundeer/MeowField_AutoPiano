using System.Runtime.InteropServices;
using MeowField.Application;

namespace MeowField.Infrastructure.Windows;

public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int WmHotKey = 0x0312;
    private const int HotkeyPlayPause = 1;
    private const int HotkeyStop = 2;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint KeyC = 0x43;
    private const uint KeyF9 = 0x78;
    private nint _window;

    public event EventHandler? PlayPauseRequested;
    public event EventHandler? StopRequested;
    public bool IsEnabled { get; private set; }

    public bool TryRegister(nint windowHandle)
    {
        Unregister();
        if (windowHandle == 0)
        {
            return false;
        }

        var playRegistered = RegisterHotKey(windowHandle, HotkeyPlayPause, ModControl | ModShift, KeyC);
        var stopRegistered = RegisterHotKey(windowHandle, HotkeyStop, 0, KeyF9);
        if (!playRegistered || !stopRegistered)
        {
            if (playRegistered) UnregisterHotKey(windowHandle, HotkeyPlayPause);
            if (stopRegistered) UnregisterHotKey(windowHandle, HotkeyStop);
            return false;
        }

        _window = windowHandle;
        IsEnabled = true;
        return true;
    }

    public void Unregister()
    {
        if (_window != 0)
        {
            UnregisterHotKey(_window, HotkeyPlayPause);
            UnregisterHotKey(_window, HotkeyStop);
            _window = 0;
        }

        IsEnabled = false;
    }

    public bool HandleWindowMessage(int message, nint wParam)
    {
        if (message != WmHotKey || !IsEnabled)
        {
            return false;
        }

        switch (wParam.ToInt32())
        {
            case HotkeyPlayPause:
                PlayPauseRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case HotkeyStop:
                StopRequested?.Invoke(this, EventArgs.Empty);
                return true;
            default:
                return false;
        }
    }

    public void Dispose() => Unregister();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
