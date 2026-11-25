using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class DesktopIntegration
{
    bool portableLocked, portableHoverActive;
    bool PortablePassThrough => portableLocked && !portableHoverActive;
    DispatcherTimer? portableHoverTimer;
    readonly PortableHoverGesture portableHover = new();
    Border? portableSurface;
    Border? portableHeader;
    QuietScrollViewer? portableTaskScroll, portableTimelineScroll;
    PortableWheelInput? portableWheel;
    readonly SolidColorBrush portableSectionTint = new();
    readonly SolidColorBrush portableWindowFill = new();
    readonly Dictionary<string, SolidColorBrush> portableFillBrushes = new();
    readonly Dictionary<string, SolidColorBrush> portableInkBrushes = new();
    readonly Dictionary<string, SolidColorBrush> portableOutlineBrushes = new();
    double PortableOverallOpacity => Math.Clamp(preferences.PortableOverallOpacity, .10, 1);
    double PortableBackgroundOpacity => Math.Clamp(preferences.PortableOpacity, 0, 1);
    double PortableTextOpacity => Math.Clamp(preferences.PortableTextOpacity, .10, 1);
    double PortableBorderOpacity => Math.Clamp(preferences.PortableBorderOpacity, 0, 1);
    PortableStyle ActivePortableStyle => preferences.PortableStyle is { IsValid: true } style ? style : PortableStyle.Default;
    bool NeedsPortableLabelBacking => ActivePortableStyle.GlassStrength > 0 || PortableBackgroundOpacity * PortableOverallOpacity < .90;
    Forms.ToolStripMenuItem? unlockPortable;
    Border? portableNowLine;
    TextBlock? portableClock;
    Window? portableUnlock;
    Button? portableLockTool, portablePinTool;
    Border? portableAppearancePanel;
    readonly System.Collections.Generic.List<Button> portableToolbarButtons=new();

    Button PortableTool(string label, Action action)
    {
        var button = PortableButton(label, action);
        button.Margin = new(4, 0, 0, 0);
        portableToolbarButtons.Add(button);
        button.MouseEnter+=(_,_)=>PaintPortableTool(button);
        button.MouseLeave+=(_,_)=>PaintPortableTool(button);
        button.IsKeyboardFocusedChanged+=(_,_)=>PaintPortableTool(button);
        PaintPortableTool(button);
        return button;
    }

    void PaintPortableTool(Button button)
    {
        bool active = button.IsMouseOver || button.IsKeyboardFocused || SystemParameters.HighContrast;
        bool pinned = button == portablePinTool && preferences.PortableTopmost;
        string fill = pinned ? "#EBF2FF" : "#F6F9FF";
        string ink = pinned ? "#2357CE" : "#192C46";
        button.Background = active ? Brush(fill) : PortableFill(fill);
        button.Foreground = active ? Brush(PortableInkColor(ink)) : PortableInk(ink);
        UpdatePortableToolInk(button);
        SchedulePortableContrast();
    }

    static SolidColorBrush OpacityBrush(Dictionary<string, SolidColorBrush> brushes, string color, double opacity, string? configured)
    {
        if (brushes.TryGetValue(color, out var existing)) return existing;
        var brush = Brush(configured ?? color);
        brush.Opacity = opacity;
        brushes.Add(color, brush);
        return brush;
    }

    SolidColorBrush PortableFill(string color) => OpacityBrush(portableFillBrushes, color, PortableBackgroundOpacity, PortableFillColor(color));
    SolidColorBrush PortableInk(string color) => OpacityBrush(portableInkBrushes, color, PortableTextOpacity, PortableInkColor(color));
    SolidColorBrush PortableOutline(string color) => OpacityBrush(portableOutlineBrushes, color, PortableBorderOpacity, ActivePortableStyle.OutlineColor);

    string PortableFillColor(string color) => color == "#EDF3FF" ? ActivePortableStyle.EventColor ?? color : color;
    string PortableInkColor(string color) => color switch
    {
        "#192C46" or "#263650" => ActivePortableStyle.TextColor ?? color,
        "#52647D" => ActivePortableStyle.MutedTextColor ?? color,
        PortableNowAccent or "#2357CE" => ActivePortableStyle.AccentColor ?? color,
        _ => color
    };

    TextBlock PortableText(string label, double size, string color, Thickness? margin = null)
    {
        return new PortableGlyphText
        {
            Text = label, FontSize = size, Foreground = PortableInk(color),
            TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0)
        };
    }

    TextBlock PortableLabel(string label, double size = 13, Thickness? margin = null) =>
        PortableText(label, size, "#192C46", margin);

    Border PortableSection(UIElement child, Thickness padding) => new()
    {
        Child = child, Padding = padding, Background = portableSectionTint,
        CornerRadius = new(10), BorderBrush = PortableOutline("#AABAD1"), BorderThickness = new(1),
        Margin = new(0, 0, 0, 12)
    };

    Border BuildPortableAppearancePanel()
    {
        var panel = Surface(new StackPanel(), new(8));
        panel.Margin = new(8, 2, 8, 4);
        panel.Visibility = Visibility.Collapsed;
        return panel;
    }

    void TogglePortableAppearance()
    {
        if (portableAppearancePanel == null) return;
        if (portableAppearancePanel.IsVisible)
        {
            ClosePortableAppearance();
            return;
        }

        var body = new StackPanel();
        var heading = Text("随身日程外观", 12, margin: new(0, 0, 0, 4));
        heading.FontWeight = FontWeights.SemiBold;
        body.Children.Add(heading);
        AddPortableStyleShortcuts(body);
        AddPortableTransparencyControls(body);
        var footer = new DockPanel { Margin = new(0, 4, 0, 0) };
        var done = CompactPortableButton("完成", ClosePortableAppearance);
        DockPanel.SetDock(done, Dock.Right);
        footer.Children.Add(done);
        var palette = new WrapPanel();
        foreach (var (label, color) in new[] { ("雾白", "white"), ("浅蓝", "blue"), ("暖米", "warm") })
        {
            var choice = CompactPortableButton(label, () =>
            {
                preferences = preferences with { PortableColor = color, PortableStyle = ActivePortableStyle with { SectionColor = null } };
                ApplyPortableAppearance();
            });
            choice.Margin = new(0, 0, 5, 0);
            palette.Children.Add(choice);
        }
        footer.Children.Add(palette);
        body.Children.Add(footer);
        portableAppearancePanel.Child = body;
        portableAppearancePanel.Visibility = Visibility.Visible;
    }

    void ClosePortableAppearance()
    {
        if (portableAppearancePanel is not { IsVisible: true } panel) return;
        SavePreferences();
        panel.Visibility = Visibility.Collapsed;
    }

    void TogglePortableTopmost()
    {
        var previous = preferences;
        preferences = preferences with { PortableTopmost = !preferences.PortableTopmost };
        if (!SavePreferences()) preferences = previous;
        ApplyPortableAppearance();
    }

    void PositionPortableUnlock()
    {
        if (portableUnlock is not { } unlock || portable is not { } window || portableHeader is not { } header) return;
        unlock.Left = window.Left + (window.ActualWidth - unlock.Width) / 2;
        unlock.Top = window.Top + (header.ActualHeight - unlock.Height) / 2 + (portableSurface?.BorderThickness.Top ?? 0);
    }

    void ShowPortableUnlock()
    {
        if (portable == null) return;
        // Independent button stays clickable when this window passes clicks to the desktop.
        if (portableUnlock == null)
        {
            portableUnlock = new Window
            {
                Owner = portable, Title = "恢复随身日程交互", Width = 100, Height = 33,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
                Background = Brush("#01FFFFFF"), ShowInTaskbar = false, ShowActivated = false,
                Topmost = portable.Topmost, FontFamily = shell.FontFamily
            };
            portableUnlock.SourceInitialized += (sender, _) =>
            {
                if (sender is Window window && PresentationSource.FromVisual(window) is HwndSource source)
                    source.AddHook(PortableUnlockMessage);
            };
            var unlock = CompactPortableButton("解锁交互", () => SetPortableLocked(false));
            unlock.ClickMode = ClickMode.Press;
            unlock.Margin = new(3);
            unlock.ToolTip = "关闭鼠标穿透，恢复完整交互";
            unlock.Effect = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 3, Opacity = .22 };
            unlock.MouseEnter += (_, _) => PaintPortableUnlock(unlock);
            unlock.MouseLeave += (_, _) => PaintPortableUnlock(unlock);
            unlock.IsKeyboardFocusedChanged += (_, _) => PaintPortableUnlock(unlock);
            portableUnlock.Content = unlock;
        }
        ApplyPortableUnlockAppearance();
        PositionPortableUnlock();
        if (!portableUnlock.IsVisible) portableUnlock.Show();
        SchedulePortableContrast();
    }

    static nint PortableUnlockMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x0021) return 0; // WM_MOUSEACTIVATE
        handled = true;
        return 3; // MA_NOACTIVATE: keep the click, defer activation until interaction is unlocked.
    }

    const int ExtendedStyle = -20;
    const long TransparentStyle = 0x20, NoActivateStyle = 0x08000000;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    static extern nint GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    static extern nint SetWindowLongPtr(nint handle, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);

    bool ApplyPortableInputStyle(nint hwnd, bool passThrough)
    {
        long value = GetWindowLongPtr(hwnd, ExtendedStyle).ToInt64();
        long next = passThrough ? value | TransparentStyle | NoActivateStyle : value & ~(TransparentStyle | NoActivateStyle);
        if (next == value) return true;
        var old = SetWindowLongPtr(hwnd, ExtendedStyle, new nint(next));
        const uint refreshStyle = 0x0020 | 0x0010 | 0x0004 | 0x0002 | 0x0001; // FRAMECHANGED, NOACTIVATE, NOZORDER, NOMOVE, NOSIZE
        if (old != 0 || Marshal.GetLastPInvokeError() == 0)
        {
            if (SetWindowPos(hwnd, 0, 0, 0, 0, 0, refreshStyle)) return true;
            SetWindowLongPtr(hwnd, ExtendedStyle, new nint(value));
        }
        shell.Status.Text = "无法切换随身日程的鼠标模式，请重新打开。";
        return false;
    }

    void SetPortableLocked(bool locked)
    {
        if (portable == null || portableLocked == locked) return;
        var hwnd = new WindowInteropHelper(portable).Handle;
        if (hwnd == 0 || !ApplyPortableInputStyle(hwnd, locked)) return;

        portableLocked = locked;
        portableHoverActive = false;
        UpdatePortableHoverCue(null);
        portableHover.Reset();
        if (locked)
        {
            if (PortableWindowHitTest.TryGetCursor(out var cursor))
                portableHover.WaitForMovement(cursor.X, cursor.Y);
            ClosePortableAppearance();
            Keyboard.ClearFocus();
            SchedulePortableFocusReset();
            portableWheel = new PortableWheelInput(hwnd, ScrollLockedPortable);
            if (!portableWheel.Start())
            {
                portableWheel = null;
                shell.Status.Text = "随身日程已开启穿透，滚轮接收暂不可用。";
            }
            portableHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            portableHoverTimer.Tick += (_, _) => UpdatePortableHover(hwnd);
            portableHoverTimer.Start();
        }
        else
        {
            StopPortableInput();
            portableUnlock?.Close();
            portableUnlock = null;
        }

        UpdatePortableTools();
        RestartPortableIdleTimer();
        if (unlockPortable != null) unlockPortable.Enabled = locked;
        if (!locked) portable.Activate();
    }

    void StopPortableInput()
    {
        portableHoverTimer?.Stop();
        portableHoverTimer = null;
        UpdatePortableHoverCue(null);
        portableWheel?.Dispose();
        portableWheel = null;
    }

    void UpdatePortableHover(nint hwnd)
    {
        if (!PortableWindowHitTest.TryGetCursor(out var point)) return;
        EvaluatePortableHover(hwnd, point, Environment.TickCount64);
    }

    void EvaluatePortableHover(nint hwnd, ScreenPoint point, long now)
    {
        if (!portableLocked || portable is not { IsVisible: true } window) return;
        bool over = PortableWindowHitTest.CoversPoint(hwnd, point);
        if (over && portableUnlock is { IsVisible: true } unlock &&
            PortableWindowHitTest.ContainsPoint(new WindowInteropHelper(unlock).Handle, point))
            over = false;
        bool mouseBusy = portableDetailOpen || PortableWindowHitTest.AnyMouseButtonDown || window.IsMouseCaptureWithin;
        bool interactive = portableHover.Update(over, mouseBusy, point.X, point.Y, now);
        if (interactive == portableHoverActive)
        {
            PositionPortableHoverCue(interactive && over, point);
            return;
        }
        if (!ApplyPortableInputStyle(hwnd, !interactive)) return;

        portableHoverActive = interactive;
        if (!interactive)
        {
            ClosePortableAppearance();
            if (window.IsKeyboardFocusWithin) Keyboard.ClearFocus();
            SchedulePortableFocusReset();
        }
        else portableFocusReset?.Stop();
        PositionPortableHoverCue(interactive && over, point);
        UpdatePortableTools();
        if (interactive) SchedulePortableContrast();
    }

    void PositionPortableHoverCue(bool visible, ScreenPoint point) =>
        UpdatePortableHoverCue(visible && portableHoverCueLayer is { } layer && PresentationSource.FromVisual(layer) != null
            ? layer.PointFromScreen(new Point(point.X, point.Y)) : null);

    void UpdatePortableTools()
    {
        ApplyPortableFrame();
        if (portableLockTool != null)
        {
            portableLockTool.Content = portableHoverActive ? "解锁交互" : "鼠标穿透";
            portableLockTool.ToolTip = portableHoverActive ? "关闭穿透，移开鼠标后仍可交互"
                : "穿透后可滚动。悬停可临时操作，点击顶部的「解锁交互」可完全恢复。";
        }
        if (PortablePassThrough) ShowPortableUnlock();
        else portableUnlock?.Hide();
    }

    bool ScrollLockedPortable(int screenX, int screenY, int delta)
    {
        var scroll = portableTimelineScroll ?? portableTaskScroll;
        if (!PortablePassThrough || scroll is not { IsVisible: true } ||
            !ContainsPortablePoint(scroll, new Point(screenX, screenY)) || !scroll.TryScrollWithWheel(delta))
            return false;

        SchedulePortableFocusReset();
        return true;
    }

    static bool ContainsPortablePoint(FrameworkElement element, Point screenPoint)
        => PresentationSource.FromVisual(element) != null &&
           new Rect(element.RenderSize).Contains(element.PointFromScreen(screenPoint));

    void ApplyPortableAppearance()
    {
        double overall = PortableOverallOpacity, background = PortableBackgroundOpacity, ink = PortableTextOpacity;
        double edgeOpacity = SystemParameters.HighContrast ? 0 : Math.Min(1, ActivePortableStyle.GlassStrength / 60d);
        portableTopGlassEdge.Opacity = portableBottomGlassEdge.Opacity = edgeOpacity;
        foreach (var (color, brush) in portableFillBrushes)
        {
            brush.Color = Brush(PortableFillColor(color)).Color;
            brush.Opacity = background;
        }
        foreach (var (color, brush) in portableInkBrushes)
        {
            brush.Color = Brush(PortableInkColor(color)).Color;
            brush.Opacity = ink;
        }
        double border = PortableBorderOpacity;
        foreach (var (color, brush) in portableOutlineBrushes)
        {
            brush.Color = Brush(ActivePortableStyle.OutlineColor ?? color).Color;
            brush.Opacity = border;
        }
        portableSectionTint.Color = ActivePortableStyle.SectionColor is { } sectionColor ? Brush(sectionColor).Color :
            preferences.PortableColor switch
        {
            "blue" => Color.FromRgb(236, 243, 253),
            "warm" => Color.FromRgb(250, 246, 237),
            _ => Color.FromRgb(248, 250, 252)
        };
        portableSectionTint.Opacity = background;
        portableAppearanceChanged?.Invoke();
        if (!NeedsPortableLabelBacking) ClearPortableGlass(restoreInk: true);

        if (portable == null || portableSurface == null) return;
        ApplyPortableFrame();
        if (portableHeader != null)
        {
            portableHeader.Background = portableSectionTint;
            portableHeader.Opacity = overall;
        }
        if (portableContent != null) portableContent.Opacity = overall;
        portable.Topmost = preferences.PortableTopmost;
        if (portablePinTool != null)
        {
            portablePinTool.Content = preferences.PortableTopmost ? "已置顶" : "置顶";
            portablePinTool.ToolTip = preferences.PortableTopmost ? "取消随身日程置顶" : "保持随身日程置顶";
        }
        foreach (var button in portableToolbarButtons) PaintPortableTool(button);
        ApplyPortableUnlockAppearance();
        SchedulePortableContrast();
    }

    void ApplyPortableFrame()
    {
        if (portableHeader != null)
            portableHeader.Visibility = PortablePassThrough ? Visibility.Hidden : Visibility.Visible;
        if (portableSurface == null) return;
        // Keep a low-alpha backing for hit testing while preserving the selected window fill.
        double transparentHitSurface = (portableHoverActive ? 4 : 1) / 255d;
        if (ActivePortableStyle.FillWindow)
        {
            portableWindowFill.Color = portableSectionTint.Color;
            portableWindowFill.Opacity = Math.Max(transparentHitSurface, PortableBackgroundOpacity * PortableOverallOpacity);
            portableSurface.Background = portableWindowFill;
        }
        else portableSurface.Background = Brush(portableHoverActive ? "#04FFFFFF" : "#01FFFFFF");
        if (PortablePassThrough) portableSurface.BorderBrush = Brushes.Transparent;
        else
        {
            var outline = Brush("#AABAD1");
            outline.Opacity = PortableOverallOpacity * PortableBorderOpacity;
            portableSurface.BorderBrush = outline;
        }
    }

    void ApplyPortableUnlockAppearance()
    {
        if (portableUnlock == null) return;
        portableUnlock.Topmost = preferences.PortableTopmost;
        if (portableUnlock.Content is Button unlock) PaintPortableUnlock(unlock);
    }

    void PaintPortableUnlock(Button unlock)
    {
        bool active = unlock.IsMouseOver || unlock.IsKeyboardFocused || SystemParameters.HighContrast;
        unlock.Background = active ? Brush("#F6F9FF") : Brushes.Transparent;
        unlock.BorderBrush = active ? Brush("#ABBAD0") : Brush("#26657A98");
        unlock.Foreground = Brush("#192C46");
        unlock.ContentTemplateSelector = active ? null : PortableGlyphTemplateSelector.Instance;
        if (unlock.Effect is DropShadowEffect shadow) shadow.Opacity = active ? 0 : .22;
        if (!active) SchedulePortableContrast();
    }

    void AddPortablePreferences(StackPanel body)
    {
        body.Children.Add(Text("随身日程外观", 19, margin: new(0, 24, 0, 14)));
        AddPortableStyleControls(body);
        AddPortableTransparencyControls(body);
        var palette = new ComboBox
        {
            ItemsSource = new[] { "雾白", "浅蓝", "暖米" },
            SelectedIndex = preferences.PortableColor == "blue" ? 1 : preferences.PortableColor == "warm" ? 2 : 0
        };
        palette.SelectionChanged += (_, _) =>
        {
            preferences = preferences with { PortableColor = palette.SelectedIndex == 1 ? "blue"
                : palette.SelectedIndex == 2 ? "warm" : "white", PortableStyle = ActivePortableStyle with { SectionColor = null } };
            ApplyPortableAppearance();
        };
        body.Children.Add(palette);
        var topmost = new CheckBox
        {
            Content = "随身日程置顶", IsChecked = preferences.PortableTopmost, Margin = new(0, 14, 0, 14)
        };
        topmost.Click += (_, _) =>
        {
            preferences = preferences with { PortableTopmost = topmost.IsChecked == true };
            ApplyPortableAppearance();
        };
        body.Children.Add(topmost);
        AddPortableIdleSetting(body);
        var feedback = Text("穿透时可用滚轮浏览。鼠标停稳约 0.8 秒可临时操作，移开后恢复穿透。", 13, "#64748B");
        body.Children.Add(feedback);
        body.Children.Add(Button("保存随身日程外观", () =>
        {
            if (SavePreferences()) feedback.Text = "外观已保存，下次打开继续使用。";
        }));
        body.Children.Add(Button("恢复随身日程交互", () => SetPortableLocked(false)));
    }
}
