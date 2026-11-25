using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class DesktopIntegration
{
    const double PortableEventGlassEdgeHeight = 5;
    readonly LinearGradientBrush portableTopGlassEdge = CreatePortableEventEdge(false);
    readonly LinearGradientBrush portableBottomGlassEdge = CreatePortableEventEdge(true);

    static LinearGradientBrush CreatePortableEventEdge(bool bottom)
    {
        var edge = new LinearGradientBrush
        {
            StartPoint = new(0, bottom ? 1 : 0), EndPoint = new(0, bottom ? 0 : 1), Opacity = 0
        };
        edge.GradientStops.Add(new(Color.FromArgb(112, 168, 184, 202), 0));
        edge.GradientStops.Add(new(Color.FromArgb(63, 221, 230, 240), .32));
        edge.GradientStops.Add(new(Color.FromArgb(0, 221, 230, 240), 1));
        return edge;
    }

    Grid BuildPortableEventGlass(PortableGlassStrip label)
    {
        var content = new Grid();
        content.Children.Add(new Border
        {
            Height = PortableEventGlassEdgeHeight, VerticalAlignment = VerticalAlignment.Top,
            Background = portableTopGlassEdge, IsHitTestVisible = false, CornerRadius = new(3, 3, 0, 0)
        });
        content.Children.Add(new Border
        {
            Height = PortableEventGlassEdgeHeight, VerticalAlignment = VerticalAlignment.Bottom,
            Background = portableBottomGlassEdge, IsHitTestVisible = false, CornerRadius = new(0, 0, 3, 3)
        });
        content.Children.Add(label);
        return content;
    }
}
