using AwesomeInterview.NativeDemo;

static class PortableHoverChecks
{
    public static void Run(Action<bool, string> check)
    {
        var hover = new PortableHoverGesture();
        check(!hover.Update(true, false, 10, 10, 0) && !hover.Update(true, false, 12, 10, 799),
            "hover remains click-through before dwell");
        check(hover.Update(true, false, 12, 10, 800), "steady dwell enables temporary interaction");
        check(hover.Update(false, true, 50, 10, 900), "moving outside during a drag keeps interaction");
        check(!hover.Update(false, false, 50, 10, 1000), "leaving after the drag restores pass-through");
        check(!hover.Update(true, false, 10, 10, 1100) && !hover.Update(true, false, 17, 10, 1800) &&
            !hover.Update(true, false, 17, 10, 1900),
            "movement resets the dwell time");
        check(!hover.Update(true, true, 17, 10, 2700) && !hover.Update(true, false, 17, 10, 3000),
            "pressed mouse buttons cannot trigger dwell");
    }
}
