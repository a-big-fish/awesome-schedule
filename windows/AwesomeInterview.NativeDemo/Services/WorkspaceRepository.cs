using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AwesomeInterview.NativeDemo.Data;

public sealed class WorkspaceRepository : IDisposable
{
    readonly SemaphoreSlim syncGate = new(1, 1);
    readonly AccountCache cache;
    readonly Func<string,NativeApi> apiFactory;
    Task persistTail = Task.CompletedTask;
    SavedConnection? identity;
    int epoch;
    public NativeApi? Api { get; private set; }
    public Snapshot? Current { get; private set; }
    public bool Online { get; private set; }
    public bool Busy { get; private set; }
    public string Message { get; private set; } = "登录后查看你的日程与面经。";
    public event Action? Changed;
    public WorkspaceRepository(AccountCache? cache = null, Func<string,NativeApi>? apiFactory = null)
    { this.cache = cache ?? new(); this.apiFactory = apiFactory ?? (server => new NativeApi(server)); }

    public async Task RestoreAsync()
    {
        await syncGate.WaitAsync(); Busy = true;
        try {
            identity = await cache.ReadConnectionAsync(); if(identity == null) return;
            Api = apiFactory(identity.Server); Api.RestoreCookie(identity.Cookie);
            try { Current = await cache.ReadSnapshotAsync(Api.Server.AbsoluteUri, identity.UserId); }
            catch(Exception error) when(Expected(error)) { Current = null; Message = "本机快照不可用，正在重新读取；仍可退出并清理账号。"; }
            if(Current != null) Message = $"已显示本机缓存，更新于 {Current.ServerTime:MM-dd HH:mm}。";
        }
        catch(Exception error) when(Expected(error)) { Message = Explain(error); }
        finally { Busy=false; syncGate.Release(); Changed?.Invoke(); }
        if(Current!=null && DateTimeOffset.UtcNow-Current.ServerTime<TimeSpan.FromHours(18))
            await ValidateSessionAsync();
        else await SynchronizeAsync();
    }
    async Task ValidateSessionAsync()
    {
        await syncGate.WaitAsync();Busy=true;
        try {
            await Api!.ConnectAsync();
            if(Api.Session!.UserId!=identity?.UserId)throw new ApiFailure(401,"会话账号已变化，请重新登录。");
            Online=true;Message="已加载18小时有效期内的本机缓存；可手动同步获取跨端修改。";
        }
        catch(Exception error)when(Expected(error)){Online=false;Message=Explain(error)+" 当前仅可查看缓存。";}
        finally{Busy=false;syncGate.Release();Changed?.Invoke();}
    }
    public async Task LoginAsync(string server, string name, string password)
    {
        var candidate = apiFactory(server); await syncGate.WaitAsync(); Busy=true; Changed?.Invoke();
        try {
            await candidate.LoginAsync(name.Trim(),password);
            var snapshot = await candidate.SnapshotAsync();
            await FlushCacheAsync(); ++epoch; Api?.Dispose(); Api=candidate; Current=snapshot; Online=true;
            identity = candidate.SessionCookie is string cookie ? new(candidate.Server.AbsoluteUri,snapshot.UserId,cookie) : null;
            Message="已连接，数据与 Web 账号同步。"; QueuePersist();
        }
        catch { if(Api != candidate) candidate.Dispose(); throw; }
        finally { Busy=false; syncGate.Release(); Changed?.Invoke(); }
    }
    // Capture record lists before serializing off the dispatcher. Writes stay ordered.
    void QueuePersist()
    {
        if(Api == null || Current == null) return;
        var value = Current with { Events = new(Current.Events), Experiences = new(Current.Experiences), Todos = new(Current.Todos), Ideas = new(Current.Ideas) };
        var saved = identity; string server=Api.Server.AbsoluteUri; int version=epoch;
        persistTail = PersistAfterAsync(persistTail,value,saved,server,version);
    }
    async Task PersistAfterAsync(Task previous, Snapshot value, SavedConnection? saved, string server, int version)
    {
        await previous;
        try { await cache.SaveSnapshotAsync(server,value); if(saved != null) await cache.SaveConnectionAsync(saved); }
        catch(Exception error) when(Expected(error)) { if(version == epoch) { Message="服务端操作已成功，但本机缓存保存失败；请稍后同步。"; Changed?.Invoke(); } }
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
                var next=Current with { SchemaVersion=state.SchemaVersion, UserId=state.UserId, ServerTime=state.ServerTime,
                    Events=state.Events, Todos=state.Todos, Ideas=state.Ideas, Profile=state.Profile };
                next.Validate(Api.Session!.UserId); Current=next;
            }
            Online=true; Message=$"已同步 · {CalendarProjection.Now:HH:mm} · {Current.Events.Count} 条日程 / {Current.Experiences.Count} 份面经"; QueuePersist();
        }
        catch(Exception error) when(Expected(error)) { Online=false; Message=Explain(error)+(Current != null ? " 当前仅可查看缓存。" : ""); }
        finally { Busy=false; syncGate.Release(); Changed?.Invoke(); }
    }
    public Task<EventData> SaveEventAsync(object payload,string? id=null) => MutateAsync(
        ()=>Api!.WriteAsync<EventData>(id==null?HttpMethod.Post:HttpMethod.Put,"api/mobile/v1/events"+(id==null?"":"/"+Uri.EscapeDataString(id)),payload),ReplaceEvent);
    public async Task CompleteAsync(EventData item) => await MutateAsync(
        ()=>Api!.WriteAsync<EventData>(HttpMethod.Post,$"api/mobile/v1/events/{Uri.EscapeDataString(item.Id)}/completion",new{completed=item.CompletedAt==null}),ReplaceEvent);
    public Task<EventData> ResolveOverdueAsync(EventData item,string action,DateTimeOffset? end=null) => MutateAsync(
        ()=>Api!.WriteAsync<EventData>(HttpMethod.Post,$"api/mobile/v1/events/{Uri.EscapeDataString(item.Id)}/overdue",new{action,end_at=end}),ReplaceEvent);
    public Task<EventData> CloseRepeatAsync(EventData item) => MutateAsync(
        ()=>Api!.WriteAsync<EventData>(HttpMethod.Post,$"api/mobile/v1/events/{Uri.EscapeDataString(item.Id.Split('~')[0])}/close-repeat",new{}),ReplaceEvent);
    public async Task DeleteEventAsync(EventData item) => await MutateAsync(async()=>{
        await Api!.DeleteEventAsync(item.Id); return item;
    },value=>{Current!.Events.RemoveAll(e=>e.Id==value.Id.Split('~')[0]); Current=Current with {Ideas=Current.Ideas.Select(i=>i.ArrangedEventId==value.Id ? i with {ArrangedEventId=null}:i).ToList()};});
    public Task<TodoData> SaveTodoAsync(string title,DateOnly day) => MutateAsync(
        ()=>Api!.WriteAsync<TodoData>(HttpMethod.Put,"api/mobile/v1/todos",new{title,due_date=day}),ReplaceTodo);
    public Task<TodoData> CompleteTodoAsync(TodoData item) => MutateAsync(
        ()=>Api!.WriteAsync<TodoData>(HttpMethod.Post,$"api/mobile/v1/todos/{Uri.EscapeDataString(item.Id)}/completion",new{completed=item.CompletedAt==null}),ReplaceTodo);
    public async Task DeleteTodoAsync(TodoData item) => await MutateAsync(async()=>{
        await Api!.DeleteAsync("api/mobile/v1/todos/"+Uri.EscapeDataString(item.Id));return item;
    },value=>Current!.Todos.RemoveAll(t=>t.Id==value.Id));
    public Task<IdeaData> SaveIdeaAsync(string title,string note,List<ReferenceLink> links,string? id=null) => MutateAsync(
        ()=>Api!.WriteAsync<IdeaData>(id==null?HttpMethod.Post:HttpMethod.Put,"api/mobile/v1/ideas"+(id==null?"":"/"+Uri.EscapeDataString(id)),new{title,note,reference_links=links}),ReplaceIdea);
    public Task<IdeaData> AbandonIdeaAsync(IdeaData item) => MutateAsync(
        ()=>Api!.WriteAsync<IdeaData>(HttpMethod.Post,$"api/mobile/v1/ideas/{Uri.EscapeDataString(item.Id)}/abandon",new{}),ReplaceIdea);
    public void LinkIdea(string ideaId,string eventId)
    { if(Current==null)return; Current=Current with {Ideas=Current.Ideas.Select(i=>i.Id==ideaId?i with{ArrangedEventId=eventId}:i).ToList()};QueuePersist();Changed?.Invoke(); }
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
            throw;
        }
        finally { Busy=false;syncGate.Release();Changed?.Invoke(); }
        // No snapshot reload or automatic retry after an authoritative write response.
    }
    void ReplaceTodo(TodoData item)
    { if(item.OwnerId!=Current!.UserId)throw new System.IO.InvalidDataException("待办账号不匹配。");int n=Current.Todos.FindIndex(t=>t.Id==item.Id);if(n<0)Current.Todos.Add(item);else Current.Todos[n]=item; }
    void ReplaceIdea(IdeaData item)
    { if(item.OwnerId!=Current!.UserId)throw new System.IO.InvalidDataException("待安排账号不匹配。");int n=Current.Ideas.FindIndex(t=>t.Id==item.Id);if(n<0)Current.Ideas.Add(item);else Current.Ideas[n]=item; }
    void ReplaceEvent(EventData value)
    {
        if(Current==null||value.OwnerId!=Current.UserId)throw new System.IO.InvalidDataException("返回的日程账号不匹配。");
        if(value.Id.Contains('~')) {
            var parts=value.Id.Split('~');int n=Current.Events.FindIndex(e=>e.Id==parts[0]);
            if(n>=0){var parent=Current.Events[n];var done=new Dictionary<string,DateTimeOffset>(parent.RepeatCompletions);if(value.CompletedAt is DateTimeOffset at)done[parts[1]]=at;else done.Remove(parts[1]);Current.Events[n]=parent with{RepeatCompletions=done};}
        }
        else {int n=Current.Events.FindIndex(e=>e.Id==value.Id);if(n<0)Current.Events.Add(value);else Current.Events[n]=value;}
    }
    public async Task LogoutAsync()
    {
        await syncGate.WaitAsync();Busy=true;
        var server=Api?.Server.AbsoluteUri ?? identity?.Server;var owner=identity?.UserId ?? Current?.UserId;
        try { if(Api!=null)await Api.LogoutAsync(); }
        finally {
            await FlushCacheAsync();++epoch;
            try { if(server!=null&&owner!=null)cache.Forget(server,owner);else cache.ForgetConnection(); }
            finally {Api?.Dispose();Api=null;Current=null;identity=null;Online=false;Busy=false;Message="已退出，账号缓存已清除。";syncGate.Release();Changed?.Invoke();}
        }
    }
    void RequireOnline(){if(!Online||Api?.Session==null)throw new ApiFailure(401,"离线状态仅能查看缓存，请连接后重试。");}
    public static bool Expected(Exception error)=>error is ApiFailure or HttpRequestException or System.IO.IOException or System.Text.Json.JsonException or OperationCanceledException or System.Security.Cryptography.CryptographicException or ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException;
    public static string Explain(Exception error)=>error switch{ApiFailure=>error.Message,ArgumentException=>error.Message,OperationCanceledException=>"连接超时，请先同步核对操作结果，避免重复创建。",HttpRequestException=>"无法连接服务器，请检查网络或 VPN。",_=>"数据格式或本机缓存异常，已保留现有内容，请重新登录或同步。"};
    public void Dispose(){Api?.Dispose();}
}
