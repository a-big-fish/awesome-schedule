using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AwesomeInterview.NativeDemo;

internal static class WorkspaceControls
{
    internal static IReadOnlyList<string> Weekdays { get; } = Array.AsReadOnly(
        new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" });

    internal static SolidColorBrush Brush(string value)=>(SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
    internal static Button Button(string title, Action action)
    {
        var b=new Button{Content=title,HorizontalContentAlignment=HorizontalAlignment.Center};
        if(title.StartsWith("保存")||title.StartsWith("登录")||title.StartsWith("＋")){b.Background=Brush("#285DDD");b.Foreground=Brushes.White;b.BorderBrush=Brush("#285DDD");}
        b.Click+=(_,_)=>action(); return b;
    }
    internal static TextBlock Text(string text,double size=14,string color="#192C46",Thickness? margin=null)=>new(){Text=text,FontSize=size,Foreground=Brush(color),TextWrapping=TextWrapping.Wrap,Margin=margin??new Thickness(0)};
    internal static Border Surface(UIElement child,Thickness? padding=null)=>new(){Background=Brushes.White,BorderBrush=Brush("#E2E8F2"),BorderThickness=new(1),CornerRadius=new(10),Padding=padding??new Thickness(22),Margin=new(0,0,0,16),Child=child};
}
