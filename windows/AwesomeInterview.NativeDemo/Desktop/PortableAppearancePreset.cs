using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AwesomeInterview.NativeDemo;

internal sealed record PortableAppearancePreset(string Name, int OverallTransparency, int BackgroundTransparency,
    int BorderTransparency, int TextTransparency)
{
    internal static IReadOnlyList<PortableAppearancePreset> BuiltIn { get; } = ReadBuiltIn(AppContext.BaseDirectory);

    internal static IReadOnlyList<PortableAppearancePreset> ReadBuiltIn(string appDirectory)
    {
        var presets = new List<PortableAppearancePreset>(2);
        foreach (string file in new[] { "clear-outline.json", "soft-silhouette.json" })
        {
            try
            {
                string path = Path.Combine(appDirectory, "Presets", file);
                var preset = JsonSerializer.Deserialize<PortableAppearancePreset>(File.ReadAllText(path));
                if (preset is { Name.Length: > 0, OverallTransparency: >= 0 and <= 90,
                    BackgroundTransparency: >= 0 and <= 100, BorderTransparency: >= 0 and <= 100,
                    TextTransparency: >= 0 and <= 90 })
                    presets.Add(preset);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
        }
        return presets;
    }

    internal DesktopPreferences Apply(DesktopPreferences current) => current with
    {
        PortableOverallOpacity = 1 - OverallTransparency / 100d,
        PortableOpacity = 1 - BackgroundTransparency / 100d,
        PortableBorderOpacity = 1 - BorderTransparency / 100d,
        PortableTextOpacity = 1 - TextTransparency / 100d
    };
}
