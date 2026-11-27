using AwesomeInterview.NativeDemo.Data;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

static class OfflineDependencyChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        const string server="https://dependency.test/";var dir=Path.Combine(Path.GetTempPath(),"awesome-dependency-"+Guid.NewGuid().ToString("N"));var cache=new AccountCache(dir);string ideaId="",eventId="";
        var state=new Snapshot{SchemaVersion=1,UserId="a",ServerTime=DateTimeOffset.UtcNow,Events=new(),Experiences=new(),Todos=new()};
        try{
            await cache.SaveConnectionAsync(new(server,"a","fixture"));await cache.SaveSnapshotAsync(server,state);
            using(var repo=new WorkspaceRepository(cache,address=>new NativeApi(address,new Handler(request=>request.RequestUri!.AbsolutePath.EndsWith("/session")?Json(new SessionInfo("a","test")):throw new HttpRequestException("offline fixture"))))){
                await repo.RestoreAsync();ideaId=await repo.SaveIdeaQueuedAsync("Offline idea","source",new());await repo.FlushSyncAsync();
                eventId=await repo.SaveEventQueuedAsync(new CalendarEventPayload{Title="Offline arranged",Type="personal",StartAt=DateTimeOffset.Parse("2026-11-23T09:00:00+08:00"),EndAt=DateTimeOffset.Parse("2026-11-23T10:00:00+08:00"),SourceIdeaId=ideaId},null);await repo.FlushSyncAsync();await repo.FlushCacheAsync();
                check(repo.Current!.Ideas.Single().ArrangedEventId==eventId&&repo.Current.Events.Count==1,"offline idea can be scheduled before its server acknowledgement");
            }
            var pending=(await cache.ReadOutboxAsync(server,"a"))!;foreach(var item in pending.Items)item.NextAttempt=0;await cache.SaveOutboxAsync(server,"a",pending);
            check(pending.ConfirmedIdeas?.Count==0&&pending.ConfirmedEvents?.Count==0&&pending.Items.Count==2,"durable queue keeps confirmed bases separate from optimistic projections");
            bool reject=true;var calls=new List<string>();
            using(var repo=new WorkspaceRepository(cache,address=>new NativeApi(address,new Handler(request=>{
                var path=request.RequestUri!.AbsolutePath;if(path.EndsWith("/session"))return Json(new SessionInfo("a","test"));
                var operation=path.Split('/')[^1];
                if(path.Contains("/idea-sync/")){calls.Add("idea");if(reject)return new(HttpStatusCode.UnprocessableEntity){Content=new StringContent("{\"detail\":\"rejected source\"}",Encoding.UTF8,"application/json")};return Json(new{user_id="a",operation_id=operation,idea=new{id=ideaId,title="Offline idea",note="source",reference_links=Array.Empty<object>(),status="pending",created_at=DateTimeOffset.UtcNow,updated_at=DateTimeOffset.UtcNow}});}
                if(path.Contains("/calendar-sync/")){calls.Add("event");return Json(new{user_id="a",operation_id=operation,@event=new{id=eventId,type="personal",title="Offline arranged",start_at="2026-11-23T09:00:00+08:00",end_at="2026-11-23T10:00:00+08:00"}});}
                return new(HttpStatusCode.NoContent);
            })))){
                await repo.RestoreAsync();
                check(repo.FailedSyncCount==2&&repo.Current!.Ideas.Count==0&&repo.Current.Events.Count==0,"rejected source rolls back its optimistic idea and dependent event after restart");
                var child=repo.SyncItems.Single(item=>item.EntityId==eventId);await repo.RetrySyncAsync(child);
                check(calls.All(call=>call!="event"),"dependent event cannot reach the server while its source is rejected");
                reject=false;await repo.RetrySyncAsync(repo.SyncItems.Single(item=>item.EntityId==ideaId));await repo.RetrySyncAsync(child);
                check(repo.PendingSyncCount==0&&calls[^2]=="idea"&&calls[^1]=="event"&&repo.Current!.Ideas.Single().ArrangedEventId==eventId,"corrected source confirms before dependent schedule and both converge once");
                await repo.LogoutAsync();
            }
        }finally{foreach(var file in Directory.Exists(dir)?Directory.GetFiles(dir):Array.Empty<string>())File.Delete(file);if(Directory.Exists(dir))Directory.Delete(dir);}
    }
    static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value,Wire.Json),Encoding.UTF8,"application/json")};
    sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)=>Task.FromResult(send(request));}
}
