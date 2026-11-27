using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AwesomeInterview.NativeDemo.Data;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal static class DocumentPageViewer
{
    internal static UIElement Build(DocumentPages document, double viewportHeight)
    {
        var root=new StackPanel();if(document.Pages.Count==0){root.Children.Add(Text("文件没有可预览页面。"));return root;}
        int index=0;double scale=1;var image=new Image{Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center};
        var label=Text("",14,margin:new(10,10,10,10));var controls=new WrapPanel();root.Children.Add(controls);
        void Display(){using var input=new MemoryStream(document.Pages[index]);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=input;bitmap.EndInit();bitmap.Freeze();image.Source=bitmap;label.Text=$"{index+1} / {document.Pages.Count} 页 · {scale:P0}";}
        controls.Children.Add(Button("上一页",()=>{if(index>0){index--;Display();}}));controls.Children.Add(label);controls.Children.Add(Button("下一页",()=>{if(index+1<document.Pages.Count){index++;Display();}}));controls.Children.Add(Button("缩小",()=>{scale=Math.Max(.5,scale-.25);Display();}));controls.Children.Add(Button("放大",()=>{scale=Math.Min(2,scale+.25);Display();}));
        var scroll=new QuietScrollViewer{Content=image,Height=Math.Max(340,viewportHeight-210),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Brush("#EEF2F7")};
        void Fit(){if(scroll.ActualWidth>24)image.Width=(scroll.ActualWidth-24)*scale;label.Text=$"{index+1} / {document.Pages.Count} 页 · {scale:P0}";}
        scroll.SizeChanged+=(_,_)=>Fit();image.Margin=new(12);
        controls.Children.Add(Button("适应宽度",()=>{scale=1;Fit();}));
        controls.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent,new RoutedEventHandler((_,_)=>Fit()));
        root.Children.Add(scroll);Display();return Surface(root,new(12));
    }
}
