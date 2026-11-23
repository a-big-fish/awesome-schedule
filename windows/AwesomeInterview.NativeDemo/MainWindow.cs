using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AwesomeInterview.NativeDemo.Data;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

public sealed partial class MainWindow : Window
{
    internal WorkspaceRepository Repository { get; } = new();
    internal ScrollViewer Viewport { get; }
    internal PageRoute Route { get; private set; } = PageRoute.Today;
    internal bool FormDirty { get; set; }
    internal TextBlock Status { get; } = new()
    {
        Foreground = Brush("#64748B"), TextWrapping = TextWrapping.Wrap,
        FontSize = 12, VerticalAlignment = VerticalAlignment.Center
    };

    readonly TextBlock heading = new() { FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 6) };
    readonly TextBlock subtitle = new() { Foreground = Brush("#64748B"), Margin = new(0, 0, 0, 24) };
    readonly TextBlock connectionLabel = new()
    {
        Text = "尚未登录", Foreground = Brush("#64748B"), FontSize = 12, TextWrapping = TextWrapping.Wrap
    };
    readonly List<Button> nav = new();
    readonly Button adminNav;
    Button syncButton = null!;
    readonly WorkspaceProfile profile;
    readonly AccountPage account;
    readonly AdminPage admin;
    readonly SchedulePages schedule;
    readonly ExperienceWorkspace experiences;
    readonly InterviewWorkspace interviews;
    readonly SettingsPage settings;
    readonly DesktopIntegration desktop;

    public MainWindow()
    {
        Title = "Awesome Interview · Windows 1.6.0-W Preview 8";
        Width = 1280; Height = 850; MinWidth = 940; MinHeight = 640;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush("#F6F8FC");
        Foreground = Brush("#192C46");
        FontFamily = new("Microsoft YaHei UI");
        FontSize = 14;

        Viewport = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false
        };
        profile = new WorkspaceProfile(Repository);
        var timePicker = new ScheduleTimePicker(this, profile);
        schedule = new SchedulePages(this, profile, timePicker);
        account = new AccountPage(this, () => schedule.ShowTasks(false));
        admin = new AdminPage(this);
        experiences = new ExperienceWorkspace(this, timePicker);
        interviews = new InterviewWorkspace(this, schedule, experiences);
        desktop = new DesktopIntegration(this, schedule);
        settings = new SettingsPage(this, profile, desktop);

        var shell = new Grid();
        shell.ColumnDefinitions.Add(new() { Width = new(224) });
        shell.ColumnDefinitions.Add(new());
        var links = new StackPanel();
        shell.Children.Add(BuildSidebar(links));
        AddNav(links, "今日待办", "\uE8FD", () => schedule.ShowTasks(false));
        AddNav(links, "日历与日程", "\uE787", schedule.ShowCalendar);
        AddNav(links, "添加日程", "\uE710", schedule.ShowAdd);
        AddNav(links, "我的面试", "\uE8D4", interviews.ShowInterviews);
        AddNav(links, "面经广场", "\uE774", experiences.ShowPlaza);
        AddNav(links, "我的面经", "\uE8A5", experiences.ShowExperience);
        AddNav(links, "设置与版本", "\uE713", settings.ShowSettings);
        adminNav = AddNav(links, "管理员监控", "\uE9D9", admin.ShowAdmin);
        adminNav.Visibility = Visibility.Collapsed;

        var workspace = BuildWorkspace();
        Grid.SetColumn(workspace, 1);
        shell.Children.Add(workspace);
        Content = shell;

        desktop.Initialize(settings.ShowSettings);
        Repository.Changed += OnRepositoryChanged;
        Closed += (_, _) =>
        {
            Repository.Changed -= OnRepositoryChanged;
            Repository.Dispose();
        };
        Loaded += (_, _) => Run(Repository.RestoreAsync);
        schedule.ShowTasks(false);
    }

    Border BuildSidebar(StackPanel links)
    {
        var side = new DockPanel { Margin = new(18, 26, 18, 20) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(8, 0, 0, 30) };
        brand.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo2.png")),
            Width = 34, Height = 34, Margin = new(0, 0, 10, 0)
        });
        brand.Children.Add(new TextBlock { Text = "Awesome\nInterview", FontSize = 17, FontWeight = FontWeights.SemiBold });
        DockPanel.SetDock(brand, Dock.Top);
        side.Children.Add(brand);

        var bottom = new StackPanel { Margin = new(12, 18, 8, 0) };
        bottom.Children.Add(connectionLabel);
        bottom.Children.Add(Button("账号与同步", () => Navigate(ShowAccount)));
        DockPanel.SetDock(bottom, Dock.Bottom);
        side.Children.Add(bottom);
        side.Children.Add(links);
        return new Border
        {
            Background = Brushes.White, BorderBrush = Brush("#E4EAF2"),
            BorderThickness = new(0, 0, 1, 0), Child = side
        };
    }

    DockPanel BuildWorkspace()
    {
        var panel = new DockPanel { Margin = new(32, 28, 32, 18) };
        var top = new StackPanel();
        top.Children.Add(heading);
        top.Children.Add(subtitle);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        var footer = new DockPanel { Margin = new(0, 14, 0, 0) };
        syncButton = Button("同步", () =>
        {
            if (Repository.RequiresSignIn) Navigate(ShowAccount);
            else Run(Repository.SynchronizeAsync);
        });
        DockPanel.SetDock(syncButton, Dock.Right);
        footer.Children.Add(syncButton);
        footer.Children.Add(Status);
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);

        var paging = new StackPanel { Orientation = Orientation.Horizontal };
        paging.Children.Add(Button("上一屏", () => Viewport.ScrollToVerticalOffset(
            Math.Max(0, Viewport.VerticalOffset - Viewport.ViewportHeight))));
        paging.Children.Add(Button("下一屏", () => Viewport.ScrollToVerticalOffset(
            Viewport.VerticalOffset + Viewport.ViewportHeight)));
        DockPanel.SetDock(paging, Dock.Bottom);
        panel.Children.Add(paging);
        Viewport.ScrollChanged += (_, args) =>
        {
            if (args.OriginalSource != Viewport) return;
            paging.Visibility = Route != PageRoute.Calendar && Viewport.ScrollableHeight > 1
                ? Visibility.Visible : Visibility.Collapsed;
            experiences.OnViewportScroll(args);
        };
        panel.Children.Add(Viewport);
        return panel;
    }

    Button AddNav(Panel parent, string title, string icon, Action action)
    {
        var button = Button(title, () => Navigate(action));
        button.Tag = title;
        button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Padding = new(14, 12, 14, 12);
        button.Margin = new(0, 3, 0, 3);
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = icon, FontFamily = new("Segoe MDL2 Assets"), FontSize = 17, Width = 30,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock { Text = title });
        button.Content = row;
        nav.Add(button);
        parent.Children.Add(button);
        return button;
    }

    internal void BeginPage(PageRoute route, string? navigation, string title, string description, StackPanel? content = null)
    {
        Route = route;
        if (navigation != null)
        {
            foreach (var button in nav)
            {
                bool active = (string)button.Tag == navigation;
                button.Background = active ? Brush("#E9F0FF") : Brushes.Transparent;
                button.Foreground = active ? Brush("#2357CE") : Brush("#52647D");
                button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
        Viewport.Content = content;
        Viewport.ScrollToTop();
        heading.Text = title;
        subtitle.Text = description;

        if (route == PageRoute.Admin) return;
        var timer = Stopwatch.StartNew();
        var api = Repository.Api;
        string node = route switch
        {
            PageRoute.Calendar or PageRoute.Today => "calendar",
            PageRoute.ScheduleEditor => "schedule_new",
            PageRoute.Plaza => "plaza",
            PageRoute.Experiences => "experiences",
            PageRoute.NoteEditor or PageRoute.ExperienceDetail => "editor",
            PageRoute.NoteExport => "export",
            PageRoute.Interviews => "interviews",
            PageRoute.Resume => "resume_file",
            PageRoute.Ideas => "ideas",
            PageRoute.Settings => "profile",
            _ => "mobile"
        };
        Dispatcher.InvokeAsync(() =>
        {
            if (api == Repository.Api) api?.RecordPage(node, timer.Elapsed.TotalMilliseconds);
        }, DispatcherPriority.Loaded);
    }

    internal void ShowAccount() => account.ShowAccount();

    internal void Navigate(Action action)
    {
        if (CanLeaveForm()) action();
    }

    internal async void Run(Func<Task> work)
    {
        try { await work(); }
        catch (Exception error) when (WorkspaceRepository.Expected(error))
        {
            Status.Text = WorkspaceRepository.Explain(error);
        }
    }

    void OnRepositoryChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(OnRepositoryChanged);
            return;
        }

        bool isAdmin = Repository.Online && Repository.Api?.Session?.IsAdmin == true;
        adminNav.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        if (Route == PageRoute.Admin && !isAdmin)
            BeginPage(PageRoute.Admin, null, "管理员监控", "需要管理员身份并保持在线连接。");

        string queued = Repository.PendingSyncCount > 0 ? $" · 待同步 {Repository.PendingSyncCount} 项" : "";
        Status.Text = Repository.Busy || Repository.SyncBusy ? "正在后台同步，仍可浏览当前页面…" : Repository.Message;
        connectionLabel.Text = Repository.Current == null
            ? "尚未登录" : (Repository.Online ? "账号已连接" : "离线 · 缓存可用") + queued;
        syncButton.Content = Repository.RequiresSignIn ? "重新登录" : "同步";
        profile.UpdateFromRepository();
        desktop.RefreshPortable();
        Run(desktop.CheckRemindersAsync);

        if (FormDirty) return;
        switch (Route)
        {
            case PageRoute.Today:
            case PageRoute.Calendar:
            case PageRoute.CalendarDay:
            case PageRoute.Ideas:
                schedule.Refresh(Route);
                break;
            case PageRoute.Experiences:
                experiences.ShowExperience();
                break;
            case PageRoute.Interviews:
                interviews.ShowInterviews();
                break;
        }
    }
}
