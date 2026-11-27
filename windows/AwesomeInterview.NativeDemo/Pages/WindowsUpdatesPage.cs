using System;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Velopack;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class SettingsPage
{
    readonly WindowsUpdater updater = new();
    bool updateBusy;

    void ShowWindowsUpdates(StackPanel body)
    {
        body.Children.Add(Text("Awesome Schedule", 22));
        body.Children.Add(Text($"当前版本 · Windows {WindowsUpdater.Version}", 14, "#64748B", new(0, 12, 0, 16)));

        var actions = new WrapPanel();
        var check = new Button { Content = "检查更新", IsEnabled = updater.UnavailableReason == null };
        var install = new Button { Content = "下载并重启", Visibility = Visibility.Collapsed };
        actions.Children.Add(check);
        actions.Children.Add(install);
        actions.Children.Add(Button("打开下载页", () => OpenLink(updater.DownloadPage)));
        body.Children.Add(actions);

        var feedback = Text(updater.UnavailableReason ?? "", 14, "#52647D", new(0, 12, 0, 8));
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
        body.Children.Add(feedback);
        var progressBar = new ProgressBar
        {
            Minimum = 0, Maximum = 100, Height = 7, Margin = new(0, 4, 0, 10),
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(progressBar, "更新下载进度");
        body.Children.Add(progressBar);
        var releaseHeading = Text("", 16, margin: new(0, 10, 0, 6));
        var releaseNotes = Text("", 14);
        var notesBox = new ScrollViewer
        {
            Content = releaseNotes, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(notesBox, "更新说明");
        body.Children.Add(releaseHeading);
        body.Children.Add(notesBox);

        UpdateInfo? available = null;
        void ShowStatus(string message)
        {
            feedback.Text = message;
            UIElementAutomationPeer.FromElement(feedback)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        check.Click += async (_, _) =>
        {
            if (updateBusy)
            {
                ShowStatus("更新仍在后台处理中，请稍候。");
                return;
            }
            updateBusy = true;
            check.IsEnabled = false;
            install.Visibility = notesBox.Visibility = Visibility.Collapsed;
            releaseHeading.Text = "";
            available = null;
            ShowStatus("正在检查更新…");
            try
            {
                available = await updater.CheckAsync();
                if (available == null)
                {
                    ShowStatus("已是当前渠道的最新版本。");
                    return;
                }
                var release = available.TargetFullRelease;
                releaseHeading.Text = $"新版本 {release.Version}";
                releaseNotes.Text = string.IsNullOrWhiteSpace(release.NotesMarkdown)
                    ? "此版本没有附带更新说明。"
                    : release.NotesMarkdown;
                install.Visibility = notesBox.Visibility = Visibility.Visible;
                ShowStatus("更新可用。");
            }
            catch (Exception error) when (WindowsUpdater.IsExpected(error))
            {
                ShowStatus(WindowsUpdater.ErrorMessage(error));
            }
            finally
            {
                updateBusy = false;
                check.IsEnabled = true;
            }
        };

        install.Click += async (_, _) =>
        {
            if (updateBusy || available is not { } update) return;
            updateBusy = true;
            check.IsEnabled = install.IsEnabled = false;
            progressBar.Value = 0;
            progressBar.Visibility = Visibility.Visible;
            ShowStatus("正在后台下载更新…");
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                IProgress<int> progress = new Progress<int>(value => progressBar.Value = value);
                await updater.DownloadAsync(update, progress.Report, deadline.Token);
                if (!body.IsLoaded)
                {
                    Status.Text = "更新已下载。保存正在编辑的内容后，可在设置中重启更新。";
                    return;
                }
                ShowStatus("正在保存本机数据，准备重启…");
                await ((MainWindow)Owner).RestartForUpdateAsync(() => updater.Apply(update));
            }
            catch (Exception error) when (WindowsUpdater.IsExpected(error))
            {
                ShowStatus(WindowsUpdater.ErrorMessage(error));
            }
            finally
            {
                updateBusy = false;
                progressBar.Visibility = Visibility.Collapsed;
                check.IsEnabled = install.IsEnabled = true;
            }
        };
    }
}
