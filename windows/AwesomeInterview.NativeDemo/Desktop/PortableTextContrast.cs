using System;
using System.Windows.Media;

namespace AwesomeInterview.NativeDemo;

internal static class PortableTextContrast
{
    const double MinimumContrast = 4.5;
    static readonly Color Dark = Color.FromRgb(15, 23, 42);
    static readonly Color Light = Colors.White;
    internal readonly record struct Choice(Color Ink, Color? Outline);

    internal static Choice Choose(ReadOnlySpan<Color> backdrop, Color original, double opacity, Color previous)
    {
        if (backdrop.IsEmpty) return new(original, Colors.White);
        var originalScore = Score(backdrop, original, opacity);
        if (originalScore.Minimum >= MinimumContrast + (previous == original ? 0 : .25))
            return new(original, null);

        var dark = Score(backdrop, Dark, opacity);
        var light = Score(backdrop, Light, opacity);
        if (dark.Minimum >= MinimumContrast || light.Minimum >= MinimumContrast)
            return new(dark.Minimum >= light.Minimum ? Dark : Light, null);

        // On mixed backgrounds, give the fill the stronger average contrast and outline it in the opposite shade.
        bool preferLight = Math.Abs(light.Mean - dark.Mean) < .35 && (previous == Light || previous == Dark)
            ? previous == Light
            : light.Mean > dark.Mean;
        return preferLight ? new(Light, Dark) : new(Dark, Light);
    }

    static (double Minimum, double Mean) Score(ReadOnlySpan<Color> backdrop, Color ink, double opacity)
    {
        double minimum = double.PositiveInfinity, total = 0;
        foreach (var background in backdrop)
        {
            double shownInk = Luminance(
                background.R + (ink.R - background.R) * opacity,
                background.G + (ink.G - background.G) * opacity,
                background.B + (ink.B - background.B) * opacity);
            double basis = Luminance(background.R, background.G, background.B);
            double contrast = (Math.Max(shownInk, basis) + .05) / (Math.Min(shownInk, basis) + .05);
            minimum = Math.Min(minimum, contrast);
            total += contrast;
        }
        return (minimum, total / backdrop.Length);
    }

    static double Luminance(double red, double green, double blue) =>
        .2126 * Linear(red / 255) + .7152 * Linear(green / 255) + .0722 * Linear(blue / 255);

    static double Linear(double channel) =>
        channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
}
