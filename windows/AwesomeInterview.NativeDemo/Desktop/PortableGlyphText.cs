using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AwesomeInterview.NativeDemo;

internal sealed class PortableGlyphText : TextBlock
{
    object originalForeground = DependencyProperty.UnsetValue;
    double glyphDpi;
    internal PortableGlyphLayout? GlyphLayout { get; private set; }
    internal SolidColorBrush? NativeInk { get; private set; }

    static PortableGlyphText() => TextProperty.OverrideMetadata(typeof(PortableGlyphText),
        new FrameworkPropertyMetadata("", (element, _) => ((PortableGlyphText)element).RestoreNativeInk()));

    public PortableGlyphText() => SizeChanged += (_, _) => RestoreNativeInk();

    internal PortableGlyphLayout? PrepareGlyphs()
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (GlyphLayout != null)
        {
            if (glyphDpi == dpi) return GlyphLayout;
            RestoreNativeInk();
        }
        if (Background != null || Foreground is not SolidColorBrush ink ||
            VisualTreeHelper.GetDrawing(this) is not { } drawing) return null;

        var layout = new PortableGlyphLayout(drawing, RenderSize, ink);
        if (layout.Glyphs.Count == 0) return null;
        originalForeground = ReadLocalValue(ForegroundProperty);
        NativeInk = ink;
        glyphDpi = dpi;
        GlyphLayout = layout;
        Foreground = Brushes.Transparent;
        Background = layout.Surface;
        return layout;
    }

    internal void RestoreNativeInk()
    {
        if (GlyphLayout == null) return;
        GlyphLayout = null;
        NativeInk = null;
        ClearValue(BackgroundProperty);
        if (originalForeground == DependencyProperty.UnsetValue) ClearValue(ForegroundProperty);
        else SetValue(ForegroundProperty, originalForeground);
        originalForeground = DependencyProperty.UnsetValue;
    }
}

internal sealed class PortableGlyphTemplateSelector : DataTemplateSelector
{
    internal static PortableGlyphTemplateSelector Instance { get; } = new();
    readonly DataTemplate plainText;

    PortableGlyphTemplateSelector()
    {
        var label = new FrameworkElementFactory(typeof(PortableGlyphText));
        label.SetBinding(TextBlock.TextProperty, new Binding());
        plainText = new DataTemplate { VisualTree = label };
    }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is string ? plainText : null;
}
