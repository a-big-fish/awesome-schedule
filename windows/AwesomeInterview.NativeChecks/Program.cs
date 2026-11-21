using AwesomeInterview.NativeDemo.Data;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
using (var projectionFixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "calendar_projection_v1.json"))))
{
    var fixture = projectionFixture.RootElement;
    var events = Wire.Read<List<EventData>>(fixture.GetProperty("events").GetRawText());
    var workStart = TimeSpan.Parse(fixture.GetProperty("workday_start").GetString()!);
    var workEnd = TimeSpan.Parse(fixture.GetProperty("workday_end").GetString()!);
    foreach (var sample in fixture.GetProperty("days").EnumerateObject())
    {
        var day = DateTime.ParseExact(sample.Name, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var actual = CalendarProjection.EventsForDay(events, day, workStart, workEnd).Select(item =>
        {
            var segment = CalendarProjection.Segment(item, day, workStart, workEnd)!.Value;
            return (item.Id, (int)(segment.Start - day).TotalMinutes, (int)(segment.End - day).TotalMinutes, item.CompletedAt != null);
        });
        var expected = sample.Value.EnumerateArray().Select(row => (row[0].GetString()!, row[1].GetInt32(), row[2].GetInt32(), row[3].GetBoolean()));
        Check(actual.SequenceEqual(expected), "shared calendar projection " + sample.Name);
    }
}
Check(Wire.Read<SessionInfo>("""{"user_id":"a","csrf_token":"test","is_admin":true}""").IsAdmin && !Wire.Read<SessionInfo>("""{"user_id":"a","csrf_token":"test"}""").IsAdmin,"admin capability parsed; legacy session defaults unprivileged");
var profileContract=Wire.Read<ProfileData>("""{"user_id":"a","display_name":"自定义昵称","email":"ab@example.test","workday_start":"08:00","workday_end":"18:00"}""");
Check(profileContract.EffectiveName=="自定义昵称", "server display_name received without fallback override");
Check((profileContract with {DisplayName=null}).EffectiveName=="热心用户_ab", "empty nickname uses Web email-prefix default");
var dateOnly=ScheduleTime.Select(new DateTime(2026,11,7,23,59,0),true,TimeSpan.FromHours(12),new TimeSpan(9,30,0));
Check(dateOnly==new DateTime(2026,11,7,9,30,0)&&dateOnly.Kind==DateTimeKind.Unspecified,"date-only selection uses account work time without timezone conversion");
Check(ScheduleTime.SuggestedEnd(new(new DateTime(2026,11,7,23,45,0),false),TimeSpan.FromHours(18))==new DateTime(2026,11,8,0,45,0),"precise start suggests one hour end across midnight");
Check(ScheduleTime.SuggestedEnd(new(dateOnly,true),TimeSpan.FromHours(19))==new DateTime(2026,11,7,19,0,0),"date-only start suggests account end on selected date");
var seed = """
{"schema_version":1,"user_id":"a","server_time":"2026-11-05T03:00:00Z","events":[{"id":"e","owner_id":"a","type":"personal","title":"contract fixture","start_at":"2026-11-05T23:00:00+08:00","end_at":"2026-11-07T01:00:00+08:00","skip_weekends":true,"timezone":"Asia/Shanghai","future_field":{"enabled":true}}],"experiences":[],"todos":[]}
""";
var snapshot = Wire.Read<Snapshot>(seed); snapshot.Validate("a");
Check(snapshot.Events[0].StartAt.Offset == TimeSpan.FromHours(8), "offset and full snapshot deserialize");
Check(snapshot.Events[0].Extra!.ContainsKey("future_field"), "unknown fields retained");
Check(snapshot.Events[0].OnDay(new(2026,11,6)) != null && snapshot.Events[0].OnDay(new(2026,11,7)) == null, "cross-day and weekend projection");
try { snapshot.Validate("b"); throw new Exception("cross-account accepted"); } catch (InvalidDataException) { Check(true,"cross-account rejected"); }
try { Wire.Read<Snapshot>("{\"schema_version\":1}"); throw new Exception("partial accepted"); } catch (JsonException) { Check(true,"partial snapshot rejected"); }
var recurring = snapshot.Events[0] with { RepeatWeekdays = new(){3}, EndAt = snapshot.Events[0].StartAt.AddHours(1), RepeatCompletions = new() { ["2026-11-12"] = DateTimeOffset.UtcNow } };
Check(recurring.OnDay(new(2026,11,12))?.Id == "e~2026-11-12" && recurring.OnDay(new(2026,11,12))?.CompletedAt != null, "recurrence ID and completion");
QuestionData Q(string id, string? parent, int order) => new() { Id=id, ParentQuestionId=parent, AskedOrder=order, Category="project", ExperienceId="x", QuestionText=id };
var tree = QuestionTree.Ordered(new[]{Q("b",null,2),Q("a1","a",4),Q("a",null,1),Q("b1","b",3)}).ToList();
Check(string.Join(",",tree.Select(x=>x.Item.Id)) == "a,a1,b,b1" && tree[1].Number == "1.1", "hierarchy sorted before numbering");
Check(QuestionTree.Ordered(new[]{Q("a","b",1),Q("b","a",2)}).Count() == 2, "cycles terminate without dropped content");
var dir = Path.Combine(Path.GetTempPath(), "awesome-native-checks-" + Guid.NewGuid()); var cache = new AccountCache(dir);
try {
    await cache.SaveSnapshotAsync("https://example.test/",snapshot);
    Check((await cache.ReadSnapshotAsync("https://example.test/","a"))?.Events.Count == 1, "encrypted cache roundtrip");
    Check(await cache.ReadSnapshotAsync("https://example.test/","b") == null && await cache.ReadSnapshotAsync("https://another.test/","a") == null, "server and account isolation");
    Check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Directory.GetFiles(dir)[0])).Contains("contract fixture"), "cache is not plaintext");
    await cache.SaveConnectionAsync(new("https://example.test/","a","test-session")); cache.Forget("https://example.test/","a");
    Check(await cache.ReadConnectionAsync() == null && await cache.ReadSnapshotAsync("https://example.test/","a") == null, "logout erases identity and snapshot");
} finally { foreach (var file in Directory.GetFiles(dir)) File.Delete(file); Directory.Delete(dir); }
using (var api = new NativeApi("https://example.test/",new Reply("<html>login</html>","text/html",HttpStatusCode.OK)))
    try { await api.GetAsync<SessionInfo>("api/mobile/v1/session"); throw new Exception("html accepted"); } catch(ApiFailure e) { Check(e.Status==401,"HTML never treated as JSON"); }
