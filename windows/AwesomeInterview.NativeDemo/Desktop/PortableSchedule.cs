using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AwesomeInterview.NativeDemo.Data;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class DesktopIntegration
{
    const double PortableHourHeight = 30;
    const double PortableTimeGutter = 64;
    const double PortableDayHeight = 24 * PortableHourHeight;
    const string PortableNowAccent = "#C62F3D";
    bool portableShowCompleted, portableShowingTasks;
    double? portableTimeOffset;
    double portableTaskOffset;
    DateTime portableRenderedDay;
    PortablePresentation? portableRenderedPresentation;
    IReadOnlyList<PortableTask> portableRenderedTasks = [];
    FrameworkElement[] portableHourLabels = [];
    Border[] portableHourRules = [];
    PortableTimelineLayout.TaskRange[] portableTimelineTasks = [];
    readonly List<FrameworkElement> portableTimelineHoverControls = new();
    TimeSpan portableTimelineGuideTime;
    double portableTimelineHoverWidth;
    double portableTimelineAvailableWidth, portableTimelineOccupiedWidth;
    readonly SolidColorBrush portableFullGridInk = new();

    readonly record struct PortablePresentation(DateTime Day, bool SignedIn, bool Online,
        bool ShowingTasks, bool ShowCompleted, bool TaskActionsBusy);

    readonly record struct PortableTask(EventData Event, TimeSpan StartTime, TimeSpan EndTime)
    {
        public string Title => Event.Title;
        public bool Completed => Event.CompletedAt != null;
        public string Hours => $"{StartTime:hh\\:mm}–{EndLabel}";
        string EndLabel => EndTime == TimeSpan.FromDays(1) ? "24:00" : EndTime.ToString(@"hh\:mm");
    }

    static Button CompactPortableButton(string label, Action action)
    {
        var button = Button(label, action);
        button.FontSize = 11;
        button.MinHeight = 25;
        button.Padding = new(7, 3, 7, 3);
        button.Margin = new(0);
        return button;
    }

    Button PortableButton(string label, Action action)
    {
        var button = CompactPortableButton(label, action);
        button.ContentTemplateSelector = PortableGlyphTemplateSelector.Instance;
        button.Background = PortableFill("#FFFFFF");
        button.BorderBrush = PortableOutline("#ABBAD0");
        button.Foreground = PortableInk("#263650");
        return button;
    }

    void UpdatePortableClock()
    {
        var now = CalendarProjection.Now;
        double y = now.TimeOfDay.TotalHours * PortableHourHeight;
        if (portableNowLine != null) Canvas.SetTop(portableNowLine, y);
        if (portableClock != null)
        {
            string time = now.ToString("HH:mm");
            if (portableClock.Text != time)
            {
                portableClock.Text = time;
                SchedulePortableContrast(resetCadence: false);
            }
            var slot = (Border)portableClock.Parent;
            Canvas.SetTop(slot, Math.Clamp(y - slot.Height / 2, 0, PortableDayHeight - slot.Height));
            UpdatePortableGuides(now.TimeOfDay);
        }
    }

    void UpdatePortableGuides(TimeSpan now)
    {
        portableTimelineGuideTime = now;
        bool faded = PortablePassThrough && !PortableTimelineInMotion;
        int hide = PortableTimelineLayout.NearestHourToHide(now);
        for (int hour = 0; hour < portableHourLabels.Length; hour++)
        {
            double opacity = faded ? PortableTimelineLayout.GuideOpacity(TimeSpan.FromHours(hour), now, portableTimelineTasks) : 1;
            var line = portableHourRules[hour];
            var label = portableHourLabels[hour];
            line.Opacity = label.Opacity = opacity;
            line.Visibility = opacity > 0 ? Visibility.Visible : Visibility.Hidden;
            label.Visibility = opacity > 0 && hour != hide ? Visibility.Visible : Visibility.Hidden;
        }
        if (portableNowLine == null) return;
        var extent = PortableTimelineLayout.Rule(portableTimelineAvailableWidth, portableTimelineOccupiedWidth);
        portableTimelineHoverWidth = PortableTimeGutter + extent.Width;
        double width = faded ? extent.Width : portableTimelineAvailableWidth;
        var mask = faded ? CreatePortableRuleFade(extent.SolidFraction) : null;
        var gridInk = faded ? PortableOutline("#DFE7F1") : portableFullGridInk;
        foreach (var rule in portableHourRules)
        {
            rule.Width = width;
            rule.OpacityMask = mask;
            rule.Background = gridInk;
        }
        portableNowLine.Width = width;
        portableNowLine.OpacityMask = mask;
    }

    internal void RefreshPortable(bool centerCurrentTime = false)
    {
        if (portableContent == null) return;
        var today = CalendarProjection.Now.Date;
        bool signedIn = shell.Repository.Current != null;
        var workStart = schedule.WorkTime(false);
        var workEnd = schedule.WorkTime(true);
        var tasksForToday = schedule.EventsFor(today).Where(item => item.CancelledAt == null).Select(item =>
        {
            var segment = CalendarProjection.Segment(item, today, workStart, workEnd)!.Value;
            return new PortableTask(item, segment.Start - today, segment.End - today);
        }).ToList();
        var presentation = new PortablePresentation(today, signedIn, shell.Repository.Online,
            portableShowingTasks, portableShowCompleted, portableShowingTasks && shell.Repository.Busy);
        if (portableRenderedPresentation == presentation && portableRenderedTasks.SequenceEqual(tasksForToday))
        {
            if (centerCurrentTime)
            {
                portableTimeOffset = null;
                if (portableTimelineScroll is { IsLoaded: true, Content: Canvas axis } timeline)
                    PositionPortableTimeline(timeline, axis, null, animate: true);
            }
            return;
        }
        portableRenderedPresentation = presentation;
        portableRenderedTasks = tasksForToday;

        if (portableRenderedDay != today)
        {
            portableRenderedDay = today;
            portableTimeOffset = null;
            portableTaskOffset = 0;
        }
        else
        {
            if (portableTimelineScroll is { IsLoaded: true, Content: Canvas axis } timeline)
                portableTimeOffset = timeline.VerticalOffset - axis.Margin.Top;
            if (portableTaskScroll is { IsLoaded: true } tasks) portableTaskOffset = tasks.VerticalOffset;
        }

        StopPortableTimelineMotion();
        portableContent.Children.Clear();
        portableTimelineScroll = null;
        portableTaskScroll = null;
        portableNowLine = null;
        portableClock = null;
        ClearPortableTimelineGuides();
        int completeCount = tasksForToday.Count(item => item.Completed);
        var visible = tasksForToday.Where(item => portableShowCompleted || !item.Completed).ToList();

        var summary = BuildPortableSummary(today, tasksForToday.Count - completeCount, completeCount, signedIn);
        DockPanel.SetDock(summary, Dock.Top);
        portableContent.Children.Add(summary);
        if (!signedIn)
        {
            portableContent.Children.Add(PortableEmptyState("登录后查看今日日程", "登录账号", () => shell.Navigate(() =>
            {
                shell.RestoreMain();
                shell.ShowAccount();
            })));
        }
        else if (portableShowingTasks)
        {
            portableContent.Children.Add(BuildPortableTaskList(visible, completeCount));
        }
        else
        {
            var page = new Grid();
            page.Children.Add(BuildPortableTimeline(visible, centerCurrentTime));
            if (visible.Count == 0)
            {
                var empty = PortableDayEmptyState(completeCount);
                empty.VerticalAlignment = VerticalAlignment.Center;
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                page.Children.Add(empty);
                portableTimelineHoverControls.Add(empty);
            }
            portableContent.Children.Add(page);
        }
        SchedulePortableContrast();
    }

    void ClearPortableTimelineGuides()
    {
        portableHourLabels = [];
        portableHourRules = [];
        portableTimelineTasks = [];
        portableTimelineHoverControls.Clear();
        portableTimelineHoverWidth = 0;
        portableTimelineAvailableWidth = portableTimelineOccupiedWidth = 0;
    }

    FrameworkElement BuildPortableSummary(DateTime today, int pending, int completed, bool signedIn)
    {
        var row = new DockPanel();
        if (signedIn)
        {
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var taskTab = PortableButton($"当日任务 · {pending}", () => ShowPortableTasks(true));
            taskTab.Margin = new(4, 0, 0, 0);
            var scheduleTab = PortableButton("时间表", () => ShowPortableTasks(false));
            foreach (var button in new[] { scheduleTab, taskTab }) tabs.Children.Add(button);
            var selectedView = portableShowingTasks ? taskTab : scheduleTab;
            selectedView.Background = PortableFill("#EBF2FF");
            selectedView.Foreground = PortableInk("#2357CE");
            AutomationProperties.SetHelpText(selectedView, "当前视图");
            if (completed > 0)
            {
                var toggle = new CheckBox
                {
                    Content = PortableLabel($"已完成 · {completed}", 11), IsChecked = portableShowCompleted,
                    VerticalAlignment = VerticalAlignment.Center, Padding = new(3, 1, 3, 1), Margin = new(0, 0, 5, 0)
                };
                AutomationProperties.SetName(toggle, "显示已完成任务");
                toggle.Click += (_, _) =>
                {
                    portableShowCompleted = toggle.IsChecked == true;
                    RefreshPortable();
                };
                tabs.Children.Insert(0, toggle);
            }

            var glassStrip = new PortableGlassStrip(tabs);
            DockPanel.SetDock(glassStrip, Dock.Right);
            row.Children.Add(glassStrip);
            portableTimelineHoverControls.Add(glassStrip);
        }

        var date = PortableLabel(today.ToString("M月d日 dddd"), 14);
        date.FontWeight = FontWeights.SemiBold;
        date.HorizontalAlignment = HorizontalAlignment.Left;
        date.VerticalAlignment = VerticalAlignment.Center;
        if (signedIn && !shell.Repository.Online) date.ToolTip = "离线，修改将保存在本机";
        row.Children.Add(date);
        portableTimelineHoverControls.Add(date);
        var summary = PortableSection(row, new(7, 4, 7, 4));
        summary.Margin = new(0, 0, 0, 4);
        return summary;
    }

    void ShowPortableTasks(bool show)
    {
        if (portableShowingTasks == show) return;
        portableShowingTasks = show;
        RefreshPortable();
    }

    FrameworkElement BuildPortableTimeline(IReadOnlyList<PortableTask> tasks, bool returnToNow)
    {
        var timeline = new Canvas { Height = PortableDayHeight, Background = Brushes.Transparent, ClipToBounds = true };
        portableTimelineTasks = tasks.Select(task => new PortableTimelineLayout.TaskRange(task.StartTime, task.EndTime)).ToArray();
        var hourRules = portableHourRules = new Border[24];
        portableHourLabels = new FrameworkElement[24];
        for (int hour = 0; hour < 24; hour++)
        {
            var label = PortableText($"{hour:00}:00", 10, "#52647D");
            label.TextAlignment = TextAlignment.Center;
            var tick = new PortableGlassStrip(new Border
            {
                Padding = new(4, 3, 4, 3), Child = label
            }, showRim: false) { Width = 44 };
            Canvas.SetTop(tick, Math.Max(0, hour * PortableHourHeight - 9));
            Canvas.SetLeft(tick, 3);
            timeline.Children.Add(tick);
            portableHourLabels[hour] = tick;
            var rule = new Border { Height = 1, Background = PortableOutline("#DFE7F1"), IsHitTestVisible = false };
            Canvas.SetTop(rule, hour * PortableHourHeight);
            Canvas.SetLeft(rule, PortableTimeGutter);
            timeline.Children.Add(rule);
            hourRules[hour] = rule;
        }

        var timeView = new QuietScrollViewer
        {
            Content = timeline, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        portableTimelineScroll = timeView;
        timeView.PreviewMouseWheel += (_, _) => StopPortableTimelineMotion();
        timeView.PreviewMouseDown += (_, _) => StopPortableTimelineMotion();
        timeView.PreviewKeyDown += (_, _) => StopPortableTimelineMotion();
        var body = new DockPanel();
        var overflow = PortableButton("", () => ShowPortableTasks(true));
        overflow.Visibility = Visibility.Collapsed;
        overflow.HorizontalAlignment = HorizontalAlignment.Right;
        overflow.Margin = new(0, 1, 0, 3);
        portableTimelineHoverControls.Add(overflow);
        DockPanel.SetDock(overflow, Dock.Top);
        body.Children.Add(overflow);
        body.Children.Add(timeView);

        var laneEnds = new List<double>();
        var cards = new List<(Button Button, PortableGlassStrip Label, int Lane)>();
        foreach (var task in tasks.OrderBy(item => item.StartTime).ThenBy(item => item.EndTime))
        {
            const double minimumHeight = 19;
            double height = Math.Max(minimumHeight, (task.EndTime - task.StartTime).TotalHours * PortableHourHeight);
            double top = Math.Min(task.StartTime.TotalHours * PortableHourHeight, PortableDayHeight - height);
            int lane = laneEnds.FindIndex(end => end <= top);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(top + height);
            }
            else laneEnds[lane] = top + height;

            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new PortableGlyphText
            {
                Text = task.Title, FontSize = 11, FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center,
                Foreground = PortableInk("#263650")
            });
            if (height >= 37)
            {
                var hours = PortableText(task.Hours, 10, "#52647D");
                hours.TextAlignment = TextAlignment.Center;
                label.Children.Add(hours);
            }

            var labelGlass = new PortableGlassStrip(new Border
            {
                Padding = new(6, height >= 37 ? 4 : 2, 6, height >= 37 ? 4 : 2), Child = label
            }, roundedPanel: true)
            {
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var card = PortableButton("", () => ShowTaskDetail(task));
            card.Content = BuildPortableEventGlass(labelGlass);
            card.ToolTip = $"{task.Title}\n{task.Hours}";
            card.MinHeight = 0;
            card.Height = height;
            card.Padding = new(4, 1, 4, 1);
            card.ClipToBounds = true;
            card.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            card.VerticalContentAlignment = VerticalAlignment.Stretch;
            card.Background = PortableFill(task.Completed ? "#EEF0F3" : "#EDF3FF");
            card.BorderBrush = PortableOutline(task.Completed ? "#A1ADBE" : "#869AB8");
            AutomationProperties.SetName(card, $"{task.Title}，{task.Hours}");
            Canvas.SetTop(card, top);
            timeline.Children.Add(card);
            cards.Add((card, labelGlass, lane));
        }

        var nowInk = PortableInk(PortableNowAccent);
        var nowLine = new Border { Height = 1.5, Background = nowInk, IsHitTestVisible = false };
        Canvas.SetLeft(nowLine, PortableTimeGutter);
        timeline.Children.Add(nowLine);
        portableNowLine = nowLine;
        var clock = new TextBlock
        {
            Foreground = nowInk, FontSize = 15, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var clockSlot = new Border { Child = clock, Width = 54, Height = 22, IsHitTestVisible = false };
        Canvas.SetLeft(clockSlot, 4);
        timeline.Children.Add(clockSlot);
        portableClock = clock;
        UpdatePortableClock();

        double previousAvailable = -1;
        void Fit()
        {
            if (portableTimelineScroll != timeView) return;
            const double gap = 3;
            double available = Math.Max(0, timeline.ActualWidth - PortableTimeGutter - 7);
            int visibleLanes = Math.Clamp((int)((available + gap) / 100), 1, Math.Max(1, laneEnds.Count));
            double width = Math.Min(180, Math.Max(0, (available - gap * (visibleLanes - 1)) / visibleLanes));
            if (available != previousAvailable)
            {
                previousAvailable = available;
                portableTimelineAvailableWidth = available;
                portableTimelineOccupiedWidth = laneEnds.Count == 0 ? Math.Min(128, available) : width * visibleLanes + gap * (visibleLanes - 1);
                UpdatePortableGuides(portableTimelineGuideTime);
            }
            int hidden = 0;
            foreach (var (card, label, lane) in cards)
            {
                bool fits = lane < visibleLanes;
                card.Visibility = fits ? Visibility.Visible : Visibility.Collapsed;
                if (!fits) { hidden++; continue; }
                card.Width = width;
                Canvas.SetLeft(card, PortableTimeGutter + lane * (width + gap));
                double top = Canvas.GetTop(card);
                double room = Math.Max(0, card.Height - 4);
                double viewportTop = timeView.VerticalOffset - timeline.Margin.Top;
                double start = Math.Clamp(viewportTop - top, 0, room);
                double end = Math.Clamp(viewportTop + timeView.ViewportHeight - top, start, room);
                label.Margin = new(0, start, 0, room - end);
            }
            overflow.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (hidden > 0) overflow.Content = $"另有 {hidden} 项重叠 · 查看任务";
        }
        timeline.SizeChanged += (_, _) => Fit();
        timeView.ScrollChanged += (_, args) => { if (args.OriginalSource == timeView) Fit(); };
        timeView.Loaded += (_, _) => timeView.Dispatcher.InvokeAsync(() =>
        {
            if (!timeView.IsLoaded || portableTimelineScroll != timeView) return;
            PositionPortableTimeline(timeView, timeline, returnToNow ? portableTimeOffset ?? 0 : portableTimeOffset);
            if (returnToNow) PositionPortableTimeline(timeView, timeline, null, animate: true);
            Fit();
        }, DispatcherPriority.Loaded);
        var section = PortableSection(body, new(4));
        section.Margin = new(0);
        return section;
    }

    static Brush? CreatePortableRuleFade(double solidFraction)
    {
        if (solidFraction >= 1) return null;
        var brush = new LinearGradientBrush
        {
            StartPoint = new(0, .5), EndPoint = new(1, .5),
            GradientStops = { new(Colors.White, 0), new(Colors.White, solidFraction), new(Colors.Transparent, 1) }
        };
        brush.Freeze();
        return brush;
    }

    FrameworkElement BuildPortableTaskList(IReadOnlyList<PortableTask> tasks, int completeCount)
    {
        if (tasks.Count == 0) return PortableDayEmptyState(completeCount);

        var list = new StackPanel { Margin = new(4, 3, 7, 3) };
        foreach (var group in tasks.GroupBy(task => task.Completed).OrderBy(group => group.Key))
        {
            var caption = PortableLabel(group.Key ? "今日已完成" : "当日任务", 12, new(2, 2, 0, 5));
            caption.FontWeight = FontWeights.SemiBold;
            list.Children.Add(caption);
            foreach (var task in group)
            {
                var row = new DockPanel { Margin = new(0, 0, 0, 4) };
                var action = PortableButton(task.Completed ? "恢复" : "完成", () => ToggleTask(task));
                action.Margin = new(4, 0, 0, 0);
                action.IsEnabled = !shell.Repository.Busy;
                DockPanel.SetDock(action, Dock.Right);
                row.Children.Add(action);

                var detail = PortableButton("", () => ShowTaskDetail(task));
                var label = new DockPanel();
                var hours = PortableText(task.Hours, 10, "#52647D", new(8, 0, 0, 0));
                hours.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(hours, Dock.Right);
                label.Children.Add(hours);
                label.Children.Add(new PortableGlyphText { Text = task.Title, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = PortableInk("#263650") });
                detail.Content = label;
                detail.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                detail.ToolTip = task.Title;
                AutomationProperties.SetName(detail, $"{task.Title}，{task.Hours}");
                row.Children.Add(detail);
                list.Children.Add(row);
            }
        }
        var scroll = new QuietScrollViewer
        {
            Content = list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        scroll.Loaded += (_, _) => scroll.ScrollToVerticalOffset(portableTaskOffset);
        portableTaskScroll = scroll;
        var section = PortableSection(scroll, new(4));
        section.Margin = new(0);
        return section;
    }

    Border PortableDayEmptyState(int completeCount) => PortableEmptyState(
        completeCount > 0 ? "今日任务已全部完成，真棒！" : "今天没有安排，留点时间给自己。",
        "来点 Idea？", () => shell.Navigate(() =>
        {
            shell.RestoreMain();
            schedule.ShowIdeas();
            schedule.EditIdea(null, portable);
        }));

    Border PortableEmptyState(string title, string actionLabel, Action onAction)
    {
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var heading = PortableLabel(title, 15);
        heading.FontWeight = FontWeights.SemiBold;
        heading.TextAlignment = TextAlignment.Center;
        body.Children.Add(heading);
        var action = PortableButton(actionLabel, onAction);
        action.HorizontalAlignment = HorizontalAlignment.Center;
        action.Margin = new(0, 8, 0, 0);
        action.Foreground = PortableInk("#2357CE");
        action.Background = PortableFill("#EBF2FF");
        body.Children.Add(action);
        var section = PortableSection(body, new(14));
        section.Margin = new(0);
        return section;
    }
}
