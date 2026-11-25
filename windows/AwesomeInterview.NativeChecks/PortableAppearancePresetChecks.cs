using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using AwesomeInterview.NativeDemo;
using Pixel = System.Drawing.Point;

internal static class PortableAppearancePresetChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var presets = PortableAppearancePreset.BuiltIn;
        var defaults = new DesktopPreferences();
        check(presets.Count == 3, "all three portable appearance JSON files load");
        if (presets.Count != 3) return;

        var clear = presets.Single(preset => preset.Name == "清透描边");
        var soft = presets.Single(preset => preset.Name == "清透淡影");
        var frosted = presets.Single(preset => preset.Name == "磨砂玻璃");
        check(clear is { OverallTransparency: 0, BackgroundTransparency: 100, BorderTransparency: 0, TextTransparency: 0,
            Style.GlassStrength: 0 }, "the clear outline preset uses the requested values without glass");
        check(soft is { OverallTransparency: 50, BackgroundTransparency: 100, BorderTransparency: 100, TextTransparency: 30 }
            && (soft.Apply(defaults) with { PortableStyle = null, PortableStyleFile = null }) == defaults &&
            defaults.PortableAutoLockSeconds == 10, "the new screenshot is the fresh installation default");

        check(soft.Style?.GlassStrength == 0 && frosted is { OverallTransparency: 0, BackgroundTransparency: 100,
            BorderTransparency: 100, Style.GlassStrength: >= 80 }, "only the independent frosted preset enables visible glass");

        var previousStyle = new PortableStyle(GlassStrength: 42);
        var upgraded = AppliedStyle(PortableAppearancePreset.DefaultFile);
        var custom = AppliedStyle("user-style.json");
        check(upgraded is { PortableTextOpacity: .55, PortableStyle.GlassStrength: 0 } &&
            custom.PortableStyle == previousStyle, "upgrade removes the previously bundled glass and preserves custom styles");
        DesktopPreferences AppliedStyle(string fileName) =>
            PortableAppearancePreset.RemovePreviousBundledGlass(new(PortableTextOpacity: .55,
                PortableStyle: previousStyle, PortableStyleFile: fileName));

        var existing = JsonSerializer.Deserialize<DesktopPreferences>(
            """{"PortableOpacity":0.73,"PortableTextOpacity":0.89,"PortableColor":"warm","PortableTopmost":false}""")!;
        check(existing is { PortableOpacity: .73, PortableTextOpacity: .89, PortableAutoLockSeconds: 10 },
            "stored opacity settings survive introducing style files");
        var applied = soft.Apply(existing with { PortableAutoLockSeconds = 30 });
        check(applied is { PortableColor: "warm", PortableTopmost: false, PortableAutoLockSeconds: 30,
            PortableOpacity: 0, PortableTextOpacity: .70, PortableOverallOpacity: .50, PortableBorderOpacity: 0 },
            "applying a style leaves interaction preferences intact");

        var root = Directory.CreateTempSubdirectory();
        try
        {
            var local = PortableAppearancePreset.EnsureFolder(Path.Combine(root.FullName, "PortableStyles"));
            string file = Path.Combine(root.FullName, "custom.json");
            File.WriteAllText(file, """
                {
                  // Copy this file into the styles directory or import it.
                  "Version": 1,
                  "Name": "自定义",
                  "OverallTransparency": 12,
                  "BackgroundTransparency": 70,
                  "BorderTransparency": 25,
                  "TextTransparency": 10,
                  "Style": { "TextColor": "#DDEEFF", "GlassStrength": 0, "GlassRadius": 11, },
                }
                """);
            var imported = PortableAppearancePreset.Import(file, local);
            var duplicate = PortableAppearancePreset.Import(file, local);
            check(imported.FileName == "custom.json" && duplicate.FileName == "custom-2.json" &&
                  PortableAppearancePreset.ReadFolder(local).Contains(imported) &&
                  imported.Apply(defaults).PortableStyle is { TextColor: "#DDEEFF", GlassStrength: 0, GlassRadius: 11 },
                "commented JSON imports without overwriting an existing style and persists custom tokens");

            string edited = Path.Combine(local, PortableAppearancePreset.DefaultFile);
            string source = File.ReadAllText(edited);
            File.WriteAllText(edited, source.Replace("\"GlassStrength\": 0", "\"GlassStrength\": 42"));
            PortableAppearancePreset.EnsureFolder(local);
            check(PortableAppearancePreset.TryRead(edited)?.Style?.GlassStrength == 0,
                "upgrading an untouched Preview 18 preset updates its independent glass behavior");
            File.WriteAllText(edited, source.Replace("\"GlassStrength\": 0", "\"GlassStrength\": 21"));
            PortableAppearancePreset.EnsureFolder(local);
            check(PortableAppearancePreset.ReadFolder(local).Single(style => style.FileName == PortableAppearancePreset.DefaultFile)
                      .Style?.GlassStrength == 21, "user edits survive reseeding built-in styles and are read from disk");

            File.WriteAllText(file, source.Replace("\"Version\": 1", "\"Version\": 2"));
            try { PortableAppearancePreset.Import(file, local); check(false, "unsupported styles are rejected"); }
            catch (JsonException) { check(PortableAppearancePreset.ReadFolder(local).Count == 5, "unsupported styles leave the saved directory unchanged"); }
        }
        finally { root.Delete(recursive: true); }

        var glass = new PortableGlass(new Rect(0, 0, 240, 24), new Rect(83, 41, 240, 24), frosted.Style!);
        var capture = glass.SamplePoints.Distinct().ToDictionary(point => point, point => point.X % 8 < 4 ? Colors.White : Colors.Black);
        var pixels = glass.Render(capture)!;
        var bitmap = glass.Image(pixels);
        int stride = bitmap.PixelWidth * 4;
        int middle = (bitmap.PixelHeight / 2) * stride;
        check(pixels[middle + 3] == 0 && pixels[middle + stride - 1] == 0 &&
              pixels[middle + 7] > 0 && pixels[middle + 7] < pixels[middle + stride / 2 + 3] &&
              pixels[middle + stride / 2] is > 0 and < 255 && pixels[middle + stride / 2 + 3] >= 200,
            "frosted strip blurs sampled detail and feathers both ends to fully transparent");
        var white = glass.SamplePoints.Distinct().ToDictionary(point => point, _ => Colors.White);
        var dark = glass.SamplePoints.Distinct().ToDictionary(point => point, _ => Colors.Black);
        int center = middle + stride / 2;
        check(glass.Render(white)![center + 2] < 245 && glass.Render(dark)![center + 2] > 12,
            "glass retains a visible adaptive tint on uniformly bright and dark backgrounds");
        capture.Clear();
        check(glass.Render(capture) == null, "unavailable background cannot produce a stale or black glass strip");
    }
}
