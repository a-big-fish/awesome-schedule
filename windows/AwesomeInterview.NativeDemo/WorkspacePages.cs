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

namespace AwesomeInterview.NativeDemo;

public partial class MainWindow
{
    void ShowAccount()=>ShowAccount(false);
    void ShowAccount(bool prompt)
    {
        page="account";bSelect("");Begin("账号与同步","使用与 Web 相同的账号，密码仅用于本次登录");
        if(repository.Current!=null&&!prompt)
        {
            Paragraph(repository.Online?"账号已连接，切换页面和日期直接使用本机数据。":"当前为离线只读；连接恢复后点击同步获取最新内容。");
            Paragraph($"缓存更新：{repository.Current.ServerTime.LocalDateTime:yyyy-MM-dd HH:mm}\n服务地址：{repository.Api?.Server}");
            content.Children.Add(Button("同步最新数据",()=>Run(repository.SynchronizeAsync)));
            content.Children.Add(Button("重新登录",()=>ShowAccount(true)));
            content.Children.Add(Button("退出账号并清除缓存",()=>Run(async()=>{await repository.LogoutAsync();ShowAccount();})));return;
        }
        var body=new StackPanel{MaxWidth=490,HorizontalAlignment=HorizontalAlignment.Left};
        body.Children.Add(Text("服务地址"));var server=new TextBox{Text="https://schedule.hunaochufang.xyz/",MinWidth=400};body.Children.Add(server);
        body.Children.Add(Text("邮箱或用户名"));var name=new TextBox();body.Children.Add(name);
        body.Children.Add(Text("密码"));var password=new PasswordBox{Padding=new(12),Margin=new(0,8,0,20),FontSize=16};body.Children.Add(password);
        System.Windows.Automation.AutomationProperties.SetName(server,"服务地址");System.Windows.Automation.AutomationProperties.SetName(name,"邮箱或用户名");System.Windows.Automation.AutomationProperties.SetName(password,"密码");
        var feedback=Text("",14,"#B42332");body.Children.Add(feedback);
        Button? login=null;login=Button("登录并同步",()=>Run(async()=>{
            login!.IsEnabled=false;feedback.Text="正在连接…";
            string secret=password.Password;password.Clear();
            try{await repository.LoginAsync(server.Text,name.Text,secret);ShowTasks(false);}
            catch(Exception error) when(WorkspaceRepository.Expected(error)){feedback.Text=WorkspaceRepository.Explain(error);}
            finally{login.IsEnabled=true;}
        }));body.Children.Add(login);content.Children.Add(Surface(body,new(28)));
    }

