using AwesomeInterview.NativeDemo.Data;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
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
// Optional real-server read-only acceptance. No credentials or response bodies are printed or saved.
var server = Environment.GetEnvironmentVariable("AWESOME_TEST_SERVER"); var cookie = Environment.GetEnvironmentVariable("AWESOME_TEST_COOKIE");
if (server != null && cookie != null) {
    using var api = new NativeApi(server); api.RestoreCookie(cookie); await api.ConnectAsync(); var real = await api.SnapshotAsync();
    Check(real.UserId==api.Session!.UserId,"real authenticated server snapshot");
    Console.WriteLine($"REAL: {real.Events.Count} events, {real.Experiences.Count} experiences, {real.Todos.Count} todos; payloads not logged.");
} else Console.WriteLine("Real-account acceptance pending login; no simulated result claimed as real.");
Console.WriteLine($"{passed} checks passed.");

sealed class Reply(string body,string type,HttpStatusCode code) : HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation) => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body,Encoding.UTF8,type) });
}
