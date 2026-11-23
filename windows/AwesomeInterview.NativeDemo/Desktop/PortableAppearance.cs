using System;
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
    Forms.ToolStripMenuItem? unlockPortable;
    Border? portableNowLine;
    TextBlock? portableClock;
    Window? portableUnlock;
    Button? portableLockTool;
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
        bool active=button.IsMouseOver||button.IsKeyboardFocused||SystemParameters.HighContrast;
        var tint=Color.FromRgb(246,249,255);
        tint.A=active?(byte)255:(byte)(255*(.70+.28*Math.Clamp(preferences.PortableOpacity,.15,1)));
        button.Background=new SolidColorBrush(tint);
        button.Foreground=Brush("#192C46");
    }

    static TextBlock PortableLabel(string label, double size = 13, Thickness? margin = null)
        => Text(label, size, "#192C46", margin);

    Border PortableSection(UIElement child, Thickness padding) => new()
    {
        Child = child, Padding = padding, Background = portableSectionTint,
        CornerRadius = new(10), BorderBrush = Brush("#CCD6E7"), BorderThickness = new(1),
        Margin = new(0, 0, 0, 12)
    };

    Border BuildPortableAppearancePanel()
    {
        var body=new StackPanel();
        var heading=PortableLabel("随身日程外观",12,new(0,0,0,4));
        heading.FontWeight=FontWeights.SemiBold;body.Children.Add(heading);
        var value=Text("",12,"#52647D");body.Children.Add(value);
        var slider=new Slider{Minimum=.15,Maximum=1,Value=Math.Clamp(preferences.PortableOpacity,.15,1),Margin=new(0,4,0,6)};
        System.Windows.Automation.AutomationProperties.SetName(slider,"背景透明度");
        body.Children.Add(slider);
        var palette=new WrapPanel();
        foreach(var (label,color) in new[]{("雾白","white"),("浅蓝","blue"),("暖米","warm")}){
            var choice=PortableButton(label,()=>{preferences=preferences with{PortableColor=color};ApplyPortableAppearance();});
            choice.Margin = new(0,0,5,0); palette.Children.Add(choice);
        }
        body.Children.Add(palette);
        var footer=new DockPanel{Margin=new(0,6,0,0)};
        var pin=new CheckBox{Content="置顶",IsChecked=preferences.PortableTopmost,VerticalAlignment=VerticalAlignment.Center};
        pin.Click+=(_,_)=>{preferences=preferences with{PortableTopmost=pin.IsChecked==true};ApplyPortableAppearance();};
        var done=PortableButton("完成",()=>{if(SavePreferences()&&portableAppearancePanel!=null)portableAppearancePanel.Visibility=Visibility.Collapsed;});
        DockPanel.SetDock(done,Dock.Right);footer.Children.Add(done);footer.Children.Add(pin);body.Children.Add(footer);
        void Refresh(){
            value.Text=$"透明度 {1-slider.Value:P0}";
            preferences=preferences with{PortableOpacity=slider.Value};ApplyPortableAppearance();
        }
        value.Text=$"透明度 {1-slider.Value:P0}";
        slider.IsDirectionReversed=true;
        slider.ValueChanged+=(_,_)=>Refresh();
        var panel=Surface(body,new(8));panel.Margin=new(8,2,8,4);panel.Visibility=Visibility.Collapsed;
        return panel;
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
                Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false,
                Topmost = portable.Topmost, FontFamily = shell.FontFamily
            };
            var unlock = PortableTool("解锁交互", () => SetPortableLocked(false), toolbar: false);
            unlock.Margin = new(0);
            unlock.ToolTip = "关闭鼠标穿透，恢复完整交互";
            portableUnlock.Content = unlock;
        }
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
            if (portableAppearancePanel != null) portableAppearancePanel.Visibility = Visibility.Collapsed;
            Keyboard.ClearFocus();
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
            if (portableAppearancePanel != null) portableAppearancePanel.Visibility = Visibility.Collapsed;
            if (window.IsKeyboardFocusWithin) Keyboard.ClearFocus();
        }
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
        return PortablePassThrough && scroll is { IsVisible: true } &&
            ContainsPortablePoint(scroll, new Point(screenX, screenY)) && scroll.TryScrollWithWheel(delta);
    }

    static bool ContainsPortablePoint(FrameworkElement element, Point screenPoint)
        => PresentationSource.FromVisual(element) != null &&
           new Rect(element.RenderSize).Contains(element.PointFromScreen(screenPoint));

    void ApplyPortableAppearance()
    {
        if(portable==null||portableSurface==null)return;
        var color = preferences.PortableColor switch
        {
            "blue" => Color.FromRgb(236, 243, 253),
            "warm" => Color.FromRgb(250, 246, 237),
            _ => Color.FromRgb(248, 250, 252)
        };
        double opacity = Math.Clamp(preferences.PortableOpacity, .15, 1);
        color.A = (byte)(255 * opacity);
        portableSurface.Background = new SolidColorBrush(color);
        color.A = (byte)(255 * (.86 + .12 * opacity));
        portableSectionTint.Color = color;
        if (portableHeader != null) portableHeader.Background = portableSectionTint;
        portable.Topmost = preferences.PortableTopmost;
        foreach(var button in portableToolbarButtons)PaintPortableTool(button);
        if (portableUnlock != null)
        {
            portableUnlock.Topmost = portable.Topmost;
            if (portableUnlock.Content is Button unlock) PaintPortableTool(unlock);
        }
    }
    void AddPortablePreferences(StackPanel body)
    {
        body.Children.Add(Text("随身日程外观",19,margin:new(0,24,0,14)));
        body.Children.Add(Text("背景透明度（文字保持清晰）",13));var opacity=new Slider{Minimum=.15,Maximum=1,Value=Math.Clamp(preferences.PortableOpacity,.15,1),Margin=new(0,12,0,18)};body.Children.Add(opacity);
        var palette=new ComboBox{ItemsSource=new[]{"雾白","浅蓝","暖米"},SelectedIndex=preferences.PortableColor=="blue"?1:preferences.PortableColor=="warm"?2:0};body.Children.Add(palette);
        var topmost=new CheckBox{Content="随身日程置顶",IsChecked=preferences.PortableTopmost,Margin=new(0,14,0,14)};body.Children.Add(topmost);
        var feedback=Text("穿透时可用滚轮浏览。鼠标停稳约 0.8 秒可临时操作，移开后恢复穿透。",13,"#64748B");body.Children.Add(feedback);
        void Change(){preferences=preferences with{PortableOpacity=opacity.Value,PortableColor=palette.SelectedIndex==1?"blue":palette.SelectedIndex==2?"warm":"white",PortableTopmost=topmost.IsChecked==true};ApplyPortableAppearance();}
        opacity.ValueChanged+=(_,_)=>Change();palette.SelectionChanged+=(_,_)=>Change();topmost.Click+=(_,_)=>Change();
        body.Children.Add(Button("保存随身日程外观",()=>{Change();if(SavePreferences())feedback.Text="外观已保存，下次打开继续使用。";}));
        body.Children.Add(Button("恢复随身日程交互",()=>SetPortableLocked(false)));
    }
}