    bool focusCurrentTime=true;
    void ShowCalendar()
    {
        page="calendar";bSelect("日历与日程");Begin("日历与日程","用日、周、月视图查看你的安排");if(NeedAccount())return;
        var controls=new WrapPanel{Margin=new(0,0,0,14)};
        foreach(var (key,caption) in new[]{("day","日视图"),("week","周视图"),("month","月视图")})
        {var b=Button(caption,()=>{calendarView=key;ShowCalendar();});if(key==calendarView){b.Background=Brush("#E9F0FF");b.Foreground=Brush("#2357CE");}controls.Children.Add(b);}
        controls.Children.Add(Button(focusCurrentTime?"查看24小时概览":"聚焦当前时间",()=>{focusCurrentTime=!focusCurrentTime;ShowCalendar();}));content.Children.Add(controls);
        var datebar=new Grid{Margin=new(0,0,0,16)};
        datebar.ColumnDefinitions.Add(new(){Width=new(44)});datebar.ColumnDefinitions.Add(new());datebar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});datebar.ColumnDefinitions.Add(new(){Width=new(44)});
        void Shift(int direction){selected=calendarView=="month"?selected.AddMonths(direction):selected.AddDays(direction*(calendarView=="week"?7:1));ShowCalendar();}
        Button Arrow(string glyph,string name,Action action){var button=Button("",action);button.Content=new TextBlock{Text=glyph,FontFamily=new("Segoe MDL2 Assets"),FontSize=16};button.Padding=new(8);button.Margin=new(0);System.Windows.Automation.AutomationProperties.SetName(button,name);return button;}
        datebar.Children.Add(Arrow("\uE76B","上一日期",()=>Shift(-1)));var next=Arrow("\uE76C","下一日期",()=>Shift(1));Grid.SetColumn(next,3);datebar.Children.Add(next);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(10,0,10,0)};actions.Children.Add(Button("回到今天",()=>{selected=DateTime.Today;ShowCalendar();}));actions.Children.Add(Button(hideCompleted?"切换全部日程":"切换未完成日程",()=>{hideCompleted=!hideCompleted;ShowCalendar();}));Grid.SetColumn(actions,2);datebar.Children.Add(actions);
        var first=calendarView=="week"?selected.Date.AddDays(-(((int)selected.DayOfWeek+6)%7)):selected.Date;
        var label=Text(calendarView=="month"?selected.ToString("yyyy年M月"):calendarView=="week"?$"{first:yyyy-MM-dd} — {first.AddDays(6):MM-dd}":selected.ToString("yyyy-MM-dd dddd"),19);
        label.FontWeight=FontWeights.SemiBold;label.HorizontalAlignment=HorizontalAlignment.Center;label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new(0,0,0,12);content.Children.Add(label);content.Children.Add(datebar);
        if(calendarView=="month")RenderMonth();else RenderTimeline(first,calendarView=="week"?7:1);
        content.HorizontalAlignment=HorizontalAlignment.Center;
        void ResizeCalendar(){content.Width=Math.Max(560,viewport.ActualWidth*.82);}
        ResizeCalendar();SizeChangedEventHandler resize=(_,_)=>ResizeCalendar();viewport.SizeChanged+=resize;content.Unloaded+=(_,_)=>viewport.SizeChanged-=resize;

    }
    void RenderMonth()
    {
        var grid=new Grid{MinWidth=770};for(int c=0;c<7;c++)grid.ColumnDefinitions.Add(new());for(int r=0;r<7;r++)grid.RowDefinitions.Add(new(){Height=r==0?new(36):new GridLength(1,GridUnitType.Star)});
        var first=new DateTime(selected.Year,selected.Month,1);first=first.AddDays(-(((int)first.DayOfWeek+6)%7));
        for(int c=0;c<7;c++){var title=Text(new[]{"周一","周二","周三","周四","周五","周六","周日"}[c],12,"#64748B");title.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetColumn(title,c);grid.Children.Add(title);}
        for(int i=0;i<42;i++)
        {
            var day=first.AddDays(i);var body=new StackPanel{Margin=new(5)};var date=Button(day.Day.ToString(),()=>{selected=day;calendarView="day";ShowCalendar();});date.Padding=new(6,3,6,3);date.MinHeight=26;date.HorizontalAlignment=HorizontalAlignment.Left;date.Margin=new(0,0,0,4);date.BorderThickness=new(0);date.Background=day==DateTime.Today?Brush("#285DDD"):Brushes.Transparent;date.Foreground=day==DateTime.Today?Brushes.White:Brush(day.Month==selected.Month?"#192C46":"#94A0B2");body.Children.Add(date);
            foreach(var item in EventsFor(day).Where(e=>!hideCompleted||e.CompletedAt==null))
            {
                string text=(item.Local(item.StartAt).Date<day?"‹ ":"")+item.Title+(item.Local(item.EndAt)>day.AddDays(1)?" ›":"");
                var entry=Button(text,()=>ShowEventDetail(item));entry.MinHeight=34;entry.Padding=new(6,5,6,5);entry.FontSize=14;entry.Margin=new(0,1,0,1);entry.HorizontalContentAlignment=HorizontalAlignment.Stretch;entry.VerticalContentAlignment=VerticalAlignment.Center;
                entry.Content=new TextBlock{Text=text,TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.Medium};entry.ToolTip=$"{item.Title}\n{item.Local(item.StartAt):MM-dd HH:mm} — {item.Local(item.EndAt):MM-dd HH:mm}";
                entry.Background=Brush(item.CompletedAt!=null?"#EEF0F3":item.CancelledAt!=null?"#F5E7E7":"#EAF1FF");entry.BorderThickness=new(0);body.Children.Add(entry);
            }
            var cell=new Border{BorderBrush=Brush("#E4EAF2"),BorderThickness=new(0,0,1,1),MinHeight=92,Child=body};Grid.SetColumn(cell,i%7);Grid.SetRow(cell,i/7+1);grid.Children.Add(cell);
        }
        var monthScroll=new ScrollViewer{Content=grid,Height=Math.Max(260,viewport.ActualHeight-220),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        SizeChangedEventHandler monthResize=(_,_)=>monthScroll.Height=Math.Max(260,viewport.ActualHeight-220);viewport.SizeChanged+=monthResize;monthScroll.Unloaded+=(_,_)=>viewport.SizeChanged-=monthResize;content.Children.Add(Surface(monthScroll,new(6)));
    }
    void RenderTimeline(DateTime first,int days)
    {
        var nowClock=TimeZoneInfo.ConvertTime(DateTimeOffset.Now,TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).DateTime;
        bool focus=focusCurrentTime&&nowClock.Date>=first.Date&&nowClock.Date<first.AddDays(days).Date;
        double span=24;double startHour=0;double timelineHeight=focus?1920:Math.Max(340,viewport.ActualHeight-230);var refreshLayout=new List<Action>();Action refreshTitles=()=>{};
        var grid=new Grid{MinWidth=days==7?560:420};grid.ColumnDefinitions.Add(new(){Width=new(52)});for(int c=0;c<days;c++)grid.ColumnDefinitions.Add(new());
        var hours=new Canvas{Height=timelineHeight,Margin=new(0,38,0,0)};for(int h=0;h<=span;h++){var tick=first.AddHours(startHour+h);var t=Text(tick.ToString("HH:mm")+(tick.Date<first.Date?" 前日":tick.Date>first.Date?" 次日":""),10,"#8492A6");Canvas.SetTop(t,h*timelineHeight/span);hours.Children.Add(t);}grid.Children.Add(hours);refreshLayout.Add(()=>{int index=0;foreach(var label in hours.Children.OfType<TextBlock>()){var tick=first.AddHours(startHour+index++);label.Text=tick.ToString("HH:mm")+(tick.Date<first.Date?" 前日":tick.Date>first.Date?" 次日":"");}});
        for(int c=0;c<days;c++)
        {
            var day=first.AddDays(c);var column=new DockPanel();Grid.SetColumn(column,c+1);grid.Children.Add(column);
            var title=Text(day.ToString("ddd M/d"),12,day==DateTime.Today?"#285DDD":"#64748B",new(4,8,4,8));title.HorizontalAlignment=HorizontalAlignment.Center;DockPanel.SetDock(title,Dock.Top);column.Children.Add(title);
            var canvas=new Canvas{Height=timelineHeight,ClipToBounds=true,Background=Brushes.White};column.Children.Add(canvas);
            for(int h=0;h<=span;h++){var line=new Border{Height=1,Background=Brush("#EBEFF6")};Canvas.SetTop(line,h*timelineHeight/span);canvas.Children.Add(line);line.SetBinding(WidthProperty,new System.Windows.Data.Binding("ActualWidth"){Source=canvas});}
            var items=EventsFor(day).Where(e=>!hideCompleted||e.CompletedAt==null).ToList();
            // Recalculate lanes from visible entries, so hidden completed events never leave empty columns.
            var laneEnds=new List<DateTime>();var placed=new List<(EventData Item,int Lane,DateTime Start,DateTime End)>();
            foreach(var item in items){var from=item.Local(item.StartAt)<day?day:item.Local(item.StartAt);var to=item.Local(item.EndAt)>day.AddDays(1)?day.AddDays(1):item.Local(item.EndAt);int lane=laneEnds.FindIndex(e=>e<=from);if(lane<0){lane=laneEnds.Count;laneEnds.Add(to);}else laneEnds[lane]=to;placed.Add((item,lane,from,to));}
            void Layout(){foreach(var child in canvas.Children.OfType<Button>().ToList())canvas.Children.Remove(child);foreach(var row in placed){var item=row.Item;var b=Button(item.Title,()=>ShowEventDetail(item));b.Content=new TextBlock{Text=(item.Local(item.StartAt).Date<day?"‹ ":"")+item.Title+(item.Local(item.EndAt)>day.AddDays(1)?" ›":""),TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.Medium};b.ToolTip=$"{item.Title}\n{item.Local(item.StartAt):MM-dd HH:mm} — {item.Local(item.EndAt):MM-dd HH:mm}";b.Margin=new(0);b.Padding=new(5);b.MinHeight=0;b.VerticalContentAlignment=VerticalAlignment.Stretch;b.HorizontalContentAlignment=HorizontalAlignment.Stretch;b.Width=Math.Max(16,(canvas.ActualWidth-4)/Math.Max(1,laneEnds.Count)-3);b.Height=Math.Max(30,(row.End-row.Start).TotalHours*timelineHeight/span);b.Background=Brush(item.CompletedAt!=null?"#EEF0F3":item.CancelledAt!=null?"#F5E7E7":"#E9F0FF");b.FontSize=16;Canvas.SetTop(b,((row.Start-day).TotalHours-startHour)*timelineHeight/span);Canvas.SetLeft(b,row.Lane*(canvas.ActualWidth-4)/Math.Max(1,laneEnds.Count)+2);canvas.Children.Add(b);}refreshTitles();}
            canvas.SizeChanged+=(_,_)=>Layout();refreshLayout.Add(Layout);
        }
        var clockLayer=new Canvas{IsHitTestVisible=false,Margin=new(52,38,0,0),ClipToBounds=true};
        Grid.SetColumn(clockLayer,1);Grid.SetColumnSpan(clockLayer,days);clockLayer.Margin=new(0,38,0,0);Panel.SetZIndex(clockLayer,10);grid.Children.Add(clockLayer);
        void UpdateClock(){clockLayer.Children.Clear();var now=TimeZoneInfo.ConvertTime(DateTimeOffset.Now,TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).DateTime;if(now.Date<first.Date||now.Date>=first.AddDays(days).Date)return;double y=(now.TimeOfDay.TotalHours-startHour)*timelineHeight/span;var line=new Border{Background=Brush("#E5484D"),Height=2,Width=clockLayer.ActualWidth};Canvas.SetTop(line,y);clockLayer.Children.Add(line);var tag=new Border{Background=Brush("#E5484D"),CornerRadius=new(4),Padding=new(5,2,5,2),Child=Text($"当前时间 {now:HH:mm}",11,"#FFFFFF")};Canvas.SetTop(tag,Math.Clamp(y-21,0,timelineHeight-25));tag.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));Canvas.SetLeft(tag,Math.Max(0,(clockLayer.ActualWidth-tag.DesiredSize.Width)/2));clockLayer.Children.Add(tag);}
        var clock=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMinutes(1)};clock.Tick+=(_,_)=>UpdateClock();clockLayer.Loaded+=(_,_)=>{UpdateClock();clock.Start();};clockLayer.Unloaded+=(_,_)=>clock.Stop();clockLayer.SizeChanged+=(_,_)=>UpdateClock();
        var timelineScroll=new ScrollViewer{Content=grid,Height=Math.Max(260,viewport.ActualHeight-220),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=false};
        bool userMoved=false;timelineScroll.PreviewMouseWheel+=(_,_)=>userMoved=true;timelineScroll.PreviewMouseDown+=(_,_)=>userMoved=true;timelineScroll.PreviewKeyDown+=(_,_)=>userMoved=true;
        void CenterNow(){if(!focus||userMoved)return;timelineScroll.ScrollToVerticalOffset(Math.Max(0,38+TimeZoneInfo.ConvertTime(DateTimeOffset.Now,TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).TimeOfDay.TotalHours*timelineHeight/24-timelineScroll.ViewportHeight/2));}
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
                title.Margin=new(0,visibleStart,0,innerHeight-visibleEnd);
            }
        }
        refreshTitles=StickyTitles;timelineScroll.SizeChanged+=(_,_)=>StickyTitles();timelineScroll.ScrollChanged+=(_,_)=>StickyTitles();timelineScroll.Loaded+=(_,_)=>{CenterNow();StickyTitles();};SizeChangedEventHandler resizeTimeline=(_,_)=>{timelineScroll.Height=Math.Max(260,viewport.ActualHeight-220);CenterNow();};viewport.SizeChanged+=resizeTimeline;timelineScroll.Unloaded+=(_,_)=>viewport.SizeChanged-=resizeTimeline;
        content.Children.Add(Surface(timelineScroll,new(8)));
    }

    void ShowAdd()=>Run(async()=>{if(repository.Online){try{await LoadProfileAsync();}catch(Exception e)when(WorkspaceRepository.Expected(e)){status.Text=WorkspaceRepository.Explain(e);}}ShowEditor(null);});
    TimeSpan WorkTime(bool end)=>TimeSpan.TryParse(end?profile?.WorkdayEnd:profile?.WorkdayStart,out var value)?value:TimeSpan.FromHours(end?18:8);
    void ShowEditor(EventData? existing)
    {
        page="editor";bSelect("添加日程");Begin(existing==null?"添加日程":"编辑日程","时间、地点与参考资料，一次整理清楚");if(NeedAccount())return;
        if(!repository.Online){Paragraph("离线状态不能新增或编辑，请先同步连接。");return;}
        if(existing?.RepeatWeekdays.Count>0){Paragraph("重复日程的整组编辑规则仍待适配，请暂时在 Web 中修改。单日完成与恢复已支持。");return;}
        var fields=new StackPanel{MaxWidth=800,HorizontalAlignment=HorizontalAlignment.Stretch};
        var types=new[]{"personal","interview","meeting","exam","course","recruiting"};var type=new ComboBox{ItemsSource=types.Select(TypeLabel),SelectedIndex=existing==null?0:Array.IndexOf(types,existing.Type),Padding=new(10),Margin=new(0,8,0,18)};
        fields.Children.Add(Text("类型"));fields.Children.Add(type);
        var invitation=new TextBox{AcceptsReturn=true,Height=94,TextWrapping=TextWrapping.Wrap};var invitationBox=new StackPanel();invitationBox.Children.Add(Text("粘贴会议邀请"));invitationBox.Children.Add(invitation);
        string? raw=null;var title=new TextBox{Text=existing?.Title??"",MaxLength=200};
        var start=new TextBox{Text=(existing?.Local(existing.StartAt)??selected.Date.Add(WorkTime(false))).ToString("yyyy-MM-dd HH:mm")};
        var end=new TextBox{Text=(existing?.Local(existing.EndAt)??selected.Date.Add(WorkTime(true))).ToString("yyyy-MM-dd HH:mm")};
        var location=new TextBox{Text=existing?.LocationText??""};var note=new TextBox{Text=existing?.Note??"",Height=100,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};
        foreach(var pair in new[]{(title,"事件标题"),(start,"开始时间 yyyy-MM-dd HH:mm"),(end,"结束时间 yyyy-MM-dd HH:mm"),(location,"地点"),(note,"备注"),(invitation,"会议邀请原文")})System.Windows.Automation.AutomationProperties.SetName(pair.Item1,pair.Item2);
        invitationBox.Children.Add(Button("解析并填入",()=>Run(async()=>{var parsed=await repository.Api!.WriteAsync<EventData>(HttpMethod.Post,"api/mobile/v1/events/parse",new{raw_text=invitation.Text});raw=invitation.Text;title.Text=parsed.Title;start.Text=parsed.Local(parsed.StartAt).ToString("yyyy-MM-dd HH:mm");end.Text=parsed.Local(parsed.EndAt).ToString("yyyy-MM-dd HH:mm");formDirty=true;})));
        fields.Children.Add(invitationBox);void UpdateInvitationVisibility(){invitationBox.Visibility=existing==null&&type.SelectedIndex is 1 or 2?Visibility.Visible:Visibility.Collapsed;}UpdateInvitationVisibility();type.SelectionChanged+=(_,_)=>{UpdateInvitationVisibility();formDirty=true;};
        fields.Children.Add(Text("标题"));fields.Children.Add(title);
        var skip=new CheckBox{Content="任务跳过周末",IsChecked=existing?.SkipWeekends??selected.DayOfWeek is not(DayOfWeek.Saturday or DayOfWeek.Sunday),Margin=new(0,0,0,20)};
        bool ValidTime(string text,out DateTime value)=>DateTime.TryParseExact(text,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out value);
        void SyncWeekend(){if(ValidTime(start.Text,out var value)){bool weekend=value.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;skip.IsEnabled=!weekend;if(weekend)skip.IsChecked=false;}}
        string previousStart=start.Text;
        UIElement TimeInput(TextBox input,bool isEnd)
        {
            var row=new DockPanel();var choose=Button("选择",()=>{
                if(!ValidTime(input.Text,out var initial))initial=selected.Date.Add(WorkTime(isEnd));
                var choice=PickScheduleTime(isEnd?"结束时间":"开始时间",initial,isEnd,existing?.Timezone??"Asia/Shanghai");
                if(choice is not ScheduleTimeChoice picked)return;
                input.Text=picked.Value.ToString("yyyy-MM-dd HH:mm");
                if(!isEnd){if(existing==null)end.Text=ScheduleTime.SuggestedEnd(picked,WorkTime(true)).ToString("yyyy-MM-dd HH:mm");previousStart=start.Text;SyncWeekend();}
                formDirty=true;
            });choose.Padding=new(10,8,10,8);choose.Margin=new(8,8,0,20);System.Windows.Automation.AutomationProperties.SetName(choose,isEnd?"选择结束日期和时间":"选择开始日期和时间");DockPanel.SetDock(choose,Dock.Right);row.Children.Add(choose);row.Children.Add(input);return row;
        }
        var times=new Grid();times.ColumnDefinitions.Add(new());times.ColumnDefinitions.Add(new(){Width=new(18)});times.ColumnDefinitions.Add(new());
        var a=new StackPanel();a.Children.Add(Text("开始时间"));a.Children.Add(TimeInput(start,false));times.Children.Add(a);var b=new StackPanel();b.Children.Add(Text("结束时间"));b.Children.Add(TimeInput(end,true));Grid.SetColumn(b,2);times.Children.Add(b);fields.Children.Add(times);fields.Children.Add(skip);SyncWeekend();
        start.LostFocus+=(_,_)=>{if(ValidTime(start.Text,out var from)){if(existing==null&&start.Text!=previousStart)end.Text=from.AddHours(1).ToString("yyyy-MM-dd HH:mm");SyncWeekend();previousStart=start.Text;}};
        fields.Children.Add(Text("地点"));fields.Children.Add(location);fields.Children.Add(Text("备注"));fields.Children.Add(note);
        Paragraph($"只选日期时使用默认工作时段 {WorkTime(false):hh\\:mm}–{WorkTime(true):hh\\:mm}；也可指定时分或使用当前时间。默认时段可在日程偏好中修改。");
        foreach(var input in new[]{title,start,end,location,note,invitation})input.TextChanged+=(_,_)=>formDirty=true;skip.Click+=(_,_)=>formDirty=true;
        var error=Text("",14,"#B42332");fields.Children.Add(error);Button? save=null;
        save=Button("保存日程",()=>Run(async()=>{
            if(string.IsNullOrWhiteSpace(title.Text)||!ValidTime(start.Text,out var from)||!ValidTime(end.Text,out var to)||to<=from){error.Text="请填写标题和有效的起止时间，格式为 yyyy-MM-dd HH:mm。";return;}
            if(type.SelectedIndex<0){error.Text="请选择事件类型。";return;}save!.IsEnabled=false;
            try{var zone=TimeZoneInfo.FindSystemTimeZoneById(existing?.Timezone??"Asia/Shanghai");object Payload(bool confirm)=>new {title=title.Text.Trim(),event_type=types[type.SelectedIndex],start_at=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(from,DateTimeKind.Unspecified),zone)),end_at=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(to,DateTimeKind.Unspecified),zone)),timezone=zone.Id,location_text=location.Text,note=note.Text,skip_weekends=skip.IsChecked==true,reference_links=existing?.ReferenceLinks??new List<ReferenceLink>(),location_url=existing?.LocationUrl,position=existing?.Position,reminder_offsets_minutes=existing?.ReminderOffsetsMinutes??new List<int>(),raw_invitation=raw,confirm_overlap=confirm};
                try{await repository.SaveEventAsync(Payload(false),existing?.Id);}catch(ApiFailure failure) when(failure.Status==409){if(!Confirm("时间冲突","这段时间与已有日程重叠：\n"+string.Join("\n",failure.ConflictTitles),"仍然保存","返回修改"))return;await repository.SaveEventAsync(Payload(true),existing?.Id);}
                formDirty=false;selected=from.Date;calendarView="day";ShowCalendar();
            }catch(Exception failure) when(WorkspaceRepository.Expected(failure)){error.Text=WorkspaceRepository.Explain(failure);}finally{save.IsEnabled=true;}
        }));fields.Children.Add(save);content.Children.Add(Surface(fields,new(26)));formDirty=false;
    }

    void ShowEventDetail(EventData item)
    {
        var body=new StackPanel{Margin=new(28)};body.Children.Add(Text(TypeLabel(item.Type),13,"#285DDD"));var title=Text(item.Title,26,margin:new(0,14,0,20));title.FontWeight=FontWeights.SemiBold;body.Children.Add(title);
        void Field(string label,string? value){body.Children.Add(Text(label,12,"#8492A6",new(0,14,0,5)));body.Children.Add(Text(string.IsNullOrWhiteSpace(value)?"未填写":value,15));}
        Field("时间",$"{item.Local(item.StartAt):yyyy-MM-dd HH:mm} — {item.Local(item.EndAt):yyyy-MM-dd HH:mm}");Field("地点",item.LocationText);
        if(item.MeetingUrl!=null)body.Children.Add(Button("点击入会",()=>OpenLink(item.MeetingUrl)));if(item.RoomNumber!=null)Field("会议号",item.RoomNumber);
        if(item.LocationUrl!=null)body.Children.Add(Button("查看地图",()=>OpenLink(item.LocationUrl)));
        if(item.ReferenceLinks.Count>0){Field("参考资料","");foreach(var link in item.ReferenceLinks)body.Children.Add(Button(string.IsNullOrWhiteSpace(link.Title)?new Uri(link.Url).Host:link.Title,()=>OpenLink(link.Url)));}
        Field("备注",item.Note);var dialog=new Window{Title="日程详情",Owner=IsVisible?this:portable,Width=620,Height=580,MaxHeight=SystemParameters.WorkArea.Height-60,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Background=Brushes.White,FontFamily=FontFamily};
        var edit=Button("编辑日程",()=>{dialog.Close();RestoreMain();Navigate(()=>ShowEditor(item));});edit.IsEnabled=repository.Online&&!item.Id.Contains('~');body.Children.Add(edit);
        if(item.RepeatWeekdays.Count==0){var remove=Button("删除日程",()=>{if(Confirm("删除日程","删除后无法恢复，是否删除这条日程？","删除日程","取消",dialog))Run(async()=>{await repository.DeleteEventAsync(item);dialog.Close();});});remove.Foreground=Brush("#B42332");remove.IsEnabled=repository.Online&&!repository.Busy;body.Children.Add(remove);}
        body.Children.Add(Button("关闭",()=>dialog.Close()));dialog.ShowDialog();
    }
    void OpenLink(string value)
    {
        if(Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http")
            try{Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){status.Text="无法打开系统浏览器。";}
    }
}
