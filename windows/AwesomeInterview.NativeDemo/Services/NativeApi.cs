using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AwesomeInterview.NativeDemo.Data;

public sealed class ApiFailure : Exception
{
    public int Status { get; }
    public IReadOnlyList<string> ConflictTitles { get; init; } = Array.Empty<string>();
    public ApiFailure(int status, string message) : base(message) => Status = status;
}

public sealed class NativeApi : IDisposable
{
    readonly CookieContainer cookies = new();
    readonly System.Collections.Concurrent.ConcurrentQueue<object> timings=new();
    readonly Timer flushTimer;
    int flushing;
    static string Node(string path) => path.Contains("/plaza/")?"plaza_detail":path.Contains("/plaza")?"plaza":path.Contains("/ideas")?"ideas":path.Contains("/calendar")||path.Contains("/todos")?"calendar":path.Contains("/profile")?"profile":path.Contains("/parse")?"event_parse":path.Contains("/events")?"event_write":path.Contains("/snapshot")?"mobile":"mobile";
    public void RecordPage(string node,double duration){if(Session!=null&&timings.Count<100)timings.Enqueue(new{node,metric="cache_render",duration=Math.Clamp(duration,0,120000),status=200});}
    async Task FlushAsync(){if(Session==null||Interlocked.Exchange(ref flushing,1)!=0)return;try{var batch=new List<object>();while(batch.Count<20&&timings.TryDequeue(out var sample))batch.Add(sample);if(batch.Count==0)return;using var req=new HttpRequestMessage(HttpMethod.Post,"my-schedule/telemetry"){Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"payload",JsonSerializer.Serialize(new{samples=batch},Wire.Json)}})};req.Headers.Add("X-CSRF-Token",Session.CsrfToken);using var response=await http.SendAsync(req);}catch(Exception e)when(e is HttpRequestException or OperationCanceledException or ObjectDisposedException){}finally{Interlocked.Exchange(ref flushing,0);}}

    readonly HttpClient http;
    public Uri Server { get; }
    public SessionInfo? Session { get; private set; }
    public NativeApi(string address, HttpMessageHandler? transport = null)
    {
        var uri = new Uri(address.Trim());
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) throw new ArgumentException("服务地址必须使用 HTTPS；本机调试可用 HTTP。");
        if (!string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "") throw new ArgumentException("请填写网站根地址，不包含路径、参数或账号。");
        Server = uri;
        http = new HttpClient(transport ?? new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false }) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Add("X-Awesome-Client","windows");
        flushTimer=new Timer(_=>{_ = FlushAsync();},null,TimeSpan.FromMinutes(1),TimeSpan.FromMinutes(1));
    }
    public string? SessionCookie => cookies.GetCookies(Server)["schedule_session"]?.Value;
    public void RestoreCookie(string value) => cookies.Add(Server, new Cookie("schedule_session", value, "/") { Secure = Server.Scheme == "https", HttpOnly = true });
    public async Task LoginAsync(string identifier, string password, CancellationToken cancellation = default)
    {
        using var page = await http.GetAsync("auth/login", cancellation);
        if (page.StatusCode != HttpStatusCode.OK && page.StatusCode != HttpStatusCode.SeeOther) throw Failure((int)page.StatusCode);
        var csrf = cookies.GetCookies(Server)["schedule_guest_csrf"]?.Value;
        if (csrf == null) throw new ApiFailure(403, "无法建立登录会话，请重试。");
        using var response = await http.PostAsync("auth/login", new FormUrlEncodedContent(new Dictionary<string,string> {
            ["identifier"] = identifier, ["password"] = password, ["csrf_token"] = csrf, ["next"] = "/my-schedule"
        }), cancellation);
        if (response.StatusCode != HttpStatusCode.SeeOther) throw Failure((int)response.StatusCode);
        await ConnectAsync(cancellation);
    }
    public async Task ConnectAsync(CancellationToken cancellation = default)
    {
        var session = await GetAsync<SessionInfo>("api/mobile/v1/session", cancellation);
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.CsrfToken)) throw new InvalidDataException("服务端会话格式无效。");
        Session = session;
    }
    public async Task<Snapshot> SnapshotAsync(CancellationToken cancellation = default)
    {
        var owner = Session?.UserId ?? throw new ApiFailure(401, "请先登录。");
        var snapshot = await GetAsync<Snapshot>("api/mobile/v1/snapshot", cancellation);
        snapshot.Validate(owner); return snapshot;
    }
    public async Task<T> GetAsync<T>(string path, CancellationToken cancellation = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync<T>(request, cancellation);
    }
    public async Task<T> WriteAsync<T>(HttpMethod method, string path, object payload, CancellationToken cancellation = default)
    {
        using var request = new HttpRequestMessage(method, path) { Content = new StringContent(JsonSerializer.Serialize(payload, Wire.Json), Encoding.UTF8, "application/json") };
        request.Headers.Add("X-CSRF-Token", Session?.CsrfToken ?? throw new ApiFailure(401, "请先登录。"));
        return await SendAsync<T>(request, cancellation);
    }
    async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellation)
    {
        var started=System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage received;
        try{received=await http.SendAsync(request,cancellation);}catch{if(timings.Count<100)timings.Enqueue(new{node=Node(request.RequestUri?.ToString()??""),metric="request",duration=Math.Min(120000,started.Elapsed.TotalMilliseconds),status=0});throw;}
        using var response=received;
        if(!(request.RequestUri?.ToString()??"").Contains("/admin")&&timings.Count<100)timings.Enqueue(new{node=Node(request.RequestUri?.ToString()??""),metric="request",duration=Math.Min(120000,started.Elapsed.TotalMilliseconds),status=(int)response.StatusCode});
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode==HttpStatusCode.Conflict && response.Content.Headers.ContentType?.MediaType=="application/json")
            {
                try {
                    using var data=JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
                    if(data.RootElement.TryGetProperty("detail",out var detail) && detail.ValueKind==JsonValueKind.Object && detail.TryGetProperty("conflicts",out var list) && list.ValueKind==JsonValueKind.Array) {
                        var names=new List<string>();foreach(var item in list.EnumerateArray())if(item.ValueKind==JsonValueKind.String && names.Count<20)names.Add((item.GetString()??"")[..Math.Min(200,item.GetString()!.Length)]);
                        throw new ApiFailure(409,"与已有日程重叠，请确认后保存。") { ConflictTitles=names };
                    }
                } catch(JsonException) { }
            }
            throw Failure((int)response.StatusCode);
        }
        if (response.Content.Headers.ContentType?.MediaType != "application/json") throw new ApiFailure(401, "会话失效或服务未返回 JSON，请重新登录。");
        var text = await response.Content.ReadAsStringAsync(cancellation);
        return Wire.Read<T>(text);
    }
    public async Task LogoutAsync()
    {
        if (Session != null) { using var response = await http.PostAsync("auth/logout", new FormUrlEncodedContent(new Dictionary<string,string> { ["csrf_token"] = Session.CsrfToken })); }
        Session = null;
    }
    public async Task<byte[]> DownloadAsync(string path, string expectedType)
    {
        using var response = await http.GetAsync(path);
        if (!response.IsSuccessStatusCode) throw Failure((int)response.StatusCode);
        if (response.Content.Headers.ContentType?.MediaType != expectedType) throw new ApiFailure(401,"下载内容无效或登录已过期。");
        return await response.Content.ReadAsByteArrayAsync();
    }
    public async Task DeleteEventAsync(string id) => await DeleteAsync("api/mobile/v1/events/"+Uri.EscapeDataString(id));
    public async Task DeleteAsync(string path)
    {
        using var request=new HttpRequestMessage(HttpMethod.Delete,path);
        request.Headers.Add("X-CSRF-Token",Session?.CsrfToken??throw new ApiFailure(401,"请先登录。"));
        using var response=await http.SendAsync(request);
        if(response.StatusCode!=HttpStatusCode.NoContent)throw Failure((int)response.StatusCode);
    }
    static ApiFailure Failure(int status) => new(status, status switch {
        301 or 302 or 303 or 307 or 401 => "登录已过期，请重新登录。", 403 => "会话校验失败，请重新登录。",
        404 => "内容已删除或服务器尚未支持此接口。", 409 => "与已有日程重叠，请核对后确认保存。",
        422 => "提交的数据不符合要求，请检查时间和内容。", 429 => "请求过于频繁，请稍后再试。",
        503 => "数据库暂时不可用；保留缓存，可稍后手动同步。", _ => $"服务请求失败（{status}），请稍后重试。" });
    public void Dispose(){flushTimer.Dispose();http.Dispose();}
}
