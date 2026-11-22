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
    Grid ResumeColumns(StackPanel document)
    {
        var layout=new Grid();
        var timeline=new StackPanel{Margin=new(0)};
        var plan=new StackPanel();
        var dayCard=Surface(timeline,new(14));var planCard=Surface(plan,new(14));
        dayCard.VerticalAlignment=VerticalAlignment.Top;planCard.VerticalAlignment=VerticalAlignment.Top;
        layout.Children.Add(document);layout.Children.Add(dayCard);layout.Children.Add(planCard);
        void Resize(){
            double width=viewport.ActualWidth;
            layout.ColumnDefinitions.Clear();layout.RowDefinitions.Clear();
            var weights=width>=1080?new[]{5d,3d,2.5}:width>=760?new[]{1d,1d}:new[]{1d};
            foreach(var weight in weights)layout.ColumnDefinitions.Add(new(){Width=new(weight,GridUnitType.Star)});
            for(int i=0;i<(weights.Length==3?1:weights.Length==2?2:3);i++)layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
            Grid.SetColumnSpan(document,weights.Length==2?2:1);
            Grid.SetColumn(dayCard,weights.Length==3?1:0);Grid.SetRow(dayCard,weights.Length==3?0:1);
            Grid.SetColumn(planCard,weights.Length-1);Grid.SetRow(planCard,weights.Length==3?0:weights.Length==2?1:2);
            document.Margin=new(0,0,weights.Length==3?14:0,0);dayCard.Margin=new(0,0,weights.Length>=2?14:0,16);
        }
        Resize();SizeChangedEventHandler resize=(_,_)=>Resize();viewport.SizeChanged+=resize;layout.Unloaded+=(_,_)=>viewport.SizeChanged-=resize;

        DateTime date=CalendarProjection.Now.Date;
        var toolbar=new DockPanel{Margin=new(0,0,0,10)};
        var dateTitle=Text("",15);dateTitle.FontWeight=FontWeights.SemiBold;dateTitle.HorizontalAlignment=HorizontalAlignment.Center;dateTitle.VerticalAlignment=VerticalAlignment.Center;
        var prior=Button("‹",()=>{});var next=Button("›",()=>{});
        foreach(var button in new[]{prior,next}){button.Margin=new(0);button.Padding=new(7,3,7,3);button.MinHeight=32;}
        System.Windows.Automation.AutomationProperties.SetName(prior,"前一天");System.Windows.Automation.AutomationProperties.SetName(next,"后一天");
        DockPanel.SetDock(prior,Dock.Left);DockPanel.SetDock(next,Dock.Right);toolbar.Children.Add(prior);toolbar.Children.Add(next);toolbar.Children.Add(dateTitle);
        timeline.Children.Add(toolbar);
        var quick=new WrapPanel();timeline.Children.Add(quick);
        var gridHost=new StackPanel{Margin=new(0,10,0,0)};timeline.Children.Add(gridHost);
        var clock=new DispatcherTimer{Interval=TimeSpan.FromMinutes(1)};
        Action refreshClock=()=>{};
        clock.Tick+=(_,_)=>refreshClock();timeline.Loaded+=(_,_)=>{refreshClock();clock.Start();};timeline.Unloaded+=(_,_)=>clock.Stop();
        prior.Click+=(_,_)=>{date=date.AddDays(-1);Refresh();};
        next.Click+=(_,_)=>{date=date.AddDays(1);Refresh();};
        foreach(var (label,delta) in new[]{("昨天",-1),("今天",0),("明天",1)}){
            var button=Button(label,()=>{date=CalendarProjection.Now.Date.AddDays(delta);Refresh();});button.MinHeight=30;button.Padding=new(9,3,9,3);quick.Children.Add(button);
        }
        void Refresh(){
            dateTitle.Text=date.ToString("M月d日 ddd");
            gridHost.Children.Clear();
            var rows=EventsFor(date).Where(e=>e.Type=="interview"&&e.CancelledAt==null).OrderBy(e=>e.StartAt).ToList();
            const double hourHeight=48;
            int first=rows.Count==0?8:Math.Min(8,rows.Min(e=>CalendarProjection.DisplayTime(e.StartAt).Date<date?0:CalendarProjection.DisplayTime(e.StartAt).Hour));
            var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new(48)});grid.ColumnDefinitions.Add(new());
            var times=new Canvas{Height=(24-first)*hourHeight};grid.Children.Add(times);
            var body=new Grid();Grid.SetColumn(body,1);grid.Children.Add(body);
            for(int hour=first;hour<24;hour++){
                double y=(hour-first)*hourHeight;
                var time=Text($"{hour:00}:00",11,"#62728A");Canvas.SetTop(time,y+3);times.Children.Add(time);
                body.Children.Add(new Border{Height=1,Background=Brush("#E6ECF5"),VerticalAlignment=VerticalAlignment.Top,Margin=new(0,y,0,0)});
            }
            var laneEnds=new List<DateTime>();var positions=new List<(EventData Item,DateTime From,DateTime To,int Lane)>();
            foreach(var item in rows){
                if(CalendarProjection.Segment(item,date,WorkTime(false),WorkTime(true)) is not { } segment)continue;
                int lane=laneEnds.FindIndex(end=>end<=segment.Start);
                if(lane<0){lane=laneEnds.Count;laneEnds.Add(segment.End);}else laneEnds[lane]=segment.End;
                positions.Add((item,segment.Start,segment.End,lane));
            }
            var cards=new Grid();for(int lane=0;lane<laneEnds.Count;lane++)cards.ColumnDefinitions.Add(new());body.Children.Add(cards);
            foreach(var (item,from,to,lane) in positions){
                var card=CalendarEntry(item,item.Title,13);card.MinHeight=0;card.Padding=new(8,2,8,2);
                card.Height=Math.Max(22,(to-from).TotalHours*hourHeight-2);
                card.Margin=new(2,Math.Max(0,(from-date).TotalHours-first)*hourHeight,2,0);
                card.VerticalAlignment=VerticalAlignment.Top;Grid.SetColumn(card,lane);cards.Children.Add(card);
            }
            var line=new Border{Height=1,Background=Brush("#E5484D"),VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false};
            body.Children.Add(line);var badge=Text("",11,"#D43F48");badge.Background=Brush("#FFFFFF");times.Children.Add(badge);
            refreshClock=()=>{
                var now=CalendarProjection.Now;
                var y=(now.TimeOfDay.TotalHours-first)*hourHeight;
                line.Visibility=badge.Visibility=now.Date==date&&y>=0?Visibility.Visible:Visibility.Collapsed;
                line.Margin=new(0,Math.Max(0,y),0,0);Canvas.SetTop(badge,Math.Max(0,y-15));badge.Text=now.ToString("HH:mm");
            };
            refreshClock();
            var scroll=new QuietScrollViewer{Content=grid,Height=365,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            scroll.Loaded+=(_,_)=>{double target=date==CalendarProjection.Now.Date?CalendarProjection.Now.TimeOfDay.TotalHours:positions.Count>0?(positions[0].From-date).TotalHours:first;scroll.ScrollToVerticalOffset(Math.Max(0,(target-first)*hourHeight-scroll.ViewportHeight/2));};
            gridHost.Children.Add(scroll);
            if(rows.Count==0)gridHost.Children.Add(Text("这一天暂无面试",13,"#52647D",new(0,12,0,0)));
            RefreshPlan();
        }
        void RefreshPlan(){
            plan.Children.Clear();
            var heading=Text("面试计划",18,margin:new(0,0,0,16));heading.FontWeight=FontWeights.SemiBold;plan.Children.Add(heading);
            var now=DateTimeOffset.UtcNow;var today=CalendarProjection.Now.Date;
            var items=repository.Current?.Events.Where(e=>e.Type=="interview"&&e.CancelledAt==null).OrderBy(e=>e.StartAt).ToList()??new();
            bool Active(EventData e)=>e.EndAt>now&&e.CompletedAt==null;
            void Group(string name,IEnumerable<EventData> entries){
                plan.Children.Add(Text(name,14,"#2357CE",new(0,0,0,9)));
                var rows=entries.ToList();
                foreach(var item in rows){
                    var card=CalendarEntry(item,item.Title,13);
                    var detail=new StackPanel();detail.Children.Add(Text(CalendarProjection.DisplayTime(item.StartAt).ToString("M/d HH:mm"),11,"#52647D"));
                    detail.Children.Add(Text(item.Title,13,margin:new(0,5,0,0)));card.Content=detail;card.Margin=new(0,0,0,8);
                    plan.Children.Add(card);
                }
                if(rows.Count==0)plan.Children.Add(Text("暂无安排",12,"#62728A",new(0,0,0,16)));
            }
            Group("今日面试",items.Where(e=>Active(e)&&CalendarProjection.DisplayTime(e.StartAt).Date==today));
            Group("未来面试",items.Where(e=>Active(e)&&CalendarProjection.DisplayTime(e.StartAt).Date>today));
            var finished=items.Where(e=>!Active(e)).Reverse().ToList();
            if(finished.Count>0){
                var list=new StackPanel();foreach(var item in finished)list.Children.Add(CalendarEntry(item,item.Title,13));
                plan.Children.Add(new Expander{Header=$"已结束 · {finished.Count}",Content=list,Margin=new(0,6,0,0)});
            }
        }
        void OnChanged(){
            if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(OnChanged));return;}
            if(layout.IsLoaded&&page=="resume")Refresh();
        }
        layout.Loaded+=(_,_)=>repository.Changed+=OnChanged;
        layout.Unloaded+=(_,_)=>repository.Changed-=OnChanged;
        Refresh();
        return layout;
    }
}
