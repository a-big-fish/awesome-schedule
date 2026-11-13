using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AwesomeInterview.NativeDemo.Data;

public static class Wire
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true };
    public static T Read<T>(string text) => JsonSerializer.Deserialize<T>(text, Json) ?? throw new InvalidDataException("服务端返回空数据。");
}

public record SessionInfo(string UserId, string CsrfToken, bool IsAdmin=false);
public record ProfileData(string UserId, string? DisplayName, string? Email, string WorkdayStart, string WorkdayEnd)
{
    [JsonIgnore] public string EffectiveName => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : "热心用户_" + (Email?.Split('@')[0] is { Length: > 0 } prefix ? prefix[..Math.Min(3,prefix.Length)] : "用户");
}
public record PlazaItem(string Id, string Title, string? Company, string? Position, DateTimeOffset? InterviewedAt, string? InterviewRound, string Author, int QuestionCount);
public record PlazaBatch(List<PlazaItem> Items, int Total, int? NextPage);
public record PublicExperience(ExperienceData Experience, string Author);
public record ReferenceLink(string Title, string Url);
public record EventData
{
    public required string Id { get; init; }
    public required string OwnerId { get; init; }
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required DateTimeOffset StartAt { get; init; }
    public required DateTimeOffset EndAt { get; init; }
    public string Timezone { get; init; } = "Asia/Shanghai";
    public bool SkipWeekends { get; init; }
    public bool WorkdayOnly { get; init; }
    public List<int> RepeatWeekdays { get; init; } = new();
    public string? RepeatClosedOn { get; init; }
    public Dictionary<string, DateTimeOffset> RepeatCompletions { get; init; } = new();
    public int RepeatStreak { get; init; }
    public int RepeatMissed { get; init; }
    public string? LocationText { get; init; }
    public string? LocationUrl { get; init; }
    public List<ReferenceLink> ReferenceLinks { get; init; } = new();
    public string? Note { get; init; }
    public string? Source { get; init; }
    public string? Position { get; init; }
    public string? Platform { get; init; }
    public string? RoomNumber { get; init; }
    public string? MeetingUrl { get; init; }
    public string? ResumeId { get; init; }
    public string? LegacyEntryId { get; init; }
    public List<string> AttachmentRefs { get; init; } = new();
    public List<int> ReminderOffsetsMinutes { get; init; } = new();
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }

    [JsonIgnore] public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(Timezone);
    public DateTime Local(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, Zone).DateTime;

    // Snapshot contains recurring series; occurrence IDs match the server's parent~YYYY-MM-DD contract.
    public EventData? OnDay(DateTime day)
    {
        var start = Local(StartAt); var end = Local(EndAt); day = day.Date;
        if (RepeatWeekdays.Count > 0)
        {
            if (day < start.Date || !RepeatWeekdays.Contains(((int)day.DayOfWeek + 6) % 7) ||
                (RepeatClosedOn != null && string.CompareOrdinal(day.ToString("yyyy-MM-dd"), RepeatClosedOn) >= 0)) return null;
            string key = day.ToString("yyyy-MM-dd");
            return this with { Id = Id + "~" + key, StartAt = At(day + start.TimeOfDay), EndAt = At(day + end.TimeOfDay),
                CompletedAt = RepeatCompletions.TryGetValue(key, out var completed) ? completed : null };
        }
        if (start >= day.AddDays(1) || end <= day || (SkipWeekends && day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)) return null;
        return this;
    }
    public DateTimeOffset At(DateTime local) => new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone), TimeSpan.Zero);
}

