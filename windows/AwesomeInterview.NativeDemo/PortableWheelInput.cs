using System;
using System.Runtime.InteropServices;

namespace AwesomeInterview.NativeDemo;

internal sealed class PortableWheelInput : IDisposable
{
    const int LowLevelMouseHook = 14;
    const int MouseWheel = 0x020A;
    const uint RootWindow = 2;
    const uint PreviousWindow = 3;

    readonly HookProc callback;
    readonly nint window;
    readonly Func<int, int, int, bool> scroll;
    nint hook;

    public PortableWheelInput(nint window, Func<int, int, int, bool> scroll)
    {
        this.window = window;
        this.scroll = scroll;
        callback = OnMouse;
    }

    public bool Start()
    {
        hook = SetWindowsHookEx(LowLevelMouseHook, callback, GetModuleHandle(null), 0);
        return hook != 0;
    }

    public void Dispose()
    {
        if (hook == 0) return;
        UnhookWindowsHookEx(hook);
        hook = 0;
    }

    nint OnMouse(int code, nint message, nint data)
    {
        if (code == 0 && message == MouseWheel)
        {
            var input = Marshal.PtrToStructure<MouseInput>(data);
            var delta = unchecked((short)(input.MouseData >> 16));
            if (delta != 0 && IsAboveWindowAt(input.Position) && scroll(input.Position.X, input.Position.Y, delta))
                return 1;
        }
        return CallNextHookEx(0, code, message, data);
    }

    bool IsAboveWindowAt(ScreenPoint point)
    {
        if (!GetWindowRect(window, out var bounds) ||
            point.X < bounds.Left || point.X >= bounds.Right ||
            point.Y < bounds.Top || point.Y >= bounds.Bottom)
            return false;

        var hit = WindowFromPoint(point);
        if (hit == 0) return false;
        var candidate = GetAncestor(hit, RootWindow);

        // WindowFromPoint skips the transparent surface. Check that no interactive
        // window covers it here, including the separate unlock control.
        for (int i = 0; candidate != 0 && i < 128; i++)
        {
            if (candidate == window) return true;
            candidate = GetWindow(candidate, PreviousWindow);
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ScreenPoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct ScreenRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MouseInput
    {
        public ScreenPoint Position;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }

    delegate nint HookProc(int code, nint message, nint data);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
    static extern nint SetWindowsHookEx(int type, HookProc callback, nint module, uint threadId);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(nint window, out ScreenRect bounds);

    [DllImport("user32.dll")]
    static extern nint WindowFromPoint(ScreenPoint point);

    [DllImport("user32.dll")]
    static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    static extern nint GetWindow(nint window, uint command);
}
