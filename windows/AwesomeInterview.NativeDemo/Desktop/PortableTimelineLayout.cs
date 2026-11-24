using System;

namespace AwesomeInterview.NativeDemo;

internal static class PortableTimelineLayout
{
    internal readonly record struct RuleExtent(double Width, double SolidFraction);

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
}
