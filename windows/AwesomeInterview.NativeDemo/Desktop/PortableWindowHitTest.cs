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
    const uint NextWindow = 2;

    public static bool TryGetCursor(out ScreenPoint point) => GetCursorPos(out point);

    public static bool AnyMouseButtonDown =>
        GetAsyncKeyState(1) < 0 || GetAsyncKeyState(2) < 0 || GetAsyncKeyState(4) < 0;

    public static bool CoversPoint(nint window, ScreenPoint point)
    {
        if (!ContainsPoint(window, point)) return false;
        var hit = WindowFromPoint(point);
        var hitRoot = hit != 0 ? GetAncestor(hit, RootWindow) : 0;
        if (hitRoot == window) return true;

        // A transparent pixel can report the desktop or no HWND. Check from the front
        // so a foreground window blocks hover, while the desktop behind the widget doesn't.
        var candidate = GetTopWindow(0);
        for (int i = 0; candidate != 0 && i < 512; i++)
        {
            if (candidate == window) return true;
            if (candidate == hitRoot) return false;
            candidate = GetWindow(candidate, NextWindow);
        }
        return false;
    }

    internal static bool TryGetBounds(nint window, out DesktopRect bounds) => GetWindowRect(window, out bounds);

    internal static bool ContainsPoint(nint window, ScreenPoint point) =>
        TryGetBounds(window, out var bounds) && point.X >= bounds.Left && point.X < bounds.Right &&
        point.Y >= bounds.Top && point.Y < bounds.Bottom;

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out ScreenPoint point);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(nint window, out DesktopRect bounds);

    [DllImport("user32.dll")]
    static extern nint WindowFromPoint(ScreenPoint point);

    [DllImport("user32.dll")]
    static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    static extern nint GetTopWindow(nint window);
}
