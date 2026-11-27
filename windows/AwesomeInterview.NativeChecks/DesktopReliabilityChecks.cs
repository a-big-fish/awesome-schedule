using AwesomeInterview.NativeDemo.Data;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

static class DesktopReliabilityChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.Parse("2026-11-23T09:50:30+08:00");
        var task=new EventData{Id=Guid.NewGuid().ToString(),OwnerId="a",Type="meeting",Title="Generated reminder",StartAt=now.AddMinutes(9.5),EndAt=now.AddMinutes(69.5),ReminderOffsetsMinutes=new(){10,10,99}};
        var due=ReminderPlanner.Due(new[]{task},now,new HashSet<string>());
        check(due.Count==1&&due[0].EventId==task.Id,"one due reminder per supported offset and event");
        check(ReminderPlanner.Due(new[]{task},now,due.Select(r=>r.Key).ToHashSet()).Count==0,"persisted delivery key prevents reminder duplication");
        check(ReminderPlanner.Due(new[]{task with{CompletedAt=now},task with{CancelledAt=now}},now,new HashSet<string>()).Count==0,"completed and abolished tasks cannot notify");
        check(ReminderPlanner.Due(new[]{task},now.AddMinutes(4),new HashSet<string>()).Count==0,"stale reminders do not flood on resume");
        check(ReminderPlanner.Due(new[]{task},now.AddMinutes(-1),new HashSet<string>()).Count==0,"future reminder does not fire early");
        var tomorrow=task with{Id="tomorrow",StartAt=now.AddDays(1),EndAt=now.AddDays(1).AddHours(1),ReminderOffsetsMinutes=new(){1440}};
        check(ReminderPlanner.Due(new[]{tomorrow},now,new HashSet<string>()).Count==1,"one-day reminder includes tomorrow's occurrence");
        var recurring=task with{RepeatWeekdays=new(){0},RepeatCompletions=new(){["2026-11-23"]=now}};
        check(ReminderPlanner.Due(new[]{recurring},now,new HashSet<string>()).Count==0,"recurring occurrence completion suppresses only that reminder");
        var foreign=task with{Timezone="America/New_York"};
        check(ReminderPlanner.Due(new[]{foreign},now,new HashSet<string>()).Count==1,"reminders preserve instants across event timezones");

        var dir=Path.Combine(Path.GetTempPath(),"awesome-receipt-"+Guid.NewGuid().ToString("N"));var cache=new AccountCache(dir);
        try{
            await cache.SaveConnectionAsync(new("https://fixture.test/","a","synthetic"));
            await cache.SaveSnapshotAsync("https://fixture.test/",new Snapshot{SchemaVersion=1,UserId="a",ServerTime=DateTimeOffset.UtcNow,Events=new(),Experiences=new(),Todos=new()});
            string mode="missing";var operations=new List<string>();
            var transport=new GuardTransport(request=>{
                string path=request.RequestUri!.AbsolutePath;
                if(path.EndsWith("/session"))return Json(new SessionInfo("a","test"));
                if(path.Contains("/calendar-sync/")){
                    string operation=path.Split('/')[^1];operations.Add(operation);
                    var command=JsonSerializer.Deserialize<CalendarSyncCommand>(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult(),Wire.Json)!;
                    if(mode=="missing")return Json(new{operation_id=operation,user_id="a"});
                    return Json(new{operation_id=operation,user_id="a",@event=task with{Id=mode=="wrong"?"wrong-entity":command.EventId}});
                }
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });
            using(var repo=new WorkspaceRepository(cache,address=>new NativeApi(address,transport))){
                await repo.RestoreAsync();var id=await repo.SaveEventQueuedAsync(new CalendarEventPayload{Title="Receipt fixture",Type="meeting",StartAt=task.StartAt,EndAt=task.EndAt},null);await repo.FlushSyncAsync();
                check(repo.PendingSyncCount==1&&repo.Current!.Events.Any(e=>e.Id==id),"matching operation id without entity body cannot erase a pending command");
                var entry=repo.SyncItems.Single();mode="wrong";await repo.RetrySyncAsync(entry);
                check(repo.PendingSyncCount==1&&repo.Current!.Events.All(e=>e.Id!="wrong-entity"),"receipt for wrong entity stays queued and cannot contaminate the snapshot");
                mode="valid";await repo.RetrySyncAsync(entry);
                check(repo.PendingSyncCount==0&&operations.Distinct().Count()==1,"valid receipt confirms original command after safe same-id retries");
                await repo.SaveReminderLedgerAsync(new(){"fixture-key"});check((await repo.ReadReminderLedgerAsync()).Single()=="fixture-key","reminder deduplication survives encrypted persistence");
                await repo.LogoutAsync();check(await cache.ReadReminderLedgerAsync("https://fixture.test/","a")==null,"logout clears notification history with the private account cache");
            }
            await cache.SaveConnectionAsync(new("https://fixture.test/","a","synthetic"));
            await cache.SaveSnapshotAsync("https://fixture.test/",new Snapshot{SchemaVersion=1,UserId="a",ServerTime=DateTimeOffset.UtcNow,Events=new(),Experiences=new(),Todos=new()});
            await cache.SaveOutboxAsync("https://fixture.test/","a",new SyncOutbox{SchemaVersion=2,UserId="a"});
            var queueFile=Directory.GetFiles(dir).Single(path=>path.EndsWith(".outbox.bin"));var protectedQueue=await File.ReadAllBytesAsync(queueFile);
            using(var repo=new WorkspaceRepository(cache,address=>new NativeApi(address,new GuardTransport(_=>Json(new SessionInfo("a","test")))))){
                await repo.RestoreAsync();
                try{await repo.SaveIdeaQueuedAsync("must not replace future queue","",new());throw new Exception("future queue overwritten");}catch(ApiFailure e){check(e.Status==409,"unknown queue schema blocks new mutations without discarding pending data");}
                check((await File.ReadAllBytesAsync(queueFile)).SequenceEqual(protectedQueue),"unsupported encrypted queue remains byte-identical for recovery");
                await repo.LogoutAsync();
            }
        }finally{foreach(var file in Directory.Exists(dir)?Directory.GetFiles(dir):Array.Empty<string>())File.Delete(file);if(Directory.Exists(dir))Directory.Delete(dir);}
    }
    static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value,Wire.Json),Encoding.UTF8,"application/json")};
    sealed class GuardTransport(Func<HttpRequestMessage,HttpResponseMessage> handler):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)=>Task.FromResult(handler(request));}
}
