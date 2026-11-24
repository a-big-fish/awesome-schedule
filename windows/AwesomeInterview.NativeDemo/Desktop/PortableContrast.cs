using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Pixel = System.Drawing.Point;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class DesktopIntegration
{
    static readonly TimeSpan PortableSettleTime = TimeSpan.FromMilliseconds(450);
    static readonly TimeSpan PortableContrastRefresh = TimeSpan.FromSeconds(2);
    static readonly Duration PortableInkTransition = new(TimeSpan.FromMilliseconds(180));
    readonly List<PortableGlyphText> displayedPortableText = new();
    DispatcherTimer? portableContrastTimer;
    HwndSource? portableContrastSource;
    long portableContrastRevision;
    bool portableMoving, portableSampling, portableSessionLocked;

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool WTSRegisterSessionNotification(nint window, uint flags);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool WTSUnRegisterSessionNotification(nint window);

    readonly record struct PortableLabelSample(PortableGlyphText Label, PortableGlyphLayout Layout,
        Matrix ToScreen, Rect InkBounds, Rect Viewport, double Opacity);
    readonly record struct PortableGlyphSample(PortableGlyphLayout.Glyph Glyph, Pixel[] Points);

    void AttachPortableContrast(Window window)
    {
        portableContrastTimer = new DispatcherTimer(DispatcherPriority.Background);
        portableContrastTimer.Tick += SamplePortableContrast;
        window.SourceInitialized += (_, _) =>
        {
            portableContrastSource = PresentationSource.FromVisual(window) as HwndSource;
            if (portableContrastSource is { } source)
            {
                source.AddHook(PortableContrastWindowMessage);
                WTSRegisterSessionNotification(source.Handle, 0);
            }
        };
        window.IsVisibleChanged += (_, _) => SchedulePortableContrast();
        window.LocationChanged += (_, _) => SchedulePortableContrast();
        window.SizeChanged += (_, _) => SchedulePortableContrast();
        window.DpiChanged += (_, _) => SchedulePortableContrast();
        window.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, args) =>
        {
            if (args.HorizontalChange != 0 || args.VerticalChange != 0) SchedulePortableContrast();
        }), handledEventsToo: true);
    }

    nint PortableContrastWindowMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int EnterSizeMove = 0x0231, ExitSizeMove = 0x0232, SessionChanged = 0x02B1;
        const int SessionLock = 7, SessionUnlock = 8;
        if (message == SessionChanged && (wParam == SessionLock || wParam == SessionUnlock))
        {
            portableSessionLocked = wParam == SessionLock;
            SchedulePortableContrast();
        }
        if (message == EnterSizeMove)
        {
            portableMoving = true;
            SchedulePortableContrast();
            foreach (var label in displayedPortableText)
                if (label.GlyphLayout is { } layout)
                    foreach (var glyph in layout.Glyphs)
                    {
                        Freeze(glyph.Fill, SolidColorBrush.ColorProperty);
                        Freeze(glyph.Outline, SolidColorBrush.ColorProperty);
                        Freeze(glyph.Outline, Brush.OpacityProperty);
                    }
        }
        else if (message == ExitSizeMove)
        {
            portableMoving = false;
            SchedulePortableContrast();
            RestartPortableIdleTimer();
        }
        return 0;
    }

    static void Freeze(Animatable value, DependencyProperty property)
    {
        if (!value.HasAnimatedProperties) return;
        var shown = value.GetValue(property);
        value.BeginAnimation(property, null);
        value.SetValue(property, shown);
    }

    void SchedulePortableContrast()
    {
        if (portableContrastTimer == null) return;
        portableContrastRevision++;
        portableContrastTimer.Stop();
        if (portable is not { IsVisible: true } || portableMoving || portableSessionLocked) return;
        portableContrastTimer.Interval = PortableSettleTime;
        portableContrastTimer.Start();
    }

    void StopPortableContrast()
    {
        portableContrastRevision++;
        portableContrastTimer?.Stop();
        portableContrastTimer = null;
        if (portableContrastSource is { IsDisposed: false } source)
        {
            WTSUnRegisterSessionNotification(source.Handle);
            source.RemoveHook(PortableContrastWindowMessage);
        }
        portableContrastSource = null;
        portableMoving = false;
        portableSessionLocked = false;
        displayedPortableText.Clear();
    }

    async void SamplePortableContrast(object? sender, EventArgs args)
    {
        if (sender is not DispatcherTimer timer || timer != portableContrastTimer ||
            portable is not { IsVisible: true } || portableMoving || portableSessionLocked) return;
        if (portableSampling) return;
        timer.Interval = PortableContrastRefresh;
        long revision = portableContrastRevision;

        var labels = new List<PortableLabelSample>();
        CollectPortableLabels(portableHeader, labels);
        CollectPortableLabels(portableContent, labels);
        if (portableUnlock is { IsVisible: true, Content: FrameworkElement unlock })
            CollectPortableLabels(unlock, labels);
        displayedPortableText.Clear();
        displayedPortableText.AddRange(labels.Select(sample => sample.Label));

        if (SystemParameters.HighContrast)
        {
            foreach (var label in labels)
                foreach (var glyph in label.Layout.Glyphs)
                    PaintPortableGlyph(glyph, new(SystemColors.WindowTextColor, null), false);
            return;
        }

        var samples = labels.Select(label => PortableSamplePoints(label, labels)).ToArray();
        var locations = samples.SelectMany(label => label).SelectMany(glyph => glyph.Points).Distinct().ToArray();
        var sampler = readBackdrop ?? PortableBackdropSamples.Read;
        portableSampling = true;
        try
        {
            var colors = await Task.Run(() => sampler(locations));
            if (revision != portableContrastRevision) return;
            var backdrop = new Color[PortableGlyphLayout.SamplesPerGlyph];
            bool animate = SystemParameters.ClientAreaAnimation;
            for (int index = 0; index < labels.Count; index++)
            {
                var label = labels[index];
                if (label.Label.GlyphLayout != label.Layout || label.Label.NativeInk is not { } source) continue;
                foreach (var (glyph, points) in samples[index])
                {
                    int count = 0;
                    foreach (var point in points)
                        if (colors.TryGetValue(point, out var color)) backdrop[count++] = color;
                    var choice = PortableTextContrast.Choose(backdrop.AsSpan(0, count), source.Color,
                        source.Opacity * label.Opacity, glyph.Fill.Color);
                    PaintPortableGlyph(glyph, choice, animate);
                }
            }
        }
        finally { portableSampling = false; }
    }

    static void CollectPortableLabels(FrameworkElement? root, List<PortableLabelSample> labels)
    {
        if (root is not { IsVisible: true, ActualWidth: > 0, ActualHeight: > 0 } ||
            PresentationSource.FromVisual(root) == null) return;
        CollectPortableLabels(root, ScreenBounds(root), 1, labels);
    }

    static void CollectPortableLabels(Visual visual, Rect viewport, double opacity, List<PortableLabelSample> labels)
    {
        if (visual is FrameworkElement element)
        {
            if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
            opacity *= element.Opacity;
            if (element.ClipToBounds || element is ScrollContentPresenter)
            {
                viewport.Intersect(ScreenBounds(element));
                if (viewport.IsEmpty) return;
            }
        }

        if (visual is PortableGlyphText { Text.Length: > 0 } text)
        {
            var toScreen = PortableScreenTransform(text);
            var layoutBounds = new MatrixTransform(toScreen).TransformBounds(new Rect(text.RenderSize));
            if (!layoutBounds.IntersectsWith(viewport) || text.PrepareGlyphs() is not { } layout) return;

            var ink = new MatrixTransform(toScreen).TransformBounds(layout.Bounds);
            ink.Intersect(layoutBounds);
            ink.Intersect(viewport);
            if (!ink.IsEmpty) labels.Add(new(text, layout, toScreen, ink, viewport, opacity));
            return;
        }

        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            if (VisualTreeHelper.GetChild(visual, index) is Visual child)
                CollectPortableLabels(child, viewport, opacity, labels);
    }

    static Rect ScreenBounds(FrameworkElement element) =>
        new MatrixTransform(PortableScreenTransform(element)).TransformBounds(new Rect(element.RenderSize));

    static Matrix PortableScreenTransform(FrameworkElement element)
    {
        var origin = element.PointToScreen(new Point());
        var horizontal = (element.PointToScreen(new Point(element.ActualWidth, 0)) - origin) / element.ActualWidth;
        var vertical = (element.PointToScreen(new Point(0, element.ActualHeight)) - origin) / element.ActualHeight;
        return new Matrix(horizontal.X, horizontal.Y, vertical.X, vertical.Y, origin.X, origin.Y);
    }

    static PortableGlyphSample[] PortableSamplePoints(PortableLabelSample label, IReadOnlyList<PortableLabelSample> labels)
    {
        var transform = new MatrixTransform(label.ToScreen);
        var result = new List<PortableGlyphSample>(label.Layout.Glyphs.Count);
        foreach (var glyph in label.Layout.Glyphs)
        {
            if (!label.InkBounds.IntersectsWith(transform.TransformBounds(glyph.Ink.Bounds))) continue;
            var points = glyph.SamplePoints.Select(label.ToScreen.Transform)
                .Select(point => new Pixel((int)Math.Round(point.X), (int)Math.Round(point.Y)))
                .Distinct().Where(pixel =>
                {
                    var point = new Point(pixel.X, pixel.Y);
                    return label.Viewport.Contains(point) && !labels.Any(other =>
                        other.Label != label.Label && other.InkBounds.Contains(point));
                }).ToArray();
            result.Add(new(glyph, points));
        }
        return result.ToArray();
    }

    void RestorePortableToolInk(Button button)
    {
        foreach (var label in displayedPortableText)
            if (button.IsAncestorOf(label) && !ReferenceEquals(label.NativeInk, button.Foreground))
                label.RestoreNativeInk();
    }

    static void PaintPortableGlyph(PortableGlyphLayout.Glyph glyph, PortableTextContrast.Choice choice, bool animate)
    {
        if (choice.Outline is Color outline)
            ChangeColor(glyph.Outline, SolidColorBrush.ColorProperty, outline, animate);
        ChangeOpacity(glyph.Outline, choice.Outline != null ? .95 : 0, animate);
        ChangeColor(glyph.Fill, SolidColorBrush.ColorProperty, choice.Ink, animate);
    }

    static void ChangeColor(Animatable owner, DependencyProperty property, Color next, bool animate)
    {
        var previous = (Color)owner.GetValue(property);
        if (previous == next && (Color)owner.GetAnimationBaseValue(property) == next) return;
        owner.BeginAnimation(property, null);
        owner.SetValue(property, next);
        if (animate && previous != next)
            owner.BeginAnimation(property, new ColorAnimation(previous, next, PortableInkTransition) { FillBehavior = FillBehavior.Stop });
    }

    static void ChangeOpacity(SolidColorBrush edge, double next, bool animate)
    {
        double previous = edge.Opacity;
        if (previous == next && (double)edge.GetAnimationBaseValue(Brush.OpacityProperty) == next) return;
        edge.BeginAnimation(Brush.OpacityProperty, null);
        edge.Opacity = next;
        if (animate && previous != next)
            edge.BeginAnimation(Brush.OpacityProperty,
                new DoubleAnimation(previous, next, PortableInkTransition) { FillBehavior = FillBehavior.Stop });
    }
}
