using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AwesomeInterview.NativeDemo.Data;

public static class SyncStates
{
    public const string Pending = "pending";
    public const string Sending = "sending";
    public const string Auth = "auth";
    public const string Failed = "failed";
}

// One durable, replayable command. `Command` is the exact JSON body the server hashed into
// its receipt, so retries must never rewrite it after a successful deadline.
public sealed class SyncQueueItem
{
    public string Id { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Title { get; set; } = "";
    public JsonElement Command { get; set; }
    public string CreatedAt { get; set; } = "";
    public int Attempts { get; set; }
    public long NextAttempt { get; set; }
    public string State { get; set; } = SyncStates.Pending;
    public string? Error { get; set; }
    public int? Status { get; set; }
    public List<string> ConflictTitles { get; set; } = new();

    public SyncQueueItem Clone() => new()
    {
        Id = Id, Endpoint = Endpoint, EntityId = EntityId, Title = Title, Command = Command,
        CreatedAt = CreatedAt, Attempts = Attempts, NextAttempt = NextAttempt,
        State = State, Error = Error, Status = Status, ConflictTitles = new(ConflictTitles),
    };
}

public sealed record SyncOutbox
{
    public int SchemaVersion { get; init; } = 1;
    public required string UserId { get; init; }
    public List<SyncQueueItem> Items { get; init; } = new();
    public List<EventData>? ConfirmedEvents { get; init; }
    public List<IdeaData>? ConfirmedIdeas { get; init; }
}

public sealed record IdeaSyncCommand(string Action, string IdeaId, string Title, string Note, List<ReferenceLink> ReferenceLinks);

public sealed record IdeaSyncPayload(string Id, string Title, string Note, List<ReferenceLink> ReferenceLinks,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

// Mirrors CalendarEventInput; every property name must stay valid for its extra="forbid" server DTO.
public sealed class CalendarEventPayload
{
    public required string Title { get; init; }
    public required string Type { get; init; }
    public required DateTimeOffset StartAt { get; init; }
    public required DateTimeOffset EndAt { get; init; }
    public string Timezone { get; init; } = "Asia/Shanghai";
    public bool SkipWeekends { get; init; }
    public bool WorkdayOnly { get; init; }
    public List<int> RepeatWeekdays { get; init; } = new();
    public List<int> ReminderOffsetsMinutes { get; init; } = new();
    public string? LocationText { get; init; }
    public string? LocationUrl { get; init; }
    public string? Note { get; init; }
    public string? Position { get; init; }
    public List<ReferenceLink> ReferenceLinks { get; init; } = new();
    public string Source { get; init; } = "manual";
    public string RawText { get; init; } = "";
    public string? Platform { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RoomNumber { get; init; }
    public string? MeetingUrl { get; init; }
    public string? SourceIdeaId { get; init; }
}

public sealed record CalendarSyncCommand(string Action, string EventId, string? OccurrenceDay = null,
    CalendarEventPayload? Event = null, bool? Completed = null, DateTimeOffset? EndAt = null, bool ConfirmOverlap = false);

public static class SyncQueueRules
{
    public const int Capacity = 200;
    public const string IdeaEndpoint = "my-schedule/idea-sync";
    public const string EventEndpoint = "my-schedule/calendar-sync";

    public static string CreateStamp(DateTimeOffset? value = null) =>
        (value ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("O");

    public static DateTimeOffset ParseStamp(string value) =>
        DateTimeOffset.TryParse(value, out var stamp) ? stamp : DateTimeOffset.UtcNow;

    public static long RetryDelayMilliseconds(int attempts) =>
        (long)Math.Min(60000, 1000 * Math.Pow(2, Math.Min(6, Math.Max(0, attempts - 1))));

    public static bool IsPermanentRejection(int status) => status is 400 or 404 or 409 or 422;

    // Repeating events address one occurrence, while the queue still stores the parent entity.
    public static (string Parent, string? Day) SplitOccurrence(string id)
    {
        int index = id.IndexOf('~');
        return index < 0 ? (id, null) : (id[..index], id[(index + 1)..]);
    }
}
