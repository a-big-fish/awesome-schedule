using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AwesomeInterview.NativeDemo;
public record DemoTask(string Title, DateTime Day, string Note, bool Completed = false);
public class MainWindow : Window {
    readonly string dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AwesomeInterviewNativeDemo", "demo-tasks.json");
    List<DemoTask> tasks = new();
    readonly StackPanel content = new();
    readonly TextBlock heading = new() { FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,8) };
    readonly TextBlock status = new() { Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap };
    readonly List<Button> nav = new();
    DateTime selected = DateTime.Today;
    public MainWindow() {
        Title = "Awesome Interview · Windows 原生预览 1.1-W"; Width=1180; Height=780; MinWidth=840; MinHeight=580;
        WindowStartupLocation=WindowStartupLocation.CenterScreen; Background=Brush("#F4F6FB"); FontFamily=new FontFamily("Microsoft YaHei UI"); FontSize=14;
        try { if(File.Exists(dataPath)) tasks=JsonSerializer.Deserialize<List<DemoTask>>(File.ReadAllText(dataPath)) ?? new(); }
        catch { status.Text="本地示例文件无法读取，已使用演示数据；保存后可重建。"; }
        if(!File.Exists(dataPath)) tasks=new() { new("准备项目介绍", DateTime.Today, "整理项目亮点、难点和个人贡献。"), new("模拟技术面试", DateTime.Today, "示例数据：复习数据库索引与并发控制。"), new("整理面试复盘", DateTime.Today.AddDays(1), "记录追问和待补充知识。") };
        var shell=new Grid(); shell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(230)}); shell.ColumnDefinitions.Add(new ColumnDefinition()); Content=shell;
        var sidebar=new StackPanel{Margin=new Thickness(18,28,18,18)};
        var sideBorder=new Border{Background=Brush("#172442"),Child=sidebar}; shell.Children.Add(sideBorder);
        sidebar.Children.Add(new TextBlock{Text="Awesome\nInterview",FontSize=25,FontWeight=FontWeights.Bold,Foreground=Brushes.White,Margin=new Thickness(12,0,0,12)});
        sidebar.Children.Add(new TextBlock{Text="WINDOWS NATIVE / 1.1-W",Foreground=Brush("#B2C3E7"),FontSize=11,Margin=new Thickness(12,0,0,30)});
        AddNav(sidebar,"今日待办",()=>ShowTasks(false)); AddNav(sidebar,"日历与日程",ShowCalendar); AddNav(sidebar,"添加日程",ShowAdd); AddNav(sidebar,"面经预览",ShowExperience); AddNav(sidebar,"设置与版本",ShowSettings);
        sidebar.Children.Add(new TextBlock{Text="本地交互 Demo\n未连接服务器\n数据与正式账号完全隔离".Replace("\n","\n"),Foreground=Brush("#B2C3E7"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12,40,8,0)});
        var main=new DockPanel{Margin=new Thickness(32)};Grid.SetColumn(main,1);shell.Children.Add(main);
        var top=new StackPanel();top.Children.Add(heading);top.Children.Add(new TextBlock{Text="原生控件 · 键盘操作 · 本机示例数据",Foreground=Brushes.SlateGray,Margin=new Thickness(0,0,0,20)});DockPanel.SetDock(top,Dock.Top);main.Children.Add(top);
        DockPanel.SetDock(status,Dock.Bottom);main.Children.Add(status);
        main.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        ShowTasks(false);
    }
    static SolidColorBrush Brush(string value)=>(SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
    void AddNav(Panel parent,string title,Action action){var b=Button(title,()=>{foreach(var item in nav)item.Background=Brushes.Transparent;bSelect(title);action();});b.Foreground=Brushes.White;b.Background=Brushes.Transparent;b.BorderThickness=new Thickness(0);b.HorizontalContentAlignment=HorizontalAlignment.Left;nav.Add(b);parent.Children.Add(b);}
    void bSelect(string title){foreach(var b in nav)b.Background=(string)b.Content==title?Brush("#3057A0"):Brushes.Transparent;}
    static Button Button(string title,Action action){var b=new Button{Content=title};b.Click+=(_,_)=>action();return b;}
    void Begin(string title){heading.Text=title;content.Children.Clear();}
    void Paragraph(string text){content.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,Margin=new Thickness(0,0,0,16)});}
    void ShowTasks(bool calendar){if(!calendar){selected=DateTime.Today;bSelect("今日待办");}Begin(calendar?$"{selected:yyyy-MM-dd} 的日程":"今天，专注下一件事");Paragraph($"{selected:MM月dd日 dddd}   ·   {tasks.Count(t=>t.Day.Date==selected.Date&&!t.Completed)} 项待办");
        var bar=new StackPanel{Orientation=Orientation.Horizontal};bar.Children.Add(Button("＋ 添加日程",ShowAdd));bar.Children.Add(Button("回到今天",()=>ShowTasks(false)));content.Children.Add(bar);
        foreach(var t in tasks.Where(t=>t.Day.Date==selected.Date).OrderBy(t=>t.Completed).ToList()){
            var row=new DockPanel{Margin=new Thickness(18)};var check=new CheckBox{Content=t.Completed?"恢复":"完成",IsChecked=t.Completed,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(20,0,0,0)};
            DockPanel.SetDock(check,Dock.Right);row.Children.Add(check);check.Click+=(_,_)=>{tasks[tasks.IndexOf(t)]=t with{Completed=!t.Completed};Save();ShowTasks(calendar);};
            var text=new StackPanel();text.Children.Add(new TextBlock{Text=t.Title,FontSize=18,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Foreground=t.Completed?Brushes.Gray:Brush("#172442")});text.Children.Add(new TextBlock{Text=t.Note,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,Margin=new Thickness(0,8,0,0)});row.Children.Add(text);
            content.Children.Add(new Border{Child=row,Background=Brushes.White,CornerRadius=new CornerRadius(12),Margin=new Thickness(0,8,0,8)});
        }
        if(!tasks.Any(t=>t.Day.Date==selected.Date))Paragraph("这一天没有安排，可添加一条本地示例日程。");
    }
    void ShowCalendar(){bSelect("日历与日程");Begin("日历与日程");var picker=new Calendar{SelectedDate=selected,HorizontalAlignment=HorizontalAlignment.Left};picker.SelectedDatesChanged+=(_,_)=>{if(picker.SelectedDate is DateTime day){selected=day;ShowTasks(true);}};content.Children.Add(picker);content.Children.Add(Button("查看所选日期日程",()=>ShowTasks(true)));Paragraph("选择日期查看日程；完整周/月时间轴将在后续版本接入。");}
    void ShowAdd(){bSelect("添加日程");Begin("添加日程");Paragraph("本轮为本机演示模式，保存不会上传正式服务。");content.Children.Add(new TextBlock{Text="事件标题"});var title=new TextBox{MaxLength=200};content.Children.Add(title);content.Children.Add(new TextBlock{Text="日期"});var date=new DatePicker{SelectedDate=selected,Margin=new Thickness(0,8,0,18)};content.Children.Add(date);content.Children.Add(new TextBlock{Text="备注"});var note=new TextBox{AcceptsReturn=true,Height=110,TextWrapping=TextWrapping.Wrap,MaxLength=2000};content.Children.Add(note);var error=new TextBlock{Foreground=Brushes.Firebrick,TextWrapping=TextWrapping.Wrap};content.Children.Add(error);
        content.Children.Add(Button("保存本地日程",()=>{if(string.IsNullOrWhiteSpace(title.Text)||date.SelectedDate==null){error.Text="请填写标题并选择有效日期。";return;}tasks.Add(new(title.Text.Trim(),date.SelectedDate.Value,note.Text.Trim()));if(Save()){selected=date.SelectedDate.Value;ShowTasks(true);}}));title.Focus();}
    bool Save(){try{Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);var temp=dataPath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(tasks));File.Move(temp,dataPath,true);status.Text="示例数据已保存至当前 Windows 用户目录。";return true;}catch{status.Text="保存失败：请检查磁盘空间与目录权限。";return false;}}
    void ShowExperience(){bSelect("面经预览");Begin("结构化面经");Paragraph("以下为合成示例，尚未接入面经广场或真实账号。");foreach(var line in new[]{"示例公司 · 后端开发 · 二面","项目问题（20 min）","Q1 介绍项目中的技术难点","Q1.1 如何验证方案的可靠性？","八股问题（15 min）","Q2 数据库索引如何工作？","反问（5 min）","Q3 团队如何开展代码评审？"})content.Children.Add(new TextBlock{Text=line,TextWrapping=TextWrapping.Wrap,FontSize=line.StartsWith("Q")?16:20,Margin=new Thickness(0,12,0,12)});}
    void ShowSettings(){bSelect("设置与版本");Begin("设置与版本");Paragraph("Awesome Interview Native Demo 1.1.0-W\n技术：C# / WPF / .NET 10；业务界面无 WebView。\n演示数据："+dataPath);Paragraph("正式版本将使用现有账号登录；先展示账号隔离缓存，再异步同步。服务器不可达时只读缓存。\n系统通知、真实登录、服务端同步和面经导出尚未实现。" );}
}