public record QuestionData
{
    public required string Id { get; init; }
    public required string ExperienceId { get; init; }
    public string? ParentQuestionId { get; init; }
    public required string Category { get; init; }
    public int AskedOrder { get; init; }
    public int? SiblingOrder { get; init; }
    public required string QuestionText { get; init; }
    public string? OnSiteAnswer { get; init; }
    public string? ReviewAnswer { get; init; }
    public string? InterviewerAnswer { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
}
public record ExperienceData
{
    public required string Id { get; init; }
    public required string OwnerId { get; init; }
    public required string Title { get; init; }
    public string? LinkedEventId { get; init; }
    public string? Company { get; init; }
    public string? Position { get; init; }
    public DateTimeOffset? InterviewedAt { get; init; }
    public string? Feeling { get; init; }
    public bool IsPublic { get; init; }
    public string? InterviewRound { get; init; }
    public string? ReviewStatus { get; init; }
    public Dictionary<string, int> CategoryMinutes { get; init; } = new();
    public int? TotalMinutes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public List<QuestionData> Questions { get; init; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
}
public record TodoData(string Id, string OwnerId, string Title, DateOnly DueDate, DateTimeOffset? CompletedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record IdeaData(string Id, string OwnerId, string Title, string Note, List<ReferenceLink> ReferenceLinks, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? ArrangedEventId = null);
public record CalendarState(int SchemaVersion, string UserId, DateTimeOffset ServerTime, List<EventData> Events, List<TodoData> Todos, List<IdeaData> Ideas, ProfileData Profile);
public record Snapshot
{
    public required int SchemaVersion { get; init; }
    public required string UserId { get; init; }
    public required DateTimeOffset ServerTime { get; init; }
    public required List<EventData> Events { get; init; }
    public required List<ExperienceData> Experiences { get; init; }
    public required List<TodoData> Todos { get; init; }
    public List<IdeaData> Ideas { get; init; } = new();
    public ProfileData? Profile { get; init; }
    public void Validate(string owner)
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(owner) || UserId != owner || ServerTime == default || Events == null || Experiences == null || Todos == null)
            throw new InvalidDataException("快照版本或账号不匹配，已保留原缓存。");
        if (Events.Any(e => e.OwnerId != owner || e.EndAt <= e.StartAt || string.IsNullOrWhiteSpace(e.Id)) ||
            Experiences.Any(e => e.OwnerId != owner || string.IsNullOrWhiteSpace(e.Id) || e.Questions == null || e.Questions.Any(q => q.ExperienceId != e.Id)) || Todos.Any(t => t.OwnerId != owner))
            throw new InvalidDataException("快照包含无效记录或其他账号的数据。");
        if (Events.Select(e => e.Id).Distinct().Count() != Events.Count || Experiences.Select(e => e.Id).Distinct().Count() != Experiences.Count || Todos.Select(t => t.Id).Distinct().Count() != Todos.Count)
            throw new InvalidDataException("快照包含重复记录。");
        foreach (var e in Events) _ = e.Zone;
        if (Ideas == null || Ideas.Any(i => i.OwnerId != owner || string.IsNullOrWhiteSpace(i.Id)) ||
            Ideas.Select(i => i.Id).Distinct().Count() != Ideas.Count || (Profile != null && Profile.UserId != owner))
            throw new InvalidDataException("日历附加数据的账号不匹配。");
    }
}

public static class CalendarProjection
{
    public static DateTime Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).DateTime;
    public static (DateTime Start, DateTime End)? Segment(EventData item, DateTime day, TimeSpan workStart, TimeSpan workEnd)
    {
        day = day.Date;
        var from = item.Local(item.StartAt) > day ? item.Local(item.StartAt) : day;
        var until = item.Local(item.EndAt) < day.AddDays(1) ? item.Local(item.EndAt) : day.AddDays(1);
        if(item.WorkdayOnly) { from = from > day.Add(workStart) ? from : day.Add(workStart); until = until < day.Add(workEnd) ? until : day.Add(workEnd); }
        return until > from ? (from, until) : null;
    }
}

public static class QuestionTree
{
    public static IEnumerable<(QuestionData Item, string Number, int Depth)> Ordered(IEnumerable<QuestionData> source)
    {
        var items = source.OrderBy(q => q.SiblingOrder ?? q.AskedOrder).ThenBy(q => q.AskedOrder).ToList();
        var ids = items.Select(q => q.Id).ToHashSet(); var seen = new HashSet<string>();
        IEnumerable<(QuestionData, string, int)> Walk(QuestionData item, string number, int depth)
        {
            if (!seen.Add(item.Id)) yield break;
            yield return (item, number, depth);
            int index = 0;
            foreach (var child in items.Where(q => q.ParentQuestionId == item.Id))
                foreach (var row in Walk(child, number + "." + ++index, depth + 1)) yield return row;
        }
        int root = 0;
        foreach (var item in items.Where(q => q.ParentQuestionId == null || !ids.Contains(q.ParentQuestionId)))
            foreach (var row in Walk(item, (++root).ToString(), 0)) yield return row;
        // Corrupt cycles must not hang rendering or silently drop saved content.
        foreach (var item in items.Where(q => !seen.Contains(q.Id)).ToList())
            foreach (var row in Walk(item, (++root).ToString(), 0)) yield return row;
    }
}
