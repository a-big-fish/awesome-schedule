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

        check(onDark.Ink == Colors.White && onDark.Outline == null, "portable contrast uses light ink over a dark region");
        check(onLight.Ink == ink && onLight.Outline == null, "portable contrast keeps brand ink where it is readable");
        check(mixed.Outline is Color edge && edge != mixed.Ink, "portable contrast outlines mixed backgrounds");
        check(faded.Outline != null, "portable contrast accounts for the chosen font transparency");
        check(unavailable.Ink == ink && unavailable.Outline == Colors.White, "portable contrast falls back when no screen sample is available");
    }
}
