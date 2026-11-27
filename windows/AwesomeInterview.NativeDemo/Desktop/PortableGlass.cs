using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pixel = System.Drawing.Point;
using static AwesomeInterview.NativeDemo.PortableGlassOptics;

namespace AwesomeInterview.NativeDemo;

internal sealed record PortableGlassFrame(int Width, int Height, byte[] Pixels, Color Ink, double BackdropDetail = 0)
{
    internal Color RecoverBackdrop(Color composite, double u, double v, double opacity)
    {
        double x = Math.Clamp(u * Width - .5, 0, Width - 1);
        double y = Math.Clamp(v * Height - .5, 0, Height - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, Width - 1), y1 = Math.Min(y0 + 1, Height - 1);
        double Mix(double a, double b, double amount) => a + (b - a) * amount;
        double Channel(int channel, bool premultiply = false)
        {
            double At(int column, int row)
            {
                int i = (row * Width + column) * 4;
                return premultiply ? Pixels[i + channel] * Pixels[i + 3] / 255d : Pixels[i + channel];
            }
            return Mix(Mix(At(x0, y0), At(x1, y0), x - x0), Mix(At(x0, y1), At(x1, y1), x - x0), y - y0);
        }
        double alpha = Channel(3) * opacity / 255;
        byte Recover(byte value, int channel) => (byte)Math.Clamp(Math.Round((value - Channel(channel, premultiply: true) * opacity) / (1 - alpha)), 0, 255);
        return Color.FromRgb(Recover(composite.R, 2), Recover(composite.G, 1), Recover(composite.B, 0));
    }
}

internal sealed class PortableGlass
{
    static readonly Color DarkFrost = Color.FromRgb(32, 42, 56);
    static readonly Color LightFrost = Color.FromRgb(247, 248, 249);
    const int Padding = 7;
    readonly PortableStyle style;
    readonly bool capsule, highlights, roundedPanel;
    readonly int columns, rows, sampleWidth, sampleHeight;
    readonly double dx, dy, strength;
    readonly Pixel[] points;

    internal Rect ScreenBounds { get; }
    internal Rect LocalBounds { get; }
    internal Rect[] ExcludedInk { get; }
    internal IReadOnlyList<Pixel> SamplePoints => points;
    internal bool IsReadabilityBacking => style.GlassStrength == 0;

    internal PortableGlass(Rect local, Rect screen, PortableStyle style, Rect[]? excludedInk = null,
        bool capsule = false, bool highlights = true, bool roundedPanel = false)
    {
        LocalBounds = local;
        ScreenBounds = screen;
        ExcludedInk = excludedInk ?? [];
        this.style = style;
        this.capsule = capsule;
        this.highlights = highlights;
        this.roundedPanel = roundedPanel;
        strength = style.GlassStrength / 100d;
        columns = Math.Clamp((int)Math.Ceiling(screen.Width / 2.2), 8, 128);
        rows = Math.Clamp((int)Math.Ceiling(screen.Height / 1.5), 8, 40);
        sampleWidth = columns + 2 * Padding;
        sampleHeight = rows + 2 * Padding;
        dx = screen.Width / columns;
        dy = screen.Height / rows;
        points = new Pixel[sampleWidth * sampleHeight];
        for (int y = 0; y < sampleHeight; y++)
            for (int x = 0; x < sampleWidth; x++)
                points[y * sampleWidth + x] = new((int)Math.Floor(screen.Left + (x + .5 - Padding) * dx),
                    (int)Math.Floor(screen.Top + (y + .5 - Padding) * dy));
    }

    internal static Rect Expand(Rect ink, int feather)
    {
        ink.Inflate(feather, Math.Round(feather * .58));
        return ink;
    }

    internal byte[] Render(IReadOnlyDictionary<Pixel, Color> backdrop) => RenderFrame(backdrop).Pixels;

