namespace AwesomeInterview.NativeDemo;

internal sealed record DesktopPreferences(
    bool CloseToTray = true,
    bool TrayHintShown = false,
    string DemoName = "演示用户",
    double PortableOpacity = 0,
    string PortableColor = "white",
    bool PortableTopmost = true,
    double PortableOverallOpacity = .50,
    double PortableTextOpacity = .70,
    double PortableBorderOpacity = 0,
    int PortableAutoLockSeconds = 10,
    PortableStyle? PortableStyle = null,
    string? PortableStyleFile = null);
