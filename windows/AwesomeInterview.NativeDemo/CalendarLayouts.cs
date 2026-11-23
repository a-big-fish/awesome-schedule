using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AwesomeInterview.NativeDemo.Data;

namespace AwesomeInterview.NativeDemo;

public partial class MainWindow
{
    static readonly string[] Weekdays = {"周一","周二","周三","周四","周五","周六","周日"};
    static string CalendarColor(EventData item) => item.CancelledAt!=null?"#F9E9E9":item.CompletedAt!=null?"#EFF2F5":"#EBF2FF";

    Button CalendarEntry(EventData item,string title,double size)
    {
        var button=Button("",()=>ShowEventDetail(item));
        button.Content=new TextBlock{Text=title,FontSize=size,FontWeight=FontWeights.Medium,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
        button.Background=Brush(CalendarColor(item));
        button.BorderThickness=new(0);
        button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        button.VerticalContentAlignment=VerticalAlignment.Stretch;
        button.Margin=new(0);
        button.ToolTip=$"{item.Title}\n{item.Local(item.StartAt):MM-dd HH:mm} — {item.Local(item.EndAt):MM-dd HH:mm}";
        System.Windows.Automation.AutomationProperties.SetName(button,item.Title);
        return button;
    }

    void OpenCalendarDay(DateTime day){selected=day;calendarView="day";ShowCalendar();}
    IEnumerable<EventData> VisibleCalendarItems(DateTime day)=>EventsFor(day).Where(item=>!hideCompleted||item.CompletedAt==null);
    QuietScrollViewer AddCalendarScroller(Grid grid,bool horizontal)
    {
        var scroll=new QuietScrollViewer{Content=grid,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=horizontal?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled};
        void Resize(){scroll.MaxHeight=Math.Max(270,viewport.ActualHeight-184);}
        Resize();
        scroll.SizeChanged+=(_,_)=>grid.Width=Math.Max(grid.MinWidth,scroll.ActualWidth);
        SizeChangedEventHandler resize=(_,_)=>Resize();
        viewport.SizeChanged+=resize;
        scroll.Unloaded+=(_,_)=>viewport.SizeChanged-=resize;
        content.Children.Add(Surface(scroll,new(8)));
        return scroll;
    }

    void RenderWeek(DateTime first)
    {
        var grid=new Grid{MinWidth=7*138};
        for(int i=0;i<7;i++)grid.ColumnDefinitions.Add(new());
        for(int i=0;i<7;i++){
            var day=first.AddDays(i);
            var items=VisibleCalendarItems(day).OrderBy(item=>item.StartAt).ToList();
            var body=new StackPanel{MinHeight=250,Margin=new(8,6,8,12)};
            var date=Button($"{Weekdays[i]}  {day:M/d}",()=>OpenCalendarDay(day));
            date.Margin=new(0,0,0,12);date.Padding=new(8,6,8,6);date.MinHeight=36;
            if(day==CalendarProjection.Now.Date){date.Background=Brush("#285DDD");date.Foreground=Brush("#FFFFFF");}
            body.Children.Add(date);
            foreach(var item in items){
                bool continues=CalendarProjection.DisplayTime(item.StartAt)<day||CalendarProjection.DisplayTime(item.EndAt)>day.AddDays(1);
                var card=CalendarEntry(item,item.Title,13);
                var label=new StackPanel();
                label.Children.Add(Text(continues?"跨天 · "+CalendarProjection.DisplayTime(item.EndAt).ToString("M/d HH:mm")+" 结束":$"{CalendarProjection.DisplayTime(item.StartAt):HH:mm} — {CalendarProjection.DisplayTime(item.EndAt):HH:mm}",11,"#52647D"));
                label.Children.Add(new TextBlock{Text=item.Title,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=38,FontSize=13,FontWeight=FontWeights.SemiBold,Margin=new(0,5,0,0)});
                card.Content=label;card.Padding=new(9,8,9,8);card.Margin=new(0,0,0,8);
                body.Children.Add(card);
            }
            if(items.Count==0)body.Children.Add(Text("暂无安排",12,"#64748B",new(6,4,0,0)));
            var column=new Border{Child=body,BorderBrush=Brush("#E4EAF2"),BorderThickness=new(0,0,i==6?0:1,0)};
            Grid.SetColumn(column,i);grid.Children.Add(column);
        }
        AddCalendarScroller(grid,true);
    }

    void RenderMonth(bool centerToday)
    {
        var grid=new Grid{MinWidth=7*108};
        var today = CalendarProjection.Now.Date;
        Button? todayButton = null;
        for(int c=0;c<7;c++){
            grid.ColumnDefinitions.Add(new());
            var title=Text(Weekdays[c],12,"#52647D",new(0,6,0,10));title.HorizontalAlignment=HorizontalAlignment.Center;
            Grid.SetColumn(title,c);grid.Children.Add(title);
        }
        grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var monthStart=new DateTime(selected.Year,selected.Month,1);
        var first=monthStart.AddDays(-(((int)monthStart.DayOfWeek+6)%7));
        for(int week=0;week<6;week++){
            var start=first.AddDays(week*7);
            if(start>=monthStart.AddMonths(1))break;
            var days=Enumerable.Range(0,7).Select(i=>VisibleCalendarItems(start.AddDays(i)).ToList()).ToList();
            var rows=MonthLanes.Arrange(days);
            var band=new Grid{MinHeight=78,Margin=new(0,0,0,6)};
            for(int col=0;col<7;col++)band.ColumnDefinitions.Add(new());
            band.RowDefinitions.Add(new(){Height=new(32)});
            for(int row=0;row<rows.Count;row++)band.RowDefinitions.Add(new(){Height=new(26)});
            band.RowDefinitions.Add(new());
            for(int col=0;col<7;col++){
                var day=start.AddDays(col);
                var divider=new Border{BorderBrush=Brush("#E4EAF2"),BorderThickness=new(0,1,col==6?0:1,0),IsHitTestVisible=false};
                Grid.SetColumn(divider,col);Grid.SetRowSpan(divider,band.RowDefinitions.Count);band.Children.Add(divider);
                var date=Button(day.Day.ToString(),()=>OpenCalendarDay(day));
                date.Margin=new(4,3,0,3);date.MinHeight=0;date.MinWidth=28;date.Padding=new(5,0,5,0);date.HorizontalAlignment=HorizontalAlignment.Left;date.BorderThickness=new(0);
                bool isToday = day == today;
                if (isToday) todayButton = date;
                date.Background=Brush(isToday?"#285DDD":"#FFFFFF");date.Foreground=Brush(isToday?"#FFFFFF":day.Month==selected.Month?"#192C46":"#64748B");
                Grid.SetColumn(date,col);band.Children.Add(date);
            }
            for(int row=0;row<rows.Count;row++){
                var lane=rows[row];
                for(int col=0;col<lane.Length;col++){
                    if(lane[col] is not EventData item)continue;
                    int span=1;
                    while(col+span<lane.Length&&lane[col+span]?.Id==item.Id)span++;
                    var card=CalendarEntry(item,item.Title,12);
                    card.MinHeight=0;card.Margin=new(4,1,4,1);card.Padding=new(7,0,7,0);
                    Grid.SetRow(card,row+1);Grid.SetColumn(card,col);Grid.SetColumnSpan(card,span);band.Children.Add(card);
                    col+=span-1;
                }
            }
            grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
            Grid.SetRow(band,grid.RowDefinitions.Count-1);Grid.SetColumnSpan(band,7);grid.Children.Add(band);
        }
        var scroll = AddCalendarScroller(grid, true);
        if (!centerToday || todayButton == null) return;

        scroll.Loaded += CenterToday;
        void CenterToday(object sender, RoutedEventArgs args)
        {
            scroll.Loaded -= CenterToday;
            // The viewport and variable-height week rows must be arranged before calculating the offset.
            scroll.Dispatcher.InvokeAsync(() =>
            {
                if (!scroll.IsLoaded) return;
                var center = todayButton.TranslatePoint(
                    new Point(todayButton.ActualWidth / 2, todayButton.ActualHeight / 2), grid);
                scroll.ScrollToVerticalOffset(Math.Clamp(center.Y - scroll.ViewportHeight / 2, 0, scroll.ScrollableHeight));
                scroll.ScrollToHorizontalOffset(Math.Clamp(center.X - scroll.ViewportWidth / 2, 0, scroll.ScrollableWidth));
            }, DispatcherPriority.Loaded);
        }
    }
}
