using System;
using System.Linq;

namespace AwesomeInterview.NativeDemo;

internal sealed record PortableStyle(string? TextColor = null, string? MutedTextColor = null,
    string? AccentColor = null, string? SectionColor = null, string? EventColor = null, string? OutlineColor = null,
    int GlassStrength = 0, int GlassRadius = 8, int GlassFeather = 10)
{
    internal static PortableStyle Default { get; } = new();

    internal bool IsValid => new[] { TextColor, MutedTextColor, AccentColor, SectionColor, EventColor, OutlineColor }.All(IsColor) &&
        GlassStrength is >= 0 and <= 85 && GlassRadius is >= 2 and <= 22 && GlassFeather is >= 3 and <= 24;

    static bool IsColor(string? color) =>
        color == null || (color.Length == 7 && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit));
}
