using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class DesktopIntegration
{
    Window? portableResizeFrame;

    void UpdatePortableResizeFrame()
    {
        if (!PortablePassThrough || portable is not { IsVisible: true } owner)
        {
            portableResizeFrame?.Hide();
            return;
        }

        if (portableResizeFrame == null)
        {
            var frame = new Window
            {
                Owner = owner, Title = "随身日程缩放边缘", ShowActivated = false, ShowInTaskbar = false,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
                Background = Brushes.Transparent, Left = owner.Left, Top = owner.Top, Width = owner.ActualWidth,
                Height = owner.ActualHeight, Topmost = owner.Topmost,
                // Zero alpha inside the ring keeps the timetable clickable through to other windows.
                Content = new Border { BorderThickness = new(NativeWindowPlacement.ResizeEdgeDip), BorderBrush = Brush("#01FFFFFF") }
            };
            frame.SourceInitialized += (_, _) =>
            {
                if (PresentationSource.FromVisual(frame) is HwndSource source) source.AddHook(PortableUnlockMessage);
            };
            frame.QueryCursor += (_, e) =>
            {
                var handle = new WindowInteropHelper(owner).Handle;
                if (!PortableWindowHitTest.TryGetCursor(out var point)) return;
                e.Cursor = portablePlacement?.PortableResizeHit(handle, point) switch
                {
                    DesktopResizeHit.Left or DesktopResizeHit.Right => Cursors.SizeWE,
                    DesktopResizeHit.Top or DesktopResizeHit.Bottom => Cursors.SizeNS,
                    DesktopResizeHit.TopLeft or DesktopResizeHit.BottomRight => Cursors.SizeNWSE,
                    DesktopResizeHit.TopRight or DesktopResizeHit.BottomLeft => Cursors.SizeNESW,
                    _ => Cursors.Arrow
                };
                e.Handled = true;
            };
            frame.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (!PortablePassThrough || !PortableWindowHitTest.TryGetCursor(out var point)) return;
                e.Handled = true;
                portableFocusReset?.Stop();
                StopPortableTimelineMotion();
                portablePlacement?.ResizePortableFromEdge(new WindowInteropHelper(owner).Handle, point);
                SchedulePortableFocusReset();
            };
            portableResizeFrame = frame;
        }

        portableResizeFrame.Topmost = owner.Topmost;
        if (!portableResizeFrame.IsVisible) portableResizeFrame.Show();
        PositionPortableResizeFrame();
    }

    void PositionPortableResizeFrame()
    {
        if (portableResizeFrame is not { IsVisible: true } frame || portable is not { } owner ||
            !PortableWindowHitTest.TryGetBounds(new WindowInteropHelper(owner).Handle, out var bounds)) return;
        const uint NoActivate = 0x0010, NoZOrder = 0x0004;
        SetWindowPos(new WindowInteropHelper(frame).Handle, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, NoActivate | NoZOrder);
    }
}
