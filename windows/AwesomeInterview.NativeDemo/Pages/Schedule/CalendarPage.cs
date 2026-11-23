using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AwesomeInterview.NativeDemo.Data;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class SchedulePages(MainWindow shell, WorkspaceProfile accountProfile, ScheduleTimePicker timePicker) : WorkspacePage(shell)
{
    DateTime selected = CalendarProjection.Now.Date;
    string calendarView = "day";
    bool hideCompleted = true;

    bool focusCurrentTime=true;
    internal void ShowCalendar() => RenderCalendar();

    void RenderCalendar(bool centerToday = false)
    {
        Begin(PageRoute.Calendar,"日历与日程","日历与日程","用日、周、月视图查看你的安排");if(NeedAccount())return;
        var toolbar = BuildCalendarToolbar();
        Content.Children.Add(toolbar);
        AddSyncStatus();
        var datebar=new Grid{Margin=new(0,0,0,16)};
        datebar.ColumnDefinitions.Add(new(){Width=new(44)});datebar.ColumnDefinitions.Add(new());datebar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});datebar.ColumnDefinitions.Add(new(){Width=new(44)});
        void Shift(int direction){selected=calendarView=="month"?selected.AddMonths(direction):selected.AddDays(direction*(calendarView=="week"?7:1));ShowCalendar();}
        Button Arrow(string glyph,string name,Action action){var button=Button("",action);button.Content=new TextBlock{Text=glyph,FontFamily=new("Segoe MDL2 Assets"),FontSize=16};button.Padding=new(8);button.Margin=new(0);System.Windows.Automation.AutomationProperties.SetName(button,name);return button;}
        datebar.Children.Add(Arrow("\uE76B","上一日期",()=>Shift(-1)));var next=Arrow("\uE76C","下一日期",()=>Shift(1));Grid.SetColumn(next,3);datebar.Children.Add(next);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(10,0,10,0)};actions.Children.Add(Button("回到今天",()=>{selected=CalendarProjection.Now.Date;if(calendarView=="day")focusCurrentTime=true;RenderCalendar(centerToday: true);}));actions.Children.Add(Button(hideCompleted?"切换全部日程":"切换未完成日程",()=>{hideCompleted=!hideCompleted;ShowCalendar();}));Grid.SetColumn(actions,2);datebar.Children.Add(actions);
        var first=calendarView=="week"?selected.Date.AddDays(-(((int)selected.DayOfWeek+6)%7)):selected.Date;
        var label=Text(calendarView=="month"?selected.ToString("yyyy年M月"):calendarView=="week"?$"{first:yyyy-MM-dd} — {first.AddDays(6):MM-dd}":selected.ToString("yyyy-MM-dd dddd"),19);
        label.FontWeight=FontWeights.SemiBold;label.HorizontalAlignment=HorizontalAlignment.Center;label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new(0,0,0,12);Content.Children.Add(label);Content.Children.Add(datebar);
        if(calendarView=="month")RenderMonth(centerToday);else if(calendarView=="week")RenderWeek(first);else RenderTimeline(first, centerToday);
        Content.HorizontalAlignment=HorizontalAlignment.Center;
        void ResizeCalendar(){Content.Width=Math.Max(560,Viewport.ActualWidth*(calendarView=="day"?.88:1));}
        TrackViewportSize(toolbar, ResizeCalendar);

    }
    Grid BuildCalendarToolbar()
    {
        var views = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left
        };
        foreach (var (key, caption) in new[] { ("day", "日视图"), ("week", "周视图"), ("month", "月视图") })
        {
            var view = Button(caption, () =>
            {
                calendarView = key;
                ShowCalendar();
            });
            if (key == calendarView)
            {
                view.Background = Brush("#E9F0FF");
                view.Foreground = Brush("#2357CE");
            }
            views.Children.Add(view);
        }

        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right
        };
        int pendingIdeas = Repository.Current?.Ideas.Count(item => item.Status == "pending" && item.ArrangedEventId == null) ?? 0;
        commands.Children.Add(Button($"待安排 · {pendingIdeas}", () => Navigate(() => ShowIdeas())));
        commands.Children.Add(Button("刷新日历", () => Run(Repository.RefreshCalendarAsync)));
        if (calendarView == "day")
        {
            commands.Children.Add(Button(focusCurrentTime ? "查看24小时概览" : "聚焦当前时间", () =>
            {
                focusCurrentTime = !focusCurrentTime;
                ShowCalendar();
            }));
        }

        foreach (var group in new[] { views, commands })
        {
            var last = (Button)group.Children[group.Children.Count - 1];
            last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, 0, last.Margin.Bottom);
        }

        var toolbar = new Grid { Margin = new(0, 0, 0, 14) };
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        toolbar.Children.Add(views);
        toolbar.Children.Add(commands);
        void ArrangeGroups()
        {
            const double gap = 12;
            bool fitsOnOneRow = views.DesiredSize.Width + commands.DesiredSize.Width + gap <= toolbar.ActualWidth;
            Grid.SetRow(commands, fitsOnOneRow ? 0 : 1);
        }
        toolbar.Loaded += (_, _) => ArrangeGroups();
        toolbar.SizeChanged += (_, args) =>
        {
            if (args.WidthChanged) ArrangeGroups();
        };
        return toolbar;
    }

    void RenderTimeline(DateTime first, bool scrollFromTop)
    {
        bool focus=focusCurrentTime&&CalendarProjection.Now.Date==first.Date;
        double span=24;double startHour=0;double timelineHeight=focus?1920:Math.Max(340,Viewport.ActualHeight-230);
        var grid=new Grid{MinWidth=420};grid.ColumnDefinitions.Add(new(){Width=new(52)});grid.ColumnDefinitions.Add(new());
        var hours=new Canvas{Height=timelineHeight,Margin=new(0,38,0,0)};for(int h=0;h<=span;h++){var tick=first.AddHours(startHour+h);var t=Text(tick.ToString("HH:mm")+(tick.Date<first.Date?" 前日":tick.Date>first.Date?" 次日":""),10,"#8492A6");Canvas.SetTop(t,h*timelineHeight/span);hours.Children.Add(t);}grid.Children.Add(hours);
        {
            var day=first.Date;var column=new DockPanel();Grid.SetColumn(column,1);grid.Children.Add(column);
            var title=Text(day.ToString("ddd M/d"),12,day==CalendarProjection.Now.Date?"#285DDD":"#64748B",new(4,0,4,0));title.Height=38;title.Padding=new(0,9,0,0);title.HorizontalAlignment=HorizontalAlignment.Center;DockPanel.SetDock(title,Dock.Top);column.Children.Add(title);
            var canvas=new Canvas{Height=timelineHeight,ClipToBounds=true,Background=Brushes.White};column.Children.Add(canvas);
            for(int h=0;h<=span;h++){var line=new Border{Height=1,Background=Brush("#EBEFF6")};Canvas.SetTop(line,h*timelineHeight/span);canvas.Children.Add(line);line.SetBinding(FrameworkElement.WidthProperty,new System.Windows.Data.Binding("ActualWidth"){Source=canvas});}
            var items=EventsFor(day).Where(e=>!hideCompleted||e.CompletedAt==null).ToList();
            // Recalculate lanes from visible entries, so hidden completed events never leave empty columns.
            var laneEnds=new List<DateTime>();var placed=new List<(EventData Item,int Lane,DateTime Start,DateTime End)>();
            foreach(var item in items){var segment=CalendarProjection.Segment(item,day,WorkTime(false),WorkTime(true));if(segment==null)continue;var (from,to)=segment.Value;int lane=laneEnds.FindIndex(e=>e<=from);if(lane<0){lane=laneEnds.Count;laneEnds.Add(to);}else laneEnds[lane]=to;placed.Add((item,lane,from,to));}
            var laneWidths=new double[laneEnds.Count];
            var cards=new List<(Button Card,int Lane)>();
            foreach(var row in placed){
                var item=row.Item;
                string caption=(CalendarProjection.DisplayTime(item.StartAt).Date<day?"‹ ":"")+item.Title+(CalendarProjection.DisplayTime(item.EndAt)>day.AddDays(1)?" ›":"");
                var card=CalendarEntry(item,caption,15);
                if(card.Content is TextBlock titleBlock)titleBlock.TextAlignment=TextAlignment.Center;
                card.FontFamily=FontFamily;card.Padding=new(12,3,12,3);card.MinHeight=0;
                card.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
                laneWidths[row.Lane]=Math.Max(laneWidths[row.Lane],Math.Clamp(card.DesiredSize.Width,96,260));
                card.Height=Math.Clamp((row.End-row.Start).TotalHours*timelineHeight/span,24,timelineHeight);
                Canvas.SetTop(card,(row.Start-day).TotalHours*timelineHeight/span);
                canvas.Children.Add(card);cards.Add((card,row.Lane));
            }
            double[] offsets=new double[laneWidths.Length];
            for(int i=1;i<offsets.Length;i++)offsets[i]=offsets[i-1]+laneWidths[i-1]+10;
            foreach(var (card,lane) in cards){card.Width=Math.Clamp(card.DesiredSize.Width,96,laneWidths[lane]);Canvas.SetLeft(card,8+offsets[lane]);}
            if(laneWidths.Length>0)grid.MinWidth=Math.Max(grid.MinWidth,52+laneWidths.Sum()+laneWidths.Length*10+8);

        }
        var clockLayer=new Canvas{IsHitTestVisible=false,Margin=new(0,38,0,0),ClipToBounds=true};
        Grid.SetColumnSpan(clockLayer,2);Panel.SetZIndex(clockLayer,10);grid.Children.Add(clockLayer);
        void UpdateClock(){
            clockLayer.Children.Clear();var now=CalendarProjection.Now;if(now.Date!=first.Date)return;
            double y=now.TimeOfDay.TotalHours*timelineHeight/24;double width=Math.Max(0,clockLayer.ActualWidth-52);const double left=52;
            var line=new Border{Background=Brush("#E5484D"),Height=1,Width=width,Opacity=.75};Canvas.SetTop(line,y);Canvas.SetLeft(line,left);clockLayer.Children.Add(line);
            var tag=new Border{Background=Brushes.White,CornerRadius=new(4),Padding=new(4,2,4,2),Child=Text(now.ToString("HH:mm"),11,"#D43F48")};
            Canvas.SetTop(tag,Math.Clamp(y-20,0,timelineHeight-24));Canvas.SetLeft(tag,0);clockLayer.Children.Add(tag);
        }
        var clock=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMinutes(1)};clock.Tick+=(_,_)=>UpdateClock();clockLayer.Loaded+=(_,_)=>{UpdateClock();clock.Start();};clockLayer.Unloaded+=(_,_)=>clock.Stop();clockLayer.SizeChanged+=(_,_)=>UpdateClock();
        var timelineScroll=new QuietScrollViewer{Content=grid,Height=Math.Max(260,Viewport.ActualHeight-220),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,CanContentScroll=false};
        bool userMoved = false;
        var animation = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var elapsed = new Stopwatch();
        double TargetOffset()
        {
            double nowY = 38 + CalendarProjection.Now.TimeOfDay.TotalHours * timelineHeight / 24;
            return Math.Clamp(nowY - timelineScroll.ViewportHeight / 2, 0, timelineScroll.ScrollableHeight);
        }
        void StopForInput()
        {
            userMoved = true;
            animation.Stop();
        }
        timelineScroll.PreviewMouseWheel += (_, _) => StopForInput();
        timelineScroll.PreviewMouseDown += (_, _) => StopForInput();
        timelineScroll.PreviewKeyDown += (_, _) => StopForInput();
        timelineScroll.Unloaded += (_, _) => animation.Stop();
        animation.Tick += (_, _) =>
        {
            double progress = Math.Min(1, elapsed.Elapsed.TotalMilliseconds / 460);
            timelineScroll.ScrollToVerticalOffset(TargetOffset() * (1 - Math.Pow(1 - progress, 3)));
            if (progress == 1) animation.Stop();
        };
        timelineScroll.SizeChanged+=(_,_)=>grid.Width=Math.Max(grid.MinWidth,timelineScroll.ActualWidth);
        void CenterNow()
        {
            if (focus && !userMoved && !scrollFromTop && !animation.IsEnabled)
                timelineScroll.ScrollToVerticalOffset(TargetOffset());
        }
        bool animatedReturn = false;
        timelineScroll.Loaded += (_, _) =>
        {
            if (!focus || !scrollFromTop || animatedReturn) return;
            animatedReturn = true;
            timelineScroll.Dispatcher.InvokeAsync(() =>
            {
                if (!timelineScroll.IsLoaded || userMoved) return;
                if (!SystemParameters.ClientAreaAnimation)
                {
                    timelineScroll.ScrollToVerticalOffset(TargetOffset());
                    return;
                }
                timelineScroll.ScrollToTop();
                elapsed.Restart();
                animation.Start();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        };
        void StickyTitles()
        {
            foreach(var column in grid.Children.OfType<DockPanel>())
            foreach(var canvas in column.Children.OfType<Canvas>())
            foreach(var button in canvas.Children.OfType<Button>())
            if(button.Content is TextBlock title)
            {
                double innerHeight=Math.Max(0,button.Height-button.Padding.Top-button.Padding.Bottom);
                double top=38+Canvas.GetTop(button)+button.Padding.Top;
                double visibleStart=Math.Clamp(timelineScroll.VerticalOffset-top,0,innerHeight);
                double visibleEnd=Math.Clamp(timelineScroll.VerticalOffset+timelineScroll.ViewportHeight-top,visibleStart,innerHeight);
                double currentY=38+CalendarProjection.Now.TimeOfDay.TotalHours*timelineHeight/24;
                double center=top+(visibleStart+visibleEnd)/2;
                double shift=CalendarProjection.Now.Date==first.Date&&Math.Abs(center-currentY)<26&&visibleEnd-visibleStart>90?-30:0;
                title.Margin=new(0,Math.Max(0,visibleStart+shift),0,Math.Max(0,innerHeight-visibleEnd-shift));
            }
        }
        timelineScroll.SizeChanged+=(_,_)=>StickyTitles();timelineScroll.ScrollChanged+=(_,_)=>StickyTitles();timelineScroll.Loaded+=(_,_)=>{CenterNow();StickyTitles();};
        TrackViewportSize(timelineScroll, () =>
        {
            timelineScroll.Height = Math.Max(260, Viewport.ActualHeight - 220);
            CenterNow();
        });
        Content.Children.Add(Surface(timelineScroll,new(8)));
    }
}
