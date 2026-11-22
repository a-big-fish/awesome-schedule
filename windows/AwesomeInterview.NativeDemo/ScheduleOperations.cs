using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AwesomeInterview.NativeDemo.Data;

namespace AwesomeInterview.NativeDemo;

public partial class MainWindow
{
    sealed class ReferenceEditor : StackPanel
    {
        readonly List<(TextBox Name,TextBox Url)> rows = new();
        readonly StackPanel entries = new();
        readonly Action changed;
        public bool HasLinks => rows.Count>0;
        public ReferenceEditor(List<ReferenceLink> values,Action changed)
        {
            this.changed=changed;Children.Add(Text("名称可以留空；每条链接单独保存。",12,"#64748B",new(0,8,0,10)));Children.Add(entries);
            foreach(var value in values)Add(value);
            Children.Add(Button("＋ 添加链接",()=>{if(rows.Count<20){Add(new("",""));changed();}}));
        }
        void Add(ReferenceLink value)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new(2,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new(3,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var name=new TextBox{Text=value.Title,MaxLength=200,ToolTip="名称（可选）",Margin=new(0,4,8,8)};
            var url=new TextBox{Text=value.Url,MaxLength=2048,ToolTip="https:// 链接",Margin=new(0,4,8,8)};Grid.SetColumn(url,1);
            System.Windows.Automation.AutomationProperties.SetName(name,"参考资料名称");System.Windows.Automation.AutomationProperties.SetName(url,"参考资料链接");
            var pair=(name,url);var remove=Button("移除",()=>{rows.Remove(pair);entries.Children.Remove(row);changed();});remove.Margin=new(0,4,0,8);Grid.SetColumn(remove,2);
            rows.Add(pair);name.TextChanged+=(_,_)=>changed();url.TextChanged+=(_,_)=>changed();row.Children.Add(name);row.Children.Add(url);row.Children.Add(remove);entries.Children.Add(row);
        }
        public static void ValidateUrl(string value)
        {
            if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https")||string.IsNullOrEmpty(uri.Host)||!string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("链接需为不含账号密码的 http:// 或 https:// 地址。");
        }
        public List<ReferenceLink> Read()
        {
            var result=new List<ReferenceLink>();
            foreach(var (name,url) in rows){if(string.IsNullOrWhiteSpace(name.Text)&&string.IsNullOrWhiteSpace(url.Text))continue;ValidateUrl(url.Text.Trim());result.Add(new(name.Text.Trim(),url.Text.Trim()));}
            return result;
        }
    }

