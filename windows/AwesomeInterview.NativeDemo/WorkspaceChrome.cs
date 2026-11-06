using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AwesomeInterview.NativeDemo;
public partial class MainWindow
{
    bool Confirm(string title,string message,string accept="确认",string cancel="取消",Window? owner=null)
    {
        var dialog=new Window{Title=title,Owner=owner??this,Width=480,SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.WorkArea.Height-60,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#F6F8FC"),FontFamily=FontFamily,ShowInTaskbar=false};
        var body=new StackPanel{Margin=new(28)};body.Children.Add(Text(title,21));body.Children.Add(Text(message,14,"#64748B",new(0,18,0,24)));var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var back=Button(cancel,()=>dialog.DialogResult=false);back.IsCancel=true;actions.Children.Add(back);actions.Children.Add(Button(accept,()=>dialog.DialogResult=true));body.Children.Add(actions);dialog.Content=body;dialog.Loaded+=(_,_)=>back.Focus();return dialog.ShowDialog()==true;
    }
    int adminHours=24;string adminChannel="all";
    void ShowAdmin()
    {
        page="admin";bSelect("管理员监控");Begin("管理员监控","汇总数据与页面耗时 · 不展示用户明细");
        if(!repository.Online||repository.Api?.Session?.IsAdmin!=true){Paragraph("需要在线管理员账号。权限由服务端校验。");return;}
        var controls=new WrapPanel();foreach(var hours in new[]{1,24,168})controls.Children.Add(Button($"最近 {hours} 小时",()=>{adminHours=hours;ShowAdmin();}));foreach(var channel in new[]{"all","web","windows","android"})controls.Children.Add(Button(channel,()=>{adminChannel=channel;ShowAdmin();}));controls.Children.Add(Button("刷新",ShowAdmin));content.Children.Add(controls);
        var panel=new StackPanel();content.Children.Add(Surface(panel));panel.Children.Add(Text("正在读取汇总…"));var api=repository.Api;
        Run(async()=>{try{var stats=await api.GetAsync<JsonElement>("my-schedule/admin/summary");var perf=await api.GetAsync<JsonElement>($"my-schedule/admin/performance?hours={adminHours}&channel={adminChannel}");if(page!="admin"||repository.Api!=api)return;panel.Children.Clear();
        panel.Children.Add(Text($"注册用户 {stats.GetProperty("total_users")}    面经 {stats.GetProperty("total_notes")}    公开 {stats.GetProperty("total_public")}    私密 {stats.GetProperty("total_private")}",18));
        panel.Children.Add(Text("节点耗时 P95 / P50（毫秒）",19,margin:new(0,24,0,12)));
        var groups=perf.GetProperty("groups").EnumerateArray().ToList();double max=groups.Count==0?1:Math.Max(1,groups.Max(x=>x.GetProperty("p95").GetDouble()));
        foreach(var row in groups){panel.Children.Add(Text($"{row.GetProperty("node")} · {row.GetProperty("channel_label")} · {row.GetProperty("source")} · {row.GetProperty("metric_label")}    P95 {row.GetProperty("p95")} / P50 {row.GetProperty("p50")}    样本 {row.GetProperty("count")}",13,margin:new(0,12,0,5)));panel.Children.Add(new ProgressBar{Maximum=max,Value=row.GetProperty("p95").GetDouble(),Height=8,Foreground=Brush("#285DDD")});}
        if(groups.Count==0)panel.Children.Add(Text("此时间范围暂无样本。",14,"#64748B"));
        panel.Children.Add(Text("主要延迟与错误样本",19,margin:new(0,24,0,12)));foreach(var row in perf.GetProperty("slow").EnumerateArray())panel.Children.Add(Text($"{row.GetProperty("node")} · {row.GetProperty("channel_label")} · {row.GetProperty("metric")}    {row.GetProperty("duration")} ms    HTTP {row.GetProperty("status")}",13,margin:new(0,4,0,8)));
        }catch(Exception error)when(Data.WorkspaceRepository.Expected(error)){if(page=="admin"&&repository.Api==api){panel.Children.Clear();panel.Children.Add(Text(Data.WorkspaceRepository.Explain(error),14,"#B42332"));}}});
    }
}
