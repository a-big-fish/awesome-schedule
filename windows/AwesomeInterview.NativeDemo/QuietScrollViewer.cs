using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AwesomeInterview.NativeDemo;

// Overlay rails do not reserve layout space. Only actual scrolling reveals them.
public sealed class QuietScrollViewer : ScrollViewer
{
    readonly DispatcherTimer idle=new(){Interval=TimeSpan.FromMilliseconds(800)};
    ScrollBar? vertical,horizontal;
    long lastInput=-10000;
    double? queuedWheelOffset;
    const string Markup="""
    <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollViewer">
      <Grid Background="{TemplateBinding Background}">
        <Grid.Resources>
          <Style TargetType="ScrollBar">
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
              <Track x:Name="PART_Track" Orientation="{TemplateBinding Orientation}" IsDirectionReversed="True">
                <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#8052647D" CornerRadius="3"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
              </Track>
              <ControlTemplate.Triggers><Trigger Property="Orientation" Value="Horizontal"><Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="False"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
        </Grid.Resources>
        <ScrollContentPresenter x:Name="PART_ScrollContentPresenter" Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}" CanContentScroll="{TemplateBinding CanContentScroll}"/>
        <ScrollBar x:Name="PART_VerticalScrollBar" Orientation="Vertical" HorizontalAlignment="Right" Width="5" Margin="0,4,2,8" Minimum="0" Maximum="{TemplateBinding ScrollableHeight}" ViewportSize="{TemplateBinding ViewportHeight}" Value="{Binding VerticalOffset, RelativeSource={RelativeSource TemplatedParent}, Mode=OneWay}" Visibility="{TemplateBinding ComputedVerticalScrollBarVisibility}" Opacity="0" IsHitTestVisible="False"/>
        <ScrollBar x:Name="PART_HorizontalScrollBar" Orientation="Horizontal" VerticalAlignment="Bottom" Height="5" Margin="4,0,8,2" Minimum="0" Maximum="{TemplateBinding ScrollableWidth}" ViewportSize="{TemplateBinding ViewportWidth}" Value="{Binding HorizontalOffset, RelativeSource={RelativeSource TemplatedParent}, Mode=OneWay}" Visibility="{TemplateBinding ComputedHorizontalScrollBarVisibility}" Opacity="0" IsHitTestVisible="False"/>
      </Grid>
    </ControlTemplate>
    """;
    public QuietScrollViewer()
    {
        Template=(ControlTemplate)XamlReader.Parse(Markup);
        PreviewMouseWheel+=(_,e)=>{
            lastInput=Environment.TickCount64;
            if((Keyboard.Modifiers&ModifierKeys.Shift)==0||ScrollableWidth<=0)return;
            ScrollToHorizontalOffset(Math.Clamp(HorizontalOffset-e.Delta*.75,0,ScrollableWidth));
            e.Handled=true;
        };
        PreviewMouseDown+=(_,_)=>lastInput=Environment.TickCount64;PreviewKeyDown+=(_,_)=>lastInput=Environment.TickCount64;
        ScrollChanged += (_, e) =>
        {
            if (e.OriginalSource != this) return;
            if (e.VerticalChange != 0 || e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
                queuedWheelOffset = null;
            if (e.VerticalChange == 0 && e.HorizontalChange == 0) return;
            if (Environment.TickCount64 - lastInput > 250 &&
                vertical?.IsMouseCaptureWithin != true && horizontal?.IsMouseCaptureWithin != true)
                return;
            Reveal(vertical);
            Reveal(horizontal);
            idle.Stop();
            idle.Start();
        };
        idle.Tick+=(_,_)=>{if(vertical?.IsMouseCaptureWithin==true||horizontal?.IsMouseCaptureWithin==true)return;idle.Stop();Fade(vertical);Fade(horizontal);};
        Unloaded += (_, _) => { idle.Stop(); queuedWheelOffset = null; };
    }

    internal bool TryScrollWithWheel(int delta)
    {
        double offset = queuedWheelOffset ?? VerticalOffset;
        double target = Math.Clamp(offset - delta * .4, 0, ScrollableHeight);
        if (Math.Abs(target - offset) < .01) return false;
        queuedWheelOffset = target;
        lastInput = Environment.TickCount64;
        ScrollToVerticalOffset(target);
        return true;
    }

    public override void OnApplyTemplate(){base.OnApplyTemplate();vertical=GetTemplateChild("PART_VerticalScrollBar") as ScrollBar;horizontal=GetTemplateChild("PART_HorizontalScrollBar") as ScrollBar;}
    static void Reveal(ScrollBar? rail){if(rail==null)return;rail.BeginAnimation(OpacityProperty,null);rail.Opacity=.5;rail.IsHitTestVisible=true;}
    static void Fade(ScrollBar? rail){if(rail==null)return;rail.IsHitTestVisible=false;rail.BeginAnimation(OpacityProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(180)));}
}
