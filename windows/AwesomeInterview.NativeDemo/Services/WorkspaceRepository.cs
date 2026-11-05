using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AwesomeInterview.NativeDemo.Data;

public sealed class WorkspaceRepository : IDisposable
{
    readonly SemaphoreSlim syncGate = new(1, 1);
    readonly AccountCache cache;
    public NativeApi? Api { get; private set; }
    public Snapshot? Current { get; private set; }
    public bool Online { get; private set; }
    public bool Busy { get; private set; }
    public string Message { get; private set; } = "登录后查看你的日程与面经。";
    public event Action? Changed;
    public WorkspaceRepository(AccountCache? cache = null) => this.cache = cache ?? new();
    public async Task RestoreAsync()
    {
        await syncGate.WaitAsync();Busy=true;
        try
        {
            var saved = await cache.ReadConnectionAsync(); if (saved == null) return;
            Api = new NativeApi(saved.Server); Api.RestoreCookie(saved.Cookie);
            Current = await cache.ReadSnapshotAsync(Api.Server.AbsoluteUri, saved.UserId);
            Message = Current == null ? "正在连接服务端…" : $"已显示本机缓存，更新于 {Current.ServerTime.LocalDateTime:MM-dd HH:mm}。"+(DateTimeOffset.UtcNow-Current.ServerTime>TimeSpan.FromHours(18)?" 缓存已过期，正在获取最新数据。":"");
        }
        catch (Exception error) when (Expected(error)) { Message = Explain(error); Changed?.Invoke(); }
        finally {Busy=false;syncGate.Release();Changed?.Invoke();}
        await SynchronizeAsync();
    }
    public async Task LoginAsync(string server, string name, string password)
    {
        var candidate = new NativeApi(server);
        await syncGate.WaitAsync(); Busy = true; Changed?.Invoke();
        try
        {
            await candidate.LoginAsync(name.Trim(), password);
            // Never display another account's cache while changing identities.
            var snapshot = await candidate.SnapshotAsync();
            Api?.Dispose(); Api = candidate; Current = snapshot; Online = true;
            try { await PersistAsync(); Message = "已连接，数据与 Web 账号同步。"; }
            catch (Exception error) when(Expected(error)) { Message="已连接，但缓存未能保存；本次退出后需要重新登录。"; }
        }
        catch { if (Api != candidate) candidate.Dispose(); throw; }
        finally { Busy=false;syncGate.Release();Changed?.Invoke(); }
    }
    async Task PersistAsync()
    {
        if (Api == null || Current == null) return;
        await cache.SaveSnapshotAsync(Api.Server.AbsoluteUri, Current);
        if (Api.SessionCookie is string cookie) await cache.SaveConnectionAsync(new(Api.Server.AbsoluteUri, Current.UserId, cookie));
    }
    public async Task SynchronizeAsync()
    {
        if (Api == null || !await syncGate.WaitAsync(0)) return;
        Busy = true; Changed?.Invoke();
        try
        {
            await Api.ConnectAsync();
            // A restored cookie must identify the same account as the restored cache.
            if (Current != null && Current.UserId != Api.Session!.UserId) { Current = null; Changed?.Invoke(); }
            var snapshot = await Api.SnapshotAsync(); Current = snapshot; Online = true;
            try { await PersistAsync(); Message = $"已同步 · {DateTime.Now:HH:mm} · {snapshot.Events.Count} 条日程 / {snapshot.Experiences.Count} 份面经"; }
            catch (Exception error) when (Expected(error)) { Message = "已获取最新数据，但本机缓存写入失败。"; }
        }
        catch (Exception error) when (Expected(error)) { Online = false; Message = Explain(error) + (Current != null ? " 当前仅可查看缓存。" : ""); }
        finally { Busy = false; syncGate.Release(); Changed?.Invoke(); }
    }
    public async Task<EventData> SaveEventAsync(object payload, string? id = null)
    {
        return await MutateAsync(()=>Api!.WriteAsync<EventData>(id == null ? HttpMethod.Post : HttpMethod.Put,
            "api/mobile/v1/events" + (id == null ? "" : "/" + Uri.EscapeDataString(id)), payload));
    }
    public async Task CompleteAsync(EventData item)
    {
        await MutateAsync(()=>Api!.WriteAsync<EventData>(HttpMethod.Post, $"api/mobile/v1/events/{Uri.EscapeDataString(item.Id)}/completion", new { completed = item.CompletedAt == null }));
    }
    async Task<EventData> MutateAsync(Func<Task<EventData>> write)
    {
        if (!await syncGate.WaitAsync(0)) throw new ApiFailure(429,"正在同步或保存，请稍候。");
        EventData value;
        try {
            RequireOnline();Busy=true;Changed?.Invoke();value=await write();ReplaceEvent(value);
            try { await PersistAsync(); } catch(Exception error) when(Expected(error)) { Message="服务端已保存，本机缓存写入失败。"; }
        }
        catch(Exception error) when(Expected(error)) {
            if(error is HttpRequestException or OperationCanceledException || error is ApiFailure { Status:401 or 403 or 503 }) Online=false;
            throw;
        }
        finally { Busy=false;syncGate.Release();Changed?.Invoke(); }
        // No retry of a non-idempotent write. Refresh may fail without undoing the successful result.
        await SynchronizeAsync();return value;
    }
    public async Task DeleteEventAsync(EventData item)
    {
        if (!await syncGate.WaitAsync(0))throw new ApiFailure(429,"正在同步或保存，请稍候。");
        try {
            RequireOnline();Busy=true;Changed?.Invoke();await Api!.DeleteEventAsync(item.Id);
            Current!.Events.RemoveAll(e=>e.Id==item.Id);Changed?.Invoke();
            try{await PersistAsync();}catch(Exception error)when(Expected(error)){Message="服务端已删除，但缓存写入失败。";}
        } finally{Busy=false;syncGate.Release();Changed?.Invoke();}
        await SynchronizeAsync();
    }
    void ReplaceEvent(EventData value)
    {
        if (Current == null || value.OwnerId != Current.UserId) throw new System.IO.InvalidDataException("返回的日程账号不匹配。");
        if (value.Id.Contains('~'))
        {
            var parts = value.Id.Split('~'); int parentIndex = Current.Events.FindIndex(e => e.Id == parts[0]);
            if (parentIndex >= 0) {
                var parent = Current.Events[parentIndex];
                var completed = new System.Collections.Generic.Dictionary<string, DateTimeOffset>(parent.RepeatCompletions);
                if (value.CompletedAt is DateTimeOffset at) completed[parts[1]] = at; else completed.Remove(parts[1]);
                Current.Events[parentIndex] = parent with { RepeatCompletions = completed };
            }
        }
        else { int index = Current.Events.FindIndex(e => e.Id == value.Id); if (index < 0) Current.Events.Add(value); else Current.Events[index] = value; }
        Changed?.Invoke();
    }
    public async Task LogoutAsync()
    {
        await syncGate.WaitAsync();Busy=true;
        var server = Api?.Server.AbsoluteUri; var owner = Current?.UserId;
        try { if (Api != null) await Api.LogoutAsync(); }
        finally {
            try { if (server != null && owner != null) cache.Forget(server, owner); }
            finally { Api?.Dispose(); Api = null; Current = null; Online = false; Busy=false;Message = "已退出，账号缓存已清除。";syncGate.Release();Changed?.Invoke(); }
        }
    }
    void RequireOnline() { if (!Online || Api?.Session == null) throw new ApiFailure(401, "离线状态仅能查看缓存，请连接后重试。"); }
    public static bool Expected(Exception error) => error is ApiFailure or HttpRequestException or System.IO.IOException or System.Text.Json.JsonException or OperationCanceledException or System.Security.Cryptography.CryptographicException or ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException;
    public static string Explain(Exception error) => error switch { ApiFailure => error.Message, OperationCanceledException => "连接超时，请检查网络后手动重试。", HttpRequestException => "无法连接服务器，请检查网络或 VPN。", _ => "数据格式或本机缓存异常，已保留现有内容，请重新登录或同步。" };
    public void Dispose() { Api?.Dispose(); }
}
