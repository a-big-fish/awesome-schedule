using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using static AwesomeInterview.NativeDemo.WorkspaceControls;
using AwesomeInterview.NativeDemo.Data;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class DesktopIntegration(MainWindow shell, SchedulePages schedule,
    Func<IReadOnlyCollection<System.Drawing.Point>, IReadOnlyDictionary<System.Drawing.Point, Color>>? readBackdrop = null,
    string? preferencesPath = null)
{
    DesktopPreferences preferences = new(PortableStyle: new(FillWindow: true));
    Forms.NotifyIcon? tray;
    Forms.ToolStripMenuItem? portableMenu;
    Window? portable;
    DockPanel? portableContent;
    NativeWindowPlacement? portablePlacement;
    readonly DispatcherTimer dayTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    DateTime portableDay = AwesomeInterview.NativeDemo.Data.CalendarProjection.Now.Date;
    bool exiting;
    string PreferencesPath => preferencesPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AwesomeInterviewNativeDemo", "desktop-settings.json");

    internal void Initialize(Action showSettings)
    {
        try
        {
            if (File.Exists(PreferencesPath))
            {
                // Settings written before styles were introduced used the clear-outline defaults.
                using var saved = JsonDocument.Parse(File.ReadAllText(PreferencesPath));
                preferences = PortableAppearancePreset.UpgradeSavedStyle(
                    saved.RootElement.Deserialize<DesktopPreferences>() ?? new());
                if (!saved.RootElement.TryGetProperty(nameof(DesktopPreferences.PortableOpacity), out _))
                    preferences = preferences with { PortableOpacity = 0 };
                if (!saved.RootElement.TryGetProperty(nameof(DesktopPreferences.PortableOverallOpacity), out _))
                    preferences = preferences with { PortableOverallOpacity = 1 };
                if (!saved.RootElement.TryGetProperty(nameof(DesktopPreferences.PortableTextOpacity), out _))
                    preferences = preferences with { PortableTextOpacity = 1 };
                if (!saved.RootElement.TryGetProperty(nameof(DesktopPreferences.PortableBorderOpacity), out _))
                    preferences = preferences with { PortableBorderOpacity = 1 };
            }
            else if (PortableAppearancePreset.TryRead(Path.Combine(EnsurePortableStyleFolder(),
                PortableAppearancePreset.DefaultFile)) is { } style)
            {
                preferences = style.Apply(preferences);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            shell.Status.Text = "窗口设置读取失败，使用默认值。";
        }
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开主窗口", null, (_, _) => shell.Dispatcher.Invoke(shell.RestoreMain));
        portableMenu = new Forms.ToolStripMenuItem("开启随身日程");
        portableMenu.Click += (_, _) => shell.Dispatcher.Invoke(TogglePortable);
        menu.Items.Add(portableMenu);
        unlockPortable=new Forms.ToolStripMenuItem("恢复随身日程交互"){Enabled=false};unlockPortable.Click+=(_,_)=>shell.Dispatcher.Invoke(()=>SetPortableLocked(false));menu.Items.Add(unlockPortable);
        menu.Items.Add("设置", null, (_, _) => shell.Dispatcher.Invoke(() => { shell.RestoreMain(); showSettings(); }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出应用", null, (_, _) => shell.Dispatcher.Invoke(() => { if (!shell.PreparingUpdate && shell.CanLeaveForm()) { exiting = true; shell.Close(); } }));
        tray = new Forms.NotifyIcon { Text = "Awesome Interview", ContextMenuStrip = menu,
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!), Visible = true };
        tray.DoubleClick += (_, _) => shell.Dispatcher.Invoke(shell.RestoreMain);
        tray.BalloonTipClicked+=(_,_)=>shell.Dispatcher.Invoke(OpenNotification);
        shell.Closing += (_, args) => {
            if (!exiting && shell.PreparingUpdate) { args.Cancel = true; return; }
            if (!exiting && !shell.CanLeaveForm()) { args.Cancel = true; return; }
            if (!exiting && preferences.CloseToTray) {
                args.Cancel = true;
                if (!preferences.TrayHintShown) {
                    if(!shell.Confirm("关闭到系统托盘","关闭后应用将在系统托盘继续运行。双击托盘图标可重新打开，右键可退出；可在设置中关闭此行为。","知道了","返回"))return;
                    preferences = preferences with { TrayHintShown = true }; SavePreferences();
                }
                shell.Hide();
            }
        };
        shell.Closed += (_, _) => { exiting = true; dayTimer.Stop(); portable?.Close(); tray?.Dispose(); System.Windows.Application.Current.Shutdown(); };
        dayTimer.Tick += (_, _) => {
            if (shell.PreparingUpdate) return;
            if (portableDay != AwesomeInterview.NativeDemo.Data.CalendarProjection.Now.Date) { portableDay = AwesomeInterview.NativeDemo.Data.CalendarProjection.Now.Date; RefreshPortable(); if (shell.Route == PageRoute.Today && !shell.FormDirty) schedule.ShowTasks(false); }
            if(shell.Repository.Online&&!shell.Repository.Busy&&shell.Repository.Current!=null&&DateTimeOffset.UtcNow-shell.Repository.Current.ServerTime>TimeSpan.FromHours(18))shell.Run(shell.Repository.SynchronizeAsync);
            if(!shell.Repository.Busy&&!shell.Repository.SyncBusy&&shell.Repository.HasPendingSync)shell.Run(shell.Repository.SyncNowAsync);
            UpdatePortableClock();
            shell.Run(CheckRemindersAsync);
        };
        dayTimer.Start();
    }

    internal void ExitForUpdate()
    {
        exiting = true;
        shell.Close();
    }

    internal bool SavePreferences()
    {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            File.WriteAllText(PreferencesPath + ".tmp", JsonSerializer.Serialize(preferences));
            File.Move(PreferencesPath + ".tmp", PreferencesPath, true); return true;
        } catch { shell.Status.Text = "设置保存失败，请检查目录权限或磁盘空间。"; return false; }
    }

    void ToggleTask(PortableTask task)
    {
        if (shell.PreparingUpdate) return;
        shell.Run(()=>shell.Repository.CompleteEventQueuedAsync(task.Event));
    }

    void ShowTaskDetail(PortableTask task)
    {
        portableDetailOpen = true;
        try { schedule.ShowEventDetail(task.Event, portable); }
        finally
        {
            portableDetailOpen = false;
            RestartPortableIdleTimer();
        }
    }

    void TogglePortable()
    {
        if (portable != null) { portable.Close(); return; }
        portableContent = new DockPanel { Margin = new Thickness(8, 4, 8, 8) };
        portable = new Window
        {
            Title = "随身日程", Width = 620, Height = 540, MinWidth = 420, MinHeight = 320,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false, Background = Brush("#01FFFFFF"), FontFamily = shell.FontFamily, FontSize = 12,
            UseLayoutRounding = true, SnapsToDevicePixels = true
        };
        portablePlacement = new NativeWindowPlacement(portable, () => !PortablePassThrough);

        var dock = new DockPanel();
        dock.Children.Add(BuildPortableHeader());
        portableAppearancePanel = BuildPortableAppearancePanel();
        DockPanel.SetDock(portableAppearancePanel, Dock.Top);
        dock.Children.Add(portableAppearancePanel);
        dock.Children.Add(portableContent);
        var overlay = CreatePortableHoverCueLayer();
        var surface = new Grid();
        surface.Children.Add(dock);
        surface.Children.Add(overlay);
        portableSurface = new Border
        {
            CornerRadius = new(14), BorderThickness = new(1), BorderBrush = Brush("#CCD6E7"), Child = surface
        };
        portable.PreviewMouseMove += (sender, e) =>
        {
            if (!portableHoverActive || sender is not Window window) return;
            var screenPoint = overlay.PointToScreen(e.GetPosition(overlay));
            var point = new ScreenPoint { X = (int)Math.Round(screenPoint.X), Y = (int)Math.Round(screenPoint.Y) };
            EvaluatePortableHover(new WindowInteropHelper(window).Handle, point, Environment.TickCount64);
        };
        portable.Content = portableSurface;
        AttachPortableFocusReset(portable);
        AttachPortableContrast(portable);
        AttachPortableIdleLock(portable);
        UpdatePortableTools();
        ApplyPortableAppearance();
        TextOptions.SetTextFormattingMode(portable, TextFormattingMode.Display);
        portable.PreviewKeyDown += (_, e) => { if (PortablePassThrough) e.Handled = true; };
        portable.LocationChanged += (_, _) => PositionPortableUnlock();
        portable.SizeChanged += (_, _) => PositionPortableUnlock();
        portable.Closing += (_, _) =>
        {
            ClosePortableAppearance();
            StopPortableContrast();
        };
        portable.Closed += (_, _) =>
        {
            StopPortableInput();
            StopPortableFocusReset();
            StopPortableIdleTimer();
            portableHoverActive = false;
            portableHoverCue = null;
            portableHoverCueLayer = null;
            portableUnlock = null;
            portableAppearancePanel = null;
            portableHeader = null;
            portableToolbarButtons.Clear();
            portableLockTool = null;
            portablePinTool = null;
            portable = null;
            portablePlacement = null;
            portableContent = null;
            portableSurface = null;
            portableNowLine = null;
            portableClock = null;
            ClearPortableTimelineGuides();
            portableTaskScroll = null;
            portableTimelineScroll = null;
            portableLocked = false;
            portableShowCompleted = false;
            portableShowingTasks = false;
            portableRenderedPresentation = null;
            portableRenderedTasks = [];
            portableTimeOffset = null;
            portableTaskOffset = 0;
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
        portableLockTool = PortableTool("鼠标穿透", () => SetPortableLocked(!portableLocked));
        portablePinTool = PortableTool("置顶", TogglePortableTopmost);
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(portableLockTool);
        tools.Children.Add(portablePinTool);
        tools.Children.Add(PortableTool("外观", TogglePortableAppearance));
        tools.Children.Add(PortableTool("关闭", () => portable?.Close()));
        var glassStrip = new PortableGlassStrip(tools);
        DockPanel.SetDock(glassStrip, Dock.Right);
        controls.Children.Add(glassStrip);

        var title = PortableLabel("随身日程", 13);
        title.FontWeight = FontWeights.SemiBold;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.ToolTip = "拖动移动，双击铺满或还原";
        controls.Children.Add(title);
        var header = new Border { Padding = new(10, 6, 8, 6), CornerRadius = new(13, 13, 0, 0), Child = controls };
        // ButtonBase handles its own mouse down. This handler covers the label, gaps and padding.
        Point? dragOrigin = null;
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (portable is not { IsVisible: true } window || PortablePassThrough ||
                e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;
            e.Handled = true;
            if (e.ClickCount == 2) { portablePlacement?.TogglePortableFill(); return; }
            if (header.CaptureMouse()) dragOrigin = e.GetPosition(window);
        };
        header.MouseMove += (_, e) =>
        {
            if (dragOrigin is not { } origin || portable is not { IsVisible: true } window ||
                PortablePassThrough || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            var pointer = e.GetPosition(window);
            if (Math.Abs(pointer.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pointer.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            header.ReleaseMouseCapture();
            portablePlacement?.RestorePortableForDrag(window.PointToScreen(pointer));
            window.DragMove();
        };
        header.MouseLeftButtonUp += (_, _) => { if (header.IsMouseCaptured) header.ReleaseMouseCapture(); };
        header.LostMouseCapture += (_, _) => dragOrigin = null;
        header.SizeChanged += (_, _) => PositionPortableUnlock();
        DockPanel.SetDock(header, Dock.Top);
        portableHeader = header;
        return header;
    }

    internal void AddSettings(StackPanel body)
    {
        body.Children.Add(Text("桌面行为", 19, margin: new(0, 0, 0, 20)));
        var keep = new CheckBox
        {
            Content = "关闭窗口后保留在系统托盘",
            IsChecked = preferences.CloseToTray,
            Margin = new(0, 8, 0, 18)
        };
        body.Children.Add(keep);
        var feedback = Text("", 13);
        body.Children.Add(feedback);
        var save = Button("保存", () =>
        {
            var previous = preferences;
            preferences = preferences with { CloseToTray = keep.IsChecked == true };
            if (SavePreferences()) feedback.Text = "已保存";
            else
            {
                preferences = previous;
                feedback.Text = "保存失败";
            }
        });
        save.HorizontalAlignment = HorizontalAlignment.Left;
        body.Children.Add(save);
        body.Children.Add(Button("开启 / 关闭随身日程", TogglePortable));
        AddPortablePreferences(body);
    }

}