using (var api = new NativeApi("https://example.test/",new Reply("{}","application/json",HttpStatusCode.ServiceUnavailable)))
    try { await api.GetAsync<SessionInfo>("api/mobile/v1/session"); throw new Exception("503 accepted"); } catch(ApiFailure e) { Check(e.Status==503,"database unavailable fails without retry loop"); }
try { using var api = new NativeApi("http://example.com/"); throw new Exception("HTTP accepted"); } catch(ArgumentException) { Check(true,"credentials require TLS outside localhost"); }
using(var api=new NativeApi("https://example.test/",new Reply("{\"detail\":{\"conflicts\":[\"existing task\"]}}","application/json",HttpStatusCode.Conflict)))
    try { await api.GetAsync<EventData>("api/mobile/v1/events");throw new Exception("conflict ignored"); } catch(ApiFailure e) { Check(e.Status==409&&e.ConflictTitles.Single()=="existing task","conflict titles available for native confirmation"); }

var workEvent=snapshot.Events[0] with { WorkdayOnly=true,SkipWeekends=false,StartAt=DateTimeOffset.Parse("2026-11-05T10:00:00+08:00"),EndAt=DateTimeOffset.Parse("2026-11-07T12:00:00+08:00") };
Check(CalendarProjection.Segment(workEvent,new(2026,11,6),new(9,30,0),new(17,30,0))==(new DateTime(2026,11,6,9,30,0),new DateTime(2026,11,6,17,30,0)),"cross-day clipping uses personal work hours");
Check(CalendarProjection.Segment(workEvent,new(2026,11,5),TimeSpan.FromHours(8),TimeSpan.FromHours(18))!.Value.Start.Hour==10,"first day preserves actual start inside work hours");
Check(CalendarProjection.Segment(workEvent with{EndAt=DateTimeOffset.Parse("2026-11-07T07:00:00+08:00")},new(2026,11,7),TimeSpan.FromHours(8),TimeSpan.FromHours(18))==null,"empty work-hour intersection occupies no slot");

