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
    static readonly Duration PortableInkTransition = new(TimeSpan.FromMilliseconds(280));
    static readonly SineEase PortableInkEase = CreatePortableInkEase();
    readonly PortableContrastCadence portableContrastCadence = new();
    readonly List<PortableGlyphText> displayedPortableText = new();
    readonly HashSet<PortableGlassStrip> displayedPortableStrips = new();
    DispatcherTimer? portableContrastTimer;
    HwndSource? portableContrastSource;
    PortableForegroundWatcher? portableForegroundWatcher;
    long portableContrastRevision;
    bool portableMoving, portableSampling, portableSessionLocked, portableContrastUrgent;

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool WTSRegisterSessionNotification(nint window, uint flags);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool WTSUnRegisterSessionNotification(nint window);

    readonly record struct PortableLabelSample(PortableGlyphText Label, PortableGlyphLayout Layout,
        Matrix ToScreen, Rect InkBounds, Rect Viewport, double Opacity, PortableGlassStrip? Strip);
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
                portableForegroundWatcher = new PortableForegroundWatcher(() => SchedulePortableContrast());
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

    void ClearPortableGlass(bool restoreInk = false)
    {
        var clearedStrips = displayedPortableStrips.Where(strip => strip.SetGlass(null, null)).ToArray();
        foreach (var label in displayedPortableText)
        {
            if (label.GlyphLayout is not { } layout) continue;
            bool cleared = layout.SetGlass(null, null);
            if (restoreInk && (cleared || clearedStrips.Any(strip => strip.IsAncestorOf(label))) &&
                label.NativeInk is { } original)
                foreach (var glyph in layout.Glyphs) PaintPortableGlyph(glyph, new(original.Color, null), false);
        }
    }

    void SchedulePortableContrast(bool resetCadence = true)
    {
        if (portableContrastTimer == null) return;
        if (resetCadence)
        {
            portableContrastCadence.Reset();
            portableContrastUrgent = true;
        }
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
        portableForegroundWatcher?.Dispose();
        portableForegroundWatcher = null;
        if (portableContrastSource is { IsDisposed: false } source)
        {
            WTSUnRegisterSessionNotification(source.Handle);
            source.RemoveHook(PortableContrastWindowMessage);
        }
        portableContrastSource = null;
        portableMoving = false;
        portableSessionLocked = false;
        portableContrastUrgent = false;
        displayedPortableText.Clear();
        displayedPortableStrips.Clear();
    }

    async void SamplePortableContrast(object? sender, EventArgs args)
    {
        if (sender is not DispatcherTimer timer || timer != portableContrastTimer ||
            portable is not { IsVisible: true } || portableMoving || portableSessionLocked) return;
        if (portableSampling) return;
        bool urgent = portableContrastUrgent;
        long revision = portableContrastRevision;

        var labels = new List<PortableLabelSample>();
        CollectPortableLabels(portableHeader, labels);
        CollectPortableLabels(portableContent, labels);
        if (portableUnlock is { IsVisible: true, Content: FrameworkElement unlock })
            CollectPortableLabels(unlock, labels);
        displayedPortableText.Clear();
        displayedPortableText.AddRange(labels.Select(sample => sample.Label));
        displayedPortableStrips.Clear();
        displayedPortableStrips.UnionWith(labels.Select(label => label.Strip).OfType<PortableGlassStrip>());

        if (SystemParameters.HighContrast)
        {
            ClearPortableGlass();
            foreach (var label in labels)
                foreach (var glyph in label.Layout.Glyphs)
                    PaintPortableGlyph(glyph, new(SystemColors.WindowTextColor, null), false);
            portableContrastUrgent = false;
            timer.Interval = portableContrastCadence.AfterSample(false);
            return;
        }

        var clockInk = new List<Rect>(2);
        foreach (var element in new[] { portableNowLine, portableClock?.Parent as Border })
        {
            if (element is not { IsVisible: true, ActualWidth: > 0, ActualHeight: > 0 }) continue;
            var area = ScreenBounds(element);
            area.Inflate(1, 1);
            clockInk.Add(area);
        }
        var style = ActivePortableStyle;
        var grouped = new HashSet<PortableGlassStrip>();
        var glass = labels.Select(label => label.Strip is { } strip
            ? grouped.Add(strip) ? PlanPortableStrip(strip, style) : null
            : PlanPortableGlass(label, style, labels, clockInk)).ToArray();
        var glassBounds = glass.OfType<PortableGlass>().Select(patch => patch.ScreenBounds).ToArray();
        var blocked = clockInk.Concat(glassBounds).ToList();
        if (portableHoverCue is { IsVisible: true, ActualWidth: > 0, ActualHeight: > 0 } cue)
            blocked.Add(ScreenBounds(cue));
        bool hasGlass = style.GlassStrength != 0;
        var samples = labels.Select((label, index) => glass[index] == null && !(hasGlass && label.Strip != null)
            ? PortableSamplePoints(label, labels, blocked) : []).ToArray();
        var glassPoints = labels.Select((label, index) => glass[index]?.SamplePoints.Where(pixel =>
        {
            var point = new Point(pixel.X, pixel.Y);
            return label.Viewport.Contains(point) && !blocked.Any(area => area.Contains(point)) &&
                !labels.Any(other => other.InkBounds.Contains(point));
        }).Distinct().ToArray() ?? []).ToArray();
        var locations = samples.SelectMany(label => label).SelectMany(glyph => glyph.Points)
            .Concat(glassPoints.SelectMany(points => points)).Distinct().ToArray();
        var sampler = readBackdrop ?? PortableBackdropSamples.Read;
        portableSampling = true;
        try
        {
            var (colors, glassPixels) = await Task.Run(() =>
            {
                var colors = sampler(locations);
                var frames = glass.Select((patch, index) => patch?.Render(glassPoints[index]
                    .Where(colors.ContainsKey).ToDictionary(point => point, point => colors[point]))).ToArray();
                return (colors, frames);
            });
            if (revision != portableContrastRevision) return;
            var backdrop = new Color[PortableGlyphLayout.SamplesPerGlyph];
            bool animate = SystemParameters.ClientAreaAnimation;
            bool backgroundChanged = false;
            for (int index = 0; index < labels.Count; index++)
            {
                var label = labels[index];
                if (label.Label.GlyphLayout != label.Layout || label.Label.NativeInk is not { } source) continue;
                if (label.Strip is { } strip)
                {
                    if (glass[index] is { } groupGlass)
                        backgroundChanged |= strip.SetGlass(groupGlass, glassPixels[index]);
                }
                else backgroundChanged |= label.Layout.SetGlass(glass[index], glassPixels[index]);

                if (glass[index] != null || (hasGlass && label.Strip != null))
                {
                    foreach (var glyph in label.Layout.Glyphs)
                        PaintPortableGlyph(glyph, new(PortableGlass.InkFor(style), null), false);
                    continue;
                }
                foreach (var (glyph, points) in samples[index])
                {
                    int count = 0;
                    foreach (var point in points)
                        if (colors.TryGetValue(point, out var color)) backdrop[count++] = color;

                    var (choice, changed) = glyph.Contrast.Observe(backdrop.AsSpan(0, count), source.Color,
                        source.Opacity * label.Opacity, urgent);
                    backgroundChanged |= changed;
                    PaintPortableGlyph(glyph, choice, animate);
                }
            }
            portableContrastUrgent = false;
            timer.Interval = portableContrastCadence.AfterSample(backgroundChanged || urgent);
        }
        finally { portableSampling = false; }
    }

    static PortableGlass? PlanPortableStrip(PortableGlassStrip strip, PortableStyle style)
    {
        if (style.GlassStrength == 0 || strip.ActualWidth < 3 || strip.ActualHeight < 3) return null;
        return new PortableGlass(new Rect(strip.RenderSize), ScreenBounds(strip), style, capsule: true);
    }

    static PortableGlass? PlanPortableGlass(PortableLabelSample label, PortableStyle style,
        IReadOnlyList<PortableLabelSample> labels, IReadOnlyList<Rect> clockInk)
    {
        if (style.GlassStrength == 0 || !label.ToScreen.HasInverse) return null;
        var inverse = label.ToScreen;
        inverse.Invert();
        var ink = new MatrixTransform(inverse).TransformBounds(label.InkBounds);
        var expanded = PortableGlass.Expand(ink, style.GlassFeather);

        var transform = new MatrixTransform(label.ToScreen);
        var screen = transform.TransformBounds(expanded);
        screen.Intersect(label.Viewport);
        if (screen.IsEmpty || screen.Width < 3 || screen.Height < 3) return null;
        var toLocal = new MatrixTransform(inverse);
        var local = toLocal.TransformBounds(screen);
        // Expanded glass sits behind its own label and leaves neighboring text unobscured.
        var excluded = labels.Where(other => other.Label != label.Label && !other.InkBounds.IntersectsWith(label.InkBounds))
            .Select(other => other.InkBounds).Concat(clockInk)
            .Where(bounds => bounds.IntersectsWith(screen)).Select(toLocal.TransformBounds).ToArray();
        return new PortableGlass(local, screen, style, excluded);
    }

    static void CollectPortableLabels(FrameworkElement? root, List<PortableLabelSample> labels)
    {
        if (root is not { IsVisible: true, ActualWidth: > 0, ActualHeight: > 0 } ||
            PresentationSource.FromVisual(root) == null) return;
        CollectPortableLabels(root, ScreenBounds(root), 1, labels, null);
    }

    static void CollectPortableLabels(Visual visual, Rect viewport, double opacity, List<PortableLabelSample> labels,
        PortableGlassStrip? strip)
    {
        if (visual is PortableGlassStrip group) strip = group;
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
            if (!ink.IsEmpty) labels.Add(new(text, layout, toScreen, ink, viewport, opacity, strip));
            return;
        }

        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            if (VisualTreeHelper.GetChild(visual, index) is Visual child)
                CollectPortableLabels(child, viewport, opacity, labels, strip);
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

    static PortableGlyphSample[] PortableSamplePoints(PortableLabelSample label, IReadOnlyList<PortableLabelSample> labels,
        IReadOnlyList<Rect> clockInk)
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
                    return label.Viewport.Contains(point) && !clockInk.Any(area => area.Contains(point)) && !labels.Any(other =>
                    {
                        if (other.Label == label.Label) return false;
                        var ink = other.InkBounds;
                        ink.Inflate(1, 1);
                        return ink.Contains(point);
                    });
                }).ToArray();
            result.Add(new(glyph, points));
        }
        return result.ToArray();
    }

    void UpdatePortableToolInk(Button button)
    {
        if (button.Foreground is not SolidColorBrush ink) return;
        foreach (var label in displayedPortableText)
            if (button.IsAncestorOf(label)) label.UpdateNativeInk(ink);
    }

    static void PaintPortableGlyph(PortableGlyphLayout.Glyph glyph, PortableTextContrast.Choice choice, bool animate)
    {
        if (choice.Outline is Color outline)
            ChangeColor(glyph.Outline, SolidColorBrush.ColorProperty, outline, animate && glyph.Outline.Opacity > 0);
        ChangeOpacity(glyph.Outline, choice.Outline != null ? .95 : 0, animate);
        ChangeColor(glyph.Fill, SolidColorBrush.ColorProperty, choice.Ink, animate);
    }

    static void ChangeColor(Animatable owner, DependencyProperty property, Color next, bool animate)
    {
        if ((Color)owner.GetAnimationBaseValue(property) == next) return;
        var previous = (Color)owner.GetValue(property);
        owner.SetValue(property, next);
        if (animate && previous != next)
            owner.BeginAnimation(property, new ColorAnimation(previous, next, PortableInkTransition)
            {
                FillBehavior = FillBehavior.Stop, EasingFunction = PortableInkEase
            });
        else owner.BeginAnimation(property, null);
    }

    static void ChangeOpacity(SolidColorBrush edge, double next, bool animate)
    {
        if ((double)edge.GetAnimationBaseValue(Brush.OpacityProperty) == next) return;
        double previous = edge.Opacity;
        edge.Opacity = next;
        if (animate && previous != next)
            edge.BeginAnimation(Brush.OpacityProperty,
                new DoubleAnimation(previous, next, PortableInkTransition)
                {
                    FillBehavior = FillBehavior.Stop, EasingFunction = PortableInkEase
                });
        else edge.BeginAnimation(Brush.OpacityProperty, null);
    }

    static SineEase CreatePortableInkEase()
    {
        var easing = new SineEase { EasingMode = EasingMode.EaseInOut };
        easing.Freeze();
        return easing;
    }
}
