using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class DesktopIntegration
{
    DispatcherTimer? portableIdleTimer;
    bool portableDetailOpen;
    int PortableAutoLockSeconds => Math.Clamp(preferences.PortableAutoLockSeconds, 0, 60);

    void AttachPortableIdleLock(Window window)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background);
        portableIdleTimer = timer;
        timer.Tick += (_, _) =>
        {
            if (portableMoving || window.IsMouseCaptureWithin || portableDetailOpen) return;
            timer.Stop();
            if (portable == window && window.IsVisible && !portableLocked) SetPortableLocked(true);
        };
        window.IsVisibleChanged += (_, _) => RestartPortableIdleTimer();
        window.Activated += (_, _) => RestartPortableIdleTimer();
        window.LocationChanged += (_, _) => RestartPortableIdleTimer();
        window.SizeChanged += (_, _) => RestartPortableIdleTimer();
        window.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler((_, _) => RestartPortableIdleTimer()), true);
        window.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => RestartPortableIdleTimer()), true);
        window.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler((_, _) => RestartPortableIdleTimer()), true);
        window.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, _) => RestartPortableIdleTimer()), true);
        window.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, _) => RestartPortableIdleTimer()), true);
    }

    void RestartPortableIdleTimer()
    {
        if (portableIdleTimer is not { } timer) return;
        timer.Stop();
        int seconds = PortableAutoLockSeconds;
        if (seconds == 0 || portableLocked || portable is not { IsVisible: true }) return;
        timer.Interval = TimeSpan.FromSeconds(seconds);
        timer.Start();
    }

    void StopPortableIdleTimer()
    {
        portableIdleTimer?.Stop();
        portableIdleTimer = null;
    }
}
