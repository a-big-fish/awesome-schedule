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
        tray = new Forms.NotifyIcon { Text = "Awesome Interview · 本地演示", ContextMenuStrip = menu,
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!), Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreMain);
        Closing += (_, args) => {
            if (!exiting && !CanLeaveForm()) { args.Cancel = true; return; }
            if (!exiting && preferences.CloseToTray) {
                args.Cancel = true;
                if (!preferences.TrayHintShown) {
                    MessageBox.Show(this, "关闭后应用将在系统托盘继续运行。双击托盘图标可重新打开，右键可退出；可在设置中关闭此行为。", "关闭到系统托盘", MessageBoxButton.OK, MessageBoxImage.Information);
                    preferences = preferences with { TrayHintShown = true }; SavePreferences();
                }
                Hide();
            }
        };
        Closed += (_, _) => { exiting = true; dayTimer.Stop(); portable?.Close(); tray?.Dispose(); System.Windows.Application.Current.Shutdown(); };
        dayTimer.Tick += (_, _) => { if (portableDay != DateTime.Today) { portableDay = DateTime.Today; RefreshPortable(); } };
        dayTimer.Start();
    }

    bool CanLeaveForm()
    {
        if (!formDirty) return true;
        if (MessageBox.Show(this, "当前表单尚未保存，是否放弃输入？", "未保存的日程", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
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
        int index = tasks.IndexOf(task);
        if (index < 0) return;
        tasks[index] = task with { Completed = !task.Completed };
        if (!Save()) tasks[index] = task;
        RefreshPortable();
    }

    void ShowTaskDetail(DemoTask task)
    {
        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = task.Title, FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = $"{task.Day:yyyy-MM-dd}  ·  {(task.StartTime.HasValue ? task.StartTime.Value.ToString(@"hh\:mm") + "–" + task.EndTime?.ToString(@"hh\:mm") : "未指定时间")}", Margin = new Thickness(0,18,0,16) });
        body.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(task.Note) ? "未填写备注" : task.Note, TextWrapping = TextWrapping.Wrap });
        var dialog = new Window { Title = "任务详情 · 本地演示", Owner = IsVisible ? this : portable, Width = 540, Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new ScrollViewer { Content = body }, UseLayoutRounding = true };
        body.Children.Add(Button("关闭", () => dialog.Close()));
        dialog.ShowDialog();
    }

    void TogglePortable()
    {
        if (portable != null) { portable.Close(); return; }
        portableContent = new StackPanel { Margin = new Thickness(20) };
        portable = new Window { Title = "随身日程 · 本地演示", Width = 720, Height = 620, MinWidth = 540, MinHeight = 400,
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
        portableContent.Children.Clear();
        portableContent.Children.Add(new TextBlock { Text = $"{DateTime.Today:MM月dd日 dddd}", FontSize = 24, FontWeight = FontWeights.SemiBold });
        portableContent.Children.Add(new TextBlock { Text = "仅本地演示数据 · 时间轴按当天时间显示", Foreground = Brushes.SlateGray, Margin = new Thickness(0,8,0,16) });
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
                var action = Button(completed?"恢复":"完成", () => { ToggleTask(task); if (heading.Text.Contains("日程") || heading.Text.Contains("专注")) ShowTasks(true); });
                DockPanel.SetDock(action,Dock.Right); row.Children.Add(action);
                var detail = Button(task.Title, () => ShowTaskDetail(task)); detail.HorizontalContentAlignment=HorizontalAlignment.Left; row.Children.Add(detail); list.Children.Add(row);
            }
            portableContent.Children.Add(new Expander { Header=$"{(completed?"今日已完成":"当日任务")}  {entries.Count}", Content=list, IsExpanded=!completed || entries.Count>0, Margin=new Thickness(0,14,0,0) });
        }
    }

    void ShowSettings()
    {
        bSelect("设置与版本"); Begin("设置与版本");
        Paragraph("Awesome Interview 1.2.0-W · Windows 原生演示版");
        var keep = new CheckBox { Content="关闭窗口后保留在系统托盘", IsChecked=preferences.CloseToTray, Margin=new Thickness(0,8,0,16) };
        content.Children.Add(keep);
        content.Children.Add(new TextBlock { Text="演示用户名（仅本机，不修改正式账号）" });
        var name = new TextBox { Text=preferences.DemoName, MaxLength=40 }; content.Children.Add(name);
        var feedback = new TextBlock { TextWrapping=TextWrapping.Wrap }; content.Children.Add(feedback);
        content.Children.Add(Button("保存设置", () => {
            if(string.IsNullOrWhiteSpace(name.Text)){feedback.Text="用户名不能为空。";return;}
            var previous=preferences; preferences=preferences with { CloseToTray=keep.IsChecked==true, DemoName=name.Text.Trim() };
            if(SavePreferences())feedback.Text="设置已保存，下次关闭窗口立即生效。";else {preferences=previous;feedback.Text="保存失败，已保留原设置。";}
        }));
        content.Children.Add(Button("开启 / 关闭随身日程", TogglePortable));
        Paragraph("当前未登录。正式账号用户名修改、只读邮箱将在鉴权接入后提供。随身日程支持原生时间轴、当日列表、完成恢复与详情；透明和鼠标穿透仍在规划。旧任务未指定时间时仅显示在列表。");
    }
}
