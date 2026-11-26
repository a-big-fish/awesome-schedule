using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AwesomeInterview.NativeDemo;

internal sealed class NativeWindowPlacement
{
    internal const int ResizeEdgeDip = 7;
    readonly Window window;
    readonly Func<bool>? portableInteractive;
    readonly double preferredMinWidth, preferredMinHeight, defaultWidth, defaultHeight;
    HwndSource? source;
    DesktopRect? restoreBounds;
    bool sizing, fitQueued, firstFit = true;

    internal NativeWindowPlacement(Window window, Func<bool>? portableInteractive = null)
    {
        this.window = window;
        this.portableInteractive = portableInteractive;
        preferredMinWidth = window.MinWidth;
        preferredMinHeight = window.MinHeight;
        defaultWidth = window.Width;
        defaultHeight = window.Height;
        window.SourceInitialized += (_, _) =>
        {
            source = PresentationSource.FromVisual(window) as HwndSource;
            source?.AddHook(HandleMessage);
            FitNormal();
            QueueFit();
        };
        window.DpiChanged += (_, _) => QueueFit();
        window.StateChanged += (_, _) => QueueFit();
        window.Closed += (_, _) => source = null;
    }

    internal void FitNormal()
    {
        if (sizing || window.WindowState != WindowState.Normal || !TryGetPlacement(out var current, out var area)) return;
        ApplySizeLimits(area);
        var bounds = DesktopWindowBounds.Fit(current, area, firstFit && portableInteractive == null ? 92 : 100);
        firstFit = false;
        if (bounds != current) Place(bounds);
    }

    internal void TogglePortableFill()
    {
        if (portableInteractive?.Invoke() != true || !TryGetPlacement(out var current, out var area)) return;
        if (FillsArea(current, area))
        {
            Place(RestoredBounds(area));
            restoreBounds = null;
        }
        else FillPortable(current, area);
    }

    internal void RestorePortableForDrag(Point pointer)
    {
        if (!TryGetPlacement(out var current, out var area) || !FillsArea(current, area)) return;
        var restored = RestoredBounds(area);
        double fraction = Math.Clamp((pointer.X - current.Left) / current.Width, 0, 1);
        int x = (int)Math.Round(pointer.X - restored.Width * fraction);
        int y = (int)Math.Round(pointer.Y - Math.Clamp(pointer.Y - current.Top, 0, ToPixels(34)));
        Place(DesktopWindowBounds.Fit(new(x, y, x + restored.Width, y + restored.Height), area));
        restoreBounds = null;
    }

    internal DesktopResizeHit PortableResizeHit(nint handle, ScreenPoint point) =>
        PortableWindowHitTest.TryGetBounds(handle, out var frame)
            ? DesktopWindowBounds.ResizeHit(frame, point.X, point.Y, ToPixels(ResizeEdgeDip), ToPixels(13))
            : DesktopResizeHit.Client;

    internal void ResizePortableFromEdge(nint handle, ScreenPoint point)
    {
        var edge = PortableResizeHit(handle, point);
        if (edge == DesktopResizeHit.Client) return;
        const int SystemCommand = 0x0112, Size = 0xF000;
        // The sizing direction is WMSZ_LEFT through WMSZ_BOTTOMRIGHT.
        int direction = (int)edge - (int)DesktopResizeHit.Left + 1;
        int position = (ushort)point.X | ((ushort)point.Y << 16);
        SendMessage(handle, SystemCommand, Size | direction, position);
    }

    DesktopRect RestoredBounds(DesktopRect area)
    {
        if (restoreBounds is { } previous) return DesktopWindowBounds.Fit(previous, area);
        int width = Math.Min(area.Width, ToPixels(defaultWidth));
        int height = Math.Min(area.Height, ToPixels(defaultHeight));
        int x = area.Left + (area.Width - width) / 2;
        int y = area.Top + (area.Height - height) / 2;
        return new(x, y, x + width, y + height);
    }

    static bool FillsArea(DesktopRect frame, DesktopRect area) =>
        frame.Width >= area.Width - 1 && frame.Height >= area.Height - 1;

    void FillPortable(DesktopRect current, DesktopRect area)
    {
        if (current == area) return;
        restoreBounds = DesktopWindowBounds.Fit(current, area);
        ApplySizeLimits(area);
        Place(area);
    }

