using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AwesomeInterview.NativeDemo.Data;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal sealed class AccountPage(MainWindow shell, Action showToday) : WorkspacePage(shell)
{
    internal void ShowAccount() => ShowAccount(false);

    void ShowAccount(bool manual)
    {
        Begin(PageRoute.Account, "", "账号与同步", "使用与 Web 相同的账号");
        if (Repository.Current != null && !manual && !Repository.RequiresSignIn)
        {
            Paragraph(Repository.Online ? "账号已连接，切换页面和日期直接使用本机数据。"
                : "当前显示本机缓存。连接恢复后可以同步最新内容。");
            Paragraph($"缓存更新：{Repository.Current.ServerTime.LocalDateTime:yyyy-MM-dd HH:mm}\n服务地址：{Repository.Api?.Server}");
            Content.Children.Add(Button("同步最新数据", () => Run(Repository.SynchronizeAsync)));
            Content.Children.Add(Button("重新登录", () => ShowAccount(true)));
            AddLogoutButton(Content);
            return;
        }

        var view = Content;
        var body = new StackPanel { MaxWidth = 490, HorizontalAlignment = HorizontalAlignment.Left };
        if (Repository.RequiresSignIn)
            body.Children.Add(Text("登录已过期，重新登录后可继续同步。", 14, "#B42332", new(0, 0, 0, 14)));
        const string defaultServer = "https://schedule.hunaochufang.xyz/";
        var server = new TextBox { Text = Repository.Api?.Server.AbsoluteUri ?? defaultServer, MinWidth = 400 };
        var name = new TextBox();
        var password = new PasswordBox { Padding = new(12), Margin = new(0, 8, 0, 10), FontSize = 16 };
        var remember = new CheckBox { Content = "在此电脑记住密码", Margin = new(0, 0, 0, 12) };
        body.Children.Add(Text("服务地址")); body.Children.Add(server);
        body.Children.Add(Text("邮箱或用户名")); body.Children.Add(name);
        body.Children.Add(Text("密码")); body.Children.Add(password);
        body.Children.Add(remember);
        System.Windows.Automation.AutomationProperties.SetName(server, "服务地址");
        System.Windows.Automation.AutomationProperties.SetName(name, "邮箱或用户名");
        System.Windows.Automation.AutomationProperties.SetName(password, "密码");

        var feedback = Text("", 14, "#B42332");
        var actions = new WrapPanel();
        var savedActions = new StackPanel { Visibility = Visibility.Collapsed, Margin = new(0, 0, 0, 10) };
        var savedName = Text("", 13, "#52647D");
        savedActions.Children.Add(savedName);
        var savedButtons = new WrapPanel();
        savedActions.Children.Add(savedButtons);

        bool isSubmitting = false;
        bool IsCurrentView() => Route == PageRoute.Account && Content == view;
        void Submit(Func<Task> signIn)
        {
            if (isSubmitting) return;
            Run(async () =>
            {
                isSubmitting = true;
                actions.IsEnabled = savedActions.IsEnabled = false;
                feedback.Text = "正在连接…";
                try
                {
                    await signIn();
                    if (IsCurrentView()) showToday();
                }
                catch (Exception error) when (WorkspaceRepository.Expected(error))
                {
                    feedback.Text = WorkspaceRepository.Explain(error);
                }
                finally
                {
                    isSubmitting = false;
                    actions.IsEnabled = savedActions.IsEnabled = true;
                }
            });
        }

        var quick = Button("使用已保存密码登录", () => Submit(Repository.LoginRememberedAsync));
        savedButtons.Children.Add(quick);
        savedButtons.Children.Add(Button("清除已保存密码", () => Run(async () =>
        {
            await Repository.ForgetRememberedLoginAsync();
            if (IsCurrentView()) ShowAccount(manual);
        })));
        body.Children.Add(savedActions);
        body.Children.Add(feedback);
        actions.Children.Add(Button("登录并同步", () =>
        {
            if (isSubmitting) return;
            string address = server.Text.Trim(), identifier = name.Text.Trim(), secret = password.Password;
            if (identifier.Length == 0 || secret.Length == 0)
            {
                feedback.Text = "请填写账号和密码。";
                return;
            }
            password.Clear();
            bool shouldRemember = remember.IsChecked == true;
            Submit(() => Repository.LoginAsync(address, identifier, secret, shouldRemember));
        }));
        if (Repository.Current != null && !Repository.RequiresSignIn)
            actions.Children.Add(Button("返回", ShowAccount));
        body.Children.Add(actions);
        if (Repository.Current != null && Repository.RequiresSignIn) AddLogoutButton(body);
        view.Children.Add(Surface(body, new(28)));

        string initialServer = server.Text;
        Run(async () =>
        {
            try
            {
                var saved = await Repository.ReadRememberedLoginAsync();
                if (!IsCurrentView() || saved == null || isSubmitting) return;
                savedName.Text = $"已保存账号 {saved.Identifier} · {saved.Server}";
                savedActions.Visibility = Visibility.Visible;
                using var input = password.SecurePassword;
                if (server.Text == initialServer && name.Text.Length == 0 && input.Length == 0)
                {
                    server.Text = saved.Server;
                    name.Text = saved.Identifier;
                    remember.IsChecked = true;
                }
            }
            catch (Exception error) when (WorkspaceRepository.Expected(error))
            {
                if (!IsCurrentView() || isSubmitting) return;
                savedName.Text = "本机保存的凭据无法读取。";
                quick.Visibility = Visibility.Collapsed;
                savedActions.Visibility = Visibility.Visible;
            }
        });
    }

    void AddLogoutButton(Panel panel) =>
        panel.Children.Add(Button("退出账号并清除缓存", () => Run(async () =>
        {
            if (Repository.HasPendingSync && !Confirm("退出账号",
                $"还有 {Repository.PendingSyncCount} 项本机修改尚未同步，退出会删除这些草稿。", "仍然退出", "返回"))
                return;
            await Repository.LogoutAsync();
            if (Route == PageRoute.Account) ShowAccount();
        })));
}
