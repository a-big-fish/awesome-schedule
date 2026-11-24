using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AwesomeInterview.NativeDemo;

internal static class PortableGlyphChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                var title = Label("随身日程", 170);
                var layout = title.PrepareGlyphs()!;
                check(layout.Glyphs.Count == 4, "portable glyph drawing preserves four separate Chinese glyphs");
                layout.Glyphs[0].Fill.Color = Colors.Red;
                layout.Glyphs[1].Fill.Color = Colors.Lime;
                check(ContainsRedAndGreen(Render(title)), "two characters in the same portable label render different colors");
                title.Text = "时间";
                check(title.GlyphLayout == null && title.Background == null, "new portable label text clears old glyph drawings immediately");

                var combined = Label("A😊️é", 170);
                check(combined.PrepareGlyphs()!.Glyphs.Count == 3, "portable glyph drawing retains emoji and combining accents");
                var trimmed = Label("很长一行任务标题和数据", 65, TextTrimming.CharacterEllipsis);
                var trimmedLayout = trimmed.PrepareGlyphs()!;
                check(trimmedLayout.Glyphs.Count == 3 && trimmedLayout.Bounds.Right <= 65,
                    "portable glyph drawing preserves the native ellipsis");
            }
            catch (Exception error) { failure = error; }
        });
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        if (!worker.Join(TimeSpan.FromSeconds(10))) throw new TimeoutException("Portable glyph rendering check timed out.");
        if (failure != null) throw failure;
    }

    static PortableGlyphText Label(string value, double width, TextTrimming trimming = TextTrimming.None)
    {
        var label = new PortableGlyphText
        {
            Text = value, FontSize = 18, FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center, TextTrimming = trimming
        };
        label.Measure(new Size(width, 40));
        label.Arrange(new Rect(0, 0, width, label.DesiredSize.Height));
        _ = Render(label);
        return label;
    }

    static byte[] Render(FrameworkElement label)
    {
        label.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        int width = (int)Math.Ceiling(label.ActualWidth);
        int height = (int)Math.Ceiling(label.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(label);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    static bool ContainsRedAndGreen(byte[] pixels)
    {
        int red = 0, green = 0;
        for (int index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index + 3] < 48 || pixels[index] != 0) continue;
            if (pixels[index + 2] > 48 && pixels[index + 1] == 0) red++;
            if (pixels[index + 1] > 48 && pixels[index + 2] == 0) green++;
        }
        return red > 10 && green > 10;
    }
}
