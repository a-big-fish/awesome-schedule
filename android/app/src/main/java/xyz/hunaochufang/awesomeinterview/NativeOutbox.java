package xyz.hunaochufang.awesomeinterview;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.time.Instant;
import java.util.LinkedHashMap;
import java.util.Map;

/** Stores commands and their optimistic projection in the same encrypted snapshot. */
final class NativeOutbox {
    private NativeOutbox() { }

    static JSONArray operations(JSONObject state) throws JSONException {
        JSONArray result = state.optJSONArray("outbox");
        if (result == null) {
            result = new JSONArray();
            state.put("outbox", result);
        }
        return result;
    }

    static void enqueue(JSONObject state, String id, String kind, JSONObject command) throws JSONException {
        JSONArray queue = operations(state);
        if (queue.length() >= 200) throw new IllegalStateException("待同步操作已满，请先联网同步");
        queue.put(new JSONObject().put("id", id).put("kind", kind).put("command", command)
                .put("created_at", Instant.now().toString()).put("state", "pending")
                .put("attempts", 0).put("next_attempt", 0));
        project(state);
    }

    static void project(JSONObject state) throws JSONException {
        for (String resource : new String[]{"events", "ideas", "todos"}) {
            String baseKey = "confirmed_" + resource;
            JSONArray confirmed = state.optJSONArray(baseKey);
            if (confirmed == null) {
                confirmed = state.optJSONArray(resource);
                if (confirmed == null) confirmed = new JSONArray();
                state.put(baseKey, confirmed);
            }
            Map<String, JSONObject> items = index(confirmed);
            JSONArray queue = operations(state);
            for (int i = 0; i < queue.length(); i++) {
                JSONObject op = queue.getJSONObject(i);
                if (!resource.equals(op.getString("kind")) || "failed".equals(op.optString("state"))) continue;
                JSONObject command = op.getJSONObject("command");
                String id = command.getString(resourceKey(resource));
                String action = command.getString("action");
                if ("delete".equals(action)) {
                    items.remove(id);
                    continue;
                }
                JSONObject item = items.get(id);
                if (item == null && !"create".equals(action)) continue;
                if (item == null) item = new JSONObject().put("id", id).put("created_at", op.getString("created_at"));
                String when = op.getString("created_at");
                if (resource.equals("events")) applyEvent(item, command, when);
                else if (resource.equals("ideas")) applyIdea(item, command);
                else if ("create".equals(action)) {
                    item.put("title", command.getString("title")).put("due_date", command.getString("due_date"));
                } else {
                    item.put("completed_at", command.getBoolean("completed") ? when : JSONObject.NULL);
                }
                item.put("awaiting_sync", true);
                item.put("updated_at", when);
                items.put(id, item);
            }
            state.put(resource, new JSONArray(items.values()));
        }
    }

    private static void applyEvent(JSONObject item, JSONObject command, String when) throws JSONException {
        switch (command.getString("action")) {
            case "create":
            case "edit":
                copyFields(command.getJSONObject("event"), item);
                break;
            case "completion":
                Object completed = command.getBoolean("completed") ? when : JSONObject.NULL;
                if (!command.isNull("occurrence_day")) {
                    JSONObject days = item.optJSONObject("repeat_completions");
                    if (days == null) days = new JSONObject();
                    days.put(command.getString("occurrence_day"), completed);
                    item.put("repeat_completions", days);
                } else item.put("completed_at", completed);
                break;
            case "defer": item.put("end_at", command.getString("end_at")); break;
            case "cancel": item.put("cancelled_at", when); break;
            case "close_repeat":
                java.time.ZoneId zone = java.time.ZoneId.of(item.optString("timezone", "Asia/Shanghai"));
                item.put("repeat_closed_on", java.time.Instant.parse(when).atZone(zone).toLocalDate().plusDays(1).toString());
                break;
        }
    }

    private static void applyIdea(JSONObject item, JSONObject command) throws JSONException {
        if ("abandon".equals(command.getString("action"))) item.put("status", "abandoned");
        else {
            for (String name : new String[]{"title", "note", "reference_links"}) item.put(name, command.get(name));
            item.put("status", "pending");
        }
    }

    static void acknowledge(JSONObject state, JSONObject op, JSONObject receipt) throws JSONException {
        String resource = op.getString("kind");
        String key = "confirmed_" + resource;
        Map<String, JSONObject> base = index(state.getJSONArray(key));
        JSONObject record = receipt.optJSONObject(resourceKey(resource).replace("_id", ""));
        if (record != null) base.put(record.getString("id"), record);
        else if (!receipt.isNull("deleted_id")) base.remove(receipt.getString("deleted_id"));
        else throw new JSONException("同步回执缺少实体");
        state.put(key, new JSONArray(base.values()));
        remove(state, op.getString("id"));
        project(state);
    }

    static void remove(JSONObject state, String id) throws JSONException {
        JSONArray queue = operations(state);
        for (int i = queue.length() - 1; i >= 0; i--)
            if (id.equals(queue.getJSONObject(i).optString("id"))) queue.remove(i);
    }

    static JSONObject next(JSONObject state) throws JSONException {
        JSONArray queue = operations(state);
        for (int i = 0; i < queue.length(); i++) {
            JSONObject op = queue.getJSONObject(i);
            if (!"failed".equals(op.optString("state"))) return op;
        }
        return null;
    }

    static void fail(JSONObject state, JSONObject failed, int status, String message) throws JSONException {
        JSONArray queue = operations(state);
        JSONObject cause = failed.getJSONObject("command");
        String kind = failed.getString("kind");
        String key = resourceKey(kind);
        String resourceId = cause.getString(key);
        boolean reached = false;
        for (int i = 0; i < queue.length(); i++) {
            JSONObject operation = queue.getJSONObject(i);
            if (operation == failed) reached = true;
            if (!reached) continue;
            JSONObject command = operation.getJSONObject("command");
            JSONObject event = command.optJSONObject("event");
            boolean sameResource = kind.equals(operation.optString("kind")) && resourceId.equals(command.optString(key));
            boolean dependent = kind.equals("ideas") && event != null && resourceId.equals(event.optString("source_idea_id"));
            if (sameResource || dependent)
                operation.put("state", "failed").put("status", status).put("error", message.isEmpty() ? "服务端拒绝了本次修改" : message);
        }
        project(state);
    }

    private static String resourceKey(String resource) {
        switch (resource) {
            case "ideas": return "idea_id";
            case "todos": return "todo_id";
            default: return "event_id";
        }
    }

    private static Map<String, JSONObject> index(JSONArray array) throws JSONException {
        Map<String, JSONObject> result = new LinkedHashMap<>();
        for (int i = 0; i < array.length(); i++) {
            JSONObject item = new JSONObject(array.getJSONObject(i).toString());
            result.put(item.getString("id"), item);
        }
        return result;
    }

    private static void copyFields(JSONObject from, JSONObject to) throws JSONException {
        java.util.Iterator<String> keys = from.keys();
        while (keys.hasNext()) {
            String key = keys.next();
            to.put(key, from.get(key));
        }
    }
}