    void QueueFit()
    {
        if (fitQueued || source == null) return;
        fitQueued = true;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            fitQueued = false;
            if (source == null || sizing) return;
            if (portableInteractive != null && window.WindowState == WindowState.Maximized)
            {
                window.WindowState = WindowState.Normal;
                if (TryGetPlacement(out var current, out var area)) FillPortable(current, area);
            }
            else FitNormal();
        }));
    }

    nint HandleMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int HitTest = 0x0084, SystemCommand = 0x0112, EnterSizeMove = 0x0231, ExitSizeMove = 0x0232;
        const int Sizing = 0x0214, DisplayChanged = 0x007E, SettingsChanged = 0x001A;
        const int SetWorkArea = 0x002F, Maximize = 0xF030, Restore = 0xF120;
        if (message == EnterSizeMove)
        {
            sizing = true;
            if (portableInteractive != null && TryGetPlacement(out var beforeResize, out var area) &&
                !FillsArea(beforeResize, area))
                restoreBounds = beforeResize;
        }
        else if (message == ExitSizeMove)
        {
            sizing = false;
            QueueFit();
        }
        else if (message == DisplayChanged || message == SettingsChanged && wParam == SetWorkArea) QueueFit();

        if (portableInteractive == null) return 0;
        if (message == HitTest)
        {
            if (!portableInteractive())
            {
                handled = true;
                return -1; // HTTRANSPARENT, alongside the widget's existing layered input style.
            }
            long position = lParam.ToInt64();
            var hit = PortableResizeHit(handle, new ScreenPoint { X = unchecked((short)position), Y = unchecked((short)(position >> 16)) });
            if (hit == DesktopResizeHit.Client) return 0;
            handled = true;
            return (int)hit;
        }
        if (message == Sizing && lParam != 0 && TryGetPlacement(out _, out var work))
        {
            var proposed = Marshal.PtrToStructure<DesktopRect>(lParam);
            if (!proposed.HasArea) return 0;
            Marshal.StructureToPtr(DesktopWindowBounds.LimitResize(proposed, work), lParam, false);
            handled = true;
            return 1;
        }
        if (message == SystemCommand)
        {
            int command = (int)(wParam.ToInt64() & 0xfff0);
            if (command is not (Maximize or Restore) || !TryGetPlacement(out var bounds, out var available)) return 0;
            if (command == Maximize)
            {
                FillPortable(bounds, available);
                handled = true;
            }
            else if (FillsArea(bounds, available) && restoreBounds != null)
            {
                TogglePortableFill();
                handled = true;
            }
        }
        return 0;
    }

    bool TryGetPlacement(out DesktopRect current, out DesktopRect work)
    {
        current = work = default;
        if (source is not { IsDisposed: false, Handle: var handle } || handle == 0 ||
            !PortableWindowHitTest.TryGetBounds(handle, out current) || !current.HasArea) return false;
        var monitor = MonitorFromWindow(handle, 2); // MONITOR_DEFAULTTONEAREST
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info) || !info.Work.HasArea) return false;
        work = portableInteractive == null ? info.Work : info.Work.Inset(ToPixels(10));
        return true;
    }

    double DpiScale
    {
        get
        {
            uint dpi = source is { IsDisposed: false, Handle: var handle } ? GetDpiForWindow(handle) : 0;
            return dpi == 0 ? 1 : dpi / 96d;
        }
    }

    int ToPixels(double dip) => (int)Math.Ceiling(dip * DpiScale);

    void ApplySizeLimits(DesktopRect area)
    {
        double dpi = DpiScale;
        window.MinWidth = DesktopWindowBounds.MinimumDip(preferredMinWidth, area.Width, dpi);
        window.MinHeight = DesktopWindowBounds.MinimumDip(preferredMinHeight, area.Height, dpi);
    }

    void Place(DesktopRect bounds)
    {
        if (source is not { IsDisposed: false, Handle: var handle }) return;
        const uint NoZOrder = 0x0004, NoActivate = 0x0010;
        SetWindowPos(handle, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, NoZOrder | NoActivate);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        internal int Size;
        internal DesktopRect Monitor, Work;
        internal uint Flags;
    }

    [DllImport("user32.dll")]
    static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
