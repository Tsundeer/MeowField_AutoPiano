using System.ComponentModel;
using System.Runtime.InteropServices;
using MeowField.Application;
using MeowField.Domain;
using Microsoft.Extensions.Logging;

namespace MeowField.Infrastructure.Windows;

public sealed class WindowsInputSink(ILogger<WindowsInputSink>? logger = null) : IInputSink
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmActivate = 0x0006;
    private const uint WmSetFocus = 0x0007;
    private const nuint WaActive = 1;
    private const int InterEventDelayMs = 1;

    public void SendBatch(IReadOnlyList<PlayEvent> events, InputMode mode, nint targetWindow, int latencyMs)
    {
        if (events.Count == 0)
        {
            return;
        }

        if (mode == InputMode.WindowMessage)
        {
            var messageTarget = ResolveMessageTarget(targetWindow);
            PostMessage(messageTarget, WmActivate, WaActive, 0);
            PostMessage(messageTarget, WmSetFocus, 0, 0);
            foreach (var item in events)
            {
                PostKey(messageTarget, item.Key, item.Type == PlayEventType.Down);
                if (events.Count > 1)
                {
                    Thread.Sleep(InterEventDelayMs);
                }
            }
        }
        else
        {
            EnsureTargetIsForeground(targetWindow);
            SendInputBatch(events);
        }
    }

    public void ReleaseAll(IEnumerable<string> keys, InputMode mode, nint targetWindow)
    {
        var releases = keys.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key => new PlayEvent(0, PlayEventType.Up, key, "release"))
            .ToArray();
        SendBatch(releases, mode, targetWindow, latencyMs: 0);
    }

    private void SendInputBatch(IReadOnlyList<PlayEvent> events)
    {
        var groups = new List<NativeInput[]>();
        foreach (var item in events)
        {
            var parsed = KeyboardMap.Parse(item.Key);
            if (parsed is null)
            {
                logger?.LogWarning("Unsupported key mapping: {Key}", item.Key);
                continue;
            }

            var inputs = new List<NativeInput>();
            var isDown = item.Type == PlayEventType.Down;
            if (isDown)
            {
                inputs.AddRange(parsed.Modifiers.Select(modifier => CreateScanInput(KeyboardMap.ModifierScans[modifier], true)));
                inputs.Add(CreateScanInput(KeyboardMap.ScanCodes[parsed.Primary], true));
            }
            else
            {
                inputs.Add(CreateScanInput(KeyboardMap.ScanCodes[parsed.Primary], false));
                inputs.AddRange(parsed.Modifiers.Reverse().Select(modifier => CreateScanInput(KeyboardMap.ModifierScans[modifier], false)));
            }

            if (inputs.Count > 0)
            {
                groups.Add(inputs.ToArray());
            }
        }

        for (var index = 0; index < groups.Count; index++)
        {
            SendInputBatchCore(groups[index]);
            if (index < groups.Count - 1)
            {
                // 并发按键逐键发送并留出微小间隔，避免游戏在同一个输入帧内丢弃部分按键。
                Thread.Sleep(InterEventDelayMs);
            }
        }
    }

    private void SendInputBatchCore(NativeInput[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        if (sent != inputs.Length)
        {
            var exception = new Win32Exception(Marshal.GetLastWin32Error());
            logger?.LogError(exception, "SendInput sent {SentCount} of {ExpectedCount} events", sent, inputs.Length);
            throw exception;
        }
    }

    private void PostKey(nint targetWindow, string key, bool isDown)
    {
        if (targetWindow == 0 || !IsWindow(targetWindow))
        {
            throw new InvalidOperationException("The target window is no longer available.");
        }

        var parsed = KeyboardMap.Parse(key) ?? throw new InvalidOperationException($"Unsupported key mapping: {key}");
        if (isDown)
        {
            foreach (var modifier in parsed.Modifiers)
            {
                PostSingle(targetWindow, KeyboardMap.ModifierVirtualKeys[modifier], KeyboardMap.ModifierScans[modifier], true);
            }

            PostSingle(targetWindow, KeyboardMap.VirtualKeys[parsed.Primary], KeyboardMap.ScanCodes[parsed.Primary], true);
        }
        else
        {
            PostSingle(targetWindow, KeyboardMap.VirtualKeys[parsed.Primary], KeyboardMap.ScanCodes[parsed.Primary], false);
            foreach (var modifier in parsed.Modifiers.Reverse())
            {
                PostSingle(targetWindow, KeyboardMap.ModifierVirtualKeys[modifier], KeyboardMap.ModifierScans[modifier], false);
            }
        }
    }

    private static nint ResolveMessageTarget(nint topWindow)
    {
        if (topWindow == 0 || !IsWindow(topWindow))
        {
            throw new InvalidOperationException("The target window is no longer available.");
        }

        var candidates = new List<nint>();
        EnumChildWindows(topWindow, (handle, _) =>
        {
            if (IsWindowVisible(handle) && GetClientArea(handle) > 0) candidates.Add(handle);
            return true;
        }, 0);

        // Game clients commonly receive keyboard messages in their largest render child.
        return candidates.OrderByDescending(GetClientArea).FirstOrDefault(topWindow);
    }

    private static long GetClientArea(nint handle)
    {
        if (!GetClientRect(handle, out var rect)) return 0;
        return (long)Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
    }

    private static void PostSingle(nint window, ushort virtualKey, ushort scanCode, bool isDown)
    {
        var lParam = 1L | ((long)scanCode << 16);
        if (!isDown)
        {
            lParam |= 1L << 30;
            lParam |= 1L << 31;
        }

        if (!PostMessage(window, isDown ? WmKeyDown : WmKeyUp, virtualKey, (nint)lParam))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static NativeInput CreateScanInput(ushort scanCode, bool isDown) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                Scan = scanCode,
                Flags = KeyEventScanCode | (isDown ? 0 : KeyEventKeyUp),
            },
        },
    };

    private static void EnsureTargetIsForeground(nint targetWindow)
    {
        if (targetWindow == 0)
        {
            return;
        }

        if (!IsWindow(targetWindow))
        {
            throw new InvalidOperationException("The target window is no longer available.");
        }

        var foreground = GetForegroundWindow();
        if (foreground == 0)
        {
            throw new InvalidOperationException("No foreground window is available.");
        }

        _ = GetWindowThreadProcessId(targetWindow, out var targetProcessId);
        _ = GetWindowThreadProcessId(foreground, out var foregroundProcessId);
        if (targetProcessId == 0 || targetProcessId != foregroundProcessId)
        {
            throw new InvalidOperationException("The bound target is not the foreground application.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, [In] NativeInput[] inputs, int size);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint handle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    private delegate bool EnumWindowsCallback(nint handle, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
