using System.Text.Json;
using AwesomeInterview.NativeDemo;

internal static class PortableAppearancePresetChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var presets = PortableAppearancePreset.BuiltIn;
        var defaults = new DesktopPreferences();
        check(presets.Count == 2, "both portable appearance JSON files load");
        if (presets.Count != 2) return;

        var clear = presets.Single(preset => preset.Name == "清透描边");
        var soft = presets.Single(preset => preset.Name == "轻透淡影");
        check(clear is { OverallTransparency: 0, BackgroundTransparency: 100, BorderTransparency: 0, TextTransparency: 0 }
            && clear.Apply(defaults) == defaults && defaults.PortableAutoLockSeconds == 10,
            "the second screenshot supplies fresh appearance defaults and idle lock defaults to ten seconds");
        check(soft is { OverallTransparency: 64, BackgroundTransparency: 100, BorderTransparency: 64, TextTransparency: 6 },
            "the first screenshot is preserved as the soft preset");

        var existing = JsonSerializer.Deserialize<DesktopPreferences>(
            """{"PortableOpacity":0.73,"PortableTextOpacity":0.89,"PortableColor":"warm","PortableTopmost":false}""")!;
        check(existing is { PortableOpacity: .73, PortableTextOpacity: .89, PortableAutoLockSeconds: 10 },
            "legacy saved appearance is preserved when the idle setting is added");
        var applied = soft.Apply(existing with { PortableAutoLockSeconds = 30 });
        check(applied is { PortableColor: "warm", PortableTopmost: false, PortableAutoLockSeconds: 30, PortableOpacity: 0, PortableTextOpacity: .94 }
            && Math.Abs(applied.PortableOverallOpacity - .36) < .00001
            && Math.Abs(applied.PortableBorderOpacity - .36) < .00001,
            "selecting a preset updates only the four appearance sliders");
    }
}
