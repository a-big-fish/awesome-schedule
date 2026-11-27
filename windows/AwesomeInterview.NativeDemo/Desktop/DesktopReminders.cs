using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeInterview.NativeDemo.Data;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class DesktopIntegration
{
    bool checkingReminders;
    string reminderScope="";
    List<string> deliveredReminders=new();
    string? notificationEvent;
    internal async Task CheckRemindersAsync()
    {
        if(checkingReminders||tray==null)return;
        if(shell.Repository.Current==null||shell.Repository.Api==null){deliveredReminders.Clear();reminderScope="";notificationEvent=null;return;}
        checkingReminders=true;var api=shell.Repository.Api;var owner=shell.Repository.Current.UserId;var scope=api.Server.AbsoluteUri+owner;
        try{
            if(reminderScope!=scope){var ledger=await shell.Repository.ReadReminderLedgerAsync();if(shell.Repository.Api!=api||shell.Repository.Current?.UserId!=owner)return;deliveredReminders=ledger;reminderScope=scope;}
            var due=ReminderPlanner.Due(shell.Repository.Current.Events,DateTimeOffset.UtcNow,deliveredReminders.ToHashSet());if(due.Count==0)return;
            var next=deliveredReminders.Concat(due.Select(item=>item.Key)).TakeLast(500).ToList();
            await shell.Repository.SaveReminderLedgerAsync(next);if(shell.Repository.Api!=api||shell.Repository.Current?.UserId!=owner)return;
            deliveredReminders=next;notificationEvent=due[0].EventId;
            string text=string.Join("；",due.Take(3).Select(item=>$"{item.Title} · {CalendarProjection.DisplayTime(item.StartAt):HH:mm}"));
            if(due.Count>3)text+=$"，共 {due.Count} 项";
            tray.ShowBalloonTip(10000,"日程提醒",text[..Math.Min(240,text.Length)],System.Windows.Forms.ToolTipIcon.Info);
        }catch(Exception e)when(WorkspaceRepository.Expected(e)){shell.Status.Text="提醒状态未能保存，本次未发送；"+WorkspaceRepository.Explain(e);}
        finally{checkingReminders=false;}
    }
    void OpenNotification()
    {
        if(notificationEvent==null)return;var id=notificationEvent;notificationEvent=null;
        var parent=id.Split('~')[0];var item=shell.Repository.Current?.Events.FirstOrDefault(e=>e.Id==parent);
        if(item==null)return;
        if(id.Contains('~')&&DateTime.TryParse(id[(id.IndexOf('~')+1)..],out var day))item=item.OnDay(day);
        if(item!=null){shell.RestoreMain();schedule.ShowEventDetail(item);}
    }
}
