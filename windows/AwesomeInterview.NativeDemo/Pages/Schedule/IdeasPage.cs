using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AwesomeInterview.NativeDemo.Data;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class SchedulePages
{
    bool showAbandoned;
    internal void ShowIdeas(bool abandoned=false)
    {
        showAbandoned=abandoned;Begin(PageRoute.Ideas,"日历与日程","待安排事项","先记录想法，确定时间后再加入日历");if(NeedAccount())return;
        var toolbar=new WrapPanel();toolbar.Children.Add(Button("＋ 添加想法",()=>EditIdea(null)));
        toolbar.Children.Add(Button(abandoned?"查看待安排":"查看已放弃",()=>ShowIdeas(!abandoned)));
        toolbar.Children.Add(Button("立即同步",()=>Run(Repository.SyncNowAsync)));
        toolbar.Children.Add(Button("返回日历",()=>Navigate(ShowCalendar)));toolbar.Children.Add(Button("刷新日历数据",()=>Run(Repository.RefreshCalendarAsync)));Content.Children.Add(toolbar);
        if(Repository.HasPendingSync)Paragraph(Repository.Online
            ?"修改已保存在本机，正在按顺序同步；切换页面或关闭窗口都不会丢失。"
            :"当前离线：可以先记录和排期，联网后按来源事项、日程的顺序同步。");
        var items=Repository.Current!.Ideas.Where(i=>i.Status==(abandoned?"abandoned":"pending")).ToList();
        if(items.Count==0)Content.Children.Add(Surface(Text(abandoned?"没有已放弃事项。":"还没有待安排事项，点击“添加想法”记录一件想做的事。",15,"#64748B")));
        var cards=new System.Windows.Controls.Primitives.UniformGrid{Margin=new(0,12,0,0)};
        void ResizeCards()=>cards.Columns=Math.Clamp((int)(Viewport.ActualWidth/320),1,4);
        TrackViewportSize(cards, ResizeCards);
        foreach(var item in items)
        {
            var pending=Repository.IdeaSyncState(item.Id)==SyncStates.Pending;
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
                if(item.ArrangedEventId!=null){var scheduled=Repository.Current.Events.FirstOrDefault(e=>e.Id==item.ArrangedEventId);actions.Children.Add(Button("已创建日程 · 查看",()=>{if(scheduled!=null)ShowEventDetail(scheduled);}));}
                else {var arrange=Button("安排时间",()=>Navigate(()=>{selected=CalendarProjection.Now.Date;ShowEditor(null,item);}));actions.Children.Add(arrange);}
                actions.Children.Add(Button("修改想法",()=>EditIdea(item)));
                actions.Children.Add(Button("放弃",()=>{if(Confirm("放弃想法","移入已放弃列表，不会删除已创建的日程。","放弃","取消"))Run(async()=>{await Repository.AbandonIdeaQueuedAsync(item);ShowIdeas();});}));
                foreach(var action in actions.Children.OfType<Button>()){
                    action.Padding=new(9,5,9,5);action.MinHeight=32;action.Margin=new(0,0,6,5);
                }
                body.Children.Add(actions);
            }
            var card=Surface(body,new(16));card.Margin=new(0,0,12,12);cards.Children.Add(card);
        }
        if(items.Count>0)Content.Children.Add(cards);
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
        var failed=Repository.SyncItems.Where(item=>item.State==SyncStates.Failed).ToList();
        if(failed.Count==0)return;
        var body=new StackPanel();
        body.Children.Add(Text($"{failed.Count} 项修改未被服务器接受",16,"#B42332",new(0,0,0,8)));
        foreach(var operation in failed)
        {
            var row=new WrapPanel{Margin=new(0,4,0,6)};
            row.Children.Add(Text($"{operation.Title}：{operation.Error??"同步失败"}",13,"#64748B",new(0,0,14,8)));
            if(operation.Status==409)row.Children.Add(Button("确认重叠并重试",()=>Run(()=>Repository.ConfirmOverlapAsync(operation))));
            row.Children.Add(Button("重试",()=>Run(()=>Repository.RetrySyncAsync(operation))));
            if(operation.Endpoint==SyncQueueRules.IdeaEndpoint)row.Children.Add(Button("修改草稿",()=>EditIdea(DraftIdea(operation))));
            row.Children.Add(Button("放弃本次更改",()=>Run(()=>Repository.DiscardSyncAsync(operation))));
            body.Children.Add(row);
        }
        Content.Children.Insert(0,Surface(body,new(20,14,14,14)));
    }
    internal void EditIdea(IdeaData? item, Window? owner = null)
    {
        if(Repository.Current==null){Status.Text="登录并载入账号后即可记录想法。";return;}
        var body=new StackPanel{Margin=new(24)};body.Children.Add(Text(item==null?"添加想法":"修改想法",22));
        var title=new TextBox{Text=item?.Title??"",MaxLength=200};body.Children.Add(Text("标题",14,margin:new(0,16,0,0)));body.Children.Add(title);
        var note=new TextBox{Text=item?.Note??"",Height=100,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};body.Children.Add(Text("备注"));body.Children.Add(note);
        var links=new ReferenceEditor(item?.ReferenceLinks??new(),()=>{});body.Children.Add(new Expander{Header="参考资料",Content=links,IsExpanded=links.HasLinks});
        var feedback=Text("",13,"#B42332",new(0,12,0,8));body.Children.Add(feedback);
        var dialog=new Window{Title="待安排事项",Owner=owner??Owner,Width=660,Height=620,MaxHeight=SystemParameters.WorkArea.Height-50,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.White,FontFamily=FontFamily,Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
        Button? save=null;save=Button("保存想法",()=>Run(async()=>{
            if(string.IsNullOrWhiteSpace(title.Text)){feedback.Text="请输入标题。";return;}save!.IsEnabled=false;
            try{await Repository.SaveIdeaQueuedAsync(title.Text.Trim(),note.Text,links.Read(),item?.Id);dialog.Close();ShowIdeas(showAbandoned);}
            catch(Exception error)when(WorkspaceRepository.Expected(error)){feedback.Text=WorkspaceRepository.Explain(error);}finally{save.IsEnabled=true;}
        }));body.Children.Add(save);var cancel=Button("取消",()=>dialog.Close());cancel.IsCancel=true;body.Children.Add(cancel);dialog.ShowDialog();
    }
}
