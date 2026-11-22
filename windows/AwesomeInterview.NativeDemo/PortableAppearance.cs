using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms=System.Windows.Forms;

namespace AwesomeInterview.NativeDemo;

public partial class MainWindow
{
    bool portableLocked;
    Border? portableSurface;
    Forms.ToolStripMenuItem? unlockPortable;
    Border? portableNowLine;
    TextBlock? portableClock;
    Window? portableUnlock;
    Border? portableAppearancePanel;
    readonly System.Collections.Generic.List<Button> portableToolbarButtons=new();

    Button PortableTool(string label,Action action)
    {
        var button=Button(label,action);
        button.Padding=new(12,6,12,6);button.MinHeight=34;button.Margin=new(5,0,0,0);
        portableToolbarButtons.Add(button);
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

    // Keep a readable surface directly under text, even at the lowest window opacity.
    static SolidColorBrush PortableReadabilityBrush()=>Brush("#E6F6F9FF");
    TextBlock PortableLabel(string label,double size=13,Thickness? margin=null)
    {
        var block=Text(label,size,"#192C46",margin);
        block.Background=PortableReadabilityBrush();
        block.HorizontalAlignment=HorizontalAlignment.Left;
        block.Padding=new(6,2,6,2);
        return block;
    }

    Border BuildPortableAppearancePanel()
    {
        var body=new StackPanel();
        var heading=PortableLabel("随身日程外观",14,new(0,0,0,6));
        heading.FontWeight=FontWeights.SemiBold;body.Children.Add(heading);
        var value=Text("",12,"#52647D");body.Children.Add(value);
        var slider=new Slider{Minimum=.15,Maximum=1,Value=Math.Clamp(preferences.PortableOpacity,.15,1),Margin=new(0,8,0,8)};
        System.Windows.Automation.AutomationProperties.SetName(slider,"背景透明度");
        body.Children.Add(slider);
        var palette=new WrapPanel();
        foreach(var (label,color) in new[]{("雾白","white"),("浅蓝","blue"),("暖米","warm")}){
            var choice=Button(label,()=>{preferences=preferences with{PortableColor=color};ApplyPortableAppearance();});
            choice.MinHeight=30;choice.Padding=new(9,3,9,3);palette.Children.Add(choice);
        }
        body.Children.Add(palette);
        var footer=new DockPanel{Margin=new(0,6,0,0)};
        var pin=new CheckBox{Content="置顶",IsChecked=preferences.PortableTopmost,VerticalAlignment=VerticalAlignment.Center};
        pin.Click+=(_,_)=>{preferences=preferences with{PortableTopmost=pin.IsChecked==true};ApplyPortableAppearance();};
        var done=Button("完成",()=>{if(SavePreferences()&&portableAppearancePanel!=null)portableAppearancePanel.Visibility=Visibility.Collapsed;});
        done.MinHeight=30;done.Padding=new(10,3,10,3);done.Margin=new(0);
        DockPanel.SetDock(done,Dock.Right);footer.Children.Add(done);footer.Children.Add(pin);body.Children.Add(footer);
        void Refresh(){
            value.Text=$"透明度 {1-slider.Value:P0}";
            preferences=preferences with{PortableOpacity=slider.Value};ApplyPortableAppearance();
        }
        value.Text=$"透明度 {1-slider.Value:P0}";
        slider.IsDirectionReversed=true;
        slider.ValueChanged+=(_,_)=>Refresh();
        var panel=Surface(body,new(14));panel.Margin=new(18,10,18,0);panel.Visibility=Visibility.Collapsed;
        return panel;
    }
    void PositionPortableUnlock(){if(portableUnlock!=null&&portable!=null){portableUnlock.Left=portable.Left+portable.ActualWidth-portableUnlock.Width-20;portableUnlock.Top=portable.Top+12;}}
    void ShowPortableUnlock()
    {
        if(portable==null||portableUnlock!=null)return;
        // An independent owned window remains clickable while the schedule surface passes input through.
        portableUnlock=new Window{Owner=portable,Title="恢复随身日程交互",Width=116,Height=44,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Topmost=portable.Topmost,FontFamily=FontFamily};
        var unlock=PortableTool("解锁交互",()=>SetPortableLocked(false));unlock.Margin=new(0);unlock.ToolTip="恢复随身日程的点击、滚动和拖动";portableUnlock.Content=unlock;PositionPortableUnlock();portableUnlock.Show();
    }
    const int ExtendedStyle=-20;
    const long TransparentStyle=0x20,NoActivateStyle=0x08000000;
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)]static extern nint GetWindowLongPtr(nint handle,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)]static extern nint SetWindowLongPtr(nint handle,int index,nint value);
    void SetPortableLocked(bool locked)
    {
        if(portable==null)return;
        var hwnd=new WindowInteropHelper(portable).Handle;if(hwnd==0)return;
        long value=GetWindowLongPtr(hwnd,ExtendedStyle).ToInt64();
        value=locked?value|TransparentStyle|NoActivateStyle:value&~(TransparentStyle|NoActivateStyle);
        Marshal.SetLastPInvokeError(0);var result=SetWindowLongPtr(hwnd,ExtendedStyle,new nint(value));
        if(result==0&&Marshal.GetLastWin32Error()!=0){status.Text="无法切换鼠标穿透，请重开随身日程。";return;}
        portableLocked=locked;
        if(locked){if(portableAppearancePanel!=null)portableAppearancePanel.Visibility=Visibility.Collapsed;ShowPortableUnlock();}else{if(portableUnlock?.Content is Button button)portableToolbarButtons.Remove(button);portableUnlock?.Close();portableUnlock=null;}
        if(unlockPortable!=null)unlockPortable.Enabled=locked;
        if(!locked){portable.Activate();portable.Focus();}
    }
    void ApplyPortableAppearance()
    {
        if(portable==null||portableSurface==null)return;
        var color=preferences.PortableColor switch{"blue"=>Color.FromRgb(228,237,252),"warm"=>Color.FromRgb(250,243,228),_=>Colors.White};
        color.A=(byte)(255*Math.Clamp(preferences.PortableOpacity,.15,1));
        portableSurface.Background=new SolidColorBrush(color);portable.Topmost=preferences.PortableTopmost;
        foreach(var button in portableToolbarButtons)PaintPortableTool(button);
        if(portableUnlock!=null)portableUnlock.Topmost=portable.Topmost;
    }
    void AddPortablePreferences(StackPanel body)
    {
        body.Children.Add(Text("随身日程外观",19,margin:new(0,24,0,14)));
        body.Children.Add(Text("背景透明度（文字保持清晰）",13));var opacity=new Slider{Minimum=.15,Maximum=1,Value=Math.Clamp(preferences.PortableOpacity,.15,1),Margin=new(0,12,0,18)};body.Children.Add(opacity);
        var palette=new ComboBox{ItemsSource=new[]{"雾白","浅蓝","暖米"},SelectedIndex=preferences.PortableColor=="blue"?1:preferences.PortableColor=="warm"?2:0};body.Children.Add(palette);
        var topmost=new CheckBox{Content="随身日程置顶",IsChecked=preferences.PortableTopmost,Margin=new(0,14,0,14)};body.Children.Add(topmost);
        var feedback=Text("穿透时，组件右上角保留“解锁交互”按钮；托盘右键也可恢复。",13,"#64748B");body.Children.Add(feedback);
        void Change(){preferences=preferences with{PortableOpacity=opacity.Value,PortableColor=palette.SelectedIndex==1?"blue":palette.SelectedIndex==2?"warm":"white",PortableTopmost=topmost.IsChecked==true};ApplyPortableAppearance();}
        opacity.ValueChanged+=(_,_)=>Change();palette.SelectionChanged+=(_,_)=>Change();topmost.Click+=(_,_)=>Change();
        body.Children.Add(Button("保存随身日程外观",()=>{Change();if(SavePreferences())feedback.Text="外观已保存，下次打开继续使用。";}));
        body.Children.Add(Button("恢复随身日程交互",()=>SetPortableLocked(false)));
    }
}
