using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AwesomeInterview.NativeDemo.Data;

public sealed partial class WorkspaceRepository : IDisposable
{
    readonly SemaphoreSlim syncGate = new(1, 1);
    readonly AccountCache cache;
    readonly Func<string,NativeApi> apiFactory;
    Task persistTail = Task.CompletedTask;
    Task syncTail = Task.CompletedTask;
    Exception? queuePersistenceError;
    bool outboxUnavailable;
    SavedConnection? identity;
    int epoch;
    SyncOutbox outbox = new() { UserId = "" };
    List<IdeaData> confirmedIdeas = new();
    List<EventData> confirmedEvents = new();
    public NativeApi? Api { get; private set; }
    public Snapshot? Current { get; private set; }
    public bool Online { get; private set; }
    public bool RequiresSignIn { get; private set; }
    public bool Busy { get; private set; }
    public bool SyncBusy { get; private set; }
    public string Message { get; private set; } = "登录后查看你的日程与面经。";
    public event Action? Changed;
    public WorkspaceRepository(AccountCache? cache = null, Func<string,NativeApi>? apiFactory = null)
    { this.cache = cache ?? new(); this.apiFactory = apiFactory ?? (server => new NativeApi(server)); }

    public int PendingSyncCount => outbox.Items.Count;
    public bool HasPendingSync => outbox.Items.Count > 0;
    public IReadOnlyList<SyncQueueItem> SyncItems => outbox.Items;
    public int FailedSyncCount => outbox.Items.Count(item => item.State == SyncStates.Failed);
    // Pending commands are projected into the visible list; a failed one stays in the status strip.
    public string? IdeaSyncState(string ideaId) => outbox.Items.FirstOrDefault(item => item.EntityId == ideaId)?.State;
    public string? EventSyncState(string eventId) => outbox.Items.FirstOrDefault(item => item.EntityId == eventId)?.State;
    public SyncQueueItem? FailedSyncItem(string entityId) =>
        outbox.Items.FirstOrDefault(item => item.EntityId == entityId && item.State == SyncStates.Failed);

    public async Task RestoreAsync()
    {
        await syncGate.WaitAsync(); Busy = true;
        try {
            identity = await cache.ReadConnectionAsync(); if(identity == null) return;
            Api = apiFactory(identity.Server); Api.RestoreCookie(identity.Cookie);
            try { Current = await cache.ReadSnapshotAsync(Api.Server.AbsoluteUri, identity.UserId); }
            catch(Exception error) when(Expected(error)) { Current = null; Message = "本机快照不可用，正在重新读取；仍可退出并清理账号。"; }
            await LoadOutboxAsync(identity);
            if(Current != null) Message = $"已显示本机缓存，更新于 {Current.ServerTime:MM-dd HH:mm}。";
        }
        catch(Exception error) when(Expected(error)) { Message = Explain(error); }
        finally { Busy=false; syncGate.Release(); Changed?.Invoke(); }
        if(Current!=null && DateTimeOffset.UtcNow-Current.ServerTime<TimeSpan.FromHours(18))
            await ValidateSessionAsync();
        else await SynchronizeAsync();
        await SyncNowAsync();
    }
    async Task LoadOutboxAsync(SavedConnection saved)
    {
        outboxUnavailable=false;
        try { outbox = await cache.ReadOutboxAsync(saved.Server, saved.UserId) ?? new SyncOutbox { UserId = saved.UserId }; }
        catch(Exception error) when(Expected(error)) { outboxUnavailable=true;outbox = new SyncOutbox { UserId = saved.UserId };Message="本机同步队列无法读取，已保留原文件；暂停新增离线更改。"; }
        foreach(var item in outbox.Items.Where(item=>item.State==SyncStates.Sending))item.State=SyncStates.Pending;
        confirmedIdeas = outbox.ConfirmedIdeas?.ToList() ?? Current?.Ideas.ToList() ?? new();
        confirmedEvents = outbox.ConfirmedEvents?.ToList() ?? Current?.Events.ToList() ?? new();
        if(confirmedIdeas.Any(item=>item.OwnerId!=saved.UserId)||confirmedEvents.Any(item=>item.OwnerId!=saved.UserId))throw new InvalidDataException("同步缓存账号不匹配。");
        RebuildProjection();
    }
    async Task ValidateSessionAsync()
    {
        await syncGate.WaitAsync();Busy=true;
        try {
            await Api!.ConnectAsync();
            if(Api.Session!.UserId!=identity?.UserId)throw new ApiFailure(401,"会话账号已变化，请重新登录。");
            foreach(var item in outbox.Items.Where(item=>item.State==SyncStates.Auth)){item.State=SyncStates.Pending;item.NextAttempt=0;}
            Online=true;RequiresSignIn=false;Message="已加载18小时有效期内的本机缓存；可手动同步获取跨端修改。";
        }
        catch(Exception error)when(Expected(error)){OnConnectionFailure(error);}
        finally{Busy=false;syncGate.Release();Changed?.Invoke();}
    }
    internal async Task<RememberedLogin?> ReadRememberedLoginAsync()
    {
        var saved = await cache.ReadLoginAsync();
        return IsMatchingLogin(saved) ? new RememberedLogin(saved!.Server, saved.Identifier) : null;
    }

