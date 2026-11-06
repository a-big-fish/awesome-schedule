using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace AwesomeInterview.NativeDemo;

public record DesktopPreferences(bool CloseToTray = true, bool TrayHintShown = false, string DemoName = "演示用户");

public partial class MainWindow
{
    DesktopPreferences preferences = new();
    Forms.NotifyIcon? tray;
    Forms.ToolStripMenuItem? portableMenu;
    Window? portable;
    StackPanel? portableContent;
    readonly DispatcherTimer dayTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    DateTime portableDay = DateTime.Today;
    bool exiting, formDirty;
    string PreferencesPath => Path.Combine(Path.GetDirectoryName(dataPath)!, "desktop-settings.json");

    void InitializeDesktopFeatures()
    {
        try { if (File.Exists(PreferencesPath)) preferences = JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(PreferencesPath)) ?? new(); }
        catch { status.Text = "窗口设置读取失败，使用默认值。"; }
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开主窗口", null, (_, _) => Dispatcher.Invoke(RestoreMain));
        portableMenu = new Forms.ToolStripMenuItem("开启随身日程");
        portableMenu.Click += (_, _) => Dispatcher.Invoke(TogglePortable);
        menu.Items.Add(portableMenu);
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(() => { RestoreMain(); ShowSettings(); }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出应用", null, (_, _) => Dispatcher.Invoke(() => { if (CanLeaveForm()) { exiting = true; Close(); } }));
        tray = new Forms.NotifyIcon { Text = "Awesome Interview", ContextMenuStrip = menu,
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!), Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreMain);
        Closing += (_, args) => {
            if (!exiting && !CanLeaveForm()) { args.Cancel = true; return; }
            if (!exiting && preferences.CloseToTray) {
                args.Cancel = true;
                if (!preferences.TrayHintShown) {
                    Confirm("关闭到系统托盘","关闭后应用将在系统托盘继续运行。双击托盘图标可重新打开，右键可退出；可在设置中关闭此行为。","知道了","返回");
                    preferences = preferences with { TrayHintShown = true }; SavePreferences();
                }
                Hide();
            }
        };
        Closed += (_, _) => { exiting = true; dayTimer.Stop(); portable?.Close(); tray?.Dispose(); System.Windows.Application.Current.Shutdown(); };
        dayTimer.Tick += (_, _) => {
            if (portableDay != DateTime.Today) { portableDay = DateTime.Today; RefreshPortable(); if(page=="today"&&!formDirty)ShowTasks(false); }
            if(repository.Online&&!repository.Busy&&repository.Current!=null&&DateTimeOffset.UtcNow-repository.Current.ServerTime>TimeSpan.FromHours(18))Run(repository.SynchronizeAsync);
        };
        dayTimer.Start();
    }

    bool CanLeaveForm()
    {
        if (!formDirty) return true;
        if (!Confirm("未保存的日程","当前表单尚未保存，是否放弃输入？","放弃修改","继续编辑")) return false;
        formDirty = false; return true;
    }

    void RestoreMain() { Show(); WindowState = WindowState.Normal; Activate(); }
    bool SavePreferences()
    {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            File.WriteAllText(PreferencesPath + ".tmp", JsonSerializer.Serialize(preferences));
            File.Move(PreferencesPath + ".tmp", PreferencesPath, true); return true;
        } catch { status.Text = "设置保存失败，请检查目录权限或磁盘空间。"; return false; }
    }

    void ToggleTask(DemoTask task)
    {
        if(task.Event != null) Run(()=>repository.CompleteAsync(task.Event));
    }

    void ShowTaskDetail(DemoTask task)
    {
        if(task.Event != null)ShowEventDetail(task.Event);
    }

    void TogglePortable()
    {
        if (portable != null) { portable.Close(); return; }
        portableContent = new StackPanel { Margin = new Thickness(20) };
        portable = new Window { Title = "随身日程", Width = 720, Height = 620, MinWidth = 540, MinHeight = 400,
            Background = Brush("#F4F6FB"), FontFamily = FontFamily, FontSize = 14, UseLayoutRounding = true, SnapsToDevicePixels = true,
            Content = new ScrollViewer { Content = portableContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        TextOptions.SetTextFormattingMode(portable, TextFormattingMode.Display);
        portable.Closed += (_, _) => { portable = null; portableContent = null; portableMenu!.Text = "开启随身日程"; };
        portableMenu!.Text = "关闭随身日程";
        RefreshPortable(); portable.Show();
    }

    void RefreshPortable()
    {
        if (portableContent == null) return;
        ProjectTasks(DateTime.Today);
        portableContent.Children.Clear();
        portableContent.Children.Add(new TextBlock { Text = $"{DateTime.Today:MM月dd日 dddd}", FontSize = 24, FontWeight = FontWeights.SemiBold });
        portableContent.Children.Add(new TextBlock { Text = repository.Online ? "与账号同步" : "缓存只读 · 连接后可完成或恢复", Foreground = Brushes.SlateGray, Margin = new Thickness(0,8,0,16) });
        var dayTasks = tasks.Where(t => t.Day.Date == DateTime.Today).ToList();
        var timeline = new Canvas { Height = 960, Background = Brushes.White, Margin = new Thickness(0,8,0,16) };
        for (int hour=0; hour<24; hour++) {
            var label = new TextBlock { Text = $"{hour:00}:00", Foreground = Brushes.SlateGray, FontSize = 12 };
            Canvas.SetTop(label, hour*40); Canvas.SetLeft(label, 6); timeline.Children.Add(label);
        }
        var timed = dayTasks.Where(t => t.StartTime.HasValue && t.EndTime.HasValue).ToList();
        for (int index=0; index<timed.Count; index++) {
            var task = timed[index];
            var item = Button(task.Title, () => ShowTaskDetail(task));
            item.Width = 140; item.Height = Math.Max(34, (task.EndTime!.Value-task.StartTime!.Value).TotalHours*40);
            item.Margin = new Thickness(0); item.VerticalContentAlignment = VerticalAlignment.Top;
            item.Background = task.Completed ? Brush("#E5E7EB") : Brush("#EAF1FF");
            Canvas.SetLeft(item, 58 + index*148); Canvas.SetTop(item, task.StartTime.Value.TotalHours*40);
            timeline.Children.Add(item);
        }
        var timeView = new ScrollViewer { Content = timeline, Height = 210, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        timeline.Width = Math.Max(400, 58 + timed.Count*148);
        timeView.Loaded += (_, _) => timeView.ScrollToVerticalOffset(Math.Max(0,DateTime.Now.Hour*40-40));
        portableContent.Children.Add(new Expander { Header = "日日程表（24小时）", Content = timeView, IsExpanded = true });
        foreach (bool completed in new[]{false,true}) {
            var list = new StackPanel();
            var entries = dayTasks.Where(t => t.Completed == completed).ToList();
            if (entries.Count==0) list.Children.Add(new TextBlock { Text = completed?"今日暂无已完成任务":"当日暂无待完成任务", Margin=new Thickness(0,12,0,12) });
            foreach (var task in entries) {
                var row = new DockPanel { Margin = new Thickness(0,5,0,5) };
                var action = Button(completed?"恢复":"完成", () => ToggleTask(task));
                action.IsEnabled=repository.Online&&!repository.Busy&&task.Event?.CancelledAt==null;
                DockPanel.SetDock(action,Dock.Right); row.Children.Add(action);
                var detail = Button(task.Title, () => ShowTaskDetail(task)); detail.HorizontalContentAlignment=HorizontalAlignment.Left; row.Children.Add(detail); list.Children.Add(row);
            }
            portableContent.Children.Add(new Expander { Header=$"{(completed?"今日已完成":"当日任务")}  {entries.Count}", Content=list, IsExpanded=!completed || entries.Count>0, Margin=new Thickness(0,14,0,0) });
        }
    }

    string settingsCategory="账号";
    void ShowSettings()
    {
        page="settings";bSelect("设置与版本");Begin("偏好设置","账号、日程与桌面行为");
        var layout=new Grid();layout.ColumnDefinitions.Add(new(){Width=new(150)});layout.ColumnDefinitions.Add(new());
        var categories=new StackPanel{Margin=new(0,0,24,0)};foreach(var label in new[]{"账号","日程偏好","桌面行为","关于与版本"}){var entry=Button(label,()=>{settingsCategory=label;ShowSettings();});if(label==settingsCategory)entry.Background=Brush("#E9F0FF");categories.Children.Add(entry);}layout.Children.Add(categories);
        var body=new StackPanel{MaxWidth=620,HorizontalAlignment=HorizontalAlignment.Stretch};Grid.SetColumn(body,1);layout.Children.Add(body);
        if(settingsCategory=="账号")ShowProfileSettings(body);
        else if(settingsCategory=="桌面行为"){
            body.Children.Add(Text("桌面行为",19,margin:new(0,0,0,20)));var keep=new CheckBox{Content="关闭窗口后保留在系统托盘",IsChecked=preferences.CloseToTray,Margin=new(0,8,0,18)};body.Children.Add(keep);var feedback=Text("",13);body.Children.Add(feedback);
            var save=Button("保存",()=>{var previous=preferences;preferences=preferences with{CloseToTray=keep.IsChecked==true};if(SavePreferences())feedback.Text="已保存";else{preferences=previous;feedback.Text="保存失败";}});save.HorizontalAlignment=HorizontalAlignment.Left;body.Children.Add(save);body.Children.Add(Button("开启 / 关闭随身日程",TogglePortable));
        }else if(settingsCategory=="日程偏好"){body.Children.Add(Text("默认工作时段",19));body.Children.Add(Text("账号工作时段的原生编辑仍待适配，可在 Web 个人设置中修改。",14,"#64748B",new(0,16,0,0)));}
        else{body.Children.Add(Text("Awesome Interview",22));body.Children.Add(Text("Windows 1.4.0-W · WPF / .NET 10",14,"#64748B",new(0,12,0,0)));}
        content.Children.Add(Surface(layout,new(26)));
    }
}
