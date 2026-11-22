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

namespace AwesomeInterview.NativeDemo;

// Widget view projection; authoritative state lives in WorkspaceRepository, never in this list.
public record DemoTask(string Title, DateTime Day, string Note, bool Completed = false, TimeSpan? StartTime = null, TimeSpan? EndTime = null, EventData? Event = null);
public partial class MainWindow : Window
{
    readonly string dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AwesomeInterviewNativeDemo", "demo-tasks.json");
    readonly WorkspaceRepository repository = new();
    List<DemoTask> tasks = new();
    readonly StackPanel content = new();
    ScrollViewer viewport=null!;
    Button adminNav=null!;
    readonly TextBlock heading = new() { FontSize=28, FontWeight=FontWeights.SemiBold, Margin=new(0,0,0,6) };
    readonly TextBlock status = new() { Foreground=Brush("#64748B"), TextWrapping=TextWrapping.Wrap, FontSize=12, VerticalAlignment=VerticalAlignment.Center };
    readonly List<Button> nav = new();
    readonly TextBlock connectionLabel = new() { Text="尚未登录", Foreground=Brush("#64748B"), FontSize=12, TextWrapping=TextWrapping.Wrap };
    readonly TextBlock subtitle = new() { Foreground=Brush("#64748B"), Margin=new(0,0,0,24) };
    DateTime selected = CalendarProjection.Now.Date;
    string page = "today", calendarView = "day";
    bool hideCompleted=true;

