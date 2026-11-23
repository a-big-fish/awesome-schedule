using System;
using System.Net.Http;
using System.Threading.Tasks;
using AwesomeInterview.NativeDemo.Data;

namespace AwesomeInterview.NativeDemo;

internal sealed class WorkspaceProfile(WorkspaceRepository repository)
{
    ProfileData? cached;
    Uri? cachedServer;

    internal ProfileData? Value => cached?.UserId == repository.Current?.UserId &&
        cachedServer == repository.Api?.Server ? cached : null;

    internal void UpdateFromRepository()
    {
        if (repository.Current?.Profile is { } profile)
        {
            cached = profile;
            cachedServer = repository.Api?.Server;
        }
        else if (Value == null)
        {
            Invalidate();
        }
    }

    internal void Invalidate()
    {
        cached = null;
        cachedServer = null;
    }

    internal TimeSpan WorkTime(bool end) =>
        TimeSpan.TryParse(end ? Value?.WorkdayEnd : Value?.WorkdayStart, out var time)
            ? time : TimeSpan.FromHours(end ? 18 : 8);

    internal async Task LoadAsync()
    {
        if (Value != null) return;
        var api = repository.Api ?? throw new ApiFailure(401, "请先登录。");
        string? owner = repository.Current?.UserId;
        var result = await api.GetAsync<ProfileData>("api/mobile/v1/profile");
        Apply(api, owner, result);
    }

    internal Task<ProfileData> SaveNameAsync(string name) => SaveAsync(new { display_name = name });

    internal Task<ProfileData> SaveHoursAsync(string displayName, string start, string end) =>
        SaveAsync(new { display_name = displayName, workday_start = start, workday_end = end });

    async Task<ProfileData> SaveAsync(object change)
    {
        var api = repository.Api ?? throw new ApiFailure(401, "请先登录。");
        string? owner = repository.Current?.UserId;
        var result = await api.WriteAsync<ProfileData>(HttpMethod.Put, "api/mobile/v1/profile", change);
        Apply(api, owner, result);
        return result;
    }

    void Apply(NativeApi api, string? owner, ProfileData result)
    {
        if (repository.Api != api || repository.Current?.UserId != owner || owner != result.UserId)
            throw new ApiFailure(409, "账号已切换或资料不匹配，请重新读取。");

        cached = result;
        cachedServer = api.Server;
        repository.SetProfile(result);
    }
}
