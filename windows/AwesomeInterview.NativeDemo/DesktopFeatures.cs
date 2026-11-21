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

public record DesktopPreferences(bool CloseToTray = true, bool TrayHintShown = false, string DemoName = "演示用户",double PortableOpacity=.92,string PortableColor="white",bool PortableTopmost=true);

public partial class MainWindow
{
    DesktopPreferences preferences = new();
    Forms.NotifyIcon? tray;
    Forms.ToolStripMenuItem? portableMenu;
    Window? portable;
    StackPanel? portableContent;
    readonly DispatcherTimer dayTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    DateTime portableDay = AwesomeInterview.NativeDemo.Data.CalendarProjection.Now.Date;
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
        unlockPortable=new Forms.ToolStripMenuItem("恢复随身日程交互"){Enabled=false};unlockPortable.Click+=(_,_)=>Dispatcher.Invoke(()=>SetPortableLocked(false));menu.Items.Add(unlockPortable);
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(() => { RestoreMain(); ShowSettings(); }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出应用", null, (_, _) => Dispatcher.Invoke(() => { if (CanLeaveForm()) { exiting = true; Close(); } }));
        tray = new Forms.NotifyIcon { Text = "Awesome Interview", ContextMenuStrip = menu,
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!), Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreMain);
        tray.BalloonTipClicked+=(_,_)=>Dispatcher.Invoke(OpenNotification);
        Closing += (_, args) => {
            if (!exiting && !CanLeaveForm()) { args.Cancel = true; return; }
            if (!exiting && preferences.CloseToTray) {
                args.Cancel = true;
                if (!preferences.TrayHintShown) {
                    if(!Confirm("关闭到系统托盘","关闭后应用将在系统托盘继续运行。双击托盘图标可重新打开，右键可退出；可在设置中关闭此行为。","知道了","返回"))return;
                    preferences = preferences with { TrayHintShown = true }; SavePreferences();
                }
                Hide();
            }
        };
        Closed += (_, _) => { exiting = true; dayTimer.Stop(); portable?.Close(); tray?.Dispose(); System.Windows.Application.Current.Shutdown(); };
        dayTimer.Tick += (_, _) => {
            if (portableDay != AwesomeInterview.NativeDemo.Data.CalendarProjection.Now.Date) { portableDay = AwesomeInterview.NativeDemo.Data.CalendarProjection.Now.Date; RefreshPortable(); if(page=="today"&&!formDirty)ShowTasks(false); }
            if(repository.Online&&!repository.Busy&&repository.Current!=null&&DateTimeOffset.UtcNow-repository.Current.ServerTime>TimeSpan.FromHours(18))Run(repository.SynchronizeAsync);
            if(!repository.Busy&&!repository.SyncBusy&&repository.HasPendingSync)Run(repository.SyncNowAsync);
            if(portableNowLine!=null)Canvas.SetTop(portableNowLine,AwesomeInterview.NativeDemo.Data.CalendarProjection.Now.TimeOfDay.TotalHours*40);
            Run(CheckRemindersAsync);
        };
        dayTimer.Start();
    }

    bool CanLeaveForm()
    {
        if (!formDirty) return true;
        if (!Confirm(page=="note-editor"?"尚未提交的面经":"未保存的日程",page=="note-editor"?"离开前将保留已自动保存的本机草稿；尚未同步的输入请先点击保存。是否离开？":"当前表单尚未保存，是否放弃输入？","离开页面","继续编辑")) return false;
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
        if(task.Event != null) Run(()=>repository.CompleteEventQueuedAsync(task.Event));
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
            WindowStyle=WindowStyle.None,AllowsTransparency=true,ResizeMode=ResizeMode.CanResizeWithGrip,ShowInTaskbar=false,Background=Brushes.Transparent,
            FontFamily = FontFamily, FontSize = 14, UseLayoutRounding = true, SnapsToDevicePixels = true };
        var dock=new DockPanel();var controls=new DockPanel{Margin=new(18,12,12,0)};
        var close=Button("关闭",()=>portable?.Close());DockPanel.SetDock(close,Dock.Right);controls.Children.Add(close);
        var settings=Button("外观",()=>{RestoreMain();settingsCategory="桌面行为";ShowSettings();});DockPanel.SetDock(settings,Dock.Right);controls.Children.Add(settings);
        var locked=Button("鼠标穿透",()=>SetPortableLocked(true));locked.ToolTip="锁定后通过托盘右键恢复交互";DockPanel.SetDock(locked,Dock.Right);controls.Children.Add(locked);
        var drag=Text("随身日程 · 拖动移动",15,"#52647D",new(0,12,0,0));drag.MouseLeftButtonDown+=(_,e)=>{if(e.ButtonState==System.Windows.Input.MouseButtonState.Pressed)portable?.DragMove();};controls.Children.Add(drag);DockPanel.SetDock(controls,Dock.Top);dock.Children.Add(controls);dock.Children.Add(new ScrollViewer{Content=portableContent,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        portableSurface=new Border{CornerRadius=new(14),BorderThickness=new(1),BorderBrush=Brush("#CCD6E7"),Child=dock};portable.Content=portableSurface;ApplyPortableAppearance();
        TextOptions.SetTextFormattingMode(portable, TextFormattingMode.Display);
        portable.Closed += (_, _) => { portable = null; portableContent = null;portableSurface=null;portableNowLine=null;portableLocked=false;if(unlockPortable!=null)unlockPortable.Enabled=false; portableMenu!.Text = "开启随身日程"; };
        portableMenu!.Text = "关闭随身日程";
        RefreshPortable(); portable.Show();
    }

    void RefreshPortable()
    {
        if (portableContent == null) return;
        ProjectTasks(AwesomeInterview.NativeDemo.Data.CalendarProjection.Now.Date);
        portableContent.Children.Clear();
        portableContent.Children.Add(new TextBlock { Text = $"{AwesomeInterview.NativeDemo.Data.CalendarProjection.Now.Date:MM月dd日 dddd}", FontSize = 24, FontWeight = FontWeights.SemiBold });
        portableContent.Children.Add(new TextBlock { Text = repository.Online ? "与账号同步" : "离线 · 完成或恢复会先保存在本机，联网后同步", Foreground = Brushes.SlateGray, Margin = new Thickness(0,8,0,16) });
        var dayTasks = tasks.Where(t => t.Day.Date == AwesomeInterview.NativeDemo.Data.CalendarProjection.Now.Date).ToList();
        var timeline = new Canvas { Height = 960, Background = Brushes.Transparent, Margin = new Thickness(0,8,0,16) };
        for (int hour=0; hour<24; hour++) {
            var label = new TextBlock { Text = $"{hour:00}:00", Foreground = Brushes.SlateGray, FontSize = 12 };
            Canvas.SetTop(label, hour*40); Canvas.SetLeft(label, 6); timeline.Children.Add(label);
        }
        var timed = dayTasks.Where(t => t.StartTime.HasValue && t.EndTime.HasValue).ToList();
        for (int index=0; index<timed.Count; index++) {
            var task = timed[index];
            var item = Button(task.Title, () => ShowTaskDetail(task));
            item.Width = 140; item.Height = Math.Max(34, (task.EndTime!.Value-task.StartTime!.Value).TotalHours*40);
            item.Content=Text(task.Title,16);((TextBlock)item.Content).TextAlignment=TextAlignment.Center;item.Margin = new Thickness(0); item.VerticalContentAlignment = VerticalAlignment.Center;
            item.Background = task.Completed ? Brush("#E5E7EB") : Brush("#EAF1FF");
            Canvas.SetLeft(item, 58 + index*148); Canvas.SetTop(item, task.StartTime.Value.TotalHours*40);
            timeline.Children.Add(item);
        }
        var timeView = new ScrollViewer { Content = timeline, Height = 210, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        timeline.Width = Math.Max(400, 58 + timed.Count*148);
        portableNowLine=new Border{Height=2,Width=timeline.Width-58,Background=Brush("#EF4444"),IsHitTestVisible=false};Canvas.SetLeft(portableNowLine,58);Canvas.SetTop(portableNowLine,AwesomeInterview.NativeDemo.Data.CalendarProjection.Now.TimeOfDay.TotalHours*40);timeline.Children.Add(portableNowLine);
        timeView.Loaded += (_, _) => timeView.ScrollToVerticalOffset(Math.Max(0,AwesomeInterview.NativeDemo.Data.CalendarProjection.Now.TimeOfDay.TotalHours*40-105));
        portableContent.Children.Add(new Expander { Header = "日日程表（24小时）", Content = timeView, IsExpanded = true });
        foreach (bool completed in new[]{false,true}) {
            var list = new StackPanel();
            var entries = dayTasks.Where(t => t.Completed == completed).ToList();
            if (entries.Count==0) list.Children.Add(new TextBlock { Text = completed?"今日暂无已完成任务":"当日暂无待完成任务", Margin=new Thickness(0,12,0,12) });
            foreach (var task in entries) {
                var row = new DockPanel { Margin = new Thickness(0,5,0,5) };
                var action = Button(completed?"恢复":"完成", () => ToggleTask(task));
                action.IsEnabled=!repository.Busy&&task.Event?.CancelledAt==null;
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
            AddPortablePreferences(body);
        }else if(settingsCategory=="日程偏好")ShowWorkdaySettings(body);
        else{body.Children.Add(Text("Awesome Interview",22));body.Children.Add(Text("Windows 1.6.0-W Preview 1 · WPF / .NET 10",14,"#64748B",new(0,12,0,0)));}
        content.Children.Add(Surface(layout,new(26)));
    }
}
