using System;
using System.Windows.Media;
using Choice = AwesomeInterview.NativeDemo.PortableTextContrast.Choice;

namespace AwesomeInterview.NativeDemo;

internal sealed class PortableContrastCadence
{
    const int FastSeconds = 1, SlowSeconds = 2;
    int seconds = FastSeconds;

    internal TimeSpan AfterSample(bool changed)
    {
        seconds = changed ? FastSeconds : Math.Min(seconds * 2, SlowSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    internal void Reset() => seconds = FastSeconds;
}

internal sealed class PortableGlyphContrastHistory
{
    Color[] baseline = [];
    Choice? accepted, pending;
    Color lastInk;
    double lastOpacity;

    internal void Inherit(PortableGlyphContrastHistory previous) => accepted = previous.accepted;

    internal (Choice Choice, bool BackgroundChanged) Observe(ReadOnlySpan<Color> pixels, Color original,
        double opacity, bool urgent)
    {
        if (pixels.IsEmpty)
        {
            pending = null;
            return (accepted ?? new(original, null), false);
        }

        bool first = baseline.Length == 0;
        bool backgroundChanged = !first && HasBackgroundChanged(pixels);
        bool styleChanged = original != lastInk || opacity != lastOpacity;
        if (!first && !urgent && !styleChanged && !backgroundChanged)
        {
            pending = null;
            return (accepted!.Value, false);
        }

        var next = PortableTextContrast.Choose(pixels, original, opacity, accepted?.Ink ?? original);
        if (first || urgent || styleChanged || next == accepted || next == pending)
        {
            if (baseline.Length != pixels.Length) baseline = new Color[pixels.Length];
            pixels.CopyTo(baseline);
            lastInk = original;
            lastOpacity = opacity;
            accepted = next;
            pending = null;
        }
        else
        {
            // Require a second observation before changing a stationary glyph's established colors.
            pending = next;
        }
        return (accepted!.Value, backgroundChanged);
    }

    bool HasBackgroundChanged(ReadOnlySpan<Color> pixels)
    {
        if (pixels.Length != baseline.Length) return true;
        int changed = 0;
        for (int index = 0; index < pixels.Length; index++)
        {
            var old = baseline[index];
            var current = pixels[index];
            int delta = Math.Max(Math.Abs(old.R - current.R),
                Math.Max(Math.Abs(old.G - current.G), Math.Abs(old.B - current.B)));
            if (delta >= 44 || (delta >= 12 && ++changed >= Math.Min(2, pixels.Length))) return true;
        }
        return false;
    }
}
