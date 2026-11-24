using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace AwesomeInterview.NativeDemo;

internal sealed class PortableGlyphText : TextBlock
{
    object originalForeground = DependencyProperty.UnsetValue;
    double glyphDpi;
    bool layoutDirty;
    internal PortableGlyphLayout? GlyphLayout { get; private set; }
    internal SolidColorBrush? NativeInk { get; private set; }

    static PortableGlyphText() => TextProperty.OverrideMetadata(typeof(PortableGlyphText),
        new FrameworkPropertyMetadata("", (element, _) => ((PortableGlyphText)element).RestoreNativeInk()));

    public PortableGlyphText() => SizeChanged += (_, _) =>
    {
        if (GlyphLayout == null || layoutDirty) return;
        layoutDirty = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (GlyphLayout != null && layoutDirty) PrepareGlyphs();
        }));
    };

    internal PortableGlyphLayout? PrepareGlyphs()
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var previous = GlyphLayout;
        if (previous != null && !layoutDirty && glyphDpi == dpi) return previous;
        if ((previous == null && Background != null) || (NativeInk ?? Foreground as SolidColorBrush) is not { } ink ||
            VisualTreeHelper.GetDrawing(this) is not { } drawing) return previous;

        var layout = new PortableGlyphLayout(drawing, RenderSize, ink);
        if (layout.Glyphs.Count == 0) return previous;
        if (previous != null) layout.InheritColors(previous);
        else
        {
            originalForeground = ReadLocalValue(ForegroundProperty);
            Foreground = Brushes.Transparent;
        }
        NativeInk = ink;
        glyphDpi = dpi;
        layoutDirty = false;
        GlyphLayout = layout;
        Background = layout.Surface;
        return layout;
    }

    internal void UpdateNativeInk(SolidColorBrush ink)
    {
        if (GlyphLayout is not { } layout || ReferenceEquals(NativeInk, ink)) return;
        NativeInk = ink;
        layout.SetInkSource(ink);
    }

    internal void RestoreNativeInk()
    {
        if (GlyphLayout == null) return;
        GlyphLayout = null;
        NativeInk = null;
        layoutDirty = false;
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
