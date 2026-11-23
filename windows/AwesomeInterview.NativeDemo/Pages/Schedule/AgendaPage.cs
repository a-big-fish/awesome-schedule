using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AwesomeInterview.NativeDemo.Data;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class SchedulePages
{
    internal void ShowTasks(bool calendar)
    {
        if (!calendar) selected = CalendarProjection.Now.Date;
        Begin(calendar ? PageRoute.CalendarDay : PageRoute.Today, calendar ? "日历与日程" : "今日待办", calendar?selected.ToString("yyyy年M月d日 dddd"):"今日待办",calendar?"日程详情与完成状态":"把注意力留给今天要做的事");if(NeedAccount())return;
        var entries=EventsFor(selected);var pending=entries.Where(e=>e.CompletedAt==null&&e.CancelledAt==null).ToList();
        var toolbar=new DockPanel{Margin=new(0,0,0,18)};var add=Button("＋ 添加日程",()=>Navigate(ShowAdd));DockPanel.SetDock(add,Dock.Right);toolbar.Children.Add(add);
        toolbar.Children.Add(Text($"{selected:M月d日 dddd}    {pending.Count} 项未完成",18));Content.Children.Add(toolbar);
        foreach(var item in pending)Content.Children.Add(EventRow(item));
        if(pending.Count==0)Content.Children.Add(Surface(Text("今天暂无待完成日程。",16,"#64748B")));
        AddTodos(selected);AddOverdue();AddSyncStatus();
        var done=new StackPanel();foreach(var item in entries.Where(e=>e.CompletedAt!=null))done.Children.Add(EventRow(item));
        Content.Children.Add(new Expander{Header=$"今日已完成  {entries.Count(e=>e.CompletedAt!=null)}",Content=done,Margin=new(0,18,0,0)});
    }
    UIElement EventRow(EventData item)
    {
        var row=new DockPanel();var actions=new StackPanel{Orientation=Orientation.Horizontal};
        if(item.CancelledAt==null){var complete=Button(item.CompletedAt==null?"完成":"恢复",()=>Run(()=>Repository.CompleteEventQueuedAsync(item)));complete.IsEnabled=!Repository.Busy;actions.Children.Add(complete);}
        DockPanel.SetDock(actions,Dock.Right);row.Children.Add(actions);
        var detail=Button(item.Title,()=>ShowEventDetail(item));detail.HorizontalContentAlignment=HorizontalAlignment.Left;detail.BorderBrush=Brushes.Transparent;detail.Background=Brushes.Transparent;detail.Padding=new(0,0,12,0);
        var body=new StackPanel();var title=Text(item.Title,16,item.CompletedAt!=null?"#68778A":"#192C46");title.FontWeight=FontWeights.SemiBold;
        if(item.CompletedAt!=null)title.TextDecorations=TextDecorations.Strikethrough;body.Children.Add(title);body.Children.Add(Text($"{item.Local(item.StartAt):MM-dd HH:mm} — {item.Local(item.EndAt):MM-dd HH:mm}    {TypeLabel(item.Type)}",12,"#64748B",new(0,7,0,0)));detail.Content=body;row.Children.Add(detail);
        return Surface(row,new(18,14,14,14));
    }
    static string TypeLabel(string type)=>type switch{"personal"=>"个人事项","meeting"=>"普通会议","interview"=>"面试","exam"=>"笔试","course"=>"课程","recruiting"=>"招聘活动",_=>type};

    void AddTodos(DateTime day)
    {
        Content.Children.Add(Text("独立待办",18,margin:new(0,16,0,10)));
        var add=new DockPanel();var title=new TextBox{MaxLength=200,ToolTip="无需起止时间的待办",Margin=new(0,0,10,10)};
        System.Windows.Automation.AutomationProperties.SetName(title,"新待办内容");
        var dueDay=day.Date;Button? date=null;
        date=Button(dueDay.ToString("M月d日"),()=>{var choice=timePicker.Pick("待办日期",dueDay,false,"Asia/Shanghai");if(choice!=null){dueDay=choice.Value.Value.Date;date!.Content=dueDay.ToString("M月d日");}});
        date.ToolTip="选择待办所属日期，不设置日程起止时间";
        var create=Button("＋ 添加待办",()=>Run(async()=>{
            if(string.IsNullOrWhiteSpace(title.Text))return;
            await Repository.SaveTodoAsync(title.Text.Trim(),DateOnly.FromDateTime(dueDay));ShowTasks(false);
        }));create.IsEnabled=Repository.Online&&!Repository.Busy;DockPanel.SetDock(create,Dock.Right);add.Children.Add(create);DockPanel.SetDock(date,Dock.Right);add.Children.Add(date);add.Children.Add(title);Content.Children.Add(add);
        UIElement TodoRow(TodoData item)
        {
            var row=new DockPanel();var buttons=new WrapPanel();
            var complete=Button(item.CompletedAt==null?"完成":"恢复",()=>Run(async()=>{await Repository.CompleteTodoAsync(item);ShowTasks(false);}));
            var remove=Button("删除",()=>{if(Confirm("删除待办","删除后无法恢复。","删除","取消"))Run(async()=>{await Repository.DeleteTodoAsync(item);ShowTasks(false);});});
            complete.IsEnabled=remove.IsEnabled=Repository.Online&&!Repository.Busy;buttons.Children.Add(complete);buttons.Children.Add(remove);DockPanel.SetDock(buttons,Dock.Right);row.Children.Add(buttons);
            var caption=Text($"{item.Title}   ·   {item.DueDate:MM-dd}",16,item.CompletedAt==null?"#192C46":"#8492A6");caption.VerticalAlignment=VerticalAlignment.Center;if(item.CompletedAt!=null)caption.TextDecorations=TextDecorations.Strikethrough;row.Children.Add(caption);return Surface(row,new(18,10,12,10));
        }
        foreach(var item in Repository.Current!.Todos.Where(t=>t.DueDate==DateOnly.FromDateTime(day)))Content.Children.Add(TodoRow(item));
        var others=new StackPanel();foreach(var item in Repository.Current.Todos.Where(t=>t.DueDate!=DateOnly.FromDateTime(day)).OrderBy(t=>t.DueDate))others.Children.Add(TodoRow(item));
        if(others.Children.Count>0)Content.Children.Add(new Expander{Header=$"其他日期待办 · {others.Children.Count}",Content=others,Margin=new(0,8,0,10)});
    }

    void AddOverdue()
    {
        var now=DateTimeOffset.UtcNow;
        var rows=Repository.Current!.Events.Where(e=>e.RepeatWeekdays.Count==0&&e.CompletedAt==null&&e.CancelledAt==null&&e.EndAt<=now)
            .Concat(EventsFor(CalendarProjection.Now.Date).Where(e=>e.RepeatWeekdays.Count>0&&e.CompletedAt==null&&e.CancelledAt==null&&e.EndAt<=now)).OrderBy(e=>e.EndAt).ToList();
        if(rows.Count==0)return;
        Content.Children.Add(Text($"逾期未完成  {rows.Count}",18,"#A85B23",new(0,20,0,12)));
        foreach(var item in rows)
        {
            var body=new StackPanel();body.Children.Add(Button(item.Title,()=>ShowEventDetail(item)));body.Children.Add(Text($"原结束 {item.Local(item.EndAt):yyyy-MM-dd HH:mm}",12,"#64748B",new(0,6,0,10)));
            var actions=new WrapPanel();var done=Button("完成",()=>Run(()=>Repository.CompleteEventQueuedAsync(item)));actions.Children.Add(done);
            if(item.RepeatWeekdays.Count==0){
                var defer=Button("延期",()=>{
                    var initial=item.Local(DateTimeOffset.UtcNow.AddHours(1));var choice=timePicker.Pick("延期结束时间",initial,true,item.Timezone,true);if(choice==null)return;
                    var end=item.At(choice.Value.Value);
                    if(end<item.EndAt||end<DateTimeOffset.UtcNow){Confirm("结束时间无效","新的结束时间不能早于原结束或当前时间。","知道了","返回");return;}
                    Run(()=>Repository.DeferEventQueuedAsync(item,end));
                });actions.Children.Add(defer);
                actions.Children.Add(Button("废除",()=>{if(Confirm("废除日程","废除后保留记录，以不同颜色显示，并停止提醒。","废除","取消"))Run(()=>Repository.CancelEventQueuedAsync(item));}));
            }
            foreach(var button in actions.Children.OfType<Button>())button.IsEnabled=!Repository.Busy;body.Children.Add(actions);Content.Children.Add(Surface(body));
        }
    }

    internal TimeSpan WorkTime(bool end) => accountProfile.WorkTime(end);

    internal List<EventData> EventsFor(DateTime day) => Repository.Current is { } snapshot
        ? CalendarProjection.EventsForDay(snapshot.Events, day, WorkTime(false), WorkTime(true)).ToList()
        : new();

    internal void Refresh(PageRoute route)
    {
        switch (route)
        {
            case PageRoute.Today: ShowTasks(false); break;
            case PageRoute.Calendar: ShowCalendar(); break;
            case PageRoute.CalendarDay: ShowTasks(true); break;
            case PageRoute.Ideas: ShowIdeas(showAbandoned); break;
        }
    }
}
