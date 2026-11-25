using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pixel = System.Drawing.Point;

namespace AwesomeInterview.NativeDemo;

internal sealed class PortableGlass
{
    readonly int columns, rows, blurRadius;
    readonly double featherX, featherY, strength;
    readonly Pixel[] top, bottom, sides;
    internal Rect ScreenBounds { get; }
    internal Rect LocalBounds { get; }

    internal PortableGlass(Rect local, Rect screen, PortableStyle style)
    {
        LocalBounds = local;
        ScreenBounds = screen;
        columns = Math.Clamp((int)Math.Ceiling(screen.Width / 4), 6, 96);
        rows = Math.Clamp((int)Math.Ceiling(screen.Height / 3), 6, 22);
        blurRadius = Math.Clamp((int)Math.Round(style.GlassRadius * columns / local.Width), 1, 8);
        featherX = Math.Min(.45, style.GlassFeather / local.Width);
        featherY = Math.Min(.46, style.GlassFeather / local.Height);
        strength = style.GlassStrength / 100d;
        int above = (int)Math.Floor(screen.Top) - 2;
        int below = (int)Math.Ceiling(screen.Bottom) + 2;
        top = new Pixel[columns];
        bottom = new Pixel[columns];
        for (int index = 0; index < columns; index++)
        {
            int x = (int)Math.Floor(screen.X + (index + .5) * screen.Width / columns);
            top[index] = new(x, above);
            bottom[index] = new(x, below);
        }
        int middle = (int)Math.Round(screen.Top + screen.Height / 2);
        sides = [new((int)Math.Floor(screen.Left) - 2, middle), new((int)Math.Ceiling(screen.Right) + 2, middle)];
    }

    // GDI captures the composited desktop. The guard in the caller removes other visible
    // glass and ink footprints so neither the glass nor the text is fed back into the blur.
    internal IEnumerable<Pixel> SamplePoints => top.Concat(bottom).Concat(sides);

    internal byte[]? Render(IReadOnlyDictionary<Pixel, Color> backdrop)
    {
        var all = Average(top, 0, top.Length, backdrop, out int topCount);
        var lower = Average(bottom, 0, bottom.Length, backdrop, out int bottomCount);
        var side = Average(sides, 0, sides.Length, backdrop, out int sideCount);
        if (topCount + bottomCount + sideCount == 0) return null;
        var fallback = topCount > 0 && bottomCount > 0 ? Blend(all, lower, .5)
            : topCount > 0 ? all : bottomCount > 0 ? lower : side;

        var result = new byte[columns * rows * 4];
        for (int x = 1; x < columns - 1; x++)
        {
            int start = Math.Max(0, x - blurRadius);
            int end = Math.Min(columns, x + blurRadius + 1);
            var upper = Average(top, start, end, backdrop, out int upperCount);
            var beneath = Average(bottom, start, end, backdrop, out int beneathCount);
            if (upperCount == 0) upper = beneathCount > 0 ? beneath : fallback;
            if (beneathCount == 0) beneath = upper;
            for (int y = 1; y < rows - 1; y++)
            {
                var color = Blend(upper, beneath, (double)y / (rows - 1));
                double edge = Math.Min(Edge(x, columns, featherX), Edge(y, rows, featherY));
                int index = (y * columns + x) * 4;
                result[index] = color.B;
                result[index + 1] = color.G;
                result[index + 2] = color.R;
                result[index + 3] = (byte)Math.Round(strength * edge * 255);
            }
        }
        return result;
    }

    internal BitmapSource Image(byte[] pixels)
    {
        var bitmap = BitmapSource.Create(columns, rows, 96, 96, PixelFormats.Bgra32, null, pixels, columns * 4);
        bitmap.Freeze();
        return bitmap;
    }

    static Color Average(Pixel[] points, int start, int end, IReadOnlyDictionary<Pixel, Color> samples, out int count)
    {
        int red = 0, green = 0, blue = 0;
        count = 0;
        for (int i = start; i < end; i++)
        {
            if (!samples.TryGetValue(points[i], out var color)) continue;
            red += color.R;
            green += color.G;
            blue += color.B;
            count++;
        }
        int divisor = Math.Max(1, count);
        return Color.FromRgb((byte)(red / divisor), (byte)(green / divisor), (byte)(blue / divisor));
    }

    static Color Blend(Color upper, Color lower, double fraction)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * fraction);
        return Color.FromRgb(Channel(upper.R, lower.R), Channel(upper.G, lower.G), Channel(upper.B, lower.B));
    }

    static double Edge(int index, int length, double feather)
    {
        double distance = (double)Math.Min(index, length - 1 - index) / (length - 1);
        double t = Math.Min(1, distance / feather);
        return t * t * (3 - 2 * t);
    }
}