    public MainWindow()
    {
        Title="Awesome Interview · Windows 1.6.0-W Preview 3"; Width=1280; Height=850; MinWidth=940; MinHeight=640;
        UseLayoutRounding=true; SnapsToDevicePixels=true; TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this,TextRenderingMode.ClearType); WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=Brush("#F6F8FC"); Foreground=Brush("#192C46"); FontFamily=new("Microsoft YaHei UI"); FontSize=14;
        var shell=new Grid(); shell.ColumnDefinitions.Add(new(){Width=new(224)}); shell.ColumnDefinitions.Add(new()); Content=shell;
        var side=new DockPanel{Margin=new(18,26,18,20)};
        shell.Children.Add(new Border{Background=Brushes.White,BorderBrush=Brush("#E4EAF2"),BorderThickness=new(0,0,1,0),Child=side});
        var brand=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(8,0,0,30)};
        brand.Children.Add(new Image{Source=new BitmapImage(new Uri("pack://application:,,,/Assets/logo2.png")),Width=34,Height=34,Margin=new(0,0,10,0)});
        brand.Children.Add(new TextBlock{Text="Awesome\nInterview",FontSize=17,FontWeight=FontWeights.SemiBold}); DockPanel.SetDock(brand,Dock.Top); side.Children.Add(brand);
        var bottom=new StackPanel{Margin=new(12,18,8,0)}; bottom.Children.Add(connectionLabel);
        bottom.Children.Add(Button("账号与同步",()=>Navigate(ShowAccount))); DockPanel.SetDock(bottom,Dock.Bottom); side.Children.Add(bottom);
        var links=new StackPanel(); AddNav(links,"今日待办","\uE8FD",()=>ShowTasks(false)); AddNav(links,"日历与日程","\uE787",ShowCalendar);
        AddNav(links,"添加日程","\uE710",ShowAdd); AddNav(links,"我的面试","\uE8D4",ShowInterviews); AddNav(links,"面经广场","\uE774",ShowPlaza); AddNav(links,"我的面经","\uE8A5",ShowExperience);
        AddNav(links,"设置与版本","\uE713",ShowSettings);AddNav(links,"管理员监控","\uE9D9",ShowAdmin);adminNav=nav.Last();adminNav.Visibility=Visibility.Collapsed; side.Children.Add(links);
        var main=new DockPanel{Margin=new(32,28,32,18)}; Grid.SetColumn(main,1); shell.Children.Add(main);
        var top=new StackPanel();top.Children.Add(heading);top.Children.Add(subtitle);DockPanel.SetDock(top,Dock.Top);main.Children.Add(top);
        var foot=new DockPanel{Margin=new(0,14,0,0)}; var sync=Button("同步",()=>Run(repository.SynchronizeAsync));DockPanel.SetDock(sync,Dock.Right);foot.Children.Add(sync);foot.Children.Add(status);
        DockPanel.SetDock(foot,Dock.Bottom);main.Children.Add(foot);
        var scroller=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=false};viewport=scroller;
        var screens=new StackPanel{Orientation=Orientation.Horizontal};screens.Children.Add(Button("上一屏",()=>scroller.ScrollToVerticalOffset(Math.Max(0,scroller.VerticalOffset-scroller.ViewportHeight))));screens.Children.Add(Button("下一屏",()=>scroller.ScrollToVerticalOffset(scroller.VerticalOffset+scroller.ViewportHeight)));DockPanel.SetDock(screens,Dock.Bottom);main.Children.Add(screens);scroller.ScrollChanged+=(_,eventArgs)=>{if(eventArgs.OriginalSource==scroller)screens.Visibility=page!="calendar"&&scroller.ScrollableHeight>1?Visibility.Visible:Visibility.Collapsed;};
        scroller.ScrollChanged+=(_,args)=>{if(page=="plaza"&&args.VerticalChange>0&&scroller.ScrollableHeight-scroller.VerticalOffset<160&&!plazaBusy&&nextPlazaPage!=null&&repository.Online)Run(LoadPlazaAsync);};main.Children.Add(scroller);
        InitializeDesktopFeatures(); Closed+=(_,_)=>repository.Dispose();
        repository.Changed+=OnRepositoryChanged;
        Loaded+=(_,_)=>Run(repository.RestoreAsync);
        ShowTasks(false);
    }
    static SolidColorBrush Brush(string value)=>(SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
    static Button Button(string title, Action action)
    {
        var b=new Button{Content=title,HorizontalContentAlignment=HorizontalAlignment.Center};
        if(title.StartsWith("保存")||title.StartsWith("登录")||title.StartsWith("＋")){b.Background=Brush("#285DDD");b.Foreground=Brushes.White;b.BorderBrush=Brush("#285DDD");}
        b.Click+=(_,_)=>action(); return b;
    }
    void Navigate(Action action){if(CanLeaveForm())action();}
    void AddNav(Panel parent,string title,string icon,Action action)
    {
        var b=Button(title,()=>Navigate(action));b.Tag=title;b.Background=Brushes.Transparent;b.BorderBrush=Brushes.Transparent;
        System.Windows.Automation.AutomationProperties.SetName(b,title);
        b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Padding=new(14,12,14,12);b.Margin=new(0,3,0,3);
        var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(new TextBlock{Text=icon,FontFamily=new("Segoe MDL2 Assets"),FontSize=17,Width=30,VerticalAlignment=VerticalAlignment.Center});row.Children.Add(new TextBlock{Text=title});b.Content=row;
        nav.Add(b);parent.Children.Add(b);
    }
    void bSelect(string title){foreach(var b in nav){bool active=(string)b.Tag==title;b.Background=active?Brush("#E9F0FF"):Brushes.Transparent;b.Foreground=active?Brush("#2357CE"):Brush("#52647D");b.FontWeight=active?FontWeights.SemiBold:FontWeights.Normal;}}
    void Begin(string title,string description="")
    {
        if(viewport!=null){if(viewport.Content is Viewbox previousFit)previousFit.Child=null;viewport.Content=null;content.Width=double.NaN;content.HorizontalAlignment=HorizontalAlignment.Stretch;content.LayoutTransform=Transform.Identity;viewport.Content=content;viewport.ScrollToTop();}
        heading.Text=title;subtitle.Text=description;content.Children.Clear();var render=System.Diagnostics.Stopwatch.StartNew();var api=repository.Api;
        string node=page switch{"calendar" or "today"=>"calendar","editor"=>"schedule_new","plaza"=>"plaza","experiences"=>"experiences","note-editor" or "experience-detail"=>"editor","note-export"=>"export","interviews"=>"interviews","resume"=>"resume_file","ideas"=>"ideas","settings"=>"profile",_=>"mobile"};
        if(page!="admin")Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>{if(api==repository.Api)api?.RecordPage(node,render.Elapsed.TotalMilliseconds);}));
    }
    void Paragraph(string text){content.Children.Add(Text(text,14,"#64748B",new(0,0,0,16)));}
    static TextBlock Text(string text,double size=14,string color="#192C46",Thickness? margin=null)=>new(){Text=text,FontSize=size,Foreground=Brush(color),TextWrapping=TextWrapping.Wrap,Margin=margin??new Thickness(0)};
    static Border Surface(UIElement child,Thickness? padding=null)=>new(){Background=Brushes.White,BorderBrush=Brush("#E2E8F2"),BorderThickness=new(1),CornerRadius=new(10),Padding=padding??new Thickness(22),Margin=new(0,0,0,16),Child=child};
    async void Run(Func<Task> work)
    {
        try { await work(); }
        catch(Exception error) when(WorkspaceRepository.Expected(error)) { status.Text=WorkspaceRepository.Explain(error); }
    }
    void OnRepositoryChanged()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(OnRepositoryChanged);return;}
        adminNav.Visibility=repository.Online&&repository.Api?.Session?.IsAdmin==true?Visibility.Visible:Visibility.Collapsed;
        if(page=="admin"&&adminNav.Visibility!=Visibility.Visible){Begin("管理员监控","需要管理员身份并保持在线连接。");}
        var queued=repository.PendingSyncCount>0?$" · 待同步 {repository.PendingSyncCount} 项":"";
        status.Text=repository.Busy||repository.SyncBusy?"正在后台同步，仍可浏览当前页面…":repository.Message;
        connectionLabel.Text=repository.Current==null?"尚未登录":(repository.Online?"账号已连接":"离线 · 缓存可用")+queued;
        if(repository.Current?.Profile is ProfileData cachedProfile){profile=cachedProfile;profileScope=repository.Api?.Server.AbsoluteUri+repository.Current.UserId;}else if(repository.Current==null){profile=null;profileScope="";}
        ProjectTasks(CalendarProjection.Now.Date);RefreshPortable();
        Run(CheckRemindersAsync);
        if(formDirty)return;
        if(page=="today")ShowTasks(false); else if(page=="calendar")ShowCalendar(); else if(page=="calendar-day")ShowTasks(true); else if(page=="experiences")ShowExperience();else if(page=="ideas")ShowIdeas(showAbandoned);else if(page=="interviews")ShowInterviews();
    }
    List<EventData> EventsFor(DateTime day) => repository.Current is { } snapshot
        ? CalendarProjection.EventsForDay(snapshot.Events, day, WorkTime(false), WorkTime(true)).ToList()
        : new();
    void ProjectTasks(DateTime day)
    {
        tasks = EventsFor(day).Select(item =>
        {
            var segment = CalendarProjection.Segment(item, day, WorkTime(false), WorkTime(true))!.Value;
            return new DemoTask(item.Title, day, item.Note ?? "", item.CompletedAt != null,
                segment.Start - day.Date, segment.End - day.Date, item);
        }).ToList();
    }
    bool NeedAccount()
    {
        if(repository.Current!=null)return false;
        var empty=new StackPanel{Margin=new(12,20,12,20)};empty.Children.Add(Text("从你的账号开始",22));empty.Children.Add(Text("登录后，日程与面经会出现在这里。之后断网也能查看已缓存内容。",14,"#64748B",new(0,12,0,20)));
        empty.Children.Add(Button("登录账号",ShowAccount));content.Children.Add(Surface(empty));return true;
    }
    void ShowTasks(bool calendar)
    {
        page=calendar?"calendar-day":"today";if(!calendar)selected=CalendarProjection.Now.Date;bSelect(calendar?"日历与日程":"今日待办");
        Begin(calendar?selected.ToString("yyyy年M月d日 dddd"):"今日待办",calendar?"日程详情与完成状态":"把注意力留给今天要做的事");if(NeedAccount())return;
        var entries=EventsFor(selected);var pending=entries.Where(e=>e.CompletedAt==null&&e.CancelledAt==null).ToList();
        var toolbar=new DockPanel{Margin=new(0,0,0,18)};var add=Button("＋ 添加日程",()=>Navigate(ShowAdd));DockPanel.SetDock(add,Dock.Right);toolbar.Children.Add(add);
        toolbar.Children.Add(Text($"{selected:M月d日 dddd}    {pending.Count} 项未完成",18));content.Children.Add(toolbar);
        foreach(var item in pending)content.Children.Add(EventRow(item));
        if(pending.Count==0)content.Children.Add(Surface(Text("今天暂无待完成日程。",16,"#64748B")));
        AddTodos(selected);AddOverdue();AddSyncStatus();
        var done=new StackPanel();foreach(var item in entries.Where(e=>e.CompletedAt!=null))done.Children.Add(EventRow(item));
        content.Children.Add(new Expander{Header=$"今日已完成  {entries.Count(e=>e.CompletedAt!=null)}",Content=done,Margin=new(0,18,0,0)});
    }
    UIElement EventRow(EventData item)
    {
        var row=new DockPanel();var actions=new StackPanel{Orientation=Orientation.Horizontal};
        if(item.CancelledAt==null){var complete=Button(item.CompletedAt==null?"完成":"恢复",()=>Run(()=>repository.CompleteEventQueuedAsync(item)));complete.IsEnabled=!repository.Busy;actions.Children.Add(complete);}
        DockPanel.SetDock(actions,Dock.Right);row.Children.Add(actions);
        var detail=Button(item.Title,()=>ShowEventDetail(item));detail.HorizontalContentAlignment=HorizontalAlignment.Left;detail.BorderBrush=Brushes.Transparent;detail.Background=Brushes.Transparent;detail.Padding=new(0,0,12,0);
        var body=new StackPanel();var title=Text(item.Title,16,item.CompletedAt!=null?"#68778A":"#192C46");title.FontWeight=FontWeights.SemiBold;
        if(item.CompletedAt!=null)title.TextDecorations=TextDecorations.Strikethrough;body.Children.Add(title);body.Children.Add(Text($"{item.Local(item.StartAt):MM-dd HH:mm} — {item.Local(item.EndAt):MM-dd HH:mm}    {TypeLabel(item.Type)}",12,"#64748B",new(0,7,0,0)));detail.Content=body;row.Children.Add(detail);
        return Surface(row,new(18,14,14,14));
    }
    static string TypeLabel(string type)=>type switch{"personal"=>"个人事项","meeting"=>"普通会议","interview"=>"面试","exam"=>"笔试","course"=>"课程","recruiting"=>"招聘活动",_=>type};
}
