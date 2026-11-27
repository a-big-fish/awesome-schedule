using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Windows.Media;

namespace AwesomeInterview.NativeDemo;

internal static class PortableTextContrast
{
    const double MinimumContrast = 4.5;
    static readonly double[] LightnessLevels = [.14, .22, .30, .38, .46, .54, .62, .70, .78, .86, .92, .96];
    static readonly ConcurrentDictionary<Color, Palette> Palettes = new();
    internal readonly record struct Choice(Color Ink, Color? Outline);
    sealed record Palette(Color[] Shades, Color Dark, Color Light);

    internal static Choice Choose(ReadOnlySpan<Color> backdrop, Color original, double opacity, Color previous)
    {
        if (backdrop.IsEmpty) return new(original, null);

        Span<double> backgroundLuminances = backdrop.Length <= 32 ? stackalloc double[backdrop.Length] : new double[backdrop.Length];
        for (int i = 0; i < backdrop.Length; i++)
            backgroundLuminances[i] = Luminance(backdrop[i].R, backdrop[i].G, backdrop[i].B);

        var originalScore = Score(backdrop, backgroundLuminances, original, opacity);
        if (originalScore.Minimum >= MinimumContrast + (previous == original ? 0 : .25))
            return new(original, null);

        var palette = Palettes.GetOrAdd(original, CreatePalette);
        if (previous != original && Array.IndexOf(palette.Shades, previous) >= 0 &&
            Score(backdrop, backgroundLuminances, previous, opacity).Minimum >= MinimumContrast)
            return new(previous, null);

        if (originalScore.Minimum >= MinimumContrast) return new(original, null);

        foreach (var shade in palette.Shades)
        {
            if (Score(backdrop, backgroundLuminances, shade, opacity).Minimum >= MinimumContrast)
                return new(shade, null);
        }

        var black = Score(backdrop, backgroundLuminances, Colors.Black, opacity);
        var white = Score(backdrop, backgroundLuminances, Colors.White, opacity);
        if (black.Minimum >= MinimumContrast || white.Minimum >= MinimumContrast)
            return new(black.Minimum >= white.Minimum ? Colors.Black : Colors.White, null);

        // Keep the strongest average fill and an opposite outline when the samples span both extremes.
        var dark = Score(backdrop, backgroundLuminances, palette.Dark, opacity);
        var light = Score(backdrop, backgroundLuminances, palette.Light, opacity);
        bool preferLight = Math.Abs(light.Mean - dark.Mean) < .35 && (previous == palette.Light || previous == palette.Dark)
            ? previous == palette.Light
            : light.Mean > dark.Mean;
        return preferLight ? new(palette.Light, palette.Dark) : new(palette.Dark, palette.Light);
    }

    static Palette CreatePalette(Color original)
    {
        var color = PortableOklch.FromSrgb(original);
        var shades = LightnessLevels.OrderBy(level => Math.Abs(level - color.Lightness))
            .Select(color.AtLightness).Distinct().ToArray();
        return new(shades, color.AtLightness(LightnessLevels[0]), color.AtLightness(LightnessLevels[^1]));
    }

    static (double Minimum, double Mean) Score(ReadOnlySpan<Color> backdrop, ReadOnlySpan<double> backgroundLuminances,
        Color ink, double opacity)
    {
        double minimum = double.PositiveInfinity, total = 0;
        var opaqueInk = opacity == 1 ? Luminance(ink.R, ink.G, ink.B) : 0;
        for (int i = 0; i < backdrop.Length; i++)
        {
            var background = backdrop[i];
            double shownInk = opacity == 1 ? opaqueInk : Luminance(
                background.R + (ink.R - background.R) * opacity,
                background.G + (ink.G - background.G) * opacity,
                background.B + (ink.B - background.B) * opacity);
            double contrast = ContrastRatio(shownInk, backgroundLuminances[i]);
            minimum = Math.Min(minimum, contrast);
            total += contrast;
        }
        return (minimum, total / backdrop.Length);
    }

    static double ContrastRatio(double ink, double backdrop) =>
        (Math.Max(ink, backdrop) + .05) / (Math.Min(ink, backdrop) + .05);

    static double Luminance(double red, double green, double blue) =>
        .2126 * PortableOklch.Linearize(red / 255) + .7152 * PortableOklch.Linearize(green / 255) +
        .0722 * PortableOklch.Linearize(blue / 255);
}