    bool showAbandoned;
    void ShowIdeas(bool abandoned=false)
    {
        showAbandoned=abandoned;page="ideas";bSelect("日历与日程");Begin("待安排事项","先记录想法，确定时间后再加入日历");if(NeedAccount())return;
        var toolbar=new WrapPanel();toolbar.Children.Add(Button("＋ 添加想法",()=>EditIdea(null)));
        toolbar.Children.Add(Button(abandoned?"查看待安排":"查看已放弃",()=>ShowIdeas(!abandoned)));
        toolbar.Children.Add(Button("立即同步",()=>Run(repository.SyncNowAsync)));
        toolbar.Children.Add(Button("返回日历",()=>Navigate(ShowCalendar)));toolbar.Children.Add(Button("刷新日历数据",()=>Run(repository.RefreshCalendarAsync)));content.Children.Add(toolbar);
        if(repository.HasPendingSync)Paragraph(repository.Online
            ?"修改已保存在本机，正在按顺序同步；切换页面或关闭窗口都不会丢失。"
            :"当前离线：可以先记录和排期，联网后按来源事项、日程的顺序同步。");
        var items=repository.Current!.Ideas.Where(i=>i.Status==(abandoned?"abandoned":"pending")).ToList();
        if(items.Count==0)content.Children.Add(Surface(Text(abandoned?"没有已放弃事项。":"还没有待安排事项，点击“添加想法”记录一件想做的事。",15,"#64748B")));
        var cards=new System.Windows.Controls.Primitives.UniformGrid{Margin=new(0,12,0,0)};
        void ResizeCards()=>cards.Columns=Math.Clamp((int)(viewport.ActualWidth/320),1,4);
        ResizeCards();
        SizeChangedEventHandler resize=(_,_)=>ResizeCards();viewport.SizeChanged+=resize;
        cards.Unloaded+=(_,_)=>viewport.SizeChanged-=resize;
        foreach(var item in items)
        {
            var pending=repository.IdeaSyncState(item.Id)==SyncStates.Pending;
            var body=new StackPanel();
            var title=Text(item.Title,17);title.FontWeight=FontWeights.SemiBold;body.Children.Add(title);
            if(pending)body.Children.Add(Text("待同步",12,"#52647D",new(0,5,0,0)));
            if(!string.IsNullOrWhiteSpace(item.Note))body.Children.Add(Text(item.Note,14,"#64748B",new(0,10,0,10)));
            if(item.ReferenceLinks.Count>0){
                var references=new StackPanel();
                foreach(var reference in item.ReferenceLinks){
                    var link=Button("",()=>OpenLink(reference.Url));
                    link.Content=new TextBlock{Text=string.IsNullOrWhiteSpace(reference.Title)?reference.Url:reference.Title,TextTrimming=TextTrimming.CharacterEllipsis};
                    link.ToolTip=reference.Url;link.HorizontalContentAlignment=HorizontalAlignment.Left;
                    link.MinHeight=32;link.Padding=new(9,4,9,4);link.Margin=new(0,4,0,0);references.Children.Add(link);
                }
                body.Children.Add(new Expander{Header=$"参考资料 · {item.ReferenceLinks.Count}",Content=references,Margin=new(0,10,0,0)});
            }
            if(!abandoned)
            {
                var actions=new WrapPanel{Margin=new(0,12,0,0)};
                if(item.ArrangedEventId!=null){var scheduled=repository.Current.Events.FirstOrDefault(e=>e.Id==item.ArrangedEventId);actions.Children.Add(Button("已创建日程 · 查看",()=>{if(scheduled!=null)ShowEventDetail(scheduled);}));}
                else {var arrange=Button("安排时间",()=>Navigate(()=>{selected=CalendarProjection.Now.Date;ShowEditor(null,item);}));actions.Children.Add(arrange);}
                actions.Children.Add(Button("修改想法",()=>EditIdea(item)));
                actions.Children.Add(Button("放弃",()=>{if(Confirm("放弃想法","移入已放弃列表，不会删除已创建的日程。","放弃","取消"))Run(async()=>{await repository.AbandonIdeaQueuedAsync(item);ShowIdeas();});}));
                foreach(var action in actions.Children.OfType<Button>()){
                    action.Padding=new(9,5,9,5);action.MinHeight=32;action.Margin=new(0,0,6,5);
                }
                body.Children.Add(actions);
            }
            var card=Surface(body,new(16));card.Margin=new(0,0,12,12);cards.Children.Add(card);
        }
        if(items.Count>0)content.Children.Add(cards);
        AddSyncStatus();
    }
    // Failed commands never project into the list, so their draft is rebuilt from the queued body.
    IdeaData DraftIdea(SyncQueueItem operation)
    {
        var command=operation.Command.Deserialize<IdeaSyncCommand>(Wire.Json)
            ??new IdeaSyncCommand("create",operation.EntityId,operation.Title,"",new());
        var stamp=SyncQueueRules.ParseStamp(operation.CreatedAt);
        return new IdeaData(command.IdeaId,"",command.Title,command.Note,command.ReferenceLinks??new(),"pending",stamp,stamp);
    }
    void AddSyncStatus()
    {
        var failed=repository.SyncItems.Where(item=>item.State==SyncStates.Failed).ToList();
        if(failed.Count==0)return;
        var body=new StackPanel();
        body.Children.Add(Text($"{failed.Count} 项修改未被服务器接受",16,"#B42332",new(0,0,0,8)));
        foreach(var operation in failed)
        {
            var row=new WrapPanel{Margin=new(0,4,0,6)};
            row.Children.Add(Text($"{operation.Title}：{operation.Error??"同步失败"}",13,"#64748B",new(0,0,14,8)));
            if(operation.Status==409)row.Children.Add(Button("确认重叠并重试",()=>Run(()=>repository.ConfirmOverlapAsync(operation))));
            row.Children.Add(Button("重试",()=>Run(()=>repository.RetrySyncAsync(operation))));
            if(operation.Endpoint==SyncQueueRules.IdeaEndpoint)row.Children.Add(Button("修改草稿",()=>EditIdea(DraftIdea(operation))));
            row.Children.Add(Button("放弃本次更改",()=>Run(()=>repository.DiscardSyncAsync(operation))));
            body.Children.Add(row);
        }
        content.Children.Insert(0,Surface(body,new(20,14,14,14)));
    }
    void EditIdea(IdeaData? item)
    {
        if(repository.Current==null){status.Text="登录并载入账号后即可记录想法。";return;}
        var body=new StackPanel{Margin=new(24)};body.Children.Add(Text(item==null?"添加想法":"修改想法",22));
        var title=new TextBox{Text=item?.Title??"",MaxLength=200};body.Children.Add(Text("标题",14,margin:new(0,16,0,0)));body.Children.Add(title);
        var note=new TextBox{Text=item?.Note??"",Height=100,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};body.Children.Add(Text("备注"));body.Children.Add(note);
        var links=new ReferenceEditor(item?.ReferenceLinks??new(),()=>{});body.Children.Add(new Expander{Header="参考资料",Content=links,IsExpanded=links.HasLinks});
        var feedback=Text("",13,"#B42332",new(0,12,0,8));body.Children.Add(feedback);
        var dialog=new Window{Title="待安排事项",Owner=this,Width=660,Height=620,MaxHeight=SystemParameters.WorkArea.Height-50,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.White,FontFamily=FontFamily,Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
        Button? save=null;save=Button("保存想法",()=>Run(async()=>{
            if(string.IsNullOrWhiteSpace(title.Text)){feedback.Text="请输入标题。";return;}save!.IsEnabled=false;
            try{await repository.SaveIdeaQueuedAsync(title.Text.Trim(),note.Text,links.Read(),item?.Id);dialog.Close();ShowIdeas(showAbandoned);}
            catch(Exception error)when(WorkspaceRepository.Expected(error)){feedback.Text=WorkspaceRepository.Explain(error);}finally{save.IsEnabled=true;}
        }));body.Children.Add(save);var cancel=Button("取消",()=>dialog.Close());cancel.IsCancel=true;body.Children.Add(cancel);dialog.ShowDialog();
    }

    void AddTodos(DateTime day)
    {
        content.Children.Add(Text("独立待办",18,margin:new(0,16,0,10)));
        var add=new DockPanel();var title=new TextBox{MaxLength=200,ToolTip="无需起止时间的待办",Margin=new(0,0,10,10)};
        System.Windows.Automation.AutomationProperties.SetName(title,"新待办内容");
        var dueDay=day.Date;Button? date=null;
        date=Button(dueDay.ToString("M月d日"),()=>{var choice=PickScheduleTime("待办日期",dueDay,false,"Asia/Shanghai");if(choice!=null){dueDay=choice.Value.Value.Date;date!.Content=dueDay.ToString("M月d日");}});
        date.ToolTip="选择待办所属日期，不设置日程起止时间";
        var create=Button("＋ 添加待办",()=>Run(async()=>{
            if(string.IsNullOrWhiteSpace(title.Text))return;
            await repository.SaveTodoAsync(title.Text.Trim(),DateOnly.FromDateTime(dueDay));ShowTasks(false);
        }));create.IsEnabled=repository.Online&&!repository.Busy;DockPanel.SetDock(create,Dock.Right);add.Children.Add(create);DockPanel.SetDock(date,Dock.Right);add.Children.Add(date);add.Children.Add(title);content.Children.Add(add);
        UIElement TodoRow(TodoData item)
        {
            var row=new DockPanel();var buttons=new WrapPanel();
            var complete=Button(item.CompletedAt==null?"完成":"恢复",()=>Run(async()=>{await repository.CompleteTodoAsync(item);ShowTasks(false);}));
            var remove=Button("删除",()=>{if(Confirm("删除待办","删除后无法恢复。","删除","取消"))Run(async()=>{await repository.DeleteTodoAsync(item);ShowTasks(false);});});
            complete.IsEnabled=remove.IsEnabled=repository.Online&&!repository.Busy;buttons.Children.Add(complete);buttons.Children.Add(remove);DockPanel.SetDock(buttons,Dock.Right);row.Children.Add(buttons);
            var caption=Text($"{item.Title}   ·   {item.DueDate:MM-dd}",16,item.CompletedAt==null?"#192C46":"#8492A6");caption.VerticalAlignment=VerticalAlignment.Center;if(item.CompletedAt!=null)caption.TextDecorations=TextDecorations.Strikethrough;row.Children.Add(caption);return Surface(row,new(18,10,12,10));
        }
        foreach(var item in repository.Current!.Todos.Where(t=>t.DueDate==DateOnly.FromDateTime(day)))content.Children.Add(TodoRow(item));
        var others=new StackPanel();foreach(var item in repository.Current.Todos.Where(t=>t.DueDate!=DateOnly.FromDateTime(day)).OrderBy(t=>t.DueDate))others.Children.Add(TodoRow(item));
        if(others.Children.Count>0)content.Children.Add(new Expander{Header=$"其他日期待办 · {others.Children.Count}",Content=others,Margin=new(0,8,0,10)});
    }

    void AddOverdue()
    {
        var now=DateTimeOffset.UtcNow;
        var rows=repository.Current!.Events.Where(e=>e.RepeatWeekdays.Count==0&&e.CompletedAt==null&&e.CancelledAt==null&&e.EndAt<=now)
            .Concat(EventsFor(CalendarProjection.Now.Date).Where(e=>e.RepeatWeekdays.Count>0&&e.CompletedAt==null&&e.CancelledAt==null&&e.EndAt<=now)).OrderBy(e=>e.EndAt).ToList();
        if(rows.Count==0)return;
        content.Children.Add(Text($"逾期未完成  {rows.Count}",18,"#A85B23",new(0,20,0,12)));
        foreach(var item in rows)
        {
            var body=new StackPanel();body.Children.Add(Button(item.Title,()=>ShowEventDetail(item)));body.Children.Add(Text($"原结束 {item.Local(item.EndAt):yyyy-MM-dd HH:mm}",12,"#64748B",new(0,6,0,10)));
            var actions=new WrapPanel();var done=Button("完成",()=>Run(()=>repository.CompleteEventQueuedAsync(item)));actions.Children.Add(done);
            if(item.RepeatWeekdays.Count==0){
                var defer=Button("延期",()=>{
                    var initial=item.Local(DateTimeOffset.UtcNow.AddHours(1));var choice=PickScheduleTime("延期结束时间",initial,true,item.Timezone,true);if(choice==null)return;
                    var end=item.At(choice.Value.Value);
                    if(end<item.EndAt||end<DateTimeOffset.UtcNow){Confirm("结束时间无效","新的结束时间不能早于原结束或当前时间。","知道了","返回");return;}
                    Run(()=>repository.DeferEventQueuedAsync(item,end));
                });actions.Children.Add(defer);
                actions.Children.Add(Button("废除",()=>{if(Confirm("废除日程","废除后保留记录，以不同颜色显示，并停止提醒。","废除","取消"))Run(()=>repository.CancelEventQueuedAsync(item));}));
            }
            foreach(var button in actions.Children.OfType<Button>())button.IsEnabled=!repository.Busy;body.Children.Add(actions);content.Children.Add(Surface(body));
        }
    }
}