    bool IsMatchingLogin(SavedLogin? saved) => saved != null &&
        (identity == null || (identity.UserId == saved.UserId && identity.Server == saved.Server)) &&
        (Current == null || (Current.UserId == saved.UserId && Api?.Server.AbsoluteUri == saved.Server));

    internal async Task ForgetRememberedLoginAsync()
    {
        await syncGate.WaitAsync();
        try { cache.ForgetLogin(); }
        finally { syncGate.Release(); }
    }

    internal async Task LoginRememberedAsync()
    {
        await syncGate.WaitAsync();
        try
        {
            var saved = await cache.ReadLoginAsync();
            if (!IsMatchingLogin(saved)) throw new ApiFailure(401, "当前账号没有保存的密码，请手动登录。");
            await LoginCoreAsync(saved!.Server, saved.Identifier, saved.Password, true, saved.UserId);
        }
        finally
        {
            Busy = false;
            syncGate.Release();
            Changed?.Invoke();
        }
    }

    public async Task LoginAsync(string server, string name, string password, bool rememberPassword = false)
    {
        await syncGate.WaitAsync();
        try { await LoginCoreAsync(server, name, password, rememberPassword); }
        finally
        {
            Busy = false;
            syncGate.Release();
            Changed?.Invoke();
        }
    }

    async Task LoginCoreAsync(string server, string name, string password, bool rememberPassword, string? expectedUser = null)
    {
        var candidate = apiFactory(server);
        Busy = true;
        Changed?.Invoke();
        try
        {
            await candidate.LoginAsync(name.Trim(), password);
            if (expectedUser != null && candidate.Session?.UserId != expectedUser)
                throw new ApiFailure(401, "已保存凭据对应的账号发生变化，请手动登录。");
            var snapshot = await candidate.SnapshotAsync();
            var saved = candidate.SessionCookie is string cookie
                ? new SavedConnection(candidate.Server.AbsoluteUri, snapshot.UserId, cookie) : null;
            await FlushCacheAsync();
            var queue = saved != null
                ? await cache.ReadOutboxAsync(saved.Server, saved.UserId) ?? new SyncOutbox { UserId = saved.UserId }
                : new SyncOutbox { UserId = snapshot.UserId };

            ++epoch;
            Api?.Dispose();
            Api = candidate;
            Current = snapshot;
            Online = true;
            RequiresSignIn = false;
            identity = saved;
            outbox = queue;
            outboxUnavailable = false;
            foreach (var item in outbox.Items.Where(item => item.State is SyncStates.Auth or SyncStates.Sending))
            {
                item.State = SyncStates.Pending;
                item.NextAttempt = 0;
            }
            confirmedIdeas = snapshot.Ideas.ToList();
            confirmedEvents = snapshot.Events.ToList();
            RebuildProjection();
            Message = outbox.Items.Count > 0 ? $"已连接 · {outbox.Items.Count} 项本机修改待同步。" : "已连接，数据与 Web 账号同步。";
            QueuePersist();
            try
            {
                if (rememberPassword)
                    await cache.SaveLoginAsync(new SavedLogin(candidate.Server.AbsoluteUri, snapshot.UserId, name.Trim(), password));
                else
                    cache.ForgetLogin();
            }
            catch (Exception error) when (Expected(error))
            {
                Message = rememberPassword ? "已登录，但密码未能保存。" : "已登录，但旧凭据未能删除，请在账号页重试。";
            }
        }
        catch
        {
            if (Api != candidate) candidate.Dispose();
            throw;
        }
    }

