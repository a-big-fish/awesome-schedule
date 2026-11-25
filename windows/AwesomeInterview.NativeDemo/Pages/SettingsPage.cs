using System;
using System.Windows;
using System.Windows.Controls;
using AwesomeInterview.NativeDemo.Data;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal sealed class SettingsPage(MainWindow shell, WorkspaceProfile accountProfile, DesktopIntegration desktop) : WorkspacePage(shell)
{
    string category = "账号";

    internal void ShowSettings()
    {
        Begin(PageRoute.Settings, "设置与版本", "偏好设置", "账号、日程与桌面行为");
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new() { Width = new(150) });
        layout.ColumnDefinitions.Add(new());

        var categories = new StackPanel { Margin = new(0, 0, 24, 0) };
        foreach (string label in new[] { "账号", "日程偏好", "桌面行为", "关于与版本" })
        {
            var entry = Button(label, () =>
            {
                category = label;
                ShowSettings();
            });
            if (label == category) entry.Background = Brush("#E9F0FF");
            categories.Children.Add(entry);
        }
        layout.Children.Add(categories);

        var body = new StackPanel { MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(body, 1);
        layout.Children.Add(body);
        switch (category)
        {
            case "账号":
                ShowProfileSettings(body);
                break;
            case "日程偏好":
                ShowWorkdaySettings(body);
                break;
            case "桌面行为":
                desktop.AddSettings(body);
                break;
            default:
                body.Children.Add(Text("Awesome Interview", 22));
                body.Children.Add(Text("Windows 1.6.0-W Preview 25 · WPF / .NET 10", 14, "#64748B", new(0, 12, 0, 0)));
                break;
        }
        Content.Children.Add(Surface(layout, new(26)));
    }

    void ShowWorkdaySettings(StackPanel body)
    {
        body.Children.Add(Text("默认工作时段", 19));
        body.Children.Add(Text("只选日期的新日程使用该起止时刻。格式为 HH:mm，同一天内结束须晚于开始。",
            13, "#64748B", new(0, 10, 0, 18)));
        if (accountProfile.Value is not { } profile)
        {
            LoadProfile(body, "正在读取…", "连接后可设置工作时段。", "重新读取");
            return;
        }

        body.Children.Add(Text("开始时间"));
        var start = new TextBox { Text = profile.WorkdayStart, MaxLength = 5 };
        body.Children.Add(start);
        body.Children.Add(Text("结束时间"));
        var end = new TextBox { Text = profile.WorkdayEnd, MaxLength = 5 };
        body.Children.Add(end);
        var feedback = Text("", 13);
        body.Children.Add(feedback);
        var save = Button("保存工作时段", () => Run(async () =>
        {
            try
            {
                string startValue = start.Text.Trim();
                string endValue = end.Text.Trim();
                var result = await accountProfile.SaveHoursAsync(accountProfile.Value?.EffectiveName ?? profile.EffectiveName,
                    startValue, endValue);
                feedback.Text = result.WorkdayStart == startValue && result.WorkdayEnd == endValue
                    ? "已同步，下次新增日程生效。" : "服务器未保存该时段，请更新服务端后重试。";
            }
            catch (Exception error) when (WorkspaceRepository.Expected(error))
            {
                feedback.Text = WorkspaceRepository.Explain(error);
            }
        }));
        save.IsEnabled = Repository.Online;
        save.HorizontalAlignment = HorizontalAlignment.Left;
        body.Children.Add(save);
    }

    void ShowProfileSettings(StackPanel body)
    {
        body.Children.Add(Text("账号", 19, margin: new(0, 24, 0, 16)));
        if (Repository.Current == null)
        {
            body.Children.Add(Button("登录账号", OpenAccount));
            return;
        }
        if (accountProfile.Value is not { } profile)
        {
            LoadProfile(body, "正在获取账号信息…", "连接后可查看和修改账号信息。", "重新加载账号信息");
            return;
        }

        body.Children.Add(Text("邮箱（不可修改）", 12, "#8492A6"));
        body.Children.Add(Text(profile.Email ?? "未绑定邮箱", 14, margin: new(0, 6, 0, 18)));
        body.Children.Add(Text("公开显示的用户名"));
        var name = new TextBox { Text = profile.EffectiveName, MaxLength = 40 };
        body.Children.Add(name);
        if (string.IsNullOrWhiteSpace(profile.DisplayName))
            body.Children.Add(Text("账号尚未设置昵称，保存后会同步此用户名。", 12, "#64748B"));
        body.Children.Add(Button("重新读取账号资料", () =>
        {
            accountProfile.Invalidate();
            ShowSettings();
        }));
        var save = Button("保存用户名", () => Run(async () =>
        {
            await accountProfile.SaveNameAsync(name.Text.Trim());
            Status.Text = "用户名已同步，广场将显示新名称。";
        }));
        save.IsEnabled = Repository.Online;
        body.Children.Add(save);
    }

    void LoadProfile(StackPanel body, string loadingText, string offlineText, string retryText)
    {
        var message = Text(Repository.Online ? loadingText : offlineText, 14, "#64748B");
        body.Children.Add(message);
        if (!Repository.Online) return;

        var currentView = Content;
        Run(async () =>
        {
            try
            {
                await accountProfile.LoadAsync();
                if (Route == PageRoute.Settings && Content == currentView && accountProfile.Value != null)
                    ShowSettings();
            }
            catch (Exception error) when (WorkspaceRepository.Expected(error))
            {
                message.Text = WorkspaceRepository.Explain(error);
                body.Children.Add(Button(retryText, ShowSettings));
            }
        });
    }
}
