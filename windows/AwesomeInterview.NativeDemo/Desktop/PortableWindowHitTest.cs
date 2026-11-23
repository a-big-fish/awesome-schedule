using System;
using System.Runtime.InteropServices;

namespace AwesomeInterview.NativeDemo;

[StructLayout(LayoutKind.Sequential)]
internal struct ScreenPoint
{
    public int X, Y;
}

internal static class PortableWindowHitTest
{
    const uint RootWindow = 2;
    const uint PreviousWindow = 3;

    public static bool TryGetCursor(out ScreenPoint point) => GetCursorPos(out point);

    public static bool AnyMouseButtonDown =>
        GetAsyncKeyState(1) < 0 || GetAsyncKeyState(2) < 0 || GetAsyncKeyState(4) < 0;

    public static bool CoversPoint(nint window, ScreenPoint point)
    {
        if (!GetWindowRect(window, out var bounds) ||
            point.X < bounds.Left || point.X >= bounds.Right ||
            point.Y < bounds.Top || point.Y >= bounds.Bottom)
            return false;

        var hit = WindowFromPoint(point);
        if (hit == 0) return false;
        // The hit can be the transparent window itself or a window underneath it.
        var candidate = GetAncestor(hit, RootWindow);
        for (int i = 0; candidate != 0 && i < 128; i++)
        {
            if (candidate == window) return true;
            candidate = GetWindow(candidate, PreviousWindow);
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ScreenRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out ScreenPoint point);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(nint window, out ScreenRect bounds);

    [DllImport("user32.dll")]
    static extern nint WindowFromPoint(ScreenPoint point);

    [DllImport("user32.dll")]
    static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    static extern nint GetWindow(nint window, uint command);
}
