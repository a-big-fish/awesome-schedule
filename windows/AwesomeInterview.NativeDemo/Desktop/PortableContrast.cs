using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Pixel = System.Drawing.Point;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class DesktopIntegration
{
    static readonly TimeSpan PortableSettleTime = TimeSpan.FromMilliseconds(450);
    static readonly TimeSpan PortableContrastRefresh = TimeSpan.FromSeconds(2);
    static readonly Duration PortableInkTransition = new(TimeSpan.FromMilliseconds(180));
    readonly ConditionalWeakTable<TextBlock, PortableInkState> portableInkStates = new();
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

    sealed class PortableInkState(SolidColorBrush source)
    {
        internal SolidColorBrush Source = source;
        internal SolidColorBrush Ink { get; } = new(source.Color);
        internal DropShadowEffect Edge { get; } = new() { ShadowDepth = 0, BlurRadius = 2.2, Opacity = 0 };
    }

    readonly record struct PortableLabelSample(TextBlock Label, Rect InkBounds, Rect Viewport, double Opacity);

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
            foreach (var (_, state) in portableInkStates)
            {
                Freeze(state.Ink, SolidColorBrush.ColorProperty);
                Freeze(state.Edge, DropShadowEffect.ColorProperty);
                Freeze(state.Edge, DropShadowEffect.OpacityProperty);
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
        portableInkStates.Clear();
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

        if (SystemParameters.HighContrast)
        {
            foreach (var label in labels) PaintPortableLabel(label, Array.Empty<Color>());
            return;
        }

        var points = labels.Select(label => PortableSamplePoints(label, labels)).ToArray();
        var locations = points.SelectMany(group => group).Distinct().ToArray();
        var sampler = readBackdrop ?? PortableBackdropSamples.Read;
        portableSampling = true;
        try
        {
            var colors = await Task.Run(() => sampler(locations));
            if (revision != portableContrastRevision) return;
            for (int index = 0; index < labels.Count; index++)
            {
                var backdrop = new List<Color>(points[index].Length);
                foreach (var point in points[index])
                    if (colors.TryGetValue(point, out var color)) backdrop.Add(color);
                PaintPortableLabel(labels[index], backdrop);
            }
        }
        finally { portableSampling = false; }
    }

    static void CollectPortableLabels(FrameworkElement? root, List<PortableLabelSample> labels)
    {
        if (root == null || PresentationSource.FromVisual(root) == null) return;
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

        if (visual is TextBlock { Text.Length: > 0, Foreground: SolidColorBrush } text)
        {
            var layout = ScreenBounds(text);
            if (!layout.IntersectsWith(viewport)) return;

            // A stretched TextBlock can be much wider than its glyphs. Sample around the line of text.
            var dpi = VisualTreeHelper.GetDpi(text);
            var formatted = new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
                text.FontSize, Brushes.Black, dpi.PixelsPerDip);
            double width = Math.Min(layout.Width, formatted.Width * dpi.DpiScaleX);
            double start = text.TextAlignment switch
            {
                TextAlignment.Center => layout.Left + (layout.Width - width) / 2,
                TextAlignment.Right => layout.Right - width,
                _ => layout.Left
            };
            var ink = new Rect(start, layout.Top, width, layout.Height);
            ink.Intersect(viewport);
            if (!ink.IsEmpty && ink.Width >= 2 && ink.Height >= 2)
                labels.Add(new(text, ink, viewport, opacity));
            return;
        }

        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            if (VisualTreeHelper.GetChild(visual, index) is Visual child)
                CollectPortableLabels(child, viewport, opacity, labels);
    }

    static Rect ScreenBounds(FrameworkElement element) =>
        new(element.PointToScreen(new Point()), element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight)));

    static Pixel[] PortableSamplePoints(PortableLabelSample label, IReadOnlyList<PortableLabelSample> labels)
    {
        var bounds = label.InkBounds;
        var candidates = new List<Pixel>(26);
        for (int column = 0; column < 7; column++)
        {
            int x = (int)Math.Round(bounds.Left + (column + .5) * bounds.Width / 7);
            candidates.Add(new(x, (int)Math.Floor(bounds.Top) - 2));
            candidates.Add(new(x, (int)Math.Ceiling(bounds.Bottom) + 1));
        }
        for (int row = 0; row < 3; row++)
        {
            int y = (int)Math.Round(bounds.Top + (row + .5) * bounds.Height / 3);
            candidates.Add(new((int)Math.Floor(bounds.Left) - 2, y));
            candidates.Add(new((int)Math.Ceiling(bounds.Right) + 1, y));
        }
        return candidates.Distinct().Where(point =>
        {
            var position = new Point(point.X, point.Y);
            return label.Viewport.Contains(position) && !labels.Any(other => other.InkBounds.Contains(position));
        }).ToArray();
    }

    static SolidColorBrush PortableTextSource(TextBlock text, PortableInkState state)
    {
        if (!ReferenceEquals(text.Foreground, state.Ink) && text.Foreground is SolidColorBrush replacement)
            return replacement;
        // The default button template presents strings using a TextBlock that inherits its ink.
        for (DependencyObject? parent = VisualTreeHelper.GetParent(text); parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ContentPresenter { Content: string } presenter &&
                presenter.TemplatedParent is Button { Foreground: SolidColorBrush ink }) return ink;
            if (parent is ButtonBase) break;
        }
        return state.Source;
    }

    void RestorePortableToolInk(Button button)
    {
        foreach (var (label, _) in portableInkStates)
        {
            if (!button.IsAncestorOf(label)) continue;
            label.ClearValue(TextBlock.ForegroundProperty);
            label.Effect = null;
        }
    }

    void PaintPortableLabel(PortableLabelSample sample, IReadOnlyList<Color> backdrop)
    {
        var label = sample.Label;
        if (label.Foreground is not SolidColorBrush baseline) return;
        var state = portableInkStates.GetValue(label, _ => new(baseline));
        state.Source = PortableTextSource(label, state);
        if (!ReferenceEquals(label.Foreground, state.Ink))
        {
            label.Foreground = state.Ink;
            label.Effect = state.Edge;
        }
        if (!ReferenceEquals(BindingOperations.GetBinding(state.Ink, Brush.OpacityProperty)?.Source, state.Source))
            BindingOperations.SetBinding(state.Ink, Brush.OpacityProperty, new Binding(nameof(Brush.Opacity)) { Source = state.Source });

        if (SystemParameters.HighContrast)
        {
            ChangeColor(state.Ink, SolidColorBrush.ColorProperty, SystemColors.WindowTextColor, false);
            ChangeOpacity(state.Edge, 0, false);
            return;
        }

        var choice = PortableTextContrast.Choose(backdrop, state.Source.Color,
            state.Source.Opacity * sample.Opacity, state.Ink.Color);
        bool animate = SystemParameters.ClientAreaAnimation;
        if (choice.Outline is Color outline) ChangeColor(state.Edge, DropShadowEffect.ColorProperty, outline, animate);
        ChangeOpacity(state.Edge, choice.Outline != null ? .95 * state.Source.Opacity : 0, animate);
        ChangeColor(state.Ink, SolidColorBrush.ColorProperty, choice.Ink, animate);
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

    static void ChangeOpacity(DropShadowEffect edge, double next, bool animate)
    {
        double previous = edge.Opacity;
        if (previous == next && (double)edge.GetAnimationBaseValue(DropShadowEffect.OpacityProperty) == next) return;
        edge.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        edge.Opacity = next;
        if (animate && previous != next)
            edge.BeginAnimation(DropShadowEffect.OpacityProperty,
                new DoubleAnimation(previous, next, PortableInkTransition) { FillBehavior = FillBehavior.Stop });
    }
}
