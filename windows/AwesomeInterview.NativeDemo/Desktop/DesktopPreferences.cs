namespace AwesomeInterview.NativeDemo;

internal sealed record DesktopPreferences(
    bool CloseToTray = true,
    bool TrayHintShown = false,
    string DemoName = "演示用户",
    double PortableOpacity = .92,
    string PortableColor = "white",
    bool PortableTopmost = true,
    double PortableOverallOpacity = 1,
    double PortableTextOpacity = 1,
    double PortableBorderOpacity = .85);
