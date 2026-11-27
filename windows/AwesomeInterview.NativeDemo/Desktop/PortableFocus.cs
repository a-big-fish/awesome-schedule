using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class DesktopIntegration
{
    DispatcherTimer? portableFocusReset;
    DispatcherTimer? portableTimelineReveal, portableTimelineReturn;
    bool PortableTimelineInMotion => portableTimelineReveal?.IsEnabled == true || portableTimelineReturn?.IsEnabled == true;

    void AttachPortableFocusReset(Window window)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        portableFocusReset = timer;
        portableTimelineReveal = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(750) };
        portableTimelineReveal.Tick += (_, _) =>
        {
            portableTimelineReveal?.Stop();
            RefreshPortableGuidePresentation();
        };
        timer.Tick += (_, _) =>
        {
            if (portableMoving || window.IsMouseCaptureWithin) return;
            timer.Stop();
            if (portableDetailOpen || portableHoverActive || portable != window || !window.IsVisible || (window.IsActive && !PortablePassThrough))
                return;

            ClosePortableAppearance();
            portableShowingTasks = false;
            RefreshPortable(centerCurrentTime: true);
        };
        window.Activated += (_, _) => { timer.Stop(); StopPortableTimelineMotion(); };
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
        StopPortableTimelineMotion();
        portableTimelineReveal = null;
    }

    void RefreshPortableGuidePresentation()
    {
        UpdatePortableGuides(portableTimelineGuideTime);
        SchedulePortableContrast();
    }

    void RevealPortableTimelineWhileScrolling()
    {
        if (portableTimelineReveal is not { } timer) return;
        bool reveal = !PortableTimelineInMotion;
        portableTimelineReturn?.Stop();
        portableTimelineReturn = null;
        timer.Stop();
        timer.Start();
        if (reveal) RefreshPortableGuidePresentation();
    }

    void StopPortableTimelineMotion()
    {
        bool wasMoving = PortableTimelineInMotion;
        portableTimelineReveal?.Stop();
        portableTimelineReturn?.Stop();
        portableTimelineReturn = null;
        if (wasMoving) RefreshPortableGuidePresentation();
    }

    void PositionPortableTimeline(QuietScrollViewer scroll, Canvas timeline, double? savedOffset, bool animate = false)
    {
        double viewport = scroll.ViewportHeight;
        double currentY = portableTimelineGuideTime.TotalHours * PortableHourHeight;
        double? firstShownY = null;
        if (PortablePassThrough)
        {
            for (int hour = 0; hour < 24; hour++)
            {
                if (PortableTimelineLayout.GuideOpacity(TimeSpan.FromHours(hour), portableTimelineGuideTime, portableTimelineTasks) == 0)
                    continue;
                firstShownY = Math.Min(currentY, hour * PortableHourHeight);
                break;
            }
        }
        double target = savedOffset ?? PortableTimelineLayout.CenterOffset(currentY, viewport, firstShownY, PortableHourHeight * .65);
        double start = scroll.VerticalOffset;
        if (!animate || !SystemParameters.ClientAreaAnimation || Math.Abs(target - start) < 1)
        {
            StopPortableTimelineMotion();
            timeline.Margin = new(0, 0, 0, Math.Max(0, target + viewport - PortableDayHeight));
            scroll.ScrollToVerticalOffset(target);
            return;
        }

        bool wasMoving = PortableTimelineInMotion;
        portableTimelineReveal?.Stop();
        portableTimelineReturn?.Stop();
        timeline.Margin = new(0, 0, 0, Math.Max(0, Math.Max(start, target) + viewport - PortableDayHeight));
        scroll.UpdateLayout();
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        var clock = Stopwatch.StartNew();
        portableTimelineReturn = timer;
        timer.Tick += (_, _) =>
        {
            if (!scroll.IsLoaded || portableTimelineScroll != scroll)
            {
                StopPortableTimelineMotion();
                return;
            }
            double progress = clock.Elapsed.TotalMilliseconds / 440;
            scroll.ScrollToVerticalOffset(PortableTimelineLayout.ScrollFrame(start, target, progress));
            if (progress < 1) return;
            timeline.Margin = new(0, 0, 0, Math.Max(0, target + viewport - PortableDayHeight));
            StopPortableTimelineMotion();
        };
        timer.Start();
        if (!wasMoving) RefreshPortableGuidePresentation();
    }
}
