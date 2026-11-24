using System;
using System.Runtime.InteropServices;

namespace AwesomeInterview.NativeDemo;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct DesktopRect(int Left, int Top, int Right, int Bottom)
{
    internal int Width => Right - Left;
    internal int Height => Bottom - Top;
    internal bool HasArea => Width > 0 && Height > 0;

    internal DesktopRect Inset(int pixels)
    {
        int inset = Math.Clamp(pixels, 0, Math.Max(0, (Math.Min(Width, Height) - 1) / 2));
        return new(Left + inset, Top + inset, Right - inset, Bottom - inset);
    }
}

internal enum DesktopResizeHit
{
    Client = 1, Left = 10, Right = 11, Top = 12, TopLeft = 13,
    TopRight = 14, Bottom = 15, BottomLeft = 16, BottomRight = 17
}

internal static class DesktopWindowBounds
{
    internal static DesktopRect Fit(DesktopRect window, DesktopRect area, int sizePercent = 100)
    {
        int width = Math.Min(window.Width, Math.Max(1, area.Width * sizePercent / 100));
        int height = Math.Min(window.Height, Math.Max(1, area.Height * sizePercent / 100));
        int x = width < window.Width && sizePercent < 100 ? area.Left + (area.Width - width) / 2
            : Math.Clamp(window.Left, area.Left, area.Right - width);
        int y = height < window.Height && sizePercent < 100 ? area.Top + (area.Height - height) / 2
            : Math.Clamp(window.Top, area.Top, area.Bottom - height);
        return new(x, y, x + width, y + height);
    }

    internal static double MinimumDip(double preferred, int workPixels, double dpiScale) =>
        Math.Min(preferred, Math.Max(1, Math.Floor(workPixels / dpiScale * .8)));

    internal static DesktopRect LimitResize(DesktopRect proposed, DesktopRect area)
    {
        var clipped = new DesktopRect(Math.Max(proposed.Left, area.Left), Math.Max(proposed.Top, area.Top),
            Math.Min(proposed.Right, area.Right), Math.Min(proposed.Bottom, area.Bottom));
        return clipped.HasArea ? clipped : Fit(proposed, area);
    }

    internal static DesktopResizeHit ResizeHit(DesktopRect rect, int x, int y, int edge, int corner)
    {
        if (x < rect.Left || x >= rect.Right || y < rect.Top || y >= rect.Bottom) return DesktopResizeHit.Client;
        int left = x - rect.Left, right = rect.Right - 1 - x;
        int top = y - rect.Top, bottom = rect.Bottom - 1 - y;
        if (left < edge && top < corner || top < edge && left < corner) return DesktopResizeHit.TopLeft;
        if (right < edge && top < corner || top < edge && right < corner) return DesktopResizeHit.TopRight;
        if (left < edge && bottom < corner || bottom < edge && left < corner) return DesktopResizeHit.BottomLeft;
        if (right < edge && bottom < corner || bottom < edge && right < corner) return DesktopResizeHit.BottomRight;
        if (left < edge) return DesktopResizeHit.Left;
        if (right < edge) return DesktopResizeHit.Right;
        if (top < edge) return DesktopResizeHit.Top;
        if (bottom < edge) return DesktopResizeHit.Bottom;
        return DesktopResizeHit.Client;
    }
}
