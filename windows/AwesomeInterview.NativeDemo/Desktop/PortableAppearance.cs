using System;
using System.Collections.Generic;
using System.Windows.Automation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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
    readonly Dictionary<string, SolidColorBrush> portableFillBrushes = new();
    readonly Dictionary<string, SolidColorBrush> portableInkBrushes = new();
    readonly Dictionary<string, SolidColorBrush> portableOutlineBrushes = new();
    double PortableOverallOpacity => Math.Clamp(preferences.PortableOverallOpacity, .10, 1);
    double PortableBackgroundOpacity => Math.Clamp(preferences.PortableOpacity, 0, 1);
    double PortableTextOpacity => Math.Clamp(preferences.PortableTextOpacity, .10, 1);
    double PortableBorderOpacity => Math.Clamp(preferences.PortableBorderOpacity, 0, 1);
    Forms.ToolStripMenuItem? unlockPortable;
    Border? portableNowLine;
    TextBlock? portableClock;
    Window? portableUnlock;
    Button? portableLockTool, portablePinTool;
    Border? portableAppearancePanel;
    readonly System.Collections.Generic.List<Button> portableToolbarButtons=new();

    Button PortableTool(string label, Action action, bool toolbar = true)
    {
        var button = PortableButton(label, action);
        button.Margin = new(4, 0, 0, 0);
        if (toolbar) portableToolbarButtons.Add(button);
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
        button.Foreground = active ? Brush(ink) : PortableInk(ink);
    }

    static SolidColorBrush OpacityBrush(Dictionary<string, SolidColorBrush> brushes, string color, double opacity)
    {
        if (brushes.TryGetValue(color, out var existing)) return existing;
        var brush = Brush(color);
        brush.Opacity = opacity;
        brushes.Add(color, brush);
        return brush;
    }

    SolidColorBrush PortableFill(string color) => OpacityBrush(portableFillBrushes, color, PortableBackgroundOpacity);
    SolidColorBrush PortableInk(string color) => OpacityBrush(portableInkBrushes, color, PortableTextOpacity);
    SolidColorBrush PortableOutline(string color) => OpacityBrush(portableOutlineBrushes, color, PortableBorderOpacity);

    TextBlock PortableText(string label, double size, string color, Thickness? margin = null)
    {
        var text = Text(label, size, color, margin);
        text.Foreground = PortableInk(color);
        return text;
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
                preferences = preferences with { PortableColor = color };
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

    void AddPortableTransparencyControls(Panel body)
    {
        AddTransparencySlider(body, "整体透明度", PortableOverallOpacity, .9,
            opacity => preferences = preferences with { PortableOverallOpacity = opacity });
        AddTransparencySlider(body, "底色透明度", PortableBackgroundOpacity, 1,
            opacity => preferences = preferences with { PortableOpacity = opacity });
        AddTransparencySlider(body, "边框透明度", PortableBorderOpacity, 1,
            opacity => preferences = preferences with { PortableBorderOpacity = opacity });
        AddTransparencySlider(body, "字体透明度", PortableTextOpacity, .9,
            opacity => preferences = preferences with { PortableTextOpacity = opacity });
    }

    void AddTransparencySlider(Panel body, string name, double opacity, double maxTransparency, Action<double> change)
    {
        var row = new Grid { Margin = new(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new() { Width = new(74) });
        row.ColumnDefinitions.Add(new());
        row.ColumnDefinitions.Add(new() { Width = new(40) });
        var title = Text(name, 11);
        title.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(title);
        var slider = new Slider
        {
            Minimum = 0, Maximum = maxTransparency, Value = Math.Clamp(1 - opacity, 0, maxTransparency),
            SmallChange = .01, LargeChange = .1, VerticalAlignment = VerticalAlignment.Center,
            Margin = new(2, 0, 3, 0)
        };
        AutomationProperties.SetName(slider, name);
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        var value = Text(slider.Value.ToString("P0"), 11, "#52647D");
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 2);
        row.Children.Add(value);
        slider.ValueChanged += (_, e) =>
        {
            value.Text = e.NewValue.ToString("P0");
            change(1 - e.NewValue);
            ApplyPortableAppearance();
        };
        body.Children.Add(row);
    }

    void PositionPortableUnlock(){if(portableUnlock!=null&&portable!=null){portableUnlock.Left=portable.Left+portable.ActualWidth-portableUnlock.Width-9;portableUnlock.Top=portable.Top+7;}}
    void ShowPortableUnlock()
    {
        if (portable == null) return;
        // Independent button stays clickable when this window passes clicks to the desktop.
        if (portableUnlock == null)
        {
            portableUnlock = new Window
            {
                Owner = portable, Title = "恢复随身日程交互", Width = 90, Height = 27,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
                Background = Brush("#01FFFFFF"), ShowInTaskbar = false, ShowActivated = false,
                Topmost = portable.Topmost, FontFamily = shell.FontFamily
            };
            var unlock = PortableTool("解锁交互", () => SetPortableLocked(false), toolbar: false);
            unlock.Margin = new(0);
            unlock.ToolTip = "关闭鼠标穿透，恢复完整交互";
            portableUnlock.Content = unlock;
        }
        ApplyPortableUnlockOpacity();
        PositionPortableUnlock();
        if (!portableUnlock.IsVisible) portableUnlock.Show();
    }

    const int ExtendedStyle = -20;
    const long TransparentStyle = 0x20, NoActivateStyle = 0x08000000;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    static extern nint GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    static extern nint SetWindowLongPtr(nint handle, int index, nint value);

    bool ApplyPortableInputStyle(nint hwnd, bool passThrough)
    {
        long value = GetWindowLongPtr(hwnd, ExtendedStyle).ToInt64();
        long next = passThrough ? value | TransparentStyle | NoActivateStyle : value & ~(TransparentStyle | NoActivateStyle);
        if (next == value) return true;
        var old = SetWindowLongPtr(hwnd, ExtendedStyle, new nint(next));
        if (old != 0 || Marshal.GetLastPInvokeError() == 0) return true;
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
        portableHover.Reset();
        if (locked)
        {
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
        if (unlockPortable != null) unlockPortable.Enabled = locked;
        if (!locked) portable.Activate();
    }

    void StopPortableInput()
    {
        portableHoverTimer?.Stop();
        portableHoverTimer = null;
        portableWheel?.Dispose();
        portableWheel = null;
    }

    void UpdatePortableHover(nint hwnd)
    {
        if (!portableLocked || portable is not { IsVisible: true } window) return;
        bool over = PortableWindowHitTest.TryGetCursor(out var point) && PortableWindowHitTest.CoversPoint(hwnd, point);
        bool mouseBusy = PortableWindowHitTest.AnyMouseButtonDown || window.IsMouseCaptureWithin;
        bool interactive = portableHover.Update(over, mouseBusy, point.X, point.Y, Environment.TickCount64);
        if (interactive == portableHoverActive || !ApplyPortableInputStyle(hwnd, !interactive)) return;

        portableHoverActive = interactive;
        if (!interactive)
        {
            ClosePortableAppearance();
            if (window.IsKeyboardFocusWithin) Keyboard.ClearFocus();
            SchedulePortableFocusReset();
        }
        else portableFocusReset?.Stop();
        UpdatePortableTools();
    }

    void UpdatePortableTools()
    {
        foreach (var tool in portableToolbarButtons)
            tool.Visibility = PortablePassThrough ? Visibility.Hidden : Visibility.Visible;
        if (portableLockTool != null)
        {
            portableLockTool.Content = portableHoverActive ? "解除穿透" : "鼠标穿透";
            portableLockTool.ToolTip = portableHoverActive ? "关闭穿透，移开鼠标后仍可交互"
                : "穿透后可滚动。悬停可临时操作，右上角可完全恢复。";
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
        foreach (var brush in portableFillBrushes.Values) brush.Opacity = background;
        foreach (var brush in portableInkBrushes.Values) brush.Opacity = ink;
        double border = PortableBorderOpacity;
        foreach (var brush in portableOutlineBrushes.Values) brush.Opacity = border;
        portableSectionTint.Color = preferences.PortableColor switch
        {
            "blue" => Color.FromRgb(236, 243, 253),
            "warm" => Color.FromRgb(250, 246, 237),
            _ => Color.FromRgb(248, 250, 252)
        };
        portableSectionTint.Opacity = background;

        if (portable == null || portableSurface == null) return;
        // The nearly clear hit surface lets the top bar remain draggable at 100% backdrop transparency.
        portableSurface.Background = Brush("#01FFFFFF");
        var outline = Brush("#AABAD1");
        outline.Opacity = overall * border;
        portableSurface.BorderBrush = outline;
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
        ApplyPortableUnlockOpacity();
    }

    void ApplyPortableUnlockOpacity()
    {
        if (portableUnlock == null) return;
        portableUnlock.Topmost = preferences.PortableTopmost;
        if (portableUnlock.Content is Button unlock)
        {
            unlock.Opacity = PortableOverallOpacity;
            PaintPortableTool(unlock);
        }
    }

    void AddPortablePreferences(StackPanel body)
    {
        body.Children.Add(Text("随身日程外观", 19, margin: new(0, 24, 0, 14)));
        AddPortableTransparencyControls(body);
        var palette = new ComboBox
        {
            ItemsSource = new[] { "雾白", "浅蓝", "暖米" },
            SelectedIndex = preferences.PortableColor == "blue" ? 1 : preferences.PortableColor == "warm" ? 2 : 0
        };
        palette.SelectionChanged += (_, _) =>
        {
            preferences = preferences with { PortableColor = palette.SelectedIndex == 1 ? "blue"
                : palette.SelectedIndex == 2 ? "warm" : "white" };
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
        var feedback = Text("穿透时可用滚轮浏览。鼠标停稳约 0.8 秒可临时操作，移开后恢复穿透。", 13, "#64748B");
        body.Children.Add(feedback);
        body.Children.Add(Button("保存随身日程外观", () =>
        {
            if (SavePreferences()) feedback.Text = "外观已保存，下次打开继续使用。";
        }));
        body.Children.Add(Button("恢复随身日程交互", () => SetPortableLocked(false)));
    }
}
