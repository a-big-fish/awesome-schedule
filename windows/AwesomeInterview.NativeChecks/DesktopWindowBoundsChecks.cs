using AwesomeInterview.NativeDemo;

internal static class DesktopWindowBoundsChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var work = new DesktopRect(-1920, -100, 0, 940);
        var oversized = new DesktopRect(-2400, -300, 170, 1290);
        var start = DesktopWindowBounds.Fit(oversized, work, 92);
        check(start.Width < work.Width && start.Height < work.Height &&
            start.Left > work.Left && start.Right < work.Right && start.Top > work.Top && start.Bottom < work.Bottom,
            "oversized startup windows stay within a secondary monitor's work area");
        double minimum = DesktopWindowBounds.MinimumDip(640, work.Height, 1.5);
        check(minimum < 640 && minimum < work.Height / 1.5,
            "high DPI lowers the main window minimum enough to allow vertical resizing");
        var full = work.Inset(15);
        check(DesktopWindowBounds.Fit(oversized, full) == full && full.Bottom < work.Bottom,
            "an oversized portable window is recovered inside the taskbar-safe work area");
        var resize = DesktopWindowBounds.LimitResize(new(-1500, 100, 250, 1200), full);
        check(resize == new DesktopRect(-1500, 100, full.Right, full.Bottom),
            "portable resizing keeps the opposite corner and stops at the work-area boundary");

        var expected = new (int X, int Y, DesktopResizeHit Hit)[]
        {
            (full.Left + 2, full.Top + 20, DesktopResizeHit.Left),
            (full.Right - 3, full.Top + 20, DesktopResizeHit.Right),
            (full.Left + 20, full.Top + 2, DesktopResizeHit.Top),
            (full.Left + 20, full.Bottom - 3, DesktopResizeHit.Bottom),
            (full.Left + 2, full.Top + 2, DesktopResizeHit.TopLeft),
            (full.Right - 3, full.Top + 2, DesktopResizeHit.TopRight),
            (full.Left + 2, full.Bottom - 3, DesktopResizeHit.BottomLeft),
            (full.Right - 3, full.Bottom - 3, DesktopResizeHit.BottomRight),
            (full.Left + 20, full.Top + 20, DesktopResizeHit.Client)
        };
        check(expected.All(point => DesktopWindowBounds.ResizeHit(full, point.X, point.Y, 7, 13) == point.Hit),
            "all eight resize zones and the normal content area work with negative monitor coordinates");
    }
}
