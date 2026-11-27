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

internal sealed partial class SchedulePages
{
    internal void ShowAdd()=>Run(async()=>{if(Repository.Online){try{await accountProfile.LoadAsync();}catch(Exception e)when(WorkspaceRepository.Expected(e)){Status.Text=WorkspaceRepository.Explain(e);}}ShowEditor(null);});
    internal void ShowEditor(EventData? existing, IdeaData? source = null, string defaultType = "personal")
    {
        Begin(PageRoute.ScheduleEditor,"添加日程",existing==null?"添加日程":"编辑日程",source==null?"时间、地点与参考资料，一次整理清楚":"由待安排事项创建；取消不会修改原事项");if(NeedAccount())return;
        var fields=new StackPanel{MaxWidth=900,HorizontalAlignment=HorizontalAlignment.Stretch};
        var types=new[]{"personal","interview","meeting","exam","course","recruiting"};
        var type=new ComboBox{ItemsSource=types.Select(TypeLabel),SelectedIndex=Math.Max(0,Array.IndexOf(types,existing?.Type??defaultType)),Padding=new(10),Margin=new(0,8,0,18)};
        fields.Children.Add(Text("类型"));fields.Children.Add(type);
        string? raw=null;
        var title=new TextBox{Text=existing?.Title??source?.Title??"",MaxLength=200};
        var start=new TextBox{Text=(existing?.Local(existing.StartAt)??selected.Date.Add(WorkTime(false))).ToString("yyyy-MM-dd HH:mm")};
        var end=new TextBox{Text=(existing?.Local(existing.EndAt)??selected.Date.Add(WorkTime(true))).ToString("yyyy-MM-dd HH:mm")};
        var location=new TextBox{Text=existing?.LocationText??""};var map=new TextBox{Text=existing?.LocationUrl??""};
        var meetingUrl=new TextBox{Text=existing?.MeetingUrl??"",MaxLength=2048};var room=new TextBox{Text=existing?.RoomNumber??"",MaxLength=100};string? platform=existing?.Platform;
        var position=new TextBox{Text=existing?.Position??""};
        var note=new TextBox{Text=existing?.Note??source?.Note??"",Height=100,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};
        var invitation=new TextBox{AcceptsReturn=true,Height=94,TextWrapping=TextWrapping.Wrap};
        var invitationBox=new StackPanel();invitationBox.Children.Add(Text("粘贴会议邀请"));invitationBox.Children.Add(invitation);
        var parseInvitation=Button("解析并填入",()=>Run(async()=>{
            var parsed=await Repository.Api!.WriteAsync<EventData>(HttpMethod.Post,"api/mobile/v1/events/parse",new{raw_text=invitation.Text});
            raw=invitation.Text;title.Text=parsed.Title;start.Text=parsed.Local(parsed.StartAt).ToString("yyyy-MM-dd HH:mm");end.Text=parsed.Local(parsed.EndAt).ToString("yyyy-MM-dd HH:mm");meetingUrl.Text=parsed.MeetingUrl??"";room.Text=parsed.RoomNumber??"";platform=parsed.Platform;FormDirty=true;
        }));
        parseInvitation.IsEnabled=Repository.Online;
        invitationBox.Children.Add(parseInvitation);
        if(!Repository.Online)invitationBox.Children.Add(Text("离线时不能解析邀请，联网后重试；已填内容仍可保存。",12,"#64748B",new(0,6,0,0)));
        fields.Children.Add(invitationBox);
        var positionBox=new StackPanel();positionBox.Children.Add(Text("应聘岗位"));positionBox.Children.Add(position);
        var meetingBox=new StackPanel();meetingBox.Children.Add(Text("入会链接（可选）"));meetingBox.Children.Add(meetingUrl);meetingBox.Children.Add(Text("会议号（可选）"));meetingBox.Children.Add(room);
        void TypeVisibility(){invitationBox.Visibility=existing==null&&source==null&&type.SelectedIndex is 1 or 2?Visibility.Visible:Visibility.Collapsed;positionBox.Visibility=type.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;meetingBox.Visibility=type.SelectedIndex is 1 or 2?Visibility.Visible:Visibility.Collapsed;}
        TypeVisibility();type.SelectionChanged+=(_,_)=>{TypeVisibility();FormDirty=true;};
        fields.Children.Add(Text("标题"));fields.Children.Add(title);fields.Children.Add(positionBox);
        fields.Children.Add(meetingBox);meetingUrl.TextChanged+=(_,_)=>FormDirty=true;room.TextChanged+=(_,_)=>FormDirty=true;
        var skip=new CheckBox{Content="任务跳过周末",IsChecked=existing?.SkipWeekends??selected.DayOfWeek is not(DayOfWeek.Saturday or DayOfWeek.Sunday),VerticalAlignment=VerticalAlignment.Center};
        var workOnly=new CheckBox{Content="仅在默认工作时段显示",IsChecked=existing?.WorkdayOnly??false,Margin=new(0,0,0,14)};
        bool ValidTime(string text,out DateTime value)=>DateTime.TryParseExact(text,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out value);
        var weekdays=Enumerable.Range(0,7).Select(i=>new CheckBox{Content=new[]{"周一","周二","周三","周四","周五","周六","周日"}[i],IsChecked=existing?.RepeatWeekdays.Contains(i)??false,Margin=new(0,5,14,5)}).ToArray();
        void SyncWeekend(){if(ValidTime(start.Text,out var value)){bool weekend=value.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;skip.IsEnabled=!weekend&&!weekdays.Any(d=>d.IsChecked==true);if(!skip.IsEnabled)skip.IsChecked=false;}}
        string previousStart=start.Text;
        UIElement TimeInput(TextBox input,bool isEnd)
        {
            var row=new DockPanel();var choose=Button("选择",()=>{
                if(!ValidTime(input.Text,out var initial))initial=selected.Date.Add(WorkTime(isEnd));
                var choice=timePicker.Pick(isEnd?"结束时间":"开始时间",initial,isEnd,existing?.Timezone??"Asia/Shanghai",existing!=null);
                if(choice is not ScheduleTimeChoice picked)return;
                input.Text=picked.Value.ToString("yyyy-MM-dd HH:mm");
                if(!isEnd){if(existing==null)end.Text=ScheduleTime.SuggestedEnd(picked,WorkTime(true)).ToString("yyyy-MM-dd HH:mm");previousStart=start.Text;SyncWeekend();}
                FormDirty=true;
            });choose.Padding=new(10,8,10,8);choose.Margin=new(8,8,0,20);System.Windows.Automation.AutomationProperties.SetName(choose,isEnd?"选择结束日期和时间":"选择开始日期和时间");DockPanel.SetDock(choose,Dock.Right);row.Children.Add(choose);row.Children.Add(input);return row;
        }
        var times=new Grid();times.ColumnDefinitions.Add(new());times.ColumnDefinitions.Add(new(){Width=new(18)});times.ColumnDefinitions.Add(new());
        var a=new StackPanel();a.Children.Add(Text("开始时间"));a.Children.Add(TimeInput(start,false));times.Children.Add(a);var b=new StackPanel();b.Children.Add(Text("结束时间"));b.Children.Add(TimeInput(end,true));Grid.SetColumn(b,2);times.Children.Add(b);fields.Children.Add(times);
        var dateHint=new DockPanel{Margin=new(0,0,0,18)};DockPanel.SetDock(skip,Dock.Right);dateHint.Children.Add(skip);
        dateHint.Children.Add(Text($"只选日期使用 {WorkTime(false):hh\\:mm}–{WorkTime(true):hh\\:mm}；可在个人设置中修改。",12,"#64748B"));fields.Children.Add(dateHint);
        start.LostFocus+=(_,_)=>{if(ValidTime(start.Text,out var from)){if(existing==null&&start.Text!=previousStart)end.Text=from.AddHours(1).ToString("yyyy-MM-dd HH:mm");SyncWeekend();previousStart=start.Text;}};
        fields.Children.Add(workOnly);
        var recurring=new WrapPanel();foreach(var weekday in weekdays){recurring.Children.Add(weekday);weekday.Click+=(_,_)=>{SyncWeekend();FormDirty=true;};}
        fields.Children.Add(new Expander{Header=existing?.RepeatWeekdays.Count>0?"重复任务 · 编辑整个系列":"重复任务（可选）",Content=recurring,IsExpanded=existing?.RepeatWeekdays.Count>0,Margin=new(0,0,0,18)});
        var reminders=new WrapPanel();var offsets=new[]{10,60,1440};var checks=offsets.Select(x=>new CheckBox{Content=x==10?"提前10分钟":x==60?"提前1小时":"提前1天",IsChecked=existing?.ReminderOffsetsMinutes.Contains(x)??false,Margin=new(0,8,18,8)}).ToArray();foreach(var check in checks){reminders.Children.Add(check);check.Click+=(_,_)=>FormDirty=true;}
        fields.Children.Add(new Expander{Header="系统提醒（默认关闭）",Content=reminders,IsExpanded=existing?.ReminderOffsetsMinutes.Count>0,Margin=new(0,0,0,18)});
        fields.Children.Add(Text("应用运行或留在托盘时提醒；彻底退出后不提醒。系统勿扰设置可能影响显示。",12,"#64748B",new(0,0,0,18)));
        fields.Children.Add(Text("地点"));fields.Children.Add(location);fields.Children.Add(Text("地图链接（可选）"));fields.Children.Add(map);
        var links=new ReferenceEditor(existing?.ReferenceLinks??source?.ReferenceLinks??new(),()=>FormDirty=true);
        fields.Children.Add(new Expander{Header="参考资料（可选，最多20条）",Content=links,IsExpanded=links.HasLinks,Margin=new(0,0,0,18)});
        fields.Children.Add(Text("备注"));fields.Children.Add(note);
        foreach(var pair in new[]{(title,"事件标题"),(start,"开始时间"),(end,"结束时间"),(location,"地点"),(map,"地图链接"),(position,"应聘岗位"),(note,"备注"),(invitation,"会议邀请原文")}){System.Windows.Automation.AutomationProperties.SetName(pair.Item1,pair.Item2);pair.Item1.TextChanged+=(_,_)=>FormDirty=true;}
        skip.Click+=(_,_)=>FormDirty=true;workOnly.Click+=(_,_)=>FormDirty=true;SyncWeekend();
        var error=Text("",14,"#B42332");fields.Children.Add(error);Button? save=null;
        save=Button("保存日程",()=>Run(async()=>{
            if(string.IsNullOrWhiteSpace(title.Text)||!ValidTime(start.Text,out var from)||!ValidTime(end.Text,out var to)||to<=from){error.Text="请填写标题和有效的起止时间，格式为 yyyy-MM-dd HH:mm。";return;}
            if(type.SelectedIndex<0){error.Text="请选择事件类型。";return;}save!.IsEnabled=false;
            try{
                var references=links.Read();if(!string.IsNullOrWhiteSpace(map.Text))ReferenceEditor.ValidateUrl(map.Text.Trim());
                if(type.SelectedIndex is 1 or 2&&!string.IsNullOrWhiteSpace(meetingUrl.Text))ReferenceEditor.ValidateUrl(meetingUrl.Text.Trim());
                var repeat=weekdays.Select((d,i)=>(d,i)).Where(p=>p.d.IsChecked==true).Select(p=>p.i).ToArray();
                if(repeat.Length>0&&from.Date!=to.Date){error.Text="重复任务起止时刻必须在同一天。";return;}
                var zone=TimeZoneInfo.FindSystemTimeZoneById(existing?.Timezone??"Asia/Shanghai");
                var payload=new CalendarEventPayload{
                    Title=title.Text.Trim(),Type=types[type.SelectedIndex],
                    StartAt=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(from,DateTimeKind.Unspecified),zone)),
                    EndAt=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(to,DateTimeKind.Unspecified),zone)),
                    Timezone=zone.Id,SkipWeekends=skip.IsChecked==true,WorkdayOnly=workOnly.IsChecked==true,
                    RepeatWeekdays=repeat.ToList(),
                    ReminderOffsetsMinutes=checks.Select((d,i)=>(d,i)).Where(p=>p.d.IsChecked==true).Select(p=>offsets[p.i]).ToList(),
                    ReferenceLinks=references,LocationText=location.Text,
                    LocationUrl=string.IsNullOrWhiteSpace(map.Text)?null:map.Text.Trim(),Note=note.Text,
                    Position=type.SelectedIndex==1?position.Text:null,
                    Platform=type.SelectedIndex is 1 or 2?platform:null,MeetingUrl=type.SelectedIndex is 1 or 2&&!string.IsNullOrWhiteSpace(meetingUrl.Text)?meetingUrl.Text.Trim():null,RoomNumber=type.SelectedIndex is 1 or 2?room.Text.Trim():"",
                    Source=raw==null?"manual":"invitation",RawText=raw??"",SourceIdeaId=source?.Id};
                var entity=await Repository.SaveEventQueuedAsync(payload,existing?.Id);
                await Repository.FlushSyncAsync();
                var rejected=Repository.FailedSyncItem(entity);
                if(rejected?.Status==409){
                    if(!Confirm("时间冲突","这段时间与已有日程重叠：\n"+string.Join("\n",rejected.ConflictTitles),"仍然保存","返回修改")){save.IsEnabled=true;return;}
                    await Repository.ConfirmOverlapAsync(rejected);
                    rejected=Repository.FailedSyncItem(entity);
                }
                if(rejected!=null){error.Text=rejected.Error??"服务器拒绝了本次保存，可在本机状态条重试或放弃。";save.IsEnabled=true;return;}
                FormDirty=false;selected=from.Date;calendarView="day";ShowCalendar();
            }catch(Exception failure)when(WorkspaceRepository.Expected(failure)){error.Text=WorkspaceRepository.Explain(failure);}finally{save.IsEnabled=true;}
        }));fields.Children.Add(save);fields.Children.Add(Button("取消",()=>Navigate(source==null?ShowCalendar:()=>ShowIdeas())));
        Content.Children.Add(Surface(fields,new(26)));FormDirty=false;
    }


    internal void ShowEventDetail(EventData item, Window? fallbackOwner = null)
    {
        var body=new StackPanel{Margin=new(28)};body.Children.Add(Text(TypeLabel(item.Type),13,"#285DDD"));var title=Text(item.Title,26,margin:new(0,14,0,20));title.FontWeight=FontWeights.SemiBold;body.Children.Add(title);
        void Field(string label,string? value){body.Children.Add(Text(label,12,"#8492A6",new(0,14,0,5)));body.Children.Add(Text(string.IsNullOrWhiteSpace(value)?"未填写":value,15));}
        Field("时间",$"{item.Local(item.StartAt):yyyy-MM-dd HH:mm} — {item.Local(item.EndAt):yyyy-MM-dd HH:mm}");Field("地点",item.LocationText);
        if(item.MeetingUrl!=null)body.Children.Add(Button("点击入会",()=>OpenLink(item.MeetingUrl)));if(item.RoomNumber!=null)Field("会议号",item.RoomNumber);
        if(item.LocationUrl!=null)body.Children.Add(Button("查看地图",()=>OpenLink(item.LocationUrl)));
        if(item.ReferenceLinks.Count>0){Field("参考资料","");foreach(var link in item.ReferenceLinks)body.Children.Add(Button(string.IsNullOrWhiteSpace(link.Title)?new Uri(link.Url).Host:link.Title,()=>OpenLink(link.Url)));}
        Field("备注",item.Note);var dialog=new Window{Title="日程详情",Owner=Owner.IsVisible?Owner:fallbackOwner,Width=620,Height=580,MaxHeight=SystemParameters.WorkArea.Height-60,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Background=Brushes.White,FontFamily=FontFamily};
        var edit=Button("编辑日程",()=>{dialog.Close();RestoreMain();Navigate(()=>ShowEditor(item));});edit.IsEnabled=!item.Id.Contains('~');body.Children.Add(edit);
        if(item.RepeatWeekdays.Count>0){
            var series=Repository.Current?.Events.FirstOrDefault(e=>e.Id==item.Id.Split('~')[0]);
            var editSeries=Button("编辑整个重复系列",()=>{if(series!=null&&Confirm("编辑重复系列","修改将应用到整个系列；单日完成记录保留。","继续编辑","取消",dialog)){dialog.Close();Navigate(()=>ShowEditor(series));}});body.Children.Add(editSeries);
            var closeSeries=Button("结束重复系列",()=>{if(Confirm("结束重复系列","从明天起不再生成任务，历史记录保留。","结束系列","取消",dialog))Run(async()=>{await Repository.CloseRepeatQueuedAsync(item);dialog.Close();});});closeSeries.IsEnabled=!Repository.Busy;body.Children.Add(closeSeries);
        }
        if(item.RepeatWeekdays.Count==0){var remove=Button("删除日程",()=>{if(Confirm("删除日程","删除后无法恢复，是否删除这条日程？","删除日程","取消",dialog))Run(async()=>{await Repository.DeleteEventQueuedAsync(item);dialog.Close();});});remove.Foreground=Brush("#B42332");remove.IsEnabled=!Repository.Busy;body.Children.Add(remove);}
        body.Children.Add(Button("关闭",()=>dialog.Close()));dialog.ShowDialog();
    }
}
