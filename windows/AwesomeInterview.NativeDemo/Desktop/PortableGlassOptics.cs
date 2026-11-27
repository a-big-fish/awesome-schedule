using System;
using System.Windows;
using System.Windows.Media;

namespace AwesomeInterview.NativeDemo;

internal static class PortableGlassOptics
{
    internal readonly record struct Surface(double Depth, Vector Normal);

    internal static Surface At(Point point, Size size, bool capsule)
    {
        double halfWidth = size.Width / 2;
        double halfHeight = size.Height / 2;
        double radius = capsule ? Math.Min(halfHeight, halfWidth) : Math.Min(9, Math.Min(halfHeight, halfWidth));
        var x = point.X - halfWidth;
        var y = point.Y - halfHeight;
        double qx = Math.Abs(x) - halfWidth + radius, qy = Math.Abs(y) - halfHeight + radius;
        double cx = Math.Max(qx, 0), cy = Math.Max(qy, 0);
        double corner = Math.Sqrt(cx * cx + cy * cy);
        double distance = corner + Math.Min(Math.Max(qx, qy), 0) - radius;
        var normal = corner > 0 ? new Vector(cx * Math.Sign(x) / corner, cy * Math.Sign(y) / corner)
            : qx > qy ? new Vector(x < 0 ? -1 : 1, 0) : new Vector(0, y < 0 ? -1 : 1);
        return new(-distance, normal);
    }

    internal static double Ramp(double value)
    {
        double s = Math.Clamp(value, 0, 1);
        return s * s * (3 - 2 * s);
    }

    internal static Color Blend(Color first, Color second, double fraction)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * fraction);
        return Color.FromRgb(Channel(first.R, second.R), Channel(first.G, second.G), Channel(first.B, second.B));
    }

    internal static double Brightness(Color color) => .2126 * color.R + .7152 * color.G + .0722 * color.B;
}
