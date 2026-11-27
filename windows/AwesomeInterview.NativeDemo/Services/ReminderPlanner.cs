using System;
using System.Collections.Generic;
using System.Linq;

namespace AwesomeInterview.NativeDemo.Data;

public record PlannedReminder(string Key,string EventId,string Title,DateTimeOffset StartAt,DateTimeOffset DueAt);

public static class ReminderPlanner
{
    // The desktop process checks once a minute. A small grace period covers sleep/resume
    // without showing a flood of stale alerts after a long absence.
    public static IReadOnlyList<PlannedReminder> Due(IEnumerable<EventData> events,DateTimeOffset now,ISet<string> delivered)
    {
        var day=TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).Date;
        var result=new List<PlannedReminder>();
        var sources=events.ToList();
        foreach(var item in Enumerable.Range(0,2).SelectMany(index=>CalendarProjection.EventsForDay(sources,day.AddDays(index),TimeSpan.Zero,TimeSpan.FromHours(24))).DistinctBy(item=>item.Id)){
                if(item.CompletedAt!=null||item.CancelledAt!=null||item.ReminderOffsetsMinutes.Count==0)continue;
                foreach(int offset in item.ReminderOffsetsMinutes.Where(n=>n is 10 or 60 or 1440).Distinct()){
                    var due=item.StartAt.AddMinutes(-offset);var key=$"{item.Id}:{item.StartAt.UtcTicks}:{offset}";
                    if(now>=due&&now-due<=TimeSpan.FromMinutes(3)&&now<item.StartAt&&!delivered.Contains(key))result.Add(new(key,item.Id,item.Title,item.StartAt,due));
                }
        }
        return result.DistinctBy(item=>item.Key).OrderBy(item=>item.DueAt).ToList();
    }
}

public sealed partial class WorkspaceRepository
{
    public async System.Threading.Tasks.Task<List<string>> ReadReminderLedgerAsync()
    {
        var api=Api;var owner=Current?.UserId;if(api==null||owner==null)return new();
        var values=await cache.ReadReminderLedgerAsync(api.Server.AbsoluteUri,owner);
        return Api==api&&Current?.UserId==owner?values??new():new();
    }
    public async System.Threading.Tasks.Task SaveReminderLedgerAsync(List<string> values)
    {
        var api=Api;var owner=Current?.UserId;if(api==null||owner==null)return;
        await syncGate.WaitAsync();try{if(Api==api&&Current?.UserId==owner)await cache.SaveReminderLedgerAsync(api.Server.AbsoluteUri,owner,values.TakeLast(500).ToList());}finally{syncGate.Release();}
    }
}