    internal PortableGlassFrame RenderFrame(IReadOnlyDictionary<Pixel, Color> backdrop,
        PortableGlassFrame? previous = null, double parentOpacity = 1, bool recover = true)
    {
        var samples = new Color?[points.Length];
        double total = 0, interior = 0;
        double darkest = 255, brightest = 0;
        int count = 0, interiorCount = 0;
        for (int i = 0; i < points.Length; i++)
        {
            var point = points[i];
            if (!backdrop.TryGetValue(point, out var color)) continue;
            bool inside = ScreenBounds.Contains(point.X + .5, point.Y + .5);
            if (recover && previous is { Pixels.Length: > 0 } && inside)
                color = previous.RecoverBackdrop(color, (point.X + .5 - ScreenBounds.X) / ScreenBounds.Width,
                    (point.Y + .5 - ScreenBounds.Y) / ScreenBounds.Height, parentOpacity);
            samples[i] = color;
            double brightness = Brightness(color);
            darkest = Math.Min(darkest, brightness);
            brightest = Math.Max(brightest, brightness);
            total += brightness;
            count++;
            if (inside)
            {
                interior += brightness;
                interiorCount++;
            }
        }

        var ink = style.GlassTone == "dark" ? Colors.White : Colors.Black;
        if (IsReadabilityBacking || style.GlassTone == "auto")
        {
            double threshold = previous?.Ink == Colors.White ? 143 : previous?.Ink == Colors.Black ? 112 : 126;
            double brightness = interiorCount >= 4 ? interior / interiorCount : total / Math.Max(1, count);
            ink = count == 0 ? previous?.Ink ?? Colors.Black : brightness < threshold ? Colors.White : Colors.Black;
        }
        var tint = ink == Colors.White ? DarkFrost : LightFrost;
        // Thin text strokes can occupy very few samples, even where the average looks white.
        double detail = count >= 8 ? Ramp((brightest - darkest - 36) / 100) : previous?.BackdropDetail ?? 0;
        if (previous != null && Math.Abs(previous.BackdropDetail - detail) < .08) detail = previous.BackdropDetail;
        if (IsReadabilityBacking && detail < (previous is { Pixels.Length: > 0 } ? .06 : .14))
            return new(columns, rows, [], ink);
        if (!IsReadabilityBacking) CompleteTexture(samples, tint);
        var pixels = Paint(samples, tint, detail);
        return new(columns, rows, pixels, ink, detail);
    }

