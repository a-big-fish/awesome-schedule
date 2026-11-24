using AwesomeInterview.NativeDemo;

internal static class PortableTimelineLayoutChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(PortableTimelineLayout.NearestHourToHide(new TimeSpan(14, 29, 0)) == 14 &&
            PortableTimelineLayout.NearestHourToHide(new TimeSpan(14, 31, 0)) == 15,
            "only the closer fixed hour gives way to the current-time badge");
        check(PortableTimelineLayout.NearestHourToHide(new TimeSpan(23, 59, 0)) == -1,
            "the day boundary does not hide a distant fixed time");
        var wide = PortableTimelineLayout.Rule(1400, 720);
        check(wide.Width == 752 && Math.Abs(wide.Width * wide.SolidFraction - 720) < .001,
            "the grid fades after the occupied lanes and stops before the blank area");
        check(PortableTimelineLayout.Rule(480, 480) == new PortableTimelineLayout.RuleExtent(480, 1) &&
            PortableTimelineLayout.Rule(480, 0).Width == 0,
            "compact populated timelines keep their guide lines and empty timelines omit them");
    }
}
