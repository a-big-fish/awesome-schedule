using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AwesomeInterview.NativeDemo.Data;

static class RememberedLoginChecks
{
    const string Server = "https://fixture.test/";
    const string Name = "fixture@example.test";
    const string Password = "dummy-check-value-0329";

    public static async Task Run(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "awesome-login-check-" + Guid.NewGuid());
        var cache = new AccountCache(directory);
        bool expired = false;
        string sessionUser = "fixture";
        int attempts = 0;

        NativeApi CreateApi(string address)
        {
            var api = new NativeApi(address, new LoginTransport(async request =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path == "/auth/login" && request.Method == HttpMethod.Get)
                    return new HttpResponseMessage(HttpStatusCode.OK);
                if (path == "/auth/login")
                {
                    attempts++;
                    string form = await request.Content!.ReadAsStringAsync();
                    if (!form.Contains("password=" + Password) || !form.Contains("identifier=fixture%40example.test"))
                        return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    expired = false;
                    return new HttpResponseMessage(HttpStatusCode.SeeOther);
                }
                if (path == "/api/mobile/v1/session")
                    return expired ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                        : Json(new SessionInfo(sessionUser, "test"));
                if (path == "/api/mobile/v1/snapshot")
                    return Json(new Snapshot
                    {
                        SchemaVersion = 1, ServerTime = DateTimeOffset.UtcNow, UserId = sessionUser,
                        Events = new(), Ideas = new(), Todos = new(), Experiences = new()
                    });
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }));
            // The injected handler bypasses HttpClientHandler's cookie jar.
            var cookies = (CookieContainer)typeof(NativeApi)
                .GetField("cookies", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(api)!;
            cookies.Add(api.Server, new Cookie("schedule_guest_csrf", "test"));
            api.RestoreCookie("test-cookie");
            return api;
        }

        try
        {
            using (var repo = new WorkspaceRepository(cache, CreateApi))
            {
                await repo.LoginAsync(Server, Name, Password);
                check(await repo.ReadRememberedLoginAsync() == null, "default login does not save a password");
                await repo.LoginAsync(Server, Name, Password, rememberPassword: true);
                check(await repo.ReadRememberedLoginAsync() == new RememberedLogin(Server, Name),
                    "remembered account lookup only returns server and login name");
                var savedFile = Path.Combine(directory, "remembered-login.bin");
                check(File.Exists(savedFile) && !Encoding.UTF8.GetString(await File.ReadAllBytesAsync(savedFile)).Contains(Password),
                    "remembered password is encrypted on disk");
                await repo.FlushCacheAsync();
            }
            expired = true;
            using (var reopened = new WorkspaceRepository(cache, CreateApi))
            {
                await reopened.RestoreAsync();
                check(reopened.RequiresSignIn && !reopened.Online && reopened.Current?.UserId == "fixture",
                    "an expired session keeps the offline snapshot and requests login");
                check(attempts == 2, "restoring an expired session does not submit credentials in the background");

                sessionUser = "other";
                try { await reopened.LoginRememberedAsync(); throw new Exception("changed login accepted"); }
                catch (ApiFailure failure)
                {
                    check(failure.Status == 401 && reopened.Current?.UserId == "fixture",
                        "remembered login rejects an unexpected server account");
                }
                sessionUser = "fixture";
                await reopened.LoginRememberedAsync();
                check(reopened.Online && !reopened.RequiresSignIn && attempts == 4,
                    "one explicit remembered-login action renews the session");
                await reopened.ForgetRememberedLoginAsync();
                check(await reopened.ReadRememberedLoginAsync() == null && reopened.Online,
                    "forgetting a password does not log out the current session");

                await reopened.LoginAsync(Server, Name, Password, true);
                await reopened.LoginAsync(Server, Name, Password, false);
                check(await cache.ReadLoginAsync() == null, "signing in without remember clears the previous saved password");

                await reopened.LoginAsync(Server, Name, Password, true);
                await reopened.LogoutAsync();
                check(await cache.ReadLoginAsync() == null && await cache.ReadConnectionAsync() == null,
                    "logout removes both the saved password and session");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }

    static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, Wire.Json), Encoding.UTF8, "application/json")
    };

    sealed class LoginTransport(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => respond(request);
    }
}
