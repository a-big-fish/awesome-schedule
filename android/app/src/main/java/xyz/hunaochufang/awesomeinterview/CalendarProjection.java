package xyz.hunaochufang.awesomeinterview;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.time.Duration;
import java.time.Instant;
import java.time.LocalDate;
import java.time.LocalTime;
import java.time.OffsetDateTime;
import java.time.ZoneId;
import java.time.ZonedDateTime;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

/** Calendar days use Shanghai time, while a repeated instance keeps its series wall time. */
final class CalendarProjection {
    static final ZoneId DISPLAY_ZONE = ZoneId.of("Asia/Shanghai");

    static final class Segment {
        final JSONObject event;
        final int startMinute, endMinute;
        final boolean before, after;
        final Instant startsAt;

        Segment(JSONObject event, int startMinute, int endMinute, boolean before, boolean after, Instant startsAt) {
            this.event = event;
            this.startMinute = startMinute;
            this.endMinute = endMinute;
            this.before = before;
            this.after = after;
            this.startsAt = startsAt;
        }
    }

    private CalendarProjection() { }

    static List<Segment> forDay(List<JSONObject> events, LocalDate day, LocalTime workStart, LocalTime workEnd) {
        List<Segment> result = new ArrayList<>();
        Instant dayStart = day.atStartOfDay(DISPLAY_ZONE).toInstant();
        Instant dayEnd = day.plusDays(1).atStartOfDay(DISPLAY_ZONE).toInstant();
        for (JSONObject event : events) {
            try {
                if (event.optBoolean("skip_weekends") && day.getDayOfWeek().getValue() > 5) continue;
                JSONArray repeat = event.optJSONArray("repeat_weekdays");
                if (repeat == null || repeat.length() == 0) {
                    add(result, event, parse(event, "start_at"), parse(event, "end_at"), day, dayStart, dayEnd, workStart, workEnd);
                    continue;
                }
                ZoneId zone = ZoneId.of(event.optString("timezone", DISPLAY_ZONE.getId()));
                LocalDate last = dayEnd.minusNanos(1).atZone(zone).toLocalDate();
                for (LocalDate date = dayStart.atZone(zone).toLocalDate(); !date.isAfter(last); date = date.plusDays(1)) {
                    JSONObject occurrence = occurrence(event, date);
                    if (occurrence != null)
                        add(result, occurrence, parse(occurrence, "start_at"), parse(occurrence, "end_at"), day, dayStart, dayEnd, workStart, workEnd);
                }
            } catch (RuntimeException | JSONException ignored) {
                // An invalid legacy item cannot suppress the rest of the visible day.
            }
        }
        result.sort(Comparator.comparing((Segment segment) -> segment.startsAt).thenComparing(segment -> segment.event.optString("id")));
        return result;
    }

    static JSONObject occurrence(JSONObject event, LocalDate date) throws JSONException {
        JSONArray weekdays = event.optJSONArray("repeat_weekdays");
        if (weekdays == null || weekdays.length() == 0) return null;
        boolean included = false;
        for (int i = 0; i < weekdays.length(); i++) {
            if (weekdays.optInt(i, -1) == date.getDayOfWeek().getValue() - 1) included = true;
        }
        ZoneId zone = ZoneId.of(event.optString("timezone", DISPLAY_ZONE.getId()));
        ZonedDateTime start = parse(event, "start_at").atZone(zone);
        ZonedDateTime end = parse(event, "end_at").atZone(zone);
        String closed = event.isNull("repeat_closed_on") ? "" : event.optString("repeat_closed_on");
        if (!included || date.isBefore(start.toLocalDate()) || (!closed.isEmpty() && date.toString().compareTo(closed) >= 0))
            return null;
        JSONObject occurrence = new JSONObject(event.toString());
        occurrence.put("id", event.getString("id") + "~" + date);
        occurrence.put("start_at", date.atTime(start.toLocalTime()).atZone(zone).toOffsetDateTime().toString());
        occurrence.put("end_at", date.atTime(end.toLocalTime()).atZone(zone).toOffsetDateTime().toString());
        JSONObject completions = event.optJSONObject("repeat_completions");
        occurrence.put("completed_at", completions == null || completions.isNull(date.toString())
                ? JSONObject.NULL : completions.getString(date.toString()));
        return occurrence;
    }

    private static Instant parse(JSONObject event, String field) throws JSONException {
        return OffsetDateTime.parse(event.getString(field)).toInstant();
    }

    private static void add(List<Segment> result, JSONObject event, Instant start, Instant end, LocalDate day,
                            Instant dayStart, Instant dayEnd, LocalTime workStart, LocalTime workEnd) {
        Instant lower = event.optBoolean("workday_only") ? day.atTime(workStart).atZone(DISPLAY_ZONE).toInstant() : dayStart;
        Instant upper = event.optBoolean("workday_only") ? day.atTime(workEnd).atZone(DISPLAY_ZONE).toInstant() : dayEnd;
        Instant clippedStart = start.isAfter(lower) ? start : lower;
        Instant clippedEnd = end.isBefore(upper) ? end : upper;
        if (clippedStart.isBefore(clippedEnd))
            result.add(new Segment(event, (int) Duration.between(dayStart, clippedStart).toMinutes(),
                    (int) Duration.between(dayStart, clippedEnd).toMinutes(), start.isBefore(dayStart), end.isAfter(dayEnd), start));
    }
}