var repoDir=Path.Combine(Path.GetTempPath(),"awesome-repo-checks-"+Guid.NewGuid());
var repoCache=new AccountCache(repoDir);
var noteFixture=new ExperienceData{Id="note",OwnerId="a",Title="fixture",Questions=new()};
var initialState=snapshot with{Experiences=new(){noteFixture},Profile=profileContract};
await repoCache.SaveConnectionAsync(new("https://example.test/","a","fixture-session"));
await repoCache.SaveSnapshotAsync("https://example.test/",initialState);
var paths=new List<string>();int writes=0;
var transport=new Routing(async request=>{
    string path=request.RequestUri!.AbsolutePath;paths.Add(path);
    if(path.EndsWith("/session"))return Json(new SessionInfo("a","fixture"));
    if(path.EndsWith("/snapshot"))return Json(initialState);
    if(path.EndsWith("/calendar"))return Json(new CalendarState(1,"a",DateTimeOffset.UtcNow,new(){workEvent},new(),new(),profileContract));
    if(path.Contains("/calendar-sync/")){
        writes++;
        string operation=path[(path.LastIndexOf('/')+1)..];
        string body=request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        string entity=JsonDocument.Parse(body).RootElement.GetProperty("event_id").GetString()!;
        return Json(new{schema_version=1,user_id="a",operation_id=operation,server_time="2026-11-19T10:00:00+08:00",@event=workEvent with{Id=entity,Title="saved"}});
    }
    await Task.Yield();return new(HttpStatusCode.NoContent);
});
try {
    using(var repo=new WorkspaceRepository(repoCache,address=>new NativeApi(address,transport))){
        await repo.RestoreAsync();Check(repo.Online&&repo.Current!.Profile!.EffectiveName=="自定义昵称","restored snapshot carries account preferences");
        var before=paths.Count;
        var payload=new CalendarEventPayload{Title="fixture",Type="personal",
            StartAt=DateTimeOffset.Parse("2026-11-05T09:00:00+08:00"),EndAt=DateTimeOffset.Parse("2026-11-05T10:00:00+08:00")};
        var entity=await repo.SaveEventQueuedAsync(payload,null);
        await repo.FlushSyncAsync();
        Check(writes==1&&paths.Count==before+1&&repo.Current!.Events.Single(e=>e.Id==entity).Title=="saved","queued calendar write patches locally with exactly one HTTP call");
        await repo.RefreshCalendarAsync();
        Check(repo.Current!.ServerTime==initialState.ServerTime,"calendar refresh does not renew freshness of untouched experiences");
        Check(repo.Current!.Experiences.Single().Id=="note"&&repo.Current.Events.Single().WorkdayOnly,"calendar-only refresh preserves note resources and updates display rules");
        await repo.FlushCacheAsync();
        Check((await repoCache.ReadSnapshotAsync("https://example.test/","a"))!.Events.Single().WorkdayOnly,"ordered encrypted persistence retains latest resource state");
        Check((await repoCache.ReadSnapshotAsync("https://example.test/","a"))!.ServerTime==initialState.ServerTime,"partial refresh persists the original full snapshot timestamp");
        var restoredPaths=new List<string>();
        using(var restored=new WorkspaceRepository(repoCache,address=>new NativeApi(address,new Routing(request=>{
            restoredPaths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(request.RequestUri.AbsolutePath.EndsWith("/session")?Json(new SessionInfo("a","fixture")):Json(initialState with{ServerTime=DateTimeOffset.UtcNow}));
        })))){
            await restored.RestoreAsync();
            Check(restoredPaths.Contains("/api/mobile/v1/snapshot"),"expired full snapshot reloads notes after calendar-only refresh and restart");
            await restored.FlushCacheAsync();
        }
        await repo.LogoutAsync();Check(await repoCache.ReadConnectionAsync()==null,"logout waits for persistence then clears identity");
    }
    await repoCache.SaveConnectionAsync(new("https://example.test/","a","fixture-session"));
    await repoCache.SaveSnapshotAsync("https://example.test/",snapshot);
    var badFile=Directory.GetFiles(repoDir).Single(f=>!f.EndsWith("connection.bin")&&!f.EndsWith(".outbox.bin"));await File.WriteAllTextAsync(badFile,"corrupt");
    using(var repo=new WorkspaceRepository(repoCache,address=>new NativeApi(address,new Reply("{}","application/json",HttpStatusCode.ServiceUnavailable)))){
        await repo.RestoreAsync();Check(!repo.Online&&repo.Current==null,"corrupt snapshot and unreachable backend handled without crash");
        await repo.LogoutAsync();Check(await repoCache.ReadConnectionAsync()==null&&!File.Exists(badFile),"corrupt snapshot does not prevent clearing saved identity");
    }
    await repoCache.SaveConnectionAsync(new("https://example.test/","a","fixture-session"));
    await repoCache.SaveSnapshotAsync("https://example.test/",snapshot with{ServerTime=DateTimeOffset.UtcNow});
    var freshPaths=new List<string>();
    using(var repo=new WorkspaceRepository(repoCache,address=>new NativeApi(address,new Routing(request=>{freshPaths.Add(request.RequestUri!.AbsolutePath);return Task.FromResult(Json(new SessionInfo("a","fixture")));})))){
        await repo.RestoreAsync();Check(repo.Online&&freshPaths.SequenceEqual(new[]{"/api/mobile/v1/session"}),"fresh 18-hour cache validates session without reloading calendar dates or snapshot");
        await repo.LogoutAsync();
    }
    await repoCache.SaveConnectionAsync(new("https://example.test/","a","fixture-session"));
    await repoCache.SaveSnapshotAsync("https://example.test/",snapshot);
    var wrongIdentity=new Routing(request=>Task.FromResult(Json(new SessionInfo("b","fixture"))));
    using(var repo=new WorkspaceRepository(repoCache,address=>new NativeApi(address,wrongIdentity))){
        await repo.RestoreAsync();Check(!repo.Online&&repo.Current!.UserId=="a","unexpected session account never replaces the displayed owner snapshot");
        await repo.LogoutAsync();
    }
} finally {foreach(var file in Directory.GetFiles(repoDir))File.Delete(file);Directory.Delete(repoDir);}
static HttpResponseMessage Json<T>(T value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value,Wire.Json),Encoding.UTF8,"application/json")};


