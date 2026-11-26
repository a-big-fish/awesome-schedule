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
        check(presets.Count == 4, "all four portable appearance JSON files load");
        if (presets.Count != 4) return;

        var solid = presets.Single(preset => preset.FileName == PortableAppearancePreset.DefaultFile);
        var clear = presets.Single(preset => preset.Name == "清透描边");
        var soft = presets.Single(preset => preset.Name == "清透淡影");
        var frosted = presets.Single(preset => preset.Name == "磨砂玻璃");
        check(clear is { OverallTransparency: 0, BackgroundTransparency: 100, BorderTransparency: 0, TextTransparency: 0,
            Style.GlassStrength: 0 }, "the clear outline preset uses the requested values without glass");
        var appliedOutline = clear.Apply(defaults);
        var savedOutline = JsonSerializer.Deserialize<DesktopPreferences>("""{"PortableStyleFile":"clear-outline.json","PortableOpacity":0}""")!;
        check(appliedOutline.UsesClearOutline && PortableAppearancePreset.UpgradeSavedStyle(savedOutline).UsesClearOutline &&
            !soft.Apply(defaults).UsesClearOutline && !frosted.Apply(defaults).UsesClearOutline &&
            !(appliedOutline with { PortableStyle = new(GlassStrength: 40) }).UsesClearOutline,
            "saved clear outline styles use only strokes while other presets and explicit glass retain their material");
        check(soft is { OverallTransparency: 50, BackgroundTransparency: 100, BorderTransparency: 100, TextTransparency: 30 },
            "the original translucent preset retains its opacity");
        check(presets[0] == solid && solid is { Name: "实色", OverallTransparency: 0, BackgroundTransparency: 0,
            BorderTransparency: 0, TextTransparency: 0, Style: { FillWindow: true, GlassStrength: 0 } } &&
            (solid.Apply(defaults) with { PortableStyle = null, PortableStyleFile = null }) == defaults &&
            defaults.PortableAutoLockSeconds == 10, "the first preset and fresh default are fully opaque");

        check(soft.Style?.GlassStrength == 0 && frosted is { OverallTransparency: 0, BackgroundTransparency: 100,
            BorderTransparency: 100, Style: { GlassStrength: 60, GlassTone: "auto", GlassRefraction: 7 } },
            "only the independent glass preset enables transparent optics");

        var previousStyle = new PortableStyle(GlassStrength: 42);
        var upgraded = AppliedStyle(PortableAppearancePreset.SoftFile);
        var custom = AppliedStyle("user-style.json");
        check(upgraded is { PortableTextOpacity: .55, PortableStyle.GlassStrength: 0 } &&
            custom.PortableStyle == previousStyle, "upgrade removes the previously bundled glass and preserves custom styles");
        DesktopPreferences AppliedStyle(string fileName) =>
            PortableAppearancePreset.UpgradeSavedStyle(new(PortableTextOpacity: .55,
                PortableStyle: previousStyle, PortableStyleFile: fileName));

        var oldGlass = new PortableStyle(GlassStrength: 82, GlassRadius: 14, GlassFeather: 16);
        var denseGlass = new PortableStyle(GlassStrength: 85, GlassRadius: 14, GlassFeather: 24);
        var paleGlass = new PortableStyle(GlassStrength: 60, GlassRadius: 14, GlassFeather: 14, GlassTone: "light");
        check(new[] { oldGlass, denseGlass, paleGlass }.All(previous =>
        {
            var saved = new DesktopPreferences(PortableStyle: previous,
                PortableStyleFile: PortableAppearancePreset.GlassFile, PortableTextOpacity: .88);
            var upgradedGlass = PortableAppearancePreset.UpgradeSavedStyle(saved);
            return upgradedGlass.PortableStyle == frosted.Style && upgradedGlass.PortableTextOpacity == .88;
        }), "earlier bundled glass presets migrate without changing user transparency");
        var customizedGlass = new DesktopPreferences(PortableStyle: denseGlass with { GlassFeather = 19 },
            PortableStyleFile: PortableAppearancePreset.GlassFile);
        check(PortableAppearancePreset.UpgradeSavedStyle(customizedGlass) == customizedGlass,
            "upgrading preserves explicitly customized frosted glass");

        var existing = JsonSerializer.Deserialize<DesktopPreferences>(
            """{"PortableOpacity":0.73,"PortableTextOpacity":0.89,"PortableColor":"warm","PortableTopmost":false}""")!;
        check(existing is { PortableOpacity: .73, PortableTextOpacity: .89, PortableAutoLockSeconds: 10 },
            "stored opacity settings survive introducing style files");
        var applied = soft.Apply(existing with { PortableAutoLockSeconds = 30 });
        check(PortableAppearancePreset.UpgradeSavedStyle(applied) == applied,
            "an existing selected style survives the new default");
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

            string edited = Path.Combine(local, PortableAppearancePreset.SoftFile);
            string source = File.ReadAllText(edited);
            File.WriteAllText(edited, source.Replace("\"GlassStrength\": 0", "\"GlassStrength\": 42"));
            PortableAppearancePreset.EnsureFolder(local);
            check(PortableAppearancePreset.TryRead(edited)?.Style?.GlassStrength == 0,
                "upgrading an untouched Preview 18 preset updates its independent glass behavior");
            File.WriteAllText(edited, source.Replace("\"GlassStrength\": 0", "\"GlassStrength\": 21"));
            PortableAppearancePreset.EnsureFolder(local);
            check(PortableAppearancePreset.ReadFolder(local).Single(style => style.FileName == PortableAppearancePreset.SoftFile)
                      .Style?.GlassStrength == 21, "user edits survive reseeding built-in styles and are read from disk");

            string glassFile = Path.Combine(local, PortableAppearancePreset.GlassFile);
            File.WriteAllText(glassFile, JsonSerializer.Serialize(frosted with { Style = paleGlass }));
            PortableAppearancePreset.EnsureFolder(local);
            check(PortableAppearancePreset.TryRead(glassFile)?.Style == frosted.Style,
                "a previous bundled glass file is refreshed automatically");

            File.WriteAllText(file, source.Replace("\"Version\": 1", "\"Version\": 2"));
            try { PortableAppearancePreset.Import(file, local); check(false, "unsupported styles are rejected"); }
            catch (JsonException) { check(PortableAppearancePreset.ReadFolder(local).Count == 6, "unsupported styles leave the saved directory unchanged"); }
        }
        finally { root.Delete(recursive: true); }

        var glass = new PortableGlass(new Rect(0, 0, 240, 24), new Rect(83, 41, 240, 24), frosted.Style!);
        var capture = glass.SamplePoints.Distinct().ToDictionary(point => point, point => point.X % 8 < 4 ? Colors.White : Colors.Black);
        var pixels = glass.Render(capture);
        var bitmap = glass.Image(pixels);
        int stride = bitmap.PixelWidth * 4;
        int middle = (bitmap.PixelHeight / 2) * stride;
        int across = bitmap.PixelWidth / 2 * 4;
        check(pixels[3] == 0 && pixels[stride - 1] == 0 && pixels[across + 3] < pixels[middle + across + 3] &&
              pixels[middle + across + 3] >= 235, "busy backgrounds are suppressed in the glass core with a fading edge");
        var white = glass.SamplePoints.Distinct().ToDictionary(point => point, _ => Colors.White);
        var dark = glass.SamplePoints.Distinct().ToDictionary(point => point, _ => Colors.Black);
        int center = middle + stride / 2;
        var taskInk = new Rect(20, 14, 80, 13);
        var expanded = PortableGlass.Expand(taskInk, frosted.Style!.GlassFeather);
        check(expanded.Width == 108 && expanded.Height == 29 && expanded.Contains(taskInk),
            "the glass footprint extends past the headline in both directions");
        var onWhite = glass.RenderFrame(white);
        var onDark = glass.RenderFrame(dark);
        check(onWhite.Ink == Colors.Black && onWhite.Pixels[center + 2] >= 245 &&
              onDark.Ink == Colors.White && onDark.Pixels[center + 2] < 42 &&
              ContrastOnGray(onWhite.Pixels, center, 255, onWhite.Ink) >= 4.5 &&
              ContrastOnGray(onDark.Pixels, center, 0, onDark.Ink) >= 4.5,
            "both uniform backgrounds remain transparent and readable");
        capture.Clear();
        check(glass.RenderFrame(capture, onDark).Ink == Colors.White,
            "unavailable texture retains the previous surface polarity");

        // A thin underlying line can disappear in the regional average but still overlap a label.
        var textOverWhite = glass.SamplePoints.Distinct().ToDictionary(point => point,
            point => point.Y is >= 50 and < 53 && point.X % 14 < 7 ? Colors.Black : Colors.White);
        var textOverDark = textOverWhite.ToDictionary(pair => pair.Key,
            pair => pair.Value == Colors.White ? Colors.Black : Colors.White);
        var readable = new PortableGlass(glass.LocalBounds, glass.ScreenBounds, new());
        check(readable.RenderFrame(white).Pixels.Length == 0 && readable.RenderFrame(dark).Pixels.Length == 0,
            "adaptive backing leaves plain backgrounds uncovered");
        var glassLight = glass.RenderFrame(textOverWhite);
        var glassDark = glass.RenderFrame(textOverDark);
        var clearLight = readable.RenderFrame(textOverWhite);
        var clearDark = readable.RenderFrame(textOverDark);
        check(glassLight.Ink == Colors.Black && glassDark.Ink == Colors.White &&
              ContrastOnGray(glassLight.Pixels, center, 0, glassLight.Ink) >= 4.5 &&
              ContrastOnGray(glassDark.Pixels, center, 255, glassDark.Ink) >= 4.5 &&
              glassLight.Pixels[center + 3] >= 235 && glassDark.Pixels[center + 3] >= 235,
            "frosted glass keeps dark and light labels readable over opposite underlay strokes");
        check(clearLight.Ink == Colors.Black && clearDark.Ink == Colors.White &&
              ContrastOnGray(clearLight.Pixels, center, 0, clearLight.Ink) >= 4.5 &&
              ContrastOnGray(clearDark.Pixels, center, 255, clearDark.Ink) >= 4.5 &&
              clearLight.Pixels[center + 3] >= 235 && clearDark.Pixels[center + 3] >= 235 &&
              clearLight.Pixels[across + 3] < 30 && readable.RenderFrame(white, clearLight).Pixels.Length == 0,
            "adaptive backing masks busy text only near the ink and clears over plain backgrounds");

        var stripes = glass.SamplePoints.Distinct().ToDictionary(point => point,
            point => point.Y % 15 < 7 ? Colors.Tomato : Colors.SteelBlue);
        var flat = new PortableGlass(glass.LocalBounds, glass.ScreenBounds, frosted.Style with { GlassRefraction = 0 });
        var straight = flat.Render(stripes);
        var bent = glass.Render(stripes);
        check(straight.AsSpan(center, 4).SequenceEqual(bent.AsSpan(center, 4)) &&
              !straight.AsSpan(2 * stride, stride).SequenceEqual(bent.AsSpan(2 * stride, stride)),
            "glass bends texture near the rim and leaves the center undistorted");
        var frame = new PortableGlassFrame(1, 1, [30, 40, 200, 150], Colors.Black);
        var originalBackground = Color.FromRgb(125, 95, 75);
        byte Mix(byte foreground, byte background) =>
            (byte)Math.Round((foreground * 150 + background * 105) / 255d);
        var captured = Color.FromRgb(Mix(200, originalBackground.R), Mix(40, originalBackground.G), Mix(30, originalBackground.B));
        var recovered = frame.RecoverBackdrop(captured, .5, .5, 1);
        check(Math.Abs(recovered.R - originalBackground.R) <= 1 && Math.Abs(recovered.G - originalBackground.G) <= 1 &&
              Math.Abs(recovered.B - originalBackground.B) <= 1, "a composited strip recovers its backdrop without feedback");
    }

    static double ContrastOnGray(byte[] pixels, int index, int background, Color ink)
    {
        var alpha = pixels[index + 3] / 255d;
        double Blend(byte channel) => (background + (channel - background) * alpha) / 255d;
        double luminance = .2126 * PortableOklch.Linearize(Blend(pixels[index + 2])) +
            .7152 * PortableOklch.Linearize(Blend(pixels[index + 1])) + .0722 * PortableOklch.Linearize(Blend(pixels[index]));
        return ink == Colors.White ? 1.05 / (luminance + .05) : (luminance + .05) / .05;
    }
}
