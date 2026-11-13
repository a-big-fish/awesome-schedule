using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AwesomeInterview.NativeDemo.Data;
namespace AwesomeInterview.NativeDemo;
public partial class MainWindow
{
    ScheduleTimeChoice? PickScheduleTime(string caption,DateTime initial,bool end,string timezone,bool preciseInitially=false)
    {
        ScheduleTimeChoice? result=null;bool precise=preciseInitially;
        var window=new Window{Title=caption,Owner=this,Width=440,SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.WorkArea.Height-48,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Background=Brush("#F6F8FC"),FontFamily=FontFamily};
        var body=new StackPanel{Margin=new(24)};body.Children.Add(Text(caption,21));
        var step=Text("选择日期",13,"#64748B",new(0,8,0,16));body.Children.Add(step);
        var modes=new WrapPanel();var dateOnly=Button("只选日期",()=>{});var specified=Button("指定时间",()=>{});modes.Children.Add(dateOnly);modes.Children.Add(specified);body.Children.Add(modes);
        var hint=Text($"只选日期时使用默认时刻 {WorkTime(end):hh\\:mm}，可在日程偏好中修改。",12,"#64748B",new(0,8,0,12));body.Children.Add(hint);
        var calendar=new Calendar{SelectedDate=initial.Date,DisplayDate=initial.Date,SelectionMode=CalendarSelectionMode.SingleDate,HorizontalAlignment=HorizontalAlignment.Center};body.Children.Add(calendar);
        var times=new StackPanel{Visibility=Visibility.Collapsed};times.Children.Add(Text("小时与分钟",16,margin:new(0,8,0,14)));var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new(20)});row.ColumnDefinitions.Add(new());
        var hours=new ComboBox{ItemsSource=Enumerable.Range(0,24).Select(x=>x.ToString("00")),SelectedIndex=initial.Hour};var minutes=new ComboBox{ItemsSource=Enumerable.Range(0,60).Select(x=>x.ToString("00")),SelectedIndex=initial.Minute};row.Children.Add(hours);Grid.SetColumn(minutes,2);row.Children.Add(minutes);times.Children.Add(row);body.Children.Add(times);
        System.Windows.Automation.AutomationProperties.SetName(hours,"小时");System.Windows.Automation.AutomationProperties.SetName(minutes,"分钟");System.Windows.Automation.AutomationProperties.SetName(calendar,"日程日期");
        var feedback=Text("",13,"#B42332",new(0,10,0,4));body.Children.Add(feedback);
        var actions=new WrapPanel{Margin=new(0,12,0,0)};Button? confirm=null;
        void ModeAppearance(){dateOnly.Background=Brush(precise?"#FFFFFF":"#E9F0FF");specified.Background=Brush(precise?"#E9F0FF":"#FFFFFF");}
        void DateStep(){ModeAppearance();calendar.Visibility=Visibility.Visible;times.Visibility=Visibility.Collapsed;step.Text="选择日期";confirm!.Content=precise?"下一步：选择时间":"确认日期";}
        dateOnly.Click+=(_,_)=>{precise=false;DateStep();};specified.Click+=(_,_)=>{precise=true;DateStep();};
        confirm=Button("确认日期",()=>{if(calendar.SelectedDate==null){feedback.Text="请选择日期。";return;}if(precise&&times.Visibility!=Visibility.Visible){calendar.Visibility=Visibility.Collapsed;times.Visibility=Visibility.Visible;step.Text=$"{calendar.SelectedDate:yyyy-MM-dd} · 选择时间";confirm!.Content="确认日期和时间";hours.Focus();return;}if(hours.SelectedIndex<0||minutes.SelectedIndex<0){feedback.Text="请选择小时和分钟。";return;}result=new(ScheduleTime.Select(calendar.SelectedDate.Value,!precise,new TimeSpan(hours.SelectedIndex,minutes.SelectedIndex,0),WorkTime(end)),!precise);window.DialogResult=true;});confirm.IsDefault=true;confirm.Background=Brush("#285DDD");confirm.Foreground=Brush("#FFFFFF");actions.Children.Add(confirm);
        var now=Button("当前时间",()=>{var current=TimeZoneInfo.ConvertTime(DateTimeOffset.Now,TimeZoneInfo.FindSystemTimeZoneById(timezone)).DateTime;calendar.SelectedDate=current.Date;calendar.DisplayDate=current.Date;hours.SelectedIndex=current.Hour;minutes.SelectedIndex=current.Minute;precise=true;ModeAppearance();calendar.Visibility=Visibility.Collapsed;times.Visibility=Visibility.Visible;step.Text=$"{current:yyyy-MM-dd} · 当前时间";confirm.Content="确认日期和时间";});actions.Children.Add(now);
        actions.Children.Add(Button("返回日期",DateStep));var cancel=Button("取消",()=>window.DialogResult=false);cancel.IsCancel=true;actions.Children.Add(cancel);body.Children.Add(actions);window.Content=body;
        DateStep();window.ShowDialog();return result;
    }
}
