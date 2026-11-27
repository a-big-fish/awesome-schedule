package xyz.hunaochufang.awesomeinterview;

import android.app.AlarmManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.net.Uri;
import android.os.Build;

import org.json.JSONArray;
import org.json.JSONObject;

import java.time.Instant;
import java.time.LocalDate;
import java.time.OffsetDateTime;
import java.time.ZoneId;
import java.time.temporal.ChronoUnit;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

final class ReminderScheduler {
    private static final String IDS = "alarm_ids";
    private static final int DAYS = 30, LIMIT = 500;

    private static final class Entry {
        final String key, eventId, title;
        final long trigger;
        Entry(String key, String eventId, String title, long trigger) {
            this.key = key; this.eventId = eventId; this.title = title; this.trigger = trigger;
        }
    }

    private ReminderScheduler() { }

    static void cancelAll(Context context) {
        SharedPreferences prefs = OfflineStore.prefs(context);
        AlarmManager alarms = (AlarmManager) context.getSystemService(Context.ALARM_SERVICE);
        try {
            JSONArray ids = new JSONArray(prefs.getString(IDS, "[]"));
            for (int i = 0; i < ids.length(); i++) {
                Object key = ids.get(i);
                PendingIntent pending = pending(context, key, "", "", PendingIntent.FLAG_NO_CREATE);
                if (pending != null) {alarms.cancel(pending); pending.cancel();}
            }
        } catch (Exception ignored) {
            // Ignore a malformed legacy index and use the next successful one.
        }
        prefs.edit().remove(IDS).remove("reminder_until").remove("reminder_limited").apply();
    }

    static synchronized int reschedule(Context context, JSONObject snapshot) throws Exception {
        JSONArray events = snapshot.getJSONArray("events");
        String owner = snapshot.getString("user_id");
        Instant now = Instant.now(), until = now.plus(DAYS, ChronoUnit.DAYS);
        List<Entry> plan = new ArrayList<>();
        for (int i = 0; i < events.length(); i++) {
            JSONObject event = events.getJSONObject(i);
            if (!event.isNull("cancelled_at")) continue;
            JSONArray repeat = event.optJSONArray("repeat_weekdays");
            if (repeat == null || repeat.length() == 0) {
                add(plan, owner, event, now, until);
                continue;
            }
            ZoneId zone = ZoneId.of(event.optString("timezone", "Asia/Shanghai"));
            LocalDate last = until.atZone(zone).toLocalDate();
            for (LocalDate day = now.atZone(zone).toLocalDate(); !day.isAfter(last); day = day.plusDays(1)) {
                JSONObject occurrence = CalendarProjection.occurrence(event, day);
                if (occurrence != null) add(plan, owner, occurrence, now, until);
            }
        }
        plan.sort(Comparator.comparingLong((Entry entry) -> entry.trigger).thenComparing(entry -> entry.key));
        cancelAll(context);
        AlarmManager alarms = (AlarmManager) context.getSystemService(Context.ALARM_SERVICE);
        JSONArray ids = new JSONArray();
        boolean limited = plan.size() > LIMIT;
        int count = Math.min(plan.size(), LIMIT);
        for (int i = 0; i < count; i++) {
            Entry item = plan.get(i);
            PendingIntent pending = pending(context, item.key, item.eventId, item.title, PendingIntent.FLAG_UPDATE_CURRENT);
            if (Build.VERSION.SDK_INT >= 31 && alarms.canScheduleExactAlarms())
                alarms.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, item.trigger, pending);
            else alarms.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, item.trigger, pending);
            ids.put(item.key);
        }
        Instant coverage = limited ? Instant.ofEpochMilli(plan.get(count - 1).trigger) : until;
        OfflineStore.prefs(context).edit().putString(IDS, ids.toString())
                .putString("reminder_until", coverage.toString()).putBoolean("reminder_limited", limited).apply();
        return count;
    }

    private static void add(List<Entry> plan, String owner, JSONObject event, Instant now, Instant until) throws Exception {
        JSONArray offsets = event.optJSONArray("reminder_offsets_minutes");
        if (offsets == null || !event.isNull("completed_at")) return;
        Instant start = OffsetDateTime.parse(event.getString("start_at")).toInstant();
        if (!start.isAfter(now) || start.isAfter(until)) return;
        String id = event.getString("id");
        for (int i = 0; i < offsets.length(); i++) {
            int offset = offsets.optInt(i);
            if (offset != 10 && offset != 60 && offset != 1440) continue;
            Instant trigger = start.minus(offset, ChronoUnit.MINUTES);
            if (trigger.isAfter(now)) plan.add(new Entry(owner + "/" + id + "/" + offset, id,
                    event.optString("title"), trigger.toEpochMilli()));
        }
    }

    private static PendingIntent pending(Context context, Object key, String eventId, String title, int flag) {
        Intent intent = new Intent(context, ReminderReceiver.class)
                .putExtra("event_id", eventId).putExtra("title", title);
        int requestCode = key instanceof Number ? ((Number) key).intValue() : 0;
        if (key instanceof String) intent.setData(new Uri.Builder().scheme("awesome-interview")
                .authority("reminder").appendPath((String) key).build());
        return PendingIntent.getBroadcast(context, requestCode, intent, flag | PendingIntent.FLAG_IMMUTABLE);
    }
}
