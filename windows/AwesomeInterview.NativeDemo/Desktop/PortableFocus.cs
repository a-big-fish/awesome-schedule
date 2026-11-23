using System;
using System.Windows;
using System.Windows.Threading;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class DesktopIntegration
{
    DispatcherTimer? portableFocusReset;

    void AttachPortableFocusReset(Window window)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        portableFocusReset = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (portable != window || !window.IsVisible || (window.IsActive && !PortablePassThrough))
                return;

            ClosePortableAppearance();
            portableShowingTasks = false;
            RefreshPortable(centerCurrentTime: true);
        };
        window.Activated += (_, _) => timer.Stop();
        window.Deactivated += (_, _) => SchedulePortableFocusReset();
        window.PreviewMouseDown += (_, _) => timer.Stop();
    }

    void SchedulePortableFocusReset()
    {
        if (portable is not { IsVisible: true } || portableFocusReset == null) return;
        portableFocusReset.Stop();
        portableFocusReset.Start();
    }

    void StopPortableFocusReset()
    {
        portableFocusReset?.Stop();
        portableFocusReset = null;
    }
}
