using System;
using System.Windows.Media;

namespace AwesomeInterview.NativeDemo;

internal readonly record struct PortableOklch(double Lightness, double Chroma, double Hue)
{
    // Linear sRGB and Oklab matrices by Björn Ottosson (https://bottosson.github.io/posts/oklab/).
    internal static PortableOklch FromSrgb(Color color)
    {
        var red = Linearize(color.R / 255d);
        var green = Linearize(color.G / 255d);
        var blue = Linearize(color.B / 255d);
        var l = Math.Cbrt(.4122214708 * red + .5363325363 * green + .0514459929 * blue);
        var m = Math.Cbrt(.2119034982 * red + .6806995451 * green + .1073969566 * blue);
        var s = Math.Cbrt(.0883024619 * red + .2817188376 * green + .6299787005 * blue);
        var a = 1.9779984951 * l - 2.4285922050 * m + .4505937099 * s;
        var b = .0259040371 * l + .7827717662 * m - .8086757660 * s;
        var chroma = Math.Sqrt(a * a + b * b);
        return new(.2104542553 * l + .7936177850 * m - .0040720468 * s,
            chroma < .000001 ? 0 : chroma, Math.Atan2(b, a));
    }

    internal Color AtLightness(double lightness)
    {
        var hueA = Math.Cos(Hue);
        var hueB = Math.Sin(Hue);
        var rgb = ToLinearSrgb(lightness, Chroma * hueA, Chroma * hueB);
        if (!rgb.IsInGamut)
        {
            // Find the most chromatic sRGB color at the requested lightness and original hue.
            double lower = 0, upper = Chroma;
            rgb = ToLinearSrgb(lightness, 0, 0);
            for (int i = 0; i < 12; i++)
            {
                var middle = (lower + upper) / 2;
                var candidate = ToLinearSrgb(lightness, middle * hueA, middle * hueB);
                if (candidate.IsInGamut)
                {
                    lower = middle;
                    rgb = candidate;
                }
                else
                {
                    upper = middle;
                }
            }
        }
        return Color.FromRgb(Encode(rgb.Red), Encode(rgb.Green), Encode(rgb.Blue));
    }

    static LinearRgb ToLinearSrgb(double lightness, double a, double b)
    {
        var l = Cube(lightness + .3963377774 * a + .2158037573 * b);
        var m = Cube(lightness - .1055613458 * a - .0638541728 * b);
        var s = Cube(lightness - .0894841775 * a - 1.2914855480 * b);
        return new(4.0767416621 * l - 3.3077115913 * m + .2309699292 * s,
            -1.2684380046 * l + 2.6097574011 * m - .3413193965 * s,
            -.0041960863 * l - .7034186147 * m + 1.7076147010 * s);
    }

    static double Cube(double value) => value * value * value;

    internal static double Linearize(double channel) =>
        channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);

    static byte Encode(double channel)
    {
        var srgb = channel <= .0031308 ? 12.92 * channel : 1.055 * Math.Pow(channel, 1 / 2.4) - .055;
        return (byte)Math.Round(srgb * 255);
    }

    readonly record struct LinearRgb(double Red, double Green, double Blue)
    {
        internal bool IsInGamut => Red is >= 0 and <= 1 && Green is >= 0 and <= 1 && Blue is >= 0 and <= 1;
    }
}