    byte[] Paint(Color?[] texture, Color tint, double detail)
    {
        var pixels = new byte[columns * rows * 4];
        var size = LocalBounds.Size;
        double xScale = size.Width / columns, yScale = size.Height / rows;
        double bevel = Math.Clamp(Math.Min(size.Width, size.Height) * .29, 2, 8);
        double blur = Math.Clamp(style.GlassRadius / 3d, 2, Padding - 1);
        double bodyOpacity = IsReadabilityBacking ? .74 + .20 * detail : strength + (.94 - strength) * (.45 + .55 * detail);
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                var surface = At(new((x + .5) * xScale, (y + .5) * yScale), size, capsule && !roundedPanel);
                double coverage = Ramp(.5 + surface.Depth / Math.Max(xScale, yScale));
                if (!capsule) coverage *= Ramp(surface.Depth / Math.Min(5, style.GlassFeather / 2d));
                if (coverage == 0) continue;

                double slope = Ramp(1 - surface.Depth / bevel);
                // The bevel keeps the sampled color. Under the glyphs the surface instead
                // blends a wider neighborhood with a neutral tint and covers sharp strokes.
                double core = Ramp(surface.Depth / Math.Min(4, size.Height / 4));
                var color = tint;
                double rimOpacity = 0;
                if (!IsReadabilityBacking)
                {
                    double bend = style.GlassRefraction * slope;
                    double sx = x + Padding + surface.Normal.X * bend / xScale;
                    double sy = y + Padding + surface.Normal.Y * bend / yScale;
                    var sampled = ReadTexture(texture, sx, sy);
                    var horizontal = Blend(ReadTexture(texture, sx - blur, sy), ReadTexture(texture, sx + blur, sy), .5);
                    var vertical = Blend(ReadTexture(texture, sx, sy - blur), ReadTexture(texture, sx, sy + blur), .5);
                    var frosted = Blend(Blend(sampled, Blend(horizontal, vertical, .5), .82), tint, .72 + .22 * detail);
                    var rim = Blend(sampled, tint, style.GlassTone == "auto" ? .22 : .85);
                    double facing = -.6 * surface.Normal.X - .8 * surface.Normal.Y;
                    double edgeLight = highlights ? slope * (.10 + .30 * Math.Max(0, facing)) : .08 * slope;
                    double edgeShade = highlights ? slope * .15 * Math.Max(0, -facing) : 0;
                    color = Blend(Blend(Blend(rim, frosted, core), Colors.Black, edgeShade), Colors.White, edgeLight);
                    rimOpacity = strength * .60 + edgeLight * .20;
                }
                int index = (y * columns + x) * 4;
                pixels[index] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
                pixels[index + 3] = (byte)Math.Round(coverage * (rimOpacity + (bodyOpacity - rimOpacity) * core) * 255);
            }
        return pixels;
    }

    Color ReadTexture(Color?[] field, double x, double y)
    {
        x = Math.Clamp(x, 0, sampleWidth - 1);
        y = Math.Clamp(y, 0, sampleHeight - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, sampleWidth - 1), y1 = Math.Min(y0 + 1, sampleHeight - 1);
        Color At(int column, int row) => field[row * sampleWidth + column]!.Value;
        return Blend(Blend(At(x0, y0), At(x1, y0), x - x0), Blend(At(x0, y1), At(x1, y1), x - x0), y - y0);
    }

    void CompleteTexture(Color?[] samples, Color fallback)
    {
        var original = (Color?[])samples.Clone();
        for (int row = 0; row < sampleHeight; row++)
            InterpolateLine(samples, row * sampleWidth, 1, sampleWidth);
        for (int x = 0; x < sampleWidth; x++)
            InterpolateLine(original, x, sampleWidth, sampleHeight);
        for (int i = 0; i < samples.Length; i++)
        {
            if (samples[i] is { } h && original[i] is { } v) samples[i] = Blend(h, v, .5);
            else samples[i] ??= original[i] ?? fallback;
        }
    }

    static void InterpolateLine(Color?[] data, int start, int step, int length)
    {
        int previous = -1;
        for (int i = 0; i < length; i++)
        {
            if (data[start + i * step] is not { } next) continue;
            for (int j = previous + 1; j < i; j++)
                data[start + j * step] = previous < 0 ? next :
                    Blend(data[start + previous * step]!.Value, next, (double)(j - previous) / (i - previous));
            previous = i;
        }
        if (previous >= 0)
            for (int j = previous + 1; j < length; j++)
                data[start + j * step] = data[start + previous * step];
    }

    internal BitmapSource Image(byte[] pixels)
    {
        var bitmap = BitmapSource.Create(columns, rows, 96, 96, PixelFormats.Bgra32, null, pixels, columns * 4);
        bitmap.Freeze();
        return bitmap;
    }
}

internal sealed class PortableGlassStrip : Grid
{
    readonly Image backdrop = new() { IsHitTestVisible = false, Stretch = Stretch.Fill };
    internal PortableGlassFrame? Frame { get; private set; }
    internal bool Highlights { get; }
    internal bool RoundedPanel { get; }
    internal FrameworkElement Child { get; }

    internal PortableGlassStrip(FrameworkElement content, bool showRim = true, bool roundedPanel = false)
    {
        Highlights = showRim;
        RoundedPanel = roundedPanel;
        Child = content;
        var material = new Canvas { IsHitTestVisible = false };
        material.Children.Add(backdrop);
        Children.Add(material);
        Children.Add(content);
        SizeChanged += (_, _) =>
        {
            backdrop.Width = ActualWidth;
            backdrop.Height = ActualHeight;
        };
    }

    internal bool SetGlass(PortableGlass? glass, PortableGlassFrame? frame)
    {
        if (glass == null || frame == null)
        {
            if (Frame == null) return false;
            Frame = null;
            backdrop.Source = null;
            return true;
        }
        if (Frame is { } previous && frame.Width == previous.Width && frame.Height == previous.Height &&
            frame.Ink == previous.Ink && frame.Pixels.AsSpan().SequenceEqual(previous.Pixels)) return false;
        Frame = frame;
        backdrop.Source = glass.Image(frame.Pixels);
        return true;
    }
}