// Durable idea queue: deterministic ids, replay-safe retries, rejection, isolation and cleanup.
Check(DeterministicIds.CalendarCreate("u1","00000000-0000-0000-0000-000000000001")=="d1a6ad48-2b50-5dcb-a416-21941b278590","uuid5 calendar create id matches the server vector");
Check(DeterministicIds.IdeaCreate("u1","00000000-0000-0000-0000-000000000001")=="f1b70b49-e507-5149-8adc-bde7c1689fee","uuid5 idea create id matches the server vector");
Check(DeterministicIds.IdeaEvent("u1","idea-1")=="7684c203-569c-5570-8002-be6ee4241962","uuid5 arranged event id matches the server vector");

static HttpResponseMessage Status(int code,string body)=>new((HttpStatusCode)code){Content=new StringContent(body,Encoding.UTF8,"application/json")};
var queueDir=Path.Combine(Path.GetTempPath(),"awesome-idea-queue-"+Guid.NewGuid());
var queueCache=new AccountCache(queueDir);
try {
    var baseSnapshot=snapshot with{ServerTime=DateTimeOffset.UtcNow,Experiences=new(),Ideas=new(),Profile=profileContract};
    await queueCache.SaveConnectionAsync(new("https://example.test/","a","fixture-session"));
    await queueCache.SaveSnapshotAsync("https://example.test/",baseSnapshot);
    var calls=new List<(string Id,string Body)>();var behaviour="ack";
    string IdeaMode(HttpRequestMessage request)
    {
        var path=request.RequestUri!.AbsolutePath;var operation=path[(path.LastIndexOf('/')+1)..];
        var body=request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();calls.Add((operation,body));
        if(behaviour=="reject")return "reject";
        if(behaviour=="uncertain")return "uncertain";
        if(behaviour=="unavailable")return "unavailable";
        return "ack";
    }
    HttpResponseMessage AckIdea(HttpRequestMessage request,string operation,string body)
    {
        var command=JsonSerializer.Deserialize<IdeaSyncCommand>(body,Wire.Json)!;
        var stamp="2026-11-19T10:00:00+08:00";
        return Status(200,JsonSerializer.Serialize(new{schema_version=1,user_id="a",operation_id=operation,server_time=stamp,
            idea=new{id=command.IdeaId,title=command.Title,note=command.Note,reference_links=command.ReferenceLinks,status="pending",created_at=stamp,updated_at=stamp}},Wire.Json));
    }
    var ideaTransport=new Routing(async request=>{
        var path=request.RequestUri!.AbsolutePath;
        if(path.EndsWith("/session"))return Json(new SessionInfo("a","fixture"));
        if(path.EndsWith("/snapshot"))return Json(baseSnapshot);
        if(path.EndsWith("/calendar"))return Json(new CalendarState(1,"a",DateTimeOffset.UtcNow,new(),new(),new(),profileContract));
        if(path.Contains("/idea-sync/")){
            var mode=IdeaMode(request);var operation=calls[^1].Id;var body=calls[^1].Body;
            return mode switch{
                "reject"=>Status(422,"{\"detail\":\"标题不符合要求\"}"),
                "uncertain"=>Status(200,"{\"schema_version\":1,\"user_id\":\"a\",\"operation_id\":\"00000000-0000-0000-0000-000000000000\",\"server_time\":\"2026-11-19T10:00:00+08:00\"}"),
                "unavailable"=>Status(503,"{\"detail\":\"数据库暂时不可用\"}"),
                _=>AckIdea(request,operation,body)};
        }
        await Task.Yield();return new HttpResponseMessage(HttpStatusCode.NoContent);
    });
    using(var repo=new WorkspaceRepository(queueCache,address=>new NativeApi(address,ideaTransport))){
        await repo.RestoreAsync();
        var entity=await repo.SaveIdeaQueuedAsync("合成想法","备注",new());
        Check(repo.Current!.Ideas.Any(i=>i.Id==entity&&i.Title=="合成想法"),"queued idea is visible locally before the server confirms");
        await repo.FlushSyncAsync();
        Check(repo.PendingSyncCount==0&&repo.Current.Ideas.Single().Id==entity&&repo.Current.Ideas.Single().Status=="pending","acknowledged idea replaces the local draft with the server entity");
        Check(calls.Count==1&&DeterministicIds.IdeaCreate("a",calls[0].Id)==entity,"create uses the operation id the server validated");

        behaviour="uncertain";
        var interrupted=await repo.SaveIdeaQueuedAsync("被打断的想法","",new());
        await repo.FlushSyncAsync();
        Check(repo.PendingSyncCount==1&&repo.IdeaSyncState(interrupted)==SyncStates.Pending,"a mismatched receipt keeps the command queued with its own id");
        var pendingItem=repo.SyncItems.Single();
        behaviour="ack";
        await repo.RetrySyncAsync(pendingItem);
        Check(repo.PendingSyncCount==0&&calls.Count(c=>c.Id==pendingItem.Id)==2&&calls.Where(c=>c.Id==pendingItem.Id).Select(c=>c.Body).Distinct().Count()==1,
            "retry reuses the same operation id and byte-identical command");

        behaviour="reject";
        var rejected=await repo.SaveIdeaQueuedAsync("待修正的想法","",new());
        await repo.FlushSyncAsync();
        var failed=repo.SyncItems.Single(item=>item.State==SyncStates.Failed);
        Check(repo.FailedSyncCount==1&&repo.IdeaSyncState(rejected)==SyncStates.Failed&&repo.Current!.Ideas.All(i=>i.Id!=rejected),
            "a rejected command keeps its draft out of the confirmed list");
        var outboxFile=Directory.GetFiles(queueDir).Single(file=>file.EndsWith(".outbox.bin"));
        Check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(outboxFile)).Contains("待修正的想法"),"the queued draft is encrypted at rest");
        Check(await queueCache.ReadOutboxAsync("https://example.test/","b")==null,"another account cannot read this queue");
        behaviour="ack";
        await repo.SaveIdeaQueuedAsync("修正后的想法","",new(),rejected);
        await repo.FlushSyncAsync();
        var reused=calls.Where(c=>c.Id==failed.Id).ToList();
        var firstDraft=JsonSerializer.Deserialize<IdeaSyncCommand>(reused[0].Body,Wire.Json)!;
        var correctedDraft=JsonSerializer.Deserialize<IdeaSyncCommand>(reused[^1].Body,Wire.Json)!;
        Check(reused.Count==2&&firstDraft.Action=="create"&&correctedDraft.Action=="create"&&correctedDraft.Title=="修正后的想法",
            "editing a failed draft replays the same operation id as the original create");
        Check(repo.FailedSyncCount==0&&repo.Current!.Ideas.Single(i=>i.Id==rejected).Title=="修正后的想法","the corrected draft is confirmed once");

        behaviour="uncertain";
        var restart=await repo.SaveIdeaQueuedAsync("重启后同步","",new());
        await repo.FlushSyncAsync();
        var restartItem=repo.SyncItems.Single(item=>item.EntityId==restart);
        Check(restartItem.NextAttempt>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            "a failed transport schedules a durable backoff instead of hammering the server");
        var restartOperation=restartItem.Id;
        await repo.FlushCacheAsync();
        Check(File.Exists(outboxFile),"the pending queue survives on disk for the next start");
        behaviour="ack";
        await Task.Delay(1100);
        using(var restored=new WorkspaceRepository(queueCache,address=>new NativeApi(address,ideaTransport))){
            await restored.RestoreAsync();
            Check(restored.PendingSyncCount==0,$"restart replays the persisted command once (pending={restored.PendingSyncCount})");
            Check(restored.Current!=null&&restored.Current.Ideas.Any(i=>i.Id==restart),"the restarted run confirms the replayed idea locally");
            Check(calls.Count(c=>c.Id==restartOperation)==2,"the restart reused the persisted operation id instead of creating a duplicate");
            await restored.LogoutAsync();
            Check(await queueCache.ReadOutboxAsync("https://example.test/","a")==null&&!File.Exists(outboxFile),
                "logout clears the persisted queue with the cached identity");
        }
    }
    // Capacity guard: a backend that keeps refusing leaves the queue full and blocks new saves.
    var fullDir=Path.Combine(Path.GetTempPath(),"awesome-idea-full-"+Guid.NewGuid());
    var fullCache=new AccountCache(fullDir);
    try {
        await fullCache.SaveConnectionAsync(new("https://example.test/","a","fixture-session"));
        await fullCache.SaveSnapshotAsync("https://example.test/",baseSnapshot);
        var refusing=new Routing(request=>{
            var path=request.RequestUri!.AbsolutePath;
            if(path.EndsWith("/session"))return Task.FromResult(Json(new SessionInfo("a","fixture")));
            if(path.EndsWith("/snapshot"))return Task.FromResult(Json(baseSnapshot));
            return Task.FromResult(Status(503,"{\"detail\":\"数据库暂时不可用\"}"));
        });
        using var full=new WorkspaceRepository(fullCache,address=>new NativeApi(address,refusing));
        await full.RestoreAsync();
        for(int index=0;index<SyncQueueRules.Capacity;index++)await full.SaveIdeaQueuedAsync($"积压 {index}","",new());
        await full.FlushSyncAsync();
        Check(full.PendingSyncCount==SyncQueueRules.Capacity,"the queue stops growing at its documented capacity");
        try { await full.SaveIdeaQueuedAsync("超出容量","",new());throw new Exception("capacity accepted one more item"); }
        catch(ApiFailure failure){ Check(failure.Status==429,"a full queue refuses new saves with a clear reason"); }
        await full.LogoutAsync();
    } finally { foreach(var file in Directory.GetFiles(fullDir)) File.Delete(file); Directory.Delete(fullDir); }
} finally { foreach(var file in Directory.GetFiles(queueDir)) File.Delete(file); Directory.Delete(queueDir); }

