using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeInterview.NativeDemo.Data;

namespace AwesomeInterview.NativeDemo;

public partial class MainWindow
{
    bool checkingReminders;
    string reminderScope="";
    List<string> deliveredReminders=new();
    string? notificationEvent;
    async Task CheckRemindersAsync()
    {
        if(checkingReminders||tray==null)return;
        if(repository.Current==null||repository.Api==null){deliveredReminders.Clear();reminderScope="";notificationEvent=null;return;}
        checkingReminders=true;var api=repository.Api;var owner=repository.Current.UserId;var scope=api.Server.AbsoluteUri+owner;
        try{
            if(reminderScope!=scope){var ledger=await repository.ReadReminderLedgerAsync();if(repository.Api!=api||repository.Current?.UserId!=owner)return;deliveredReminders=ledger;reminderScope=scope;}
            var due=ReminderPlanner.Due(repository.Current.Events,DateTimeOffset.UtcNow,deliveredReminders.ToHashSet());if(due.Count==0)return;
            var next=deliveredReminders.Concat(due.Select(item=>item.Key)).TakeLast(500).ToList();
            await repository.SaveReminderLedgerAsync(next);if(repository.Api!=api||repository.Current?.UserId!=owner)return;
            deliveredReminders=next;notificationEvent=due[0].EventId;
            string text=string.Join("；",due.Take(3).Select(item=>$"{item.Title} · {CalendarProjection.DisplayTime(item.StartAt):HH:mm}"));
            if(due.Count>3)text+=$"，共 {due.Count} 项";
            tray.ShowBalloonTip(10000,"日程提醒",text[..Math.Min(240,text.Length)],System.Windows.Forms.ToolTipIcon.Info);
        }catch(Exception e)when(WorkspaceRepository.Expected(e)){status.Text="提醒状态未能保存，本次未发送；"+WorkspaceRepository.Explain(e);}
        finally{checkingReminders=false;}
    }
    void OpenNotification()
    {
        if(notificationEvent==null)return;var id=notificationEvent;notificationEvent=null;
        var parent=id.Split('~')[0];var item=repository.Current?.Events.FirstOrDefault(e=>e.Id==parent);
        if(item==null)return;
        if(id.Contains('~')&&DateTime.TryParse(id[(id.IndexOf('~')+1)..],out var day))item=item.OnDay(day);
        if(item!=null){RestoreMain();ShowEventDetail(item);}
    }
}
