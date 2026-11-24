using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AwesomeInterview.NativeDemo;

internal sealed class PortableGlyphLayout
{
    internal const int SamplesPerGlyph = 8;
    static readonly Pen SampleMargin = CreateSampleMargin();
    readonly List<Glyph> glyphs = new();
    internal IReadOnlyList<Glyph> Glyphs => glyphs;
    internal Rect Bounds { get; private set; } = Rect.Empty;
    internal DrawingBrush Surface { get; }

    internal sealed class Glyph(Geometry ink, SolidColorBrush fill, SolidColorBrush outline)
    {
        internal Geometry Ink { get; } = ink;
        internal SolidColorBrush Fill { get; } = fill;
        internal SolidColorBrush Outline { get; } = outline;
        internal Point[] SamplePoints { get; set; } = [];
    }

    internal PortableGlyphLayout(DrawingGroup nativeText, Size size, SolidColorBrush source)
    {
        var local = new Rect(size);
        var rendered = CopyText(nativeText, Matrix.Identity, source.Color);
        rendered.ClipGeometry = new RectangleGeometry(local);
        // One opacity binding covers every glyph and its outline.
        BindingOperations.SetBinding(rendered, DrawingGroup.OpacityProperty,
            new Binding(nameof(Brush.Opacity)) { Source = source });
        Surface = new DrawingBrush(rendered)
        {
            ViewboxUnits = BrushMappingMode.Absolute, Viewbox = local,
            ViewportUnits = BrushMappingMode.Absolute, Viewport = local,
            Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top
        };
        foreach (var glyph in glyphs) glyph.SamplePoints = BuildSamples(glyph);
    }

    DrawingGroup CopyText(DrawingGroup nativeText, Matrix toLabel, Color original)
    {
        var rendered = new DrawingGroup
        {
            Transform = nativeText.Transform,
            ClipGeometry = nativeText.ClipGeometry,
            GuidelineSet = nativeText.GuidelineSet
        };
        if (nativeText.Transform is { } transform)
            toLabel = transform.Value * toLabel;

        foreach (var drawing in nativeText.Children)
        {
            if (drawing is DrawingGroup child)
                rendered.Children.Add(CopyText(child, toLabel, original));
            else if (drawing is GlyphRunDrawing { GlyphRun: { } run })
                foreach (var cluster in SplitRun(run))
                {
                    var shape = cluster.BuildGeometry();
                    if (shape.Bounds.IsEmpty) continue;
                    var ink = shape;
                    if (!toLabel.IsIdentity)
                        ink = new GeometryGroup { Children = { shape }, Transform = new MatrixTransform(toLabel) };
                    var glyph = new Glyph(ink, new SolidColorBrush(original), new SolidColorBrush(Colors.Black) { Opacity = 0 });
                    glyphs.Add(glyph);
                    Bounds = Rect.Union(Bounds, ink.Bounds);
                    rendered.Children.Add(new GeometryDrawing(null, new Pen(glyph.Outline, .9) { LineJoin = PenLineJoin.Round }, shape));
                    rendered.Children.Add(new GlyphRunDrawing(glyph.Fill, cluster));
                }
        }
        return rendered;
    }

    static IEnumerable<GlyphRun> SplitRun(GlyphRun run)
    {
        if (run.GlyphIndices.Count <= 1)
        {
            yield return run;
            yield break;
        }

        // WPF has already shaped the run. Keep combining marks and ligatures in their original cluster.
        var boundaries = (run.ClusterMap is { Count: > 0 } map
                ? map.Select(index => (int)index).Distinct().Order()
                : Enumerable.Range(0, run.GlyphIndices.Count))
            .Append(run.GlyphIndices.Count).ToArray();
        double x = run.BaselineOrigin.X;
        int direction = run.BidiLevel % 2 == 0 ? 1 : -1;
        for (int index = 1; index < boundaries.Length; index++)
        {
            int start = boundaries[index - 1], count = boundaries[index] - start;
            var advances = run.AdvanceWidths.Skip(start).Take(count).ToArray();
            yield return new GlyphRun(run.GlyphTypeface, run.BidiLevel, run.IsSideways, run.FontRenderingEmSize,
                run.PixelsPerDip, run.GlyphIndices.Skip(start).Take(count).ToArray(),
                new Point(x, run.BaselineOrigin.Y), advances, run.GlyphOffsets?.Skip(start).Take(count).ToArray(),
                null, run.DeviceFontName, null, null, run.Language);
            x += direction * advances.Sum();
        }
    }

    Point[] BuildSamples(Glyph glyph)
    {
        var bounds = glyph.Ink.Bounds;
        double first = bounds.Left + bounds.Width * .25, last = bounds.Right - bounds.Width * .25;
        double middle = bounds.Top + bounds.Height * .5;
        Point[] candidates =
        [
            new(first, bounds.Top - 1.4), new(last, bounds.Bottom + 1.4),
            new(last, bounds.Top - 1.4), new(first, bounds.Bottom + 1.4),
            new(bounds.Left - 1.4, middle), new(bounds.Right + 1.4, middle),
            new(first, middle), new(last, middle)
        ];
        return candidates.Where(point => !glyphs.Any(other =>
        {
            var nearby = other.Ink.Bounds;
            nearby.Inflate(1, 1);
            return nearby.Contains(point) && (other.Ink.FillContains(point) || other.Ink.StrokeContains(SampleMargin, point));
        })).ToArray();
    }

    static Pen CreateSampleMargin()
    {
        var margin = new Pen(Brushes.Black, 1.5);
        margin.Freeze();
        return margin;
    }
}
