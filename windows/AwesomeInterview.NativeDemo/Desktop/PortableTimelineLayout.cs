using System;
using System.Collections.Generic;

namespace AwesomeInterview.NativeDemo;

internal static class PortableTimelineLayout
{
    internal readonly record struct RuleExtent(double Width, double SolidFraction);
    internal readonly record struct TaskRange(TimeSpan Start, TimeSpan End);

    internal static int NearestHourToHide(TimeSpan time)
    {
        int hour = (int)Math.Round(time.TotalHours, MidpointRounding.AwayFromZero);
        return hour < 24 ? hour : -1;
    }

    internal static RuleExtent Rule(double available, double occupied)
    {
        double solid = Math.Clamp(occupied, 0, available);
        if (solid == 0) return new(0, 1);
        double width = Math.Min(available, solid + 32);
        return new(width, solid / width);
    }

    internal static double GuideOpacity(TimeSpan time, TimeSpan now, IReadOnlyList<TaskRange> tasks)
    {
        // Keep the nearest hour around the current time clear. Task edges start fading immediately.
        double distance = Math.Max(0, Math.Abs((time - now).TotalHours) - 1);
        foreach (var task in tasks)
        {
            double fromTask = Math.Max(0, Math.Max((task.Start - time).TotalHours, (time - task.End).TotalHours));
            distance = Math.Min(distance, fromTask);
            if (distance == 0) break;
        }
        double fade = 1 - Math.Clamp(distance / 2, 0, 1);
        double opacity = fade * fade * (3 - 2 * fade);
        return opacity < .01 ? 0 : opacity;
    }

    internal static bool CanWakeAt(double x, double width, TimeSpan time, TimeSpan now, IReadOnlyList<TaskRange> tasks) =>
        x >= 0 && x < width && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1) && GuideOpacity(time, now, tasks) > 0;

    internal static double CenterOffset(double currentY, double viewportHeight, double? firstShownY, double leadingSpace)
    {
        double centered = Math.Max(0, currentY - viewportHeight / 2);
        return firstShownY is { } first ? Math.Max(centered, first - leadingSpace) : centered;
    }

    internal static double ScrollFrame(double from, double to, double progress)
    {
        double remaining = 1 - Math.Clamp(progress, 0, 1);
        return to - (to - from) * remaining * remaining * remaining;
    }
}
