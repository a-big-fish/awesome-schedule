using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AwesomeInterview.NativeDemo.Data;

namespace AwesomeInterview.NativeDemo;

public partial class MainWindow
{
    ScheduleTimeChoice? PickScheduleTime(string caption,DateTime initial,bool end,string timezone,bool preciseInitially=false)
    {
        ScheduleTimeChoice? result=null;
        bool precise=preciseInitially;
        DateTime selectedDate=initial.Date,month=new(initial.Year,initial.Month,1);
        var window=new Window{Title=caption,Owner=this,Width=460,SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.WorkArea.Height-40,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Background=Brush("#F6F8FC"),FontFamily=FontFamily};
        var body=new StackPanel{Margin=new(20,18,20,20)};
        var heading=Text(caption,21);heading.FontWeight=FontWeights.SemiBold;body.Children.Add(heading);
        var step=Text("选择日期",13,"#52647D",new(0,6,0,12));body.Children.Add(step);

        var modes=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,0,8)};
        var dateOnly=Button("只选日期",()=>{});var specified=Button("指定时间",()=>{});
        modes.Children.Add(dateOnly);modes.Children.Add(specified);body.Children.Add(modes);
        var hint=Text($"只选日期时使用默认时刻 {WorkTime(end):hh\\:mm}",12,"#52647D",new(0,0,0,10));body.Children.Add(hint);
        var dates=new StackPanel();
        var monthBar=new DockPanel{Margin=new(0,0,0,8)};
        var monthLabel=Text("",15);monthLabel.FontWeight=FontWeights.SemiBold;
        monthLabel.HorizontalAlignment=HorizontalAlignment.Center;monthLabel.VerticalAlignment=VerticalAlignment.Center;
        var previous=Button("‹",()=>{});var next=Button("›",()=>{});
        foreach(var arrow in new[]{previous,next}){arrow.Width=36;arrow.MinHeight=32;arrow.Padding=new(4);arrow.Margin=new(0);}
        AutomationProperties.SetName(previous,"上个月");AutomationProperties.SetName(next,"下个月");
        DockPanel.SetDock(previous,Dock.Left);DockPanel.SetDock(next,Dock.Right);
        monthBar.Children.Add(previous);monthBar.Children.Add(next);monthBar.Children.Add(monthLabel);
        dates.Children.Add(monthBar);
        var weekdays=new UniformGrid{Columns=7,Margin=new(0,0,0,5)};
        foreach(var name in Weekdays){var day=Text(name,11,"#52647D");day.HorizontalAlignment=HorizontalAlignment.Center;weekdays.Children.Add(day);}
        dates.Children.Add(weekdays);
        var cells=new UniformGrid{Columns=7};
        dates.Children.Add(cells);
        var calendarCard=Surface(dates,new(10));calendarCard.Margin=new(0);

        var times=new StackPanel();
        var timeRow=new Grid();timeRow.ColumnDefinitions.Add(new());timeRow.ColumnDefinitions.Add(new(){Width=new(12)});timeRow.ColumnDefinitions.Add(new());
        var hours=new ComboBox{ItemsSource=Enumerable.Range(0,24).Select(x=>x.ToString("00")),SelectedIndex=initial.Hour};
        var minutes=new ComboBox{ItemsSource=Enumerable.Range(0,60).Select(x=>x.ToString("00")),SelectedIndex=initial.Minute};
        void AddTimeField(string label,ComboBox input,int col){
            var field=new StackPanel();field.Children.Add(Text(label,13,margin:new(0,0,0,7)));field.Children.Add(input);
            AutomationProperties.SetName(input,label);Grid.SetColumn(field,col);timeRow.Children.Add(field);
        }
        AddTimeField("小时",hours,0);AddTimeField("分钟",minutes,2);times.Children.Add(timeRow);
        var timeCard=Surface(times,new(16));timeCard.Margin=new(0);timeCard.Visibility=Visibility.Collapsed;
        var stage=new Grid();stage.Children.Add(calendarCard);stage.Children.Add(timeCard);
        calendarCard.VerticalAlignment=VerticalAlignment.Top;timeCard.VerticalAlignment=VerticalAlignment.Top;
        body.Children.Add(stage);

        var actions=new DockPanel{Margin=new(0,12,0,0)};
        var back=Button("返回日期",()=>{});
        var cancel=Button("取消",()=>window.DialogResult=false);cancel.IsCancel=true;
        var nowButton=Button("当前时间",()=>{});
        var confirm=Button("确认日期",()=>{});

        confirm.IsDefault=true;confirm.Background=Brush("#285DDD");confirm.Foreground=Brush("#FFFFFF");
        foreach(var button in new[]{nowButton,back}){button.Padding=new(9,7,9,7);button.Margin=new(0,0,6,0);button.MinHeight=36;DockPanel.SetDock(button,Dock.Left);actions.Children.Add(button);}
        cancel.Padding=new(9,7,9,7);cancel.Margin=new(6,0,0,0);cancel.MinHeight=36;DockPanel.SetDock(cancel,Dock.Right);actions.Children.Add(cancel);
        confirm.Margin=new(0);confirm.Padding=new(9,7,9,7);confirm.MinHeight=36;actions.Children.Add(confirm);
        body.Children.Add(actions);
        window.Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};

        void MoveMonth(int direction){month=month.AddMonths(direction);RefreshDates();}
        void RefreshDates(){
            monthLabel.Text=month.ToString("yyyy 年 M 月");
            previous.IsEnabled=month.Year>1||month.Month>1;next.IsEnabled=month.Year<9999||month.Month<12;
            cells.Children.Clear();
            int leading=((int)month.DayOfWeek+6)%7;
            for(int i=0;i<42;i++){
                long ticks=month.Ticks+(i-leading)*TimeSpan.TicksPerDay;
                if(ticks<0||ticks>DateTime.MaxValue.Ticks){cells.Children.Add(new Border());continue;}
                var day=new DateTime(ticks);
                var cell=Button(day.Day.ToString(),()=>SelectDay(day));
                cell.MinHeight=0;cell.Height=35;cell.Margin=new(2);cell.Padding=new(0);
                bool chosen=day==selectedDate;
                cell.BorderThickness=day==CalendarProjection.Now.Date?new(1):new(0);
                cell.BorderBrush=Brush("#285DDD");
                cell.Background=Brush(chosen?"#285DDD":"#FFFFFF");
                cell.Foreground=Brush(chosen?"#FFFFFF":day.Month==month.Month?"#192C46":"#69788C");
                AutomationProperties.SetName(cell,day.ToString("yyyy年M月d日 dddd")+(chosen?"，已选中":""));
                cell.Tag=day;
                cell.PreviewKeyDown+=(_,e)=>{
                    int delta=e.Key switch{System.Windows.Input.Key.Left=>-1,System.Windows.Input.Key.Right=>1,System.Windows.Input.Key.Up=>-7,System.Windows.Input.Key.Down=>7,_=>0};
                    if(delta==0)return;
                    long target=day.Ticks+delta*TimeSpan.TicksPerDay;
                    if(target>=0&&target<=DateTime.MaxValue.Ticks)SelectDay(new DateTime(target));
                    e.Handled=true;
                };
                cells.Children.Add(cell);
            }
        }
        void SelectDay(DateTime day){
            selectedDate=day;month=new(day.Year,day.Month,1);RefreshDates();
            cells.Children.OfType<Button>().FirstOrDefault(b=>Equals(b.Tag,day))?.Focus();
        }
        void ModeAppearance(){
            dateOnly.Background=Brush(precise?"#FFFFFF":"#E9F0FF");
            specified.Background=Brush(precise?"#E9F0FF":"#FFFFFF");
        }
        void DateStep(){
            ModeAppearance();calendarCard.Visibility=Visibility.Visible;timeCard.Visibility=Visibility.Collapsed;
            back.Visibility=Visibility.Collapsed;modes.Visibility=Visibility.Visible;hint.Visibility=precise?Visibility.Collapsed:Visibility.Visible;
            step.Text=selectedDate.ToString("yyyy-MM-dd · dddd");confirm.Content=precise?"下一步":"确认日期";
        }
        void TimeStep(){
            ModeAppearance();calendarCard.Visibility=Visibility.Collapsed;timeCard.Visibility=Visibility.Visible;
            back.Visibility=Visibility.Visible;modes.Visibility=Visibility.Collapsed;hint.Visibility=Visibility.Collapsed;
            step.Text=$"{selectedDate:yyyy-MM-dd} · 选择时间";confirm.Content="确认时间";hours.Focus();
        }
        previous.Click+=(_,_)=>MoveMonth(-1);
        next.Click+=(_,_)=>MoveMonth(1);
        back.Click+=(_,_)=>DateStep();
        nowButton.Click+=(_,_)=>{
            var now=TimeZoneInfo.ConvertTime(DateTimeOffset.Now,TimeZoneInfo.FindSystemTimeZoneById(timezone)).DateTime;
            selectedDate=now.Date;month=new(now.Year,now.Month,1);
            hours.SelectedIndex=now.Hour;minutes.SelectedIndex=now.Minute;precise=true;
            RefreshDates();TimeStep();
        };
        confirm.Click+=(_,_)=>{
            if(precise&&timeCard.Visibility!=Visibility.Visible){TimeStep();return;}
            result=new(ScheduleTime.Select(selectedDate,!precise,new TimeSpan(hours.SelectedIndex,minutes.SelectedIndex,0),WorkTime(end)),!precise);
            window.DialogResult=true;
        };
        dateOnly.Click+=(_,_)=>{precise=false;DateStep();};
        specified.Click+=(_,_)=>{precise=true;DateStep();};
        RefreshDates();
        DateStep();
        window.ShowDialog();
        return result;
    }
}