// Calendar commands ride the same queue: occurrence days, conflict replay and arrangement ids.
var eventDir=Path.Combine(Path.GetTempPath(),"awesome-event-queue-"+Guid.NewGuid());
var eventCache=new AccountCache(eventDir);
try {
    var repeatEvent=new EventData{Id="repeat",OwnerId="a",Type="personal",Title="每周复盘",
        StartAt=DateTimeOffset.Parse("2026-11-04T09:00:00+08:00"),EndAt=DateTimeOffset.Parse("2026-11-04T10:00:00+08:00"),
        Timezone="Asia/Shanghai",RepeatWeekdays=new(){2},RepeatCompletions=new()};
    var repeatDay=Enumerable.Range(0,14).Select(offset=>new DateTime(2026,11,4).AddDays(offset))
        .First(day=>((int)day.DayOfWeek+6)%7==2).ToString("yyyy-MM-dd");
    var eventSnapshot=snapshot with{ServerTime=DateTimeOffset.UtcNow,Experiences=new(),Ideas=new(),
        Profile=profileContract,Events=new(){repeatEvent}};
    await eventCache.SaveConnectionAsync(new("https://example.test/","a","fixture-session"));
    await eventCache.SaveSnapshotAsync("https://example.test/",eventSnapshot);
    var eventCalls=new List<(string Id,string Body)>();var eventBehaviour="ack";
    var eventTransport=new Routing(async request=>{
        var path=request.RequestUri!.AbsolutePath;
        if(path.EndsWith("/session"))return Json(new SessionInfo("a","fixture"));
        if(path.EndsWith("/snapshot"))return Json(eventSnapshot);
        if(path.EndsWith("/calendar"))return Json(new CalendarState(1,"a",DateTimeOffset.UtcNow,new(){repeatEvent},new(),new(),profileContract));
        if(path.Contains("/calendar-sync/")){
            var operation=path[(path.LastIndexOf('/')+1)..];
            var body=request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            eventCalls.Add((operation,body));
            if(eventBehaviour=="conflict")return Status(409,"{\"detail\":{\"conflicts\":[\"已有重叠日程\"]}}");
            var command=JsonSerializer.Deserialize<CalendarSyncCommand>(body,Wire.Json)!;
            var stamp="2026-11-19T10:00:00+08:00";
            if(command.Action=="delete")
                return Status(200,JsonSerializer.Serialize(new{schema_version=1,user_id="a",operation_id=operation,
                    server_time=stamp,@event=(object?)null,deleted_id=command.EventId},Wire.Json));
            var completions=new Dictionary<string,string>();
            if(command.Action=="completion"&&command.Completed==true)completions[command.OccurrenceDay??repeatDay]=stamp;
            var payload=command.Event;
            object replyEvent=payload==null
                ?new{id=command.EventId,type="personal",title="每周复盘",start_at="2026-11-04T09:00:00+08:00",
                    end_at="2026-11-04T10:00:00+08:00",timezone="Asia/Shanghai",repeat_weekdays=new[]{2},
                    repeat_completions=completions,reminder_offsets_minutes=Array.Empty<int>()}
                :new{id=command.EventId,type=payload.Type,title=payload.Title,start_at=payload.StartAt,end_at=payload.EndAt,
                    timezone=payload.Timezone,repeat_weekdays=payload.RepeatWeekdays,repeat_completions=completions,
                    reminder_offsets_minutes=payload.ReminderOffsetsMinutes};
            return Status(200,JsonSerializer.Serialize(new{schema_version=1,user_id="a",operation_id=operation,server_time=stamp,@event=replyEvent},Wire.Json));
        }
        await Task.Yield();return new HttpResponseMessage(HttpStatusCode.NoContent);
    });
    using(var repo=new WorkspaceRepository(eventCache,address=>new NativeApi(address,eventTransport))){
        await repo.RestoreAsync();
        eventBehaviour="conflict";
        var payload=new CalendarEventPayload{Title="冲突日程",Type="personal",
            StartAt=DateTimeOffset.Parse("2026-11-11T09:00:00+08:00"),EndAt=DateTimeOffset.Parse("2026-11-11T10:00:00+08:00")};
        var conflictEntity=await repo.SaveEventQueuedAsync(payload,null);
        await repo.FlushSyncAsync();
        var conflict=repo.FailedSyncItem(conflictEntity);
        Check(conflict?.Status==409&&conflict.ConflictTitles.Single()=="已有重叠日程","an overlap is queued as a failed command carrying the server conflict titles");
        eventBehaviour="ack";
        await repo.ConfirmOverlapAsync(conflict!);
        var attempts=eventCalls.Where(call=>call.Id==conflict!.Id).ToList();
        var firstAttempt=JsonSerializer.Deserialize<CalendarSyncCommand>(attempts[0].Body,Wire.Json)!;
        var secondAttempt=JsonSerializer.Deserialize<CalendarSyncCommand>(attempts[^1].Body,Wire.Json)!;
        Check(attempts.Count==2&&!firstAttempt.ConfirmOverlap&&secondAttempt.ConfirmOverlap,"confirming the overlap replays the same operation id with confirm_overlap");
        Check(repo.FailedSyncCount==0&&repo.Current!.Events.Count(item=>item.Id==conflictEntity)==1,"the confirmed event exists exactly once");
        var occurrence=repo.Current!.Events.Single(item=>item.Id=="repeat").OnDay(DateTime.Parse(repeatDay))!;
        await repo.CompleteEventQueuedAsync(occurrence);
        await repo.FlushSyncAsync();
        var completion=JsonSerializer.Deserialize<CalendarSyncCommand>(eventCalls[^1].Body,Wire.Json)!;
        Check(completion.EventId=="repeat"&&completion.OccurrenceDay==repeatDay&&completion.Completed==true,
            "a repeat occurrence completion carries the parent id and the occurrence day");
        Check(repo.Current!.Events.Single(item=>item.Id=="repeat").RepeatCompletions.ContainsKey(repeatDay),
            "the occurrence completion is projected onto that day only");
        var ideaPayload=new CalendarEventPayload{Title="安排想法",Type="personal",SourceIdeaId="idea-1",
            StartAt=DateTimeOffset.Parse("2026-11-12T09:00:00+08:00"),EndAt=DateTimeOffset.Parse("2026-11-12T10:00:00+08:00")};
        var arranged=await repo.SaveEventQueuedAsync(ideaPayload,null);
        await repo.FlushSyncAsync();
        Check(arranged==DeterministicIds.IdeaEvent("a","idea-1"),"arranging an idea uses the deterministic idea event id");
        await repo.DeleteEventQueuedAsync(repo.Current!.Events.Single(item=>item.Id==arranged));
        Check(repo.Current!.Events.All(item=>item.Id!=arranged),"a queued delete removes the event before the server confirms");
        await repo.FlushSyncAsync();
        Check(repo.Current!.Events.All(item=>item.Id!=arranged)&&repo.PendingSyncCount==0,"the delete receipt keeps the event removed");
        await repo.LogoutAsync();
    }
} finally { foreach(var file in Directory.GetFiles(eventDir)) File.Delete(file); Directory.Delete(eventDir); }

