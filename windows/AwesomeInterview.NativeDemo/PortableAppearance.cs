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
        if(unlockPortable!=null)unlockPortable.Enabled=locked;
        if(!locked){portable.Activate();portable.Focus();}
    }
    void ApplyPortableAppearance()
    {
        if(portable==null||portableSurface==null)return;
        var color=preferences.PortableColor switch{"blue"=>Color.FromRgb(228,237,252),"warm"=>Color.FromRgb(250,243,228),_=>Colors.White};
        color.A=(byte)(255*Math.Clamp(preferences.PortableOpacity,.15,1));
        portableSurface.Background=new SolidColorBrush(color);portable.Topmost=preferences.PortableTopmost;
    }
    void AddPortablePreferences(StackPanel body)
    {
        body.Children.Add(Text("随身日程外观",19,margin:new(0,24,0,14)));
        body.Children.Add(Text("背景透明度（文字保持清晰）",13));var opacity=new Slider{Minimum=.15,Maximum=1,Value=Math.Clamp(preferences.PortableOpacity,.15,1),Margin=new(0,12,0,18)};body.Children.Add(opacity);
        var palette=new ComboBox{ItemsSource=new[]{"雾白","浅蓝","暖米"},SelectedIndex=preferences.PortableColor=="blue"?1:preferences.PortableColor=="warm"?2:0};body.Children.Add(palette);
        var topmost=new CheckBox{Content="随身日程置顶",IsChecked=preferences.PortableTopmost,Margin=new(0,14,0,14)};body.Children.Add(topmost);
        var feedback=Text("锁定后鼠标可穿透；在托盘右键菜单中选择“恢复随身日程交互”解锁。",13,"#64748B");body.Children.Add(feedback);
        void Change(){preferences=preferences with{PortableOpacity=opacity.Value,PortableColor=palette.SelectedIndex==1?"blue":palette.SelectedIndex==2?"warm":"white",PortableTopmost=topmost.IsChecked==true};ApplyPortableAppearance();}
        opacity.ValueChanged+=(_,_)=>Change();palette.SelectionChanged+=(_,_)=>Change();topmost.Click+=(_,_)=>Change();
        body.Children.Add(Button("保存随身日程外观",()=>{Change();if(SavePreferences())feedback.Text="外观已保存，下次打开继续使用。";}));
        body.Children.Add(Button("恢复随身日程交互",()=>SetPortableLocked(false)));
    }
}
