using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace AwesomeInterview.NativeDemo;

internal static class PortableBackdropSamples
{
    internal static IReadOnlyDictionary<Drawing.Point, Color> Read(IReadOnlyCollection<Drawing.Point> locations)
    {
        var colors = new Dictionary<Drawing.Point, Color>(locations.Count);
        if (locations.Count == 0) return colors;
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var bounds = screen.Bounds;
            // Copy narrow bands so a maximized widget needs no full-screen bitmap.
            var bands = locations.Where(bounds.Contains).GroupBy(point => (point.Y - bounds.Top) / 48);
            foreach (var points in bands) ReadBand(points, colors);
        }
        return colors;
    }

    static void ReadBand(IEnumerable<Drawing.Point> points, Dictionary<Drawing.Point, Color> colors)
    {
        int left = points.Min(point => point.X), top = points.Min(point => point.Y);
        int width = points.Max(point => point.X) - left + 1;
        int height = points.Max(point => point.Y) - top + 1;
        try
        {
            using var bitmap = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppRgb);
            using var graphics = Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(left, top, 0, 0, bitmap.Size, Drawing.CopyPixelOperation.SourceCopy);
            foreach (var point in points)
            {
                var pixel = bitmap.GetPixel(point.X - left, point.Y - top);
                colors[point] = Color.FromRgb(pixel.R, pixel.G, pixel.B);
            }
        }
        catch (ExternalException) { } // The screen can be unavailable during a desktop or display change.
    }
}
