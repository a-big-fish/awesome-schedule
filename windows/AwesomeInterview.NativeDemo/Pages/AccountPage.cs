using System;
using System.Windows;
using System.Windows.Controls;
using AwesomeInterview.NativeDemo.Data;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal sealed class AccountPage(MainWindow shell, Action showToday) : WorkspacePage(shell)
{
    internal void ShowAccount()=>ShowAccount(false);
    void ShowAccount(bool prompt)
    {
        Begin(PageRoute.Account,"","账号与同步","使用与 Web 相同的账号，密码仅用于本次登录");
        if(Repository.Current!=null&&!prompt)
        {
            Paragraph(Repository.Online?"账号已连接，切换页面和日期直接使用本机数据。":"当前为离线只读；连接恢复后点击同步获取最新内容。");
            Paragraph($"缓存更新：{Repository.Current.ServerTime.LocalDateTime:yyyy-MM-dd HH:mm}\n服务地址：{Repository.Api?.Server}");
            Content.Children.Add(Button("同步最新数据",()=>Run(Repository.SynchronizeAsync)));
            Content.Children.Add(Button("重新登录",()=>ShowAccount(true)));
            Content.Children.Add(Button("退出账号并清除缓存",()=>Run(async()=>{
                if(Repository.HasPendingSync&&!Confirm("退出账号",$"还有 {Repository.PendingSyncCount} 项本机修改尚未同步，退出会删除这些草稿。","仍然退出","返回"))return;
                await Repository.LogoutAsync();ShowAccount();
            })));return;
        }
        var body=new StackPanel{MaxWidth=490,HorizontalAlignment=HorizontalAlignment.Left};
        body.Children.Add(Text("服务地址"));var server=new TextBox{Text="https://schedule.hunaochufang.xyz/",MinWidth=400};body.Children.Add(server);
        body.Children.Add(Text("邮箱或用户名"));var name=new TextBox();body.Children.Add(name);
        body.Children.Add(Text("密码"));var password=new PasswordBox{Padding=new(12),Margin=new(0,8,0,20),FontSize=16};body.Children.Add(password);
        System.Windows.Automation.AutomationProperties.SetName(server,"服务地址");System.Windows.Automation.AutomationProperties.SetName(name,"邮箱或用户名");System.Windows.Automation.AutomationProperties.SetName(password,"密码");
        var feedback=Text("",14,"#B42332");body.Children.Add(feedback);
        Button? login=null;login=Button("登录并同步",()=>Run(async()=>{
            login!.IsEnabled=false;feedback.Text="正在连接…";
            string secret=password.Password;password.Clear();
            try{await Repository.LoginAsync(server.Text,name.Text,secret);showToday();}
            catch(Exception error) when(WorkspaceRepository.Expected(error)){feedback.Text=WorkspaceRepository.Explain(error);}
            finally{login.IsEnabled=true;}
        }));body.Children.Add(login);Content.Children.Add(Surface(body,new(28)));
    }
}
