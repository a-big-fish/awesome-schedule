using System;

namespace AwesomeInterview.NativeDemo;

internal sealed record DesktopPreferences(
    bool CloseToTray = true,
    bool TrayHintShown = false,
    string DemoName = "演示用户",
    double PortableOpacity = 1,
    string PortableColor = "white",
    bool PortableTopmost = true,
    double PortableOverallOpacity = 1,
    double PortableTextOpacity = 1,
    double PortableBorderOpacity = 1,
    int PortableAutoLockSeconds = 10,
    PortableStyle? PortableStyle = null,
    string? PortableStyleFile = null)
{
    internal bool UsesClearOutline => string.Equals(PortableStyleFile, PortableAppearancePreset.OutlineFile, StringComparison.OrdinalIgnoreCase)
        && PortableStyle is null or { GlassStrength: 0 };
}
