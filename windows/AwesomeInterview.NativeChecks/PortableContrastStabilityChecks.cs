using System.Windows.Media;
using AwesomeInterview.NativeDemo;

internal static class PortableContrastStabilityChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var cadence = new PortableContrastCadence();
        double[] intervals = [cadence.AfterSample(true).TotalSeconds,
            .. Enumerable.Range(0, 5).Select(_ => cadence.AfterSample(false).TotalSeconds)];
        check(intervals.SequenceEqual(new double[] { 1, 2, 2, 2, 2, 2 }),
            "the parked widget keeps polling for changes inside the same app");
        check(cadence.AfterSample(true) == TimeSpan.FromSeconds(1), "a changed backdrop restores the fast interval");
        cadence.AfterSample(false);
        cadence.Reset();
        check(cadence.AfterSample(false) == TimeSpan.FromSeconds(2), "widget activity resets the sampling backoff");

        var ink = Color.FromRgb(25, 44, 70);
        var bright = Color.FromRgb(220, 218, 215);
        var dim = Color.FromRgb(23, 31, 44);
        var history = new PortableGlyphContrastHistory();
        var initial = history.Observe([bright, bright, bright, bright], ink, 1, true);
        var noise = history.Observe([bright, Color.FromRgb(209, 217, 214), bright, bright], ink, 1, false);
        check(noise.Choice == initial.Choice && !noise.BackgroundChanged,
            "small pixel variations cannot restart a glyph fade");
        var transient = history.Observe([dim, dim, dim, dim], ink, 1, false);
        var recovered = history.Observe([bright, bright, bright, bright], ink, 1, false);
        check(transient.BackgroundChanged && transient.Choice == initial.Choice && recovered.Choice == initial.Choice,
            "a one-frame background change cannot flicker the glyph");
        history.Observe([dim, dim, dim, dim], ink, 1, false);
        var committed = history.Observe([dim, dim, dim, dim], ink, 1, false);
        check(committed.Choice.Ink != initial.Choice.Ink &&
              committed.Choice == PortableTextContrast.Choose([dim], ink, 1, ink),
            "a sustained backdrop change commits the stronger glyph contrast");
        check(history.Observe([], ink, 1, false).Choice == committed.Choice,
            "an unavailable capture retains the committed glyph shade");
        check(history.Observe([bright, bright, bright, bright], ink, 1, true).Choice.Ink == ink,
            "the first capture after movement adapts without waiting for the next poll");
    }
}
