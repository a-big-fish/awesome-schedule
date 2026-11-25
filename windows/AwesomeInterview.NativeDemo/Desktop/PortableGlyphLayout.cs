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
    readonly DrawingGroup rendered;
    internal DrawingGroup GlassLayer { get; } = new();
    byte[]? glassPixels;
    Rect glassBounds;
    Rect[] glassExclusions = [];
    internal IReadOnlyList<Glyph> Glyphs => glyphs;
    internal Rect Bounds { get; private set; } = Rect.Empty;
    internal DrawingGroup Drawing { get; } = new();

    internal sealed class Glyph(GlyphRun run, Geometry ink, SolidColorBrush fill, SolidColorBrush outline)
    {
        internal GlyphRun Run { get; } = run;
        internal Geometry Ink { get; } = ink;
        internal SolidColorBrush Fill { get; } = fill;
        internal SolidColorBrush Outline { get; } = outline;
        internal Point[] SamplePoints { get; set; } = [];
        internal PortableGlyphContrastHistory Contrast { get; } = new();
    }

    internal PortableGlyphLayout(DrawingGroup nativeText, Size size, SolidColorBrush source)
    {
        var local = new Rect(size);
        rendered = CopyText(nativeText, Matrix.Identity, source.Color);
        Drawing.Children.Add(GlassLayer);
        Drawing.Children.Add(new DrawingGroup
        {
            ClipGeometry = new RectangleGeometry(local),
            Children = { rendered }
        });
        SetInkSource(source);
        foreach (var glyph in glyphs) glyph.SamplePoints = BuildSamples(glyph);
    }

    internal bool SetGlass(PortableGlass? glass, byte[]? pixels)
    {
        if (glass == null || pixels == null)
        {
            if (glassPixels == null) return false;
            GlassLayer.Children.Clear();
            glassPixels = null;
            glassExclusions = [];
            GlassLayer.ClipGeometry = null;
            return true;
        }

        if (glassBounds == glass.LocalBounds && pixels.AsSpan().SequenceEqual(glassPixels) &&
            glass.ExcludedInk.AsSpan().SequenceEqual(glassExclusions)) return false;
        glassBounds = glass.LocalBounds;
        glassPixels = pixels;
        glassExclusions = glass.ExcludedInk;
        if (glassExclusions.Length == 0) GlassLayer.ClipGeometry = null;
        else
        {
            var exclusions = new GeometryGroup { FillRule = FillRule.Nonzero };
            foreach (var ink in glassExclusions)
                exclusions.Children.Add(new RectangleGeometry(ink));
            var clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(glassBounds), exclusions);
            clip.Freeze();
            GlassLayer.ClipGeometry = clip;
        }
        GlassLayer.Children.Clear();
        GlassLayer.Children.Add(new ImageDrawing(glass.Image(pixels), glassBounds));
        return true;
    }

    internal void SetInkSource(SolidColorBrush source) =>
        BindingOperations.SetBinding(rendered, DrawingGroup.OpacityProperty,
            new Binding(nameof(Brush.Opacity)) { Source = source });

    internal void InheritColors(PortableGlyphLayout previous)
    {
        for (int index = 0; index < Math.Min(glyphs.Count, previous.glyphs.Count); index++)
        {
            var old = previous.glyphs[index];
            var glyph = glyphs[index];
            if (glyph.Run.GlyphTypeface.FontUri != old.Run.GlyphTypeface.FontUri ||
                !glyph.Run.GlyphIndices.SequenceEqual(old.Run.GlyphIndices)) continue;
            glyph.Fill.Color = old.Fill.Color;
            glyph.Outline.Color = old.Outline.Color;
            glyph.Outline.Opacity = old.Outline.Opacity;
            glyph.Contrast.Inherit(old.Contrast);
        }
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
                    var glyph = new Glyph(cluster, ink, new SolidColorBrush(original), new SolidColorBrush(Colors.Black) { Opacity = 0 });
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
            new(first, bounds.Top - 2.25), new(last, bounds.Bottom + 2.25),
            new(last, bounds.Top - 2.25), new(first, bounds.Bottom + 2.25),
            new(bounds.Left - 2.25, middle), new(bounds.Right + 2.25, middle),
            new(bounds.Left + bounds.Width * .5, bounds.Top - 2.25),
            new(bounds.Left + bounds.Width * .5, bounds.Bottom + 2.25)
        ];
        return candidates.Where(point => !glyphs.Any(other =>
        {
            var nearby = other.Ink.Bounds;
            nearby.Inflate(1.6, 1.6);
            return nearby.Contains(point) && (other.Ink.FillContains(point) || other.Ink.StrokeContains(SampleMargin, point));
        })).ToArray();
    }

    static Pen CreateSampleMargin()
    {
        var margin = new Pen(Brushes.Black, 3.2);
        margin.Freeze();
        return margin;
    }
}