    void OnConnectionFailure(Exception error)
    {
        Online = false;
        if (error is ApiFailure { Status: 401 or 403 }) RequiresSignIn = true;
        Message = (RequiresSignIn ? "登录已过期，请重新登录。" : Explain(error)) +
            (Current != null ? " 当前可查看本机缓存。" : "");
    }
    // Capture record lists before serializing off the dispatcher. Writes stay ordered.
    void QueuePersist()
    {
        if(Api == null || Current == null) return;
        var value = Current with { Events = new(Current.Events), Experiences = new(Current.Experiences), Todos = new(Current.Todos), Ideas = new(Current.Ideas) };
        var saved = identity; string server=Api.Server.AbsoluteUri; int version=epoch;
        if(saved!=null&&!outboxUnavailable){
            // Confirmed bases and commands share one atomic encrypted file. Restart must
            // not mistake an optimistic snapshot for an acknowledged server value.
            var queue = new SyncOutbox { SchemaVersion = 1, UserId = outbox.UserId,
                Items = outbox.Items.Select(item => item.Clone()).ToList(),ConfirmedEvents=new(confirmedEvents),ConfirmedIdeas=new(confirmedIdeas) };
            persistTail = PersistOutboxAfterAsync(persistTail,queue,saved,server,version);
        }
        persistTail = PersistAfterAsync(persistTail,value,saved,server,version);
    }
    async Task PersistAfterAsync(Task previous, Snapshot value, SavedConnection? saved, string server, int version)
    {
        await previous;
        try { await cache.SaveSnapshotAsync(server,value); if(saved != null) await cache.SaveConnectionAsync(saved); }
        catch(Exception error) when(Expected(error)) { if(version == epoch) { Message="服务端操作已成功，但本机缓存保存失败；请稍后同步。"; Changed?.Invoke(); } }
    }
    async Task PersistOutboxAfterAsync(Task previous, SyncOutbox value, SavedConnection saved, string server, int version)
    {
        await previous;
        try { await cache.SaveOutboxAsync(server,value.UserId,value);if(version==epoch)queuePersistenceError=null; }
        catch(Exception error) when(Expected(error)) { if(version == epoch) { queuePersistenceError=error;Message="待同步修改尚未写入磁盘，仅保留在内存；请勿退出。"; Changed?.Invoke(); } }
    }
    public Task FlushCacheAsync() => persistTail;
    public async Task SynchronizeAsync() => await RefreshAsync(true);
    public async Task RefreshCalendarAsync() => await RefreshAsync(false);
    async Task RefreshAsync(bool full)
    {
        if(Api == null || !await syncGate.WaitAsync(0)) return;
        Busy=true; Changed?.Invoke();
        try {
            await Api.ConnectAsync();
            if((Current?.UserId ?? identity?.UserId) is string owner && owner != Api.Session!.UserId)
                throw new ApiFailure(401,"会话账号已变化，请重新登录。");
            if(full || Current == null) Current=await Api.SnapshotAsync();
            else {
                var state=await Api.GetAsync<CalendarState>("api/mobile/v1/calendar");
                // ServerTime tracks the full snapshot's freshness, including experiences.
                // A calendar-only refresh must not extend the lifetime of untouched notes.
                var next=Current with { SchemaVersion=state.SchemaVersion, UserId=state.UserId,
                    Events=state.Events, Todos=state.Todos, Ideas=state.Ideas, Profile=state.Profile };
                next.Validate(Api.Session!.UserId); Current=next;
            }
            confirmedIdeas = Current.Ideas.ToList(); confirmedEvents = Current.Events.ToList(); RebuildProjection();
            Online=true; RequiresSignIn=false; Message=$"已同步 · {CalendarProjection.Now:HH:mm} · {Current.Events.Count} 条日程 / {Current.Experiences.Count} 份面经"+
                (outbox.Items.Count > 0 ? $" · {outbox.Items.Count} 项待同步" : ""); QueuePersist();
        }
        catch(Exception error) when(Expected(error)) { OnConnectionFailure(error); }
        finally { Busy=false; syncGate.Release(); Changed?.Invoke(); }
    }
    // Calendar writes use the same encrypted queue and the Web's operation-id receipts.
    public async Task<string> SaveEventQueuedAsync(CalendarEventPayload payload,string? eventId)
    {
        string entity;
        await syncGate.WaitAsync();
        try {
            RequireLoaded();
            // A failed create keeps its operation id: the entity id was derived from it.
            var failedCreate = string.IsNullOrWhiteSpace(eventId) ? null
                : outbox.Items.FirstOrDefault(item => item.State == SyncStates.Failed && item.EntityId == eventId &&
                    ReadEventCommand(item)?.Action == "create");
            if(!string.IsNullOrWhiteSpace(eventId)) outbox.Items.RemoveAll(item => item.State == SyncStates.Failed && item.EntityId == eventId);
            var operation = failedCreate?.Id ?? Guid.NewGuid().ToString();
            var action = failedCreate != null || string.IsNullOrWhiteSpace(eventId) ? "create" : "edit";
            entity = !string.IsNullOrWhiteSpace(eventId) ? eventId!
                : payload.SourceIdeaId is string ideaId && !string.IsNullOrWhiteSpace(ideaId)
                    ? DeterministicIds.IdeaEvent(OwnerId,ideaId)
                    : DeterministicIds.CalendarCreate(OwnerId,operation);
            QueueEventLocked(action,entity,operation,payload,null,null,null,false);
        }
        finally { syncGate.Release(); Changed?.Invoke(); }
        syncTail = SyncNowAsync();
        return entity;
    }
    public Task CompleteEventQueuedAsync(EventData item) =>
        EnqueueEventMutationAsync("completion",item,null,item.CompletedAt==null,null);
    public Task DeferEventQueuedAsync(EventData item,DateTimeOffset end) =>
        EnqueueEventMutationAsync("defer",item,null,null,end);
    public Task CancelEventQueuedAsync(EventData item) =>
        EnqueueEventMutationAsync("cancel",item,null,null,null);
    public Task CloseRepeatQueuedAsync(EventData item) =>
        EnqueueEventMutationAsync("close_repeat",item,null,null,null);
    public Task DeleteEventQueuedAsync(EventData item) =>
        EnqueueEventMutationAsync("delete",item,null,null,null);
    async Task EnqueueEventMutationAsync(string action,EventData item,CalendarEventPayload? payload,bool? completed,DateTimeOffset? endAt)
    {
        await syncGate.WaitAsync();
        try {
            RequireLoaded();
            var (parent,day) = SyncQueueRules.SplitOccurrence(item.Id);
            QueueEventLocked(action,parent,Guid.NewGuid().ToString(),payload,day,completed,endAt,false);
        }
        finally { syncGate.Release(); Changed?.Invoke(); }
        syncTail = SyncNowAsync();
    }
    // A 409 wrote no receipt, so the same operation id may be replayed with confirm_overlap.
    public async Task ConfirmOverlapAsync(SyncQueueItem item)
    {
        await syncGate.WaitAsync();
        try {
            var command = ReadEventCommand(item);
            if(command == null) { item.State = SyncStates.Pending; item.NextAttempt = 0; item.Error = null; item.Status = null; }
            else {
                item.Command = JsonSerializer.SerializeToElement(new { action = command.Action, event_id = command.EventId,
                    occurrence_day = command.OccurrenceDay, @event = command.Event, completed = command.Completed,
                    end_at = command.EndAt, confirm_overlap = true }, Wire.Json);
                item.State = SyncStates.Pending; item.NextAttempt = 0; item.Error = null; item.Status = null;
            }
            RebuildProjection(); QueuePersist();
        }
        finally { syncGate.Release(); }
        Changed?.Invoke(); await SyncNowAsync();
    }
    void QueueEventLocked(string action,string entity,string operation,CalendarEventPayload? payload,string? occurrenceDay,
                          bool? completed,DateTimeOffset? endAt,bool confirmOverlap)
    {
        if(outbox.Items.Count >= SyncQueueRules.Capacity) throw new ApiFailure(429,"待同步操作较多，请先联网同步。");
        var body = JsonSerializer.SerializeToElement(new { action, event_id = entity, occurrence_day = occurrenceDay,
            @event = payload, completed, end_at = endAt, confirm_overlap = confirmOverlap }, Wire.Json);
        outbox.Items.Add(new SyncQueueItem { Id = operation, Endpoint = SyncQueueRules.EventEndpoint, EntityId = entity,
            Title = payload?.Title ?? SyncActionLabel(action), Command = body, CreatedAt = SyncQueueRules.CreateStamp() });
        RebuildProjection();
        Message = Online ? "已保存到本机，正在同步…" : "已保存到本机，联网后自动同步。";
        QueuePersist();
    }
    static string SyncActionLabel(string action) => action switch {
        "completion" => "完成日程", "defer" => "延期日程", "cancel" => "废除日程",
        "close_repeat" => "结束重复系列", "delete" => "删除日程", "edit" => "修改日程", _ => "新建日程" };
    string OwnerId => Current?.UserId ?? throw new ApiFailure(401,"请先登录并载入一次账号数据。");
    void RequireLoaded() { if(Current == null) throw new ApiFailure(401,"请先登录并载入一次账号数据。");if(outboxUnavailable)throw new ApiFailure(409,"本机同步队列无法读取，已保留原文件，请先恢复队列或明确清理账号缓存。"); }
    public Task<TodoData> SaveTodoAsync(string title,DateOnly day) => MutateAsync(
        ()=>Api!.WriteAsync<TodoData>(HttpMethod.Put,"api/mobile/v1/todos",new{title,due_date=day}),ReplaceTodo);
    public Task<TodoData> CompleteTodoAsync(TodoData item) => MutateAsync(
        ()=>Api!.WriteAsync<TodoData>(HttpMethod.Post,$"api/mobile/v1/todos/{Uri.EscapeDataString(item.Id)}/completion",new{completed=item.CompletedAt==null}),ReplaceTodo);
    public async Task DeleteTodoAsync(TodoData item) => await MutateAsync(async()=>{
        await Api!.DeleteAsync("api/mobile/v1/todos/"+Uri.EscapeDataString(item.Id));return item;
    },value=>Current!.Todos.RemoveAll(t=>t.Id==value.Id));
    // Pending ideas are written to the encrypted queue first and replayed by operation id,
    // so a timeout cannot create a second record and offline input is never lost.
    public Task<string> SaveIdeaQueuedAsync(string title,string note,List<ReferenceLink> links,string? id=null) =>
        EnqueueIdeaAsync(id == null ? "create" : "edit", id, title, note, links);
    public Task<string> AbandonIdeaQueuedAsync(IdeaData item) =>
        EnqueueIdeaAsync("abandon", item.Id, item.Title, item.Note, item.ReferenceLinks);
    async Task<string> EnqueueIdeaAsync(string action,string? ideaId,string title,string note,List<ReferenceLink> links)
    {
        string entity;
        await syncGate.WaitAsync();
        try {
            RequireLoaded();
            if(outbox.Items.Count >= SyncQueueRules.Capacity) throw new ApiFailure(429,"待同步操作较多，请先联网同步。");
            // Read the failed create before dropping it: its operation id is the only valid
            // create id for this entity, because the id was derived from that operation.
            var failedCreate = string.IsNullOrWhiteSpace(ideaId) ? null
                : outbox.Items.FirstOrDefault(item => item.State == SyncStates.Failed && item.EntityId == ideaId &&
                    ReadIdeaCommand(item)?.Action == "create");
            if(!string.IsNullOrWhiteSpace(ideaId)) outbox.Items.RemoveAll(item => item.State == SyncStates.Failed && item.EntityId == ideaId);
            var operation = failedCreate?.Id ?? Guid.NewGuid().ToString();
            // A failed create never wrote a receipt, so replaying it with the same operation id
            // (and the same deterministic id) is safe and is the only valid id for that entity.
            if(failedCreate != null) action = "create";
            entity = ideaId ?? DeterministicIds.IdeaCreate(OwnerId, operation);
            var body = JsonSerializer.SerializeToElement(new { action, idea_id = entity, title, note, reference_links = links }, Wire.Json);
            outbox.Items.Add(new SyncQueueItem { Id = operation, Endpoint = SyncQueueRules.IdeaEndpoint,
                EntityId = entity, Title = title, Command = body, CreatedAt = SyncQueueRules.CreateStamp() });
            RebuildProjection();
            Message = Online ? "已保存到本机，正在同步…" : "已保存到本机，联网后自动同步。";
            QueuePersist();
        }
        finally { syncGate.Release(); Changed?.Invoke(); }
        syncTail = SyncNowAsync();
        return entity;
    }
    public Task FlushSyncAsync() => syncTail;
    public async Task RetrySyncAsync(SyncQueueItem item)
    {
        await syncGate.WaitAsync();
        try {
            item.State = SyncStates.Pending; item.NextAttempt = 0; item.Error = null; item.Status = null;
            RebuildProjection(); QueuePersist();
        }
        finally { syncGate.Release(); }
        Changed?.Invoke(); await SyncNowAsync();
    }
    public async Task DiscardSyncAsync(SyncQueueItem item)
    {
        await syncGate.WaitAsync();
        try {
            outbox.Items.RemoveAll(entry => entry.EntityId == item.EntityId && entry.State == SyncStates.Failed);
            RebuildProjection(); Message = "已放弃本次本机更改。"; QueuePersist();
        }
        finally { syncGate.Release(); }
        Changed?.Invoke();
    }
    // Arranging needs the idea on the server: waiting for the queue keeps the source id honest.
    public async Task FlushSyncForEntityAsync(string entityId)
    {
        await syncTail;
        if(!await syncGate.WaitAsync(TimeSpan.FromSeconds(5))) throw new ApiFailure(429,"正在同步，请稍候重试。");
        syncGate.Release();
        await SyncNowAsync();
        if(outbox.Items.Any(item => item.EntityId == entityId)) throw new ApiFailure(503,"事项尚未同步，请稍后重试。");
    }
    public async Task SyncNowAsync()
    {
        if(!await syncGate.WaitAsync(0)) return;
        var anySuccess = false;
        try {
            if(Api == null || Current == null || outbox.Items.Count == 0 || RequiresSignIn) return;
            await FlushCacheAsync();if(queuePersistenceError!=null){Message="本机队列尚未安全写盘，本次不发送，请检查磁盘后重试。";return;}
            if(Api.Session==null){await Api.ConnectAsync();if(Api.Session!.UserId!=Current.UserId)throw new ApiFailure(401,"会话账号已变化，请重新登录。");foreach(var pending in outbox.Items.Where(entry=>entry.State==SyncStates.Auth)){pending.State=SyncStates.Pending;pending.NextAttempt=0;}}
            SyncBusy = true; Changed?.Invoke();
            while(true) {
                var item = outbox.Items.FirstOrDefault(entry => entry.State == SyncStates.Pending);
                if(item == null || item.NextAttempt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) break;
                var source=ReadEventCommand(item)?.Event?.SourceIdeaId;
                if(source!=null&&outbox.Items.Any(entry=>entry.Endpoint==SyncQueueRules.IdeaEndpoint&&entry.EntityId==source&&entry.State==SyncStates.Failed)){MarkSyncFailed(item,new ApiFailure(422,"请先修正来源事项，再重试排期。"));continue;}
                item.State = SyncStates.Sending; Changed?.Invoke();
                JsonElement reply;
                try {
                    reply = await Api.WriteAsync<JsonElement>(HttpMethod.Post, item.Endpoint + "/" + item.Id, item.Command);
                    anySuccess = true;
                }
                catch(ApiFailure failure) when(SyncQueueRules.IsPermanentRejection(failure.Status)) { MarkSyncFailed(item,failure); continue; }
                catch(ApiFailure failure) {
                    item.Attempts++;
                    if(failure.Status is 401 or 403) {
                        item.State = SyncStates.Auth; item.Error = failure.Message; Online = false; RequiresSignIn = true;
                        Message = "登录已过期，本机修改已保留；重新登录后自动同步。";
                        RebuildProjection(); QueuePersist(); break;
                    }
                    RetrySyncLater(item,failure.Message); Online = false;
                    Message = $"{failure.Message} 本机修改已保留，稍后自动重试。";
                    break;
                }
                catch(Exception error) when(error is HttpRequestException or OperationCanceledException) {
                    RetrySyncLater(item,Explain(error)); Online = false;
                    Message = "网络不可用，本机修改仍在队列中，恢复后自动重试。";
                    break;
                }
                catch(Exception error) when(Expected(error)) { RetrySyncLater(item,Explain(error)); break; }
                AcknowledgeSync(item,reply);
            }
            if(anySuccess && !RequiresSignIn) { Online = true; Message = outbox.Items.Count == 0 ? "本机修改已全部同步。" : $"已同步 · 仍有 {outbox.Items.Count} 项待处理"; }
        }
        catch (Exception error) when (Expected(error))
        {
            if (error is ApiFailure { Status: 401 or 403 }) OnConnectionFailure(error);
            else Message = Explain(error);
        }
        // SyncNowAsync is also fire-and-forget from a save, so it must never surface an exception.
        catch(Exception) { Message = "同步时出现异常，本机修改已保留；请稍后重试。"; }
        finally { SyncBusy = false; syncGate.Release(); Changed?.Invoke(); }
    }
    void RetrySyncLater(SyncQueueItem item,string reason)
    {
        item.Attempts++; item.State = SyncStates.Pending; item.Error = reason;
        item.NextAttempt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + SyncQueueRules.RetryDelayMilliseconds(item.Attempts);
        RebuildProjection(); QueuePersist();
    }
    void MarkSyncFailed(SyncQueueItem item,ApiFailure failure)
    {
        // Later commands for the same entity depend on this one, so they fail with it.
        var cascade = false;
        foreach(var entry in outbox.Items) {
            if(entry == item) cascade = true;
            if(!cascade || entry.EntityId != item.EntityId) continue;
            entry.State = SyncStates.Failed; entry.Error = failure.Message; entry.Status = failure.Status;
            entry.ConflictTitles = failure.ConflictTitles.ToList();
        }
        if(item.Endpoint==SyncQueueRules.IdeaEndpoint)foreach(var entry in outbox.Items.Where(entry=>entry.Endpoint==SyncQueueRules.EventEndpoint&&ReadEventCommand(entry)?.Event?.SourceIdeaId==item.EntityId)){
            entry.State=SyncStates.Failed;entry.Error="来源事项尚未被服务器接受，请先修正来源事项后重试排期。";entry.Status=422;
        }
        Message = $"有 {FailedSyncCount} 项修改被服务器拒绝，可在日历或待安排页重试、确认重叠或放弃。";
        RebuildProjection(); QueuePersist();
    }
    void AcknowledgeSync(SyncQueueItem item,JsonElement reply)
    {
        string? operation = null, owner = null;
        JsonElement payload = default;
        bool isEvent = item.Endpoint == SyncQueueRules.EventEndpoint;
        if(reply.ValueKind == JsonValueKind.Object) {
            if(reply.TryGetProperty("operation_id",out var id) && id.ValueKind == JsonValueKind.String) operation = id.GetString();
            if(reply.TryGetProperty("user_id",out var user) && user.ValueKind == JsonValueKind.String) owner = user.GetString();
            if(reply.TryGetProperty(isEvent ? "event" : "idea",out var body) && body.ValueKind == JsonValueKind.Object) payload = body;
        }
        // Anything short of a matching receipt is inconclusive: keep the id and retry.
        if(operation != item.Id || owner != Current?.UserId || !HasValidReceiptBody(item,reply,payload)) { RetrySyncLater(item,"服务端回执不完整，稍后重试。"); return; }
        outbox.Items.Remove(item);
        if(isEvent) ApplyEventAck(item,reply,payload); else ApplyIdeaAck(payload);
        RebuildProjection(); QueuePersist();
    }
    void ApplyIdeaAck(JsonElement payload)
    {
        if(payload.ValueKind != JsonValueKind.Object || Current == null) return;
        IdeaSyncPayload? parsed = null;
        try { parsed = payload.Deserialize<IdeaSyncPayload>(Wire.Json); } catch(JsonException) { }
        if(parsed == null) { Message = "服务端回执格式异常，已按本机状态保留；请稍后同步核对。"; return; }
        var existing = confirmedIdeas.FirstOrDefault(idea => idea.Id == parsed.Id);
        var value = new IdeaData(parsed.Id,Current.UserId,parsed.Title,parsed.Note,parsed.ReferenceLinks ?? new(),
            parsed.Status,parsed.CreatedAt,parsed.UpdatedAt,existing?.ArrangedEventId);
        int index = confirmedIdeas.FindIndex(idea => idea.Id == value.Id);
        if(index < 0) confirmedIdeas.Add(value); else confirmedIdeas[index] = value;
    }
    void ApplyEventAck(SyncQueueItem item,JsonElement reply,JsonElement payload)
    {
        if(Current == null) return;
        if(reply.ValueKind == JsonValueKind.Object && reply.TryGetProperty("deleted_id",out var deleted) && deleted.ValueKind == JsonValueKind.String) {
            if(deleted.GetString() is string removed) confirmedEvents.RemoveAll(entry => entry.Id == removed.Split('~')[0]);
        } else if(payload.ValueKind == JsonValueKind.Object) {
            var parsed = ReadEventPayload(payload,Current.UserId);
            if(parsed == null) Message = "服务端回执格式异常，已按本机状态保留；请稍后同步核对。";
            else {
                int index = confirmedEvents.FindIndex(entry => entry.Id == parsed.Id);
                if(index < 0) confirmedEvents.Add(parsed); else confirmedEvents[index] = parsed;
            }
        }
        // Arranging an idea links it locally as soon as the server confirms the event.
        var command = ReadEventCommand(item);
        if(command?.Event?.SourceIdeaId is string ideaId && !string.IsNullOrWhiteSpace(ideaId)) {
            int index = confirmedIdeas.FindIndex(idea => idea.Id == ideaId);
            if(index >= 0) confirmedIdeas[index] = confirmedIdeas[index] with {
                ArrangedEventId = command.EventId, UpdatedAt = SyncQueueRules.ParseStamp(item.CreatedAt) };
        }
    }
    // The sync reply omits owner_id, so it is re-injected before the strict EventData read.
    static EventData? ReadEventPayload(JsonElement payload,string owner)
    {
        try {
            using var buffer = new MemoryStream();
            using(var writer = new Utf8JsonWriter(buffer)) {
                writer.WriteStartObject();
                writer.WriteString("owner_id",owner);
                foreach(var property in payload.EnumerateObject())
                    if(property.Name != "owner_id") property.WriteTo(writer);
                writer.WriteEndObject();
            }
            return JsonSerializer.Deserialize<EventData>(buffer.ToArray(),Wire.Json);
        }
        catch(JsonException) { return null; }
    }
    static IdeaSyncCommand? ReadIdeaCommand(SyncQueueItem item)
    {
        try { return item.Command.Deserialize<IdeaSyncCommand>(Wire.Json); }
        catch { return null; }
    }
    static CalendarSyncCommand? ReadEventCommand(SyncQueueItem item)
    {
        try { return item.Command.Deserialize<CalendarSyncCommand>(Wire.Json); }
        catch { return null; }
    }
    void RebuildProjection()
    {
        if(Current == null) return;
        var events = new Dictionary<string,EventData>();
        foreach(var item in confirmedEvents) events[item.Id] = item;
        var ideas = new Dictionary<string,IdeaData>();
        foreach(var idea in confirmedIdeas) ideas[idea.Id] = idea;
        foreach(var entry in outbox.Items) {
            if(entry.State == SyncStates.Failed) continue;
            var stamp = SyncQueueRules.ParseStamp(entry.CreatedAt);
            if(entry.Endpoint == SyncQueueRules.IdeaEndpoint) ProjectIdeaCommand(entry,ideas,stamp);
            else if(entry.Endpoint == SyncQueueRules.EventEndpoint) ProjectEventCommand(entry,events,stamp);
        }
        foreach(var entry in outbox.Items.Where(entry=>entry.State!=SyncStates.Failed&&entry.Endpoint==SyncQueueRules.EventEndpoint)){
            var command=ReadEventCommand(entry);if(command?.Event?.SourceIdeaId is string source&&ideas.TryGetValue(source,out var idea)&&events.ContainsKey(command.EventId))ideas[source]=idea with{ArrangedEventId=command.EventId};
        }
        Current = Current with {
            Ideas = ideas.Values.OrderBy(idea => idea.CreatedAt).ThenBy(idea => idea.Id,StringComparer.Ordinal).ToList(),
            Events = events.Values.OrderBy(item => item.StartAt).ThenBy(item => item.Id,StringComparer.Ordinal).ToList() };
    }
    void ProjectIdeaCommand(SyncQueueItem entry,Dictionary<string,IdeaData> items,DateTimeOffset stamp)
    {
        var command = ReadIdeaCommand(entry); if(command == null) return;
        items.TryGetValue(command.IdeaId,out var existing);
        if(command.Action == "abandon") {
            if(existing != null) items[existing.Id] = existing with { Status = "abandoned", UpdatedAt = stamp };
        } else if(command.Action == "create" || existing != null) {
            items[command.IdeaId] = new IdeaData(command.IdeaId,Current!.UserId,command.Title,command.Note,
                command.ReferenceLinks ?? new(),"pending",existing?.CreatedAt ?? stamp,stamp,existing?.ArrangedEventId);
        }
    }
    void ProjectEventCommand(SyncQueueItem entry,Dictionary<string,EventData> items,DateTimeOffset stamp)
    {
        var command = ReadEventCommand(entry); if(command == null) return;
        var id = command.EventId;
        if(command.Action == "delete") { items.Remove(id); return; }
        items.TryGetValue(id,out var existing);
        if(command.Action == "create") {
            var payload = command.Event; if(payload == null) return;
            items[id] = new EventData() {
                Id = id, OwnerId = Current!.UserId, Type = payload.Type, Title = payload.Title,
                StartAt = payload.StartAt, EndAt = payload.EndAt, Timezone = payload.Timezone,
                SkipWeekends = payload.SkipWeekends, WorkdayOnly = payload.WorkdayOnly,
                RepeatWeekdays = payload.RepeatWeekdays ?? new(),
                ReminderOffsetsMinutes = payload.ReminderOffsetsMinutes ?? new(),
                LocationText = payload.LocationText, LocationUrl = payload.LocationUrl, Note = payload.Note,
                Position = payload.Position, ReferenceLinks = payload.ReferenceLinks ?? new(), Source = payload.Source,
                Platform = payload.Platform, RoomNumber=payload.RoomNumber, MeetingUrl = payload.MeetingUrl, CreatedAt = stamp, UpdatedAt = stamp };
            return;
        }
        if(existing == null) return;
        switch(command.Action) {
            case "edit" when command.Event is CalendarEventPayload payload:
                existing = existing with { Title = payload.Title, Type = payload.Type, StartAt = payload.StartAt,
                    EndAt = payload.EndAt, Timezone = payload.Timezone, SkipWeekends = payload.SkipWeekends,
                    WorkdayOnly = payload.WorkdayOnly, RepeatWeekdays = payload.RepeatWeekdays ?? new(),
                    ReminderOffsetsMinutes = payload.ReminderOffsetsMinutes ?? new(), LocationText = payload.LocationText,
                    LocationUrl = payload.LocationUrl, Note = payload.Note, Position = payload.Position,
                    ReferenceLinks = payload.ReferenceLinks ?? new(), Platform = payload.Platform, RoomNumber=payload.RoomNumber??existing.RoomNumber, MeetingUrl = payload.MeetingUrl };
                break;
            case "completion" when existing.RepeatWeekdays.Count > 0 && command.OccurrenceDay is string day:
                var completions = new Dictionary<string,DateTimeOffset>(existing.RepeatCompletions);
                if(command.Completed == true) completions[day] = stamp; else completions.Remove(day);
                existing = existing with { RepeatCompletions = completions };
                break;
            case "completion":
                existing = existing with { CompletedAt = command.Completed == true ? stamp : null };
                break;
            case "defer" when command.EndAt is DateTimeOffset end:
                existing = existing with { EndAt = end };
                break;
            case "cancel":
                // Cancelling keeps the record but stops reminders, matching the Web projection.
                existing = existing with { CancelledAt = stamp, ReminderOffsetsMinutes = new() };
                break;
            case "close_repeat":
                existing = existing with { RepeatClosedOn = stamp.ToString("yyyy-MM-dd") };
                break;
        }
        items[id] = existing with { UpdatedAt = stamp };
    }
    public void LinkIdea(string ideaId,string eventId)
    {
        if(Current==null)return;
        confirmedIdeas = confirmedIdeas.Select(i=>i.Id==ideaId?i with{ArrangedEventId=eventId}:i).ToList();
        Current=Current with {Ideas=Current.Ideas.Select(i=>i.Id==ideaId?i with{ArrangedEventId=eventId}:i).ToList()};QueuePersist();Changed?.Invoke();
    }
    public void SetProfile(ProfileData profile)
    { if(Current==null||Current.UserId!=profile.UserId)return;Current=Current with{Profile=profile};QueuePersist(); }

