using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Runtime.Versioning;
using MeowField.Application;

namespace MeowField.Infrastructure.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsWindowCatalog : IWindowCatalog
{
    private const int SwRestore = 9;

    public bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public bool IsWindow(nint handle) => NativeIsWindow(handle);

    public bool TryActivate(nint handle)
    {
        if (handle == 0 || !NativeIsWindow(handle))
        {
            return false;
        }

        _ = ShowWindow(handle, SwRestore);
        return SetForegroundWindow(handle);
    }

    public bool IsForeground(nint handle)
    {
        if (handle == 0 || !NativeIsWindow(handle))
        {
            return false;
        }

        var foreground = GetForegroundWindow();
        _ = GetWindowThreadProcessId(handle, out var targetProcessId);
        _ = GetWindowThreadProcessId(foreground, out var foregroundProcessId);
        return targetProcessId != 0 && targetProcessId == foregroundProcessId;
    }

    public IReadOnlyList<WindowTarget> ListVisibleWindows()
    {
        var windows = new List<WindowTarget>();
        var processIds = new HashSet<int>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindowTextLength(handle) == 0)
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processIdValue);
            var processId = unchecked((int)processIdValue);
            if (processId <= 0 || !processIds.Add(processId))
            {
                return true;
            }

            var title = ReadTitle(handle);
            var processName = ReadProcessName(processId) ?? $"PID {processId}";
            windows.Add(new WindowTarget(handle, processId, processName, title));
            return true;
        }, 0);

        return windows.OrderBy(item => item.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string ReadTitle(nint handle)
    {
        var length = GetWindowTextLength(handle);
        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string? ReadProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? process.ProcessName
                : $"{process.ProcessName}.exe";
        }
        catch
        {
            return null;
        }
    }

    private delegate bool EnumWindowsCallback(nint handle, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeIsWindow(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
}
