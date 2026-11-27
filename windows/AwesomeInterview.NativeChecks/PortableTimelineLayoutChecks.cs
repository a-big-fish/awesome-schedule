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

        static TimeSpan Hour(double value) => TimeSpan.FromHours(value);
        PortableTimelineLayout.TaskRange[] tasks = [new(Hour(8), Hour(12)), new(Hour(18), Hour(19))];
        var now = Hour(9.5);
        double OpacityAt(double hour) => PortableTimelineLayout.GuideOpacity(Hour(hour), now, tasks);
        check(OpacityAt(8) == 1 && OpacityAt(12) == 1 && OpacityAt(18) == 1 && OpacityAt(19) == 1,
            "guide rules stay visible at the bounds of tasks away from the current time");
        check(OpacityAt(20) == .5 && OpacityAt(20.5) < OpacityAt(20) && OpacityAt(21) == 0 && OpacityAt(6) == 0,
            "the guides fade progressively for two hours beyond either edge of a task");
        check(OpacityAt(15) == 0 && OpacityAt(13) > 0 && OpacityAt(17) > 0,
            "the two independent task ranges leave a genuinely blank gap between their guides");
        check(PortableTimelineLayout.GuideOpacity(Hour(10), now, []) == 1 &&
            PortableTimelineLayout.GuideOpacity(Hour(11.5), now, []) == .5 &&
            PortableTimelineLayout.GuideOpacity(Hour(12.5), now, []) == 0,
            "a day without tasks retains only the current-time guides and their gradual fade");

        bool CanWake(double x, double hour) => PortableTimelineLayout.CanWakeAt(x, 300, Hour(hour), now, tasks);
        check(CanWake(20, 18.5) && CanWake(290, 19.5) && !CanWake(320, 18.5) && !CanWake(110, 15) &&
            !CanWake(20, -.5) && !CanWake(20, 24.5),
            "hover wakes only over the visible time range and width, excluding centering padding");
        var hover = new PortableHoverGesture();
        check(!hover.Update(CanWake(110, 15), false, 110, 450, 0) &&
            !hover.Update(CanWake(110, 15), false, 110, 450, 5000) &&
            !hover.Update(CanWake(110, 18.5), false, 110, 555, 5100) &&
            hover.Update(CanWake(110, 18.5), false, 110, 555, 5900) &&
            !hover.Update(CanWake(320, 18.5), false, 320, 555, 6000),
            "a long dwell in empty space stays locked and moving out of live guides restores pass-through");

        check(PortableTimelineLayout.CenterOffset(330, 240, 210, 20) == 210 &&
            PortableTimelineLayout.CenterOffset(330, 640, 210, 20) == 190 &&
            PortableTimelineLayout.CenterOffset(330, 640, null, 20) == 10 &&
            PortableTimelineLayout.CenterOffset(40, 640, null, 20) == 0,
            "centering caps leading blank space only in the faded view and avoids synthetic time before midnight");
        check(PortableTimelineLayout.ScrollFrame(20, 240, 0) == 20 &&
            PortableTimelineLayout.ScrollFrame(20, 240, .25) is > 20 and < 240 &&
            PortableTimelineLayout.ScrollFrame(20, 240, 1) == 240 &&
            PortableTimelineLayout.ScrollFrame(240, 20, .25) is > 20 and < 240,
            "automatic centering animates from the current offset in either direction");
    }
}
