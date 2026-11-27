using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class DesktopIntegration
{
    const double PortableHoverCueSize = 46;
    static readonly Brush PortableHoverFrost = CreatePortableHoverFrost();
    static readonly Brush PortableHoverOutline = CreatePortableHoverOutline();
    Canvas? portableHoverCueLayer;
    Ellipse? portableHoverCue;

    static Brush CreatePortableHoverFrost()
    {
        var frost = new RadialGradientBrush();
        frost.GradientStops.Add(new(Colors.Transparent, .24));
        frost.GradientStops.Add(new(Color.FromArgb(32, 245, 249, 252), .43));
        frost.GradientStops.Add(new(Color.FromArgb(121, 246, 249, 252), .66));
        frost.GradientStops.Add(new(Color.FromArgb(178, 255, 255, 255), .76));
        frost.GradientStops.Add(new(Color.FromArgb(112, 105, 130, 159), .87));
        frost.GradientStops.Add(new(Color.FromArgb(0, 105, 130, 159), 1));
        frost.Freeze();
        return frost;
    }

    static Brush CreatePortableHoverOutline()
    {
        var outline = new SolidColorBrush(Color.FromArgb(156, 121, 149, 186));
        outline.Freeze();
        return outline;
    }

    Canvas CreatePortableHoverCueLayer()
    {
        portableHoverCue = new Ellipse
        {
            Width = PortableHoverCueSize, Height = PortableHoverCueSize, StrokeThickness = 1.5,
            IsHitTestVisible = false, Visibility = Visibility.Collapsed
        };
        portableHoverCueLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        portableHoverCueLayer.Children.Add(portableHoverCue);
        return portableHoverCueLayer;
    }

    void ApplyPortableHoverCueAppearance()
    {
        if (portableHoverCue is not { } cue) return;
        cue.Fill = preferences.UsesClearOutline ? null : PortableHoverFrost;
        cue.Stroke = preferences.UsesClearOutline ? PortableHoverOutline : null;
    }

    void UpdatePortableHoverCue(Point? local)
    {
        if (portableHoverCue is not { } cue) return;
        if (local is not { } point)
        {
            cue.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(cue, point.X - PortableHoverCueSize / 2);
        Canvas.SetTop(cue, point.Y - PortableHoverCueSize / 2);
        cue.Visibility = Visibility.Visible;
    }
}