    async Task<T> MutateAsync<T>(Func<Task<T>> write,Action<T> patch)
    {
        if(!await syncGate.WaitAsync(0))throw new ApiFailure(429,"正在同步或保存，请稍候。");
        try {
            RequireOnline(); Busy=true;
            var value=await write(); patch(value); Message="已保存到服务器。"; QueuePersist(); return value;
        }
        catch(Exception error) when(Expected(error)) {
            if(error is HttpRequestException or OperationCanceledException || error is ApiFailure{Status:401 or 403 or 503}) Online=false;
            if (error is ApiFailure { Status: 401 } && Api?.Session != null) RequiresSignIn = true;
            throw;
        }
        finally { Busy=false;syncGate.Release();Changed?.Invoke(); }
        // No snapshot reload or automatic retry after an authoritative write response.
    }
    void ReplaceTodo(TodoData item)
    { if(item.OwnerId!=Current!.UserId)throw new System.IO.InvalidDataException("待办账号不匹配。");int n=Current.Todos.FindIndex(t=>t.Id==item.Id);if(n<0)Current.Todos.Add(item);else Current.Todos[n]=item; }
    public async Task LogoutAsync()
    {
        await syncGate.WaitAsync();Busy=true;
        var server=Api?.Server.AbsoluteUri ?? identity?.Server;var owner=identity?.UserId ?? Current?.UserId;
        try { if(Api!=null)await Api.LogoutAsync(); }
        finally {
            await FlushCacheAsync();++epoch;
            try
            {
                try { cache.ForgetLogin(); }
                finally { if(server!=null&&owner!=null)cache.Forget(server,owner);else cache.ForgetConnection(); }
            }
            finally {Api?.Dispose();Api=null;Current=null;identity=null;Online=false;RequiresSignIn=false;Busy=false;
                outbox=new SyncOutbox{UserId=""};confirmedIdeas=new();
                Message="已退出，账号缓存与待同步队列已清除。";syncGate.Release();Changed?.Invoke();}
        }
    }
    void RequireOnline(){if(!Online||Api?.Session==null)throw new ApiFailure(401,"离线状态仅能查看缓存，请连接后重试。");}
    public static bool Expected(Exception error)=>error is ApiFailure or HttpRequestException or System.IO.IOException or System.IO.InvalidDataException or System.Text.Json.JsonException or OperationCanceledException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException or ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException;
    public static string Explain(Exception error)=>error switch{ApiFailure=>error.Message,ArgumentException=>error.Message,OperationCanceledException=>"连接超时，请先同步核对操作结果，避免重复创建。",HttpRequestException=>"无法连接服务器，请检查网络或 VPN。",_=>"数据格式或本机缓存异常，已保留现有内容，请重新登录或同步。"};
    public void Dispose(){Api?.Dispose();}
}
