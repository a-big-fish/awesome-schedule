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
            UpdatePortableClock();
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
        portableContent = new StackPanel { Margin = new Thickness(18, 14, 18, 20) };
        portable = new Window
        {
            Title = "随身日程", Width = 720, Height = 620, MinWidth = 540, MinHeight = 400,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, ResizeMode = ResizeMode.CanResizeWithGrip,
            ShowInTaskbar = false, Background = Brushes.Transparent, FontFamily = FontFamily, FontSize = 14,
            UseLayoutRounding = true, SnapsToDevicePixels = true
        };

        var dock = new DockPanel();
        dock.Children.Add(BuildPortableHeader());
        portableAppearancePanel = BuildPortableAppearancePanel();
        DockPanel.SetDock(portableAppearancePanel, Dock.Top);
        dock.Children.Add(portableAppearancePanel);
        portableBodyScroll = new QuietScrollViewer
        {
            Content = portableContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        dock.Children.Add(portableBodyScroll);
        portableSurface = new Border
        {
            CornerRadius = new(14), BorderThickness = new(1), BorderBrush = Brush("#CCD6E7"), Child = dock
        };
        portable.Content = portableSurface;
        ApplyPortableAppearance();
        TextOptions.SetTextFormattingMode(portable, TextFormattingMode.Display);
        portable.PreviewKeyDown += (_, e) => { if (portableLocked) e.Handled = true; };
        portable.LocationChanged += (_, _) => PositionPortableUnlock();
        portable.SizeChanged += (_, _) => PositionPortableUnlock();
        portable.Closed += (_, _) =>
        {
            portableWheel?.Dispose();
            portableWheel = null;
            portableUnlock = null;
            portableAppearancePanel = null;
            portableHeader = null;
            portableToolbarButtons.Clear();
            portable = null;
            portableContent = null;
            portableSurface = null;
            portableNowLine = null;
            portableClock = null;
            portableBodyScroll = null;
            portableTimelineScroll = null;
            portableLocked = false;
            portableShowCompleted = false;
            portableTimeOffset = null;
            if (unlockPortable != null) unlockPortable.Enabled = false;
            portableMenu!.Text = "开启随身日程";
        };
        portableMenu!.Text = "关闭随身日程";
        RefreshPortable();
        portable.Show();
    }

    Border BuildPortableHeader()
    {
        var controls = new DockPanel();
        void AddTool(Button button)
        {
            DockPanel.SetDock(button, Dock.Right);
            controls.Children.Add(button);
        }
        AddTool(PortableTool("关闭", () => portable?.Close()));
        AddTool(PortableTool("外观", () =>
        {
            if (portableAppearancePanel != null)
                portableAppearancePanel.Visibility = portableAppearancePanel.IsVisible ? Visibility.Collapsed : Visibility.Visible;
        }));
        var lockButton = PortableTool("鼠标穿透", () => SetPortableLocked(true));
        lockButton.ToolTip = "可继续滚动日程，点击组件右上角可恢复交互";
        AddTool(lockButton);

        var title = PortableLabel("随身日程", 14);
        title.FontWeight = FontWeights.SemiBold;
        title.VerticalAlignment = VerticalAlignment.Center;
        controls.Children.Add(title);
        var header = new Border { Padding = new(18, 10, 12, 10), CornerRadius = new(13, 13, 0, 0), Child = controls };
        // ButtonBase handles its own mouse down. This handler covers the label, gaps and padding.
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (portable is { IsVisible: true } window && !portableLocked &&
                e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                window.DragMove();
        };
        DockPanel.SetDock(header, Dock.Top);
        portableHeader = header;
        return header;
    }

    bool portableShowCompleted;
    double? portableTimeOffset;
    DateTime portableRenderedDay;
    void UpdatePortableClock()
    {
        var now=AwesomeInterview.NativeDemo.Data.CalendarProjection.Now;
        if(portableNowLine!=null)Canvas.SetTop(portableNowLine,now.TimeOfDay.TotalHours*40);
        if(portableClock!=null){portableClock.Text=now.ToString("HH:mm");Canvas.SetTop(portableClock,Math.Max(0,now.TimeOfDay.TotalHours*40-18));}
    }
    void RefreshPortable()
    {
        if(portableContent==null)return;
        var today=AwesomeInterview.NativeDemo.Data.CalendarProjection.Now.Date;
        if(portableRenderedDay!=today){portableRenderedDay=today;portableTimeOffset=null;}
        ProjectTasks(today);
        portableContent.Children.Clear();
        portableTimelineScroll = null;
        portableNowLine = null;
        portableClock = null;

        var dayTasks = tasks.Where(t => t.Event?.CancelledAt == null).ToList();
        int completedCount = dayTasks.Count(t => t.Completed);
        var summary = new DockPanel();
        if (completedCount > 0)
        {
            var toggle = new CheckBox
            {
                Content = PortableLabel($"显示已完成 · {completedCount}", 12),
                IsChecked = portableShowCompleted, VerticalAlignment = VerticalAlignment.Center
            };
            toggle.Click += (_, _) =>
            {
                portableShowCompleted = toggle.IsChecked == true;
                RefreshPortable();
            };
            DockPanel.SetDock(toggle, Dock.Right);
            summary.Children.Add(toggle);
        }

        var date = PortableLabel(today.ToString("MM月dd日 dddd"), 20);
        date.FontWeight = FontWeights.SemiBold;
        summary.Children.Add(date);
        var summaryBody = new StackPanel();
        summaryBody.Children.Add(summary);
        if (repository.Current != null && !repository.Online)
            summaryBody.Children.Add(Text("离线，修改将保存在本机", 12, "#52647D", new(0, 5, 0, 0)));
        portableContent.Children.Add(PortableSection(summaryBody, new(14, 10, 14, 10)));

        if (repository.Current == null)
        {
            AddPortableEmptyState("登录后查看今日日程", "登录账号", () => Navigate(() =>
            {
                RestoreMain();
                ShowAccount();
            }));
            return;
        }

        bool hasPending = dayTasks.Any(t => !t.Completed);
        if (!hasPending && (!portableShowCompleted || completedCount == 0))
        {
            AddPortableEmptyState(completedCount > 0 ? "今日任务已全部完成，真棒！" : "今天没有安排，留点时间给自己。",
                "来点 Idea？", () => Navigate(() =>
                {
                    RestoreMain();
                    ShowIdeas();
                    EditIdea(null, portable);
                }));
            return;
        }

        var timeline = new Canvas { Height = 960, Background = Brushes.Transparent };
        var hourRules = new Border[24];
        for (int hour = 0; hour < 24; hour++)
        {
            var label = Text($"{hour:00}:00", 11, "#52647D");
            Canvas.SetTop(label, hour * 40 + 5);
            Canvas.SetLeft(label, 8);
            timeline.Children.Add(label);
            var hourRule = new Border { Height = 1, Background = Brush("#CCD9E8"), IsHitTestVisible = false };
            Canvas.SetTop(hourRule, hour * 40);
            Canvas.SetLeft(hourRule, 54);
            timeline.Children.Add(hourRule);
            hourRules[hour] = hourRule;
        }
        var timed = dayTasks.Where(t => (portableShowCompleted || !t.Completed) && t.StartTime.HasValue && t.EndTime.HasValue).ToList();
        var timeView = new QuietScrollViewer
        {
            Content = timeline, Height = 260, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        portableTimelineScroll = timeView;
        var cards=new System.Collections.Generic.List<(Button Button,StackPanel Label)>();
        for(int index=0;index<timed.Count;index++){
            var task=timed[index];var label=new StackPanel();label.Children.Add(new TextBlock{Text=task.Title,FontSize=15,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Foreground=Brush("#263650")});
            label.Children.Add(new TextBlock{Text=$"{task.StartTime:hh\\:mm} — {task.EndTime:hh\\:mm}",FontSize=11,TextAlignment=TextAlignment.Center,Foreground=Brush("#64748B"),Margin=new(0,6,0,0)});
            var item=Button("",()=>ShowTaskDetail(task));item.Content=label;item.ToolTip=task.Title;item.Height=Math.Max(40,(task.EndTime!.Value-task.StartTime!.Value).TotalHours*40);item.MinHeight=0;item.Padding=new(12,8,12,8);item.Margin=new(0);item.HorizontalContentAlignment=HorizontalAlignment.Stretch;item.VerticalContentAlignment=VerticalAlignment.Stretch;
            item.Background=Brush(task.Completed?"#EEF0F3":"#EDF3FF");item.BorderBrush=Brush("#E2EAF6");Canvas.SetTop(item,task.StartTime.Value.TotalHours*40);timeline.Children.Add(item);cards.Add((item,label));
        }
        portableNowLine=new Border{Height=1,Background=Brush("#E5484D"),IsHitTestVisible=false};Canvas.SetLeft(portableNowLine,54);timeline.Children.Add(portableNowLine);
        var currentLine=portableNowLine;
        portableClock=Text("",11,"#D43F48");portableClock.Background=Brush("#F8FAFC");Canvas.SetLeft(portableClock,8);timeline.Children.Add(portableClock);UpdatePortableClock();
        void Layout(){
            double available=Math.Max(340,timeView.ActualWidth-8);double width=Math.Clamp((available-64)/Math.Max(1,cards.Count)-12,140,240);timeline.Width=Math.Max(available,54+cards.Count*(width+12));
            currentLine.Width=Math.Max(0,timeline.Width-54);
            foreach (var rule in hourRules) rule.Width = currentLine.Width;
            for(int i=0;i<cards.Count;i++){
                var (button,label)=cards[i];button.Width=width;Canvas.SetLeft(button,54+i*(width+12));
                double start=Canvas.GetTop(button)+8,end=start+button.Height-16;
                double visibleStart=Math.Clamp(timeView.VerticalOffset-start,0,Math.Max(0,end-start));double visibleEnd=Math.Clamp(timeView.VerticalOffset+timeView.ViewportHeight-start,visibleStart,Math.Max(0,end-start));
                // Leave space between task captions and the live time rule.
                double center=start+(visibleStart+visibleEnd)/2,now=AwesomeInterview.NativeDemo.Data.CalendarProjection.Now.TimeOfDay.TotalHours*40;
                double shift=Math.Abs(center-now)<40&&visibleEnd-visibleStart>110?-38:0;
                label.Margin=new(0,Math.Max(0,visibleStart+shift),0,Math.Max(0,end-start-visibleEnd-shift));label.VerticalAlignment=VerticalAlignment.Center;
            }
        }
        long lastScrollInput=-10000;timeView.PreviewMouseWheel+=(_,_)=>lastScrollInput=Environment.TickCount64;timeView.PreviewMouseDown+=(_,_)=>lastScrollInput=Environment.TickCount64;timeView.PreviewKeyDown+=(_,_)=>lastScrollInput=Environment.TickCount64;
        timeView.Loaded+=(_,_)=>{double target=portableTimeOffset??Math.Max(0,AwesomeInterview.NativeDemo.Data.CalendarProjection.Now.TimeOfDay.TotalHours*40-110);timeView.ScrollToVerticalOffset(target);portableTimeOffset=target;Layout();};
        timeView.SizeChanged+=(_,_)=>Layout();timeView.ScrollChanged+=(_,e)=>{if(e.OriginalSource==timeView){if(timeView.IsVisible&&e.VerticalChange!=0&&(portableLocked||Environment.TickCount64-lastScrollInput<2000))portableTimeOffset=timeView.VerticalOffset;Layout();}};
        portableContent.Children.Add(PortableSection(new Expander{Header=PortableLabel("今日时间表"),Content=timeView,IsExpanded=true}, new(14, 10, 10, 10)));
        foreach(bool completed in new[]{false,true}){
            if(completed&&!portableShowCompleted)continue;
            var entries=dayTasks.Where(t=>t.Completed==completed).ToList();var list=new StackPanel{Margin=new(0,10,0,0)};
            if (entries.Count == 0) continue;
            foreach(var task in entries){var row=new DockPanel{Margin=new(0,0,0,10)};var action=Button(completed?"恢复":"完成",()=>ToggleTask(task));action.IsEnabled=!repository.Busy;action.Padding=new(14,8,14,8);DockPanel.SetDock(action,Dock.Right);row.Children.Add(action);var detail=Button(task.Title,()=>ShowTaskDetail(task));detail.HorizontalContentAlignment=HorizontalAlignment.Left;detail.Margin=new(0,4,12,4);row.Children.Add(detail);list.Children.Add(row);}
            portableContent.Children.Add(PortableSection(new Expander{Header=PortableLabel($"{(completed?"今日已完成":"当日任务")} · {entries.Count}"),Content=list,IsExpanded=true}, new(14, 10, 14, 8)));
        }
    }

    void AddPortableEmptyState(string title, string actionLabel, Action onAction)
    {
        var copy = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var heading = PortableLabel(title, 22);
        heading.FontWeight = FontWeights.SemiBold;
        heading.TextAlignment = TextAlignment.Center;
        copy.Children.Add(heading);
        var actionButton = Button(actionLabel, onAction);
        actionButton.Margin = new(0, 18, 0, 0);
        actionButton.Padding = new(16, 8, 16, 8);
        actionButton.HorizontalAlignment = HorizontalAlignment.Center;
        actionButton.Foreground = Brush("#2357CE");
        actionButton.Background = Brush("#EBF2FF");
        actionButton.BorderBrush = Brush("#D5E4FC");
        copy.Children.Add(actionButton);
        var card = PortableSection(copy, new(20));
        card.MinHeight = 248;
        portableContent!.Children.Add(card);
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
        else{body.Children.Add(Text("Awesome Interview",22));body.Children.Add(Text("Windows 1.6.0-W Preview 5 · WPF / .NET 10",14,"#64748B",new(0,12,0,0)));}
        content.Children.Add(Surface(layout,new(26)));
    }
}