await WorkspaceContentChecks.Run(Check);
await DesktopReliabilityChecks.Run(Check);
await OfflineDependencyChecks.Run(Check);

// Optional real-server read-only acceptance. No credentials or response bodies are printed or saved.
var server = Environment.GetEnvironmentVariable("AWESOME_TEST_SERVER"); var cookie = Environment.GetEnvironmentVariable("AWESOME_TEST_COOKIE");
if(Environment.GetEnvironmentVariable("AWESOME_TEST_SAVED_CONNECTION")=="1"){
    var saved=await new AccountCache().ReadConnectionAsync();server=saved?.Server;cookie=saved?.Cookie;
}
if (server != null && cookie != null) {
    using var api = new NativeApi(server); api.RestoreCookie(cookie); await api.ConnectAsync(); var real = await api.SnapshotAsync();
    Check(real.UserId==api.Session!.UserId,"real authenticated server snapshot");
    Console.WriteLine($"REAL: {real.Events.Count} events, {real.Experiences.Count} experiences, {real.Todos.Count} todos; payloads not logged.");
    foreach(var path in new[]{"api/mobile/v1/workspace/interviews","api/mobile/v1/workspace/resume"}){
        try{await api.GetAsync<JsonElement>(path);Check(true,"real authenticated workspace capability "+path.Split('/')[^1]);}
        catch(ApiFailure e){Console.WriteLine($"REAL capability {path.Split('/')[^1]} unverified: HTTP {e.Status}; server update may be required.");}
    }
} else Console.WriteLine("Real-account acceptance pending login; no simulated result claimed as real.");
Console.WriteLine($"{passed} checks passed.");

sealed class Reply(string body,string type,HttpStatusCode code) : HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation) => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body,Encoding.UTF8,type) });
}

sealed class Routing(Func<HttpRequestMessage,Task<HttpResponseMessage>> callback) : HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)=>callback(request);
}
