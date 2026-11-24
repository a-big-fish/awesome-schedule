using System.Windows.Media;
using AwesomeInterview.NativeDemo;

internal static class PortableContrastChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var ink = Color.FromRgb(25, 44, 70);
        var dark = Color.FromRgb(27, 35, 51);
        var light = Color.FromRgb(230, 220, 205);
        var onDark = PortableTextContrast.Choose([dark], ink, 1, ink);
        var onLight = PortableTextContrast.Choose([light], ink, 1, onDark.Ink);
        var mixed = PortableTextContrast.Choose([light, dark], ink, 1, onLight.Ink);
        var faded = PortableTextContrast.Choose([light], ink, .1, ink);
        var unavailable = PortableTextContrast.Choose([], ink, 1, ink);

        check(onDark.Ink.B > onDark.Ink.R && onDark.Outline == null && Ratio(onDark.Ink, dark) >= 4.5,
            "portable contrast chooses a readable blue tint over a dark region");
        var coral = Color.FromRgb(255, 82, 43);
        var onWhite = PortableTextContrast.Choose([Colors.White], coral, 1, coral);
        check(onWhite.Ink.R > onWhite.Ink.B && onWhite.Outline == null && Ratio(onWhite.Ink, Colors.White) >= 4.5,
            "portable contrast darkens saturated colors without losing their hue");
        var saturatedBlue = PortableOklch.FromSrgb(Colors.Blue);
        var mapped = PortableOklch.FromSrgb(saturatedBlue.AtLightness(.85));
        var hueDifference = Math.IEEERemainder(mapped.Hue - saturatedBlue.Hue, 2 * Math.PI);
        check(Math.Abs(mapped.Lightness - .85) < .005 && mapped.Chroma < saturatedBlue.Chroma &&
              Math.Abs(hueDifference) < .03,
            "out-of-gamut tints reduce chroma while preserving OKLCH lightness and hue");
        var midGray = Color.FromRgb(118, 118, 118);
        var onMidGray = PortableTextContrast.Choose([midGray], ink, 1, ink);
        check(onMidGray.Outline == null && Ratio(onMidGray.Ink, midGray) >= 4.5,
            "neutral fallback covers the middle-gray contrast boundary");
        check(onLight.Ink == ink && onLight.Outline == null, "portable contrast keeps brand ink where it is readable");
        check(mixed.Outline is Color edge && edge != mixed.Ink, "portable contrast outlines mixed backgrounds");
        check(faded.Outline != null, "portable contrast accounts for the chosen font transparency");
        check(unavailable.Ink == ink && unavailable.Outline == null, "portable contrast uses the original ink when no sample is available");
    }

    static double Ratio(Color first, Color second)
    {
        static double Linear(byte channel)
        {
            var encoded = channel / 255d;
            return encoded <= .04045 ? encoded / 12.92 : Math.Pow((encoded + .055) / 1.055, 2.4);
        }
        static double Luminance(Color color) => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
}
