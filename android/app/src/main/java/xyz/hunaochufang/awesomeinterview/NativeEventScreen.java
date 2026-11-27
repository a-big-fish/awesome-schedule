package xyz.hunaochufang.awesomeinterview;

import android.app.DatePickerDialog;
import android.app.TimePickerDialog;
import android.content.Context;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.view.Gravity;
import android.view.View;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;

import org.json.JSONArray;
import org.json.JSONObject;

import java.time.OffsetDateTime;
import java.time.ZoneId;
import java.time.ZonedDateTime;
import java.time.format.DateTimeFormatter;

/** Native event detail and manual editor backed by the personal schedule API. */
final class NativeEventScreen extends ScrollView {
    interface Actions {
        void save(String eventId, JSONObject payload);
        void parse(String rawText);
        void delete(String eventId);
        void complete(String eventId, boolean completed);
        void resolve(String eventId, String action, ZonedDateTime end);
        void openResume();
        java.time.LocalTime workTime(boolean starting);
        void openSeries(String eventId);
        void back();
    }

    private static final ZoneId ZONE = ZoneId.of("Asia/Shanghai");
    private static final String[] TYPES = {"面试", "笔试", "课程", "招聘活动", "会议", "个人事项"};
    private static final String[] VALUES = {"interview", "exam", "course", "recruiting", "meeting", "personal"};
    private final Actions actions;
    private final LinearLayout body;
    private JSONObject event;
    private String eventId;
    private ZonedDateTime start;
    private ZonedDateTime end;
    private TextView startLabel;
    private TextView endLabel;
    private String rawInvitation;
    private JSONObject sourceIdea;
    private boolean dateOnly;

    NativeEventScreen(Context context, Actions actions) {
        super(context);
        this.actions = actions;
        setFillViewport(true);
        setBackgroundColor(Color.rgb(248, 251, 252));
        body = new LinearLayout(context);
        body.setOrientation(LinearLayout.VERTICAL);
        body.setPadding(dp(20), dp(20), dp(20), dp(36));
        addView(body, new LayoutParams(-1, -2));
    }

    String editingEventId() { return eventId; }

    void showNew() {
        dateOnly = false;
        sourceIdea = null;
        event = null;
        eventId = null;
        rawInvitation = null;
        start = ZonedDateTime.now(ZONE).plusHours(1).withMinute(0).withSecond(0).withNano(0);
        end = start.plusHours(1);
        showForm(null);
    }

    void arrangeIdea(JSONObject idea) {
        dateOnly = false;
        event = null;
        eventId = null;
        rawInvitation = null;
        sourceIdea = idea;
        start = ZonedDateTime.now(ZONE).plusHours(1).withMinute(0).withSecond(0).withNano(0);
        end = start.plusHours(1);
        showForm(idea);
    }

    void showEvent(JSONObject value) {
        dateOnly = false;
        event = value;
        eventId = value.optString("id");
        rawInvitation = null;
        renderDetail();
    }

    void showDraft(JSONObject draft, String id) {
        dateOnly = false;
        event = null;
        eventId = id;
        rawInvitation = draft.optString("raw_text", "");
        if (rawInvitation.isEmpty()) rawInvitation = null;
        sourceIdea = draft.isNull("source_idea_id") ? null :
                NativeWorkspace.object("id", draft.optString("source_idea_id"));
        showForm(draft);
    }

    void applyParsed(JSONObject value, String rawText) {
        rawInvitation = rawText;
        showForm(value);
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density + .5f);
    }

    private GradientDrawable background(int color) {
        GradientDrawable shape = new GradientDrawable();
        shape.setColor(color);
        shape.setCornerRadius(dp(12));
        shape.setStroke(dp(1), Color.rgb(215, 225, 231));
        return shape;
    }

    private TextView text(String value, int size, boolean bold) {
        TextView view = new TextView(getContext());
        view.setText(value);
        view.setTextSize(size);
        view.setTextColor(Color.rgb(25, 40, 65));
        if (bold) view.setTypeface(null, Typeface.BOLD);
        return view;
    }

    private void addHeading(String title) {
        body.removeAllViews();
        TextView heading = text(title, 24, true);
        heading.setPadding(0, 0, 0, dp(20));
        body.addView(heading);
    }

    private void addLabel(String label) {
        TextView view = text(label, 14, true);
        view.setPadding(0, dp(14), 0, dp(6));
        body.addView(view);
    }

    private EditText field(String label, String value) {
        addLabel(label);
        EditText input = new EditText(getContext());
        input.setSingleLine(true);
        input.setId(View.generateViewId());
        ((TextView) body.getChildAt(body.getChildCount() - 1)).setLabelFor(input.getId());
        input.setHint("请输入" + label.replace("（面试可选）", ""));
        input.setText(value);
        input.setTextSize(16);
        input.setPadding(dp(12), dp(10), dp(12), dp(10));
        input.setBackground(background(Color.WHITE));
        body.addView(input, new LinearLayout.LayoutParams(-1, dp(52)));
        return input;
    }

    private Button button(String label, NativeButtons.Role role) {
        Button view = NativeButtons.make(getContext(), label, role);
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(-1, dp(52));
        params.topMargin = dp(16);
        body.addView(view, params);
        return view;
    }

    private ZonedDateTime parseDate(String value, ZonedDateTime fallback) {
        try { return OffsetDateTime.parse(value).atZoneSameInstant(ZONE); }
        catch (Exception ignored) { return fallback; }
    }

    private String displayDate(ZonedDateTime value) {
        String pattern = value.getYear() == ZonedDateTime.now(ZONE).getYear()
                ? "M月d日 HH:mm" : "yyyy年M月d日 HH:mm";
        return value.format(DateTimeFormatter.ofPattern(pattern, java.util.Locale.SIMPLIFIED_CHINESE));
    }

    private void pickDateTime(boolean starting) {
        ZonedDateTime current = starting ? start : end;
        new DatePickerDialog(getContext(), (picker, year, month, day) -> {
            var date = java.time.LocalDate.of(year, month + 1, day);
            if (dateOnly) {
                acceptDateTime(starting, date.atTime(actions.workTime(starting)).atZone(current.getZone()));
            } else new TimePickerDialog(getContext(), (clock, hour, minute) ->
                    acceptDateTime(starting, date.atTime(hour, minute).atZone(current.getZone())),
                    current.getHour(), current.getMinute(), true).show();
        },
                current.getYear(), current.getMonthValue() - 1, current.getDayOfMonth()).show();
    }

    private void acceptDateTime(boolean starting, ZonedDateTime picked) {
        if (starting) {
            start = picked;
            if (eventId == null) end = dateOnly
                    ? picked.toLocalDate().atTime(actions.workTime(false)).atZone(picked.getZone())
                    : picked.plusHours(1);
        } else end = picked;
        startLabel.setText(displayDate(start));
        endLabel.setText(displayDate(end));
    }

    private void showForm(JSONObject initial) {
        if (initial != null && initial.has("start_at")) {
            start = parseDate(initial.optString("start_at"), ZonedDateTime.now(ZONE));
            end = parseDate(initial.optString("end_at"), start.plusHours(1));
            try {
                ZoneId zone = ZoneId.of(initial.optString("timezone", ZONE.getId()));
                start = start.withZoneSameInstant(zone);
                end = end.withZoneSameInstant(zone);
            } catch (java.time.DateTimeException ignored) { }
        }
        addHeading(eventId == null ? "添加日程" : "编辑日程");
        LinearLayout invitationGroup = null;
        if (eventId == null && sourceIdea == null) {
            invitationGroup = new LinearLayout(getContext());
            invitationGroup.setOrientation(LinearLayout.VERTICAL);
            body.addView(invitationGroup);
            EditText invitation = field("粘贴面试邀请（可选）", rawInvitation == null ? "" : rawInvitation);
            invitation.setSingleLine(false);
            invitation.setMinLines(3);
            invitation.setGravity(Gravity.TOP);
            invitation.getLayoutParams().height = dp(116);
            Button parse = button("解析邀请并填入下方", NativeButtons.Role.TONAL);
            parse.setOnClickListener(v -> {
                String raw = invitation.getText().toString().trim();
                if (raw.isEmpty()) invitation.setError("请先粘贴邀请内容");
                else actions.parse(raw);
            });
            int groupIndex = body.indexOfChild(invitationGroup);
            for (int i = 0; i < 3; i++) {
                View child = body.getChildAt(groupIndex + 1);
                body.removeView(child);
                invitationGroup.addView(child);
            }
            LinearLayout invitationFields = new LinearLayout(getContext());
            invitationFields.setOrientation(LinearLayout.VERTICAL);
            while (invitationGroup.getChildCount() > 0) {
                View child = invitationGroup.getChildAt(0);
                invitationGroup.removeView(child);
                invitationFields.addView(child);
            }
            Button expand = NativeButtons.make(getContext(), "从面试邀请快速填写  ▾", NativeButtons.Role.TONAL);
            invitationGroup.addView(expand, new LinearLayout.LayoutParams(-1, dp(48)));
            invitationGroup.addView(invitationFields);
            invitationFields.setVisibility(GONE);
            expand.setOnClickListener(v -> {
                boolean open = invitationFields.getVisibility() != VISIBLE;
                invitationFields.setVisibility(open ? VISIBLE : GONE);
                expand.setText(open ? "收起面试邀请  ▴" : "从面试邀请快速填写  ▾");
            });
        }
        EditText title = field("日程标题", initial == null ? "" : initial.optString("title"));
        title.setHint("例如：后端开发二面");
        addLabel("类型");
        Spinner type = new Spinner(getContext());
        type.setAdapter(new ArrayAdapter<>(getContext(), android.R.layout.simple_spinner_dropdown_item, TYPES));
        String initialType = initial == null ? "interview" : initial.optString("type", "interview");
        for (int i = 0; i < VALUES.length; i++) if (VALUES[i].equals(initialType)) type.setSelection(i);
        body.addView(type, new LinearLayout.LayoutParams(-1, dp(52)));
        CheckBox dateMode = new CheckBox(getContext());
        dateMode.setText("仅选日期，使用个人工作时间");
        dateMode.setMinHeight(dp(48));
        dateMode.setChecked(dateOnly);
        dateMode.setOnCheckedChangeListener((view, checked) -> {
            dateOnly = checked;
            if (checked) {
                start = start.toLocalDate().atTime(actions.workTime(true)).atZone(start.getZone());
                end = end.toLocalDate().atTime(actions.workTime(false)).atZone(end.getZone());
                startLabel.setText(displayDate(start));
                endLabel.setText(displayDate(end));
            }
        });
        body.addView(dateMode);
        addLabel("开始时间");
        startLabel = text(displayDate(start), 16, false);
        startLabel.setGravity(Gravity.CENTER_VERTICAL);
        startLabel.setPadding(dp(12), 0, dp(12), 0);
        startLabel.setBackground(background(Color.WHITE));
        startLabel.setOnClickListener(v -> pickDateTime(true));
        body.addView(startLabel, new LinearLayout.LayoutParams(-1, dp(52)));
        addLabel("结束时间");
        endLabel = text(displayDate(end), 16, false);
        endLabel.setGravity(Gravity.CENTER_VERTICAL);
        endLabel.setPadding(dp(12), 0, dp(12), 0);
        endLabel.setBackground(background(Color.WHITE));
        endLabel.setOnClickListener(v -> pickDateTime(false));
        body.addView(endLabel, new LinearLayout.LayoutParams(-1, dp(52)));
        EditText position = field("应聘岗位（面试可选）", NativeWorkspace.value(initial, "position"));
        View positionLabel = body.getChildAt(body.getChildCount() - 2);
        LinearLayout finalInvitationGroup = invitationGroup;
        type.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(android.widget.AdapterView<?> parent, View view, int selected, long id) {
                position.setVisibility(selected == 0 ? View.VISIBLE : View.GONE);
                positionLabel.setVisibility(selected == 0 ? View.VISIBLE : View.GONE);
                if (finalInvitationGroup != null)
                    finalInvitationGroup.setVisibility(selected == 0 || "meeting".equals(VALUES[selected]) ? View.VISIBLE : View.GONE);
            }
            @Override public void onNothingSelected(android.widget.AdapterView<?> parent) { }
        });
        EditText room = field("会议号", NativeWorkspace.value(initial, "room_number"));
        EditText meeting = field("入会链接", NativeWorkspace.value(initial, "meeting_url"));
        EditText location = field("地点", NativeWorkspace.value(initial, "location_text"));
        EditText zoneField = field("时区（例如 Asia/Shanghai）", initial == null ? ZONE.getId() : initial.optString("timezone", ZONE.getId()));
        EditText map = field("地图链接", initial == null || initial.isNull("location_url") ? "" : initial.optString("location_url"));
        CheckBox workday = new CheckBox(getContext()), skipWeekend = new CheckBox(getContext()), repeatTask = new CheckBox(getContext());
        workday.setText("仅工作时间显示");
        skipWeekend.setText("任务跳过周末");
        repeatTask.setText("添加为重复任务");
        workday.setChecked(initial != null && initial.optBoolean("workday_only"));
        skipWeekend.setChecked(initial != null && initial.optBoolean("skip_weekends"));
        JSONArray oldDays = initial == null ? null : initial.optJSONArray("repeat_weekdays");
        repeatTask.setChecked(oldDays != null && oldDays.length() > 0);
        body.addView(workday); body.addView(skipWeekend); body.addView(repeatTask);
        LinearLayout repeatDays = new LinearLayout(getContext());
        repeatDays.setOrientation(LinearLayout.VERTICAL);
        CheckBox[] weekdays = new CheckBox[7];
        for (int i = 0; i < weekdays.length; i++) {
            weekdays[i] = new CheckBox(getContext());
            weekdays[i].setText("周" + "一二三四五六日".charAt(i));
            for (int j = 0; oldDays != null && j < oldDays.length(); j++)
                if (oldDays.optInt(j) == i) weekdays[i].setChecked(true);
            repeatDays.addView(weekdays[i]);
        }
        repeatDays.setVisibility(repeatTask.isChecked() ? VISIBLE : GONE);
        repeatTask.setOnCheckedChangeListener((button, enabled) -> repeatDays.setVisibility(enabled ? VISIBLE : GONE));
        body.addView(repeatDays);
        EditText references = field("参考资料（每行一条，可用 名称 | 链接）", referenceText(initial));
        references.setSingleLine(false);
        EditText note = field("备注", NativeWorkspace.value(initial, "note"));
        note.setHint("补充准备事项或其他信息（选填）");
        addLabel("系统提醒（默认关闭）");
        int[] offsets = {10, 60, 1440};
        String[] reminders = {"提前 10 分钟", "提前 1 小时", "提前 1 天"};
        CheckBox[] checks = new CheckBox[3];
        JSONArray saved = initial == null ? null : initial.optJSONArray("reminder_offsets_minutes");
        for (int i = 0; i < checks.length; i++) {
            checks[i] = new CheckBox(getContext());
            checks[i].setText(reminders[i]);
            checks[i].setTextSize(14);
            checks[i].setMinHeight(dp(48));
            if (saved != null) for (int j = 0; j < saved.length(); j++)
                if (saved.optInt(j) == offsets[i]) checks[i].setChecked(true);
            body.addView(checks[i]);
        }
        button("保存日程", NativeButtons.Role.PRIMARY).setOnClickListener(v -> {
            if (title.getText().toString().trim().isEmpty() || !end.isAfter(start)) {
                title.setError("请填写标题，并确认结束时间晚于开始时间");
                return;
            }
            try {
                JSONObject payload = new JSONObject();
                payload.put("title", title.getText().toString().trim());
                payload.put("event_type", VALUES[type.getSelectedItemPosition()]);
                ZoneId zone;
                try {zone = ZoneId.of(zoneField.getText().toString().trim());}
                catch (java.time.DateTimeException error) {zoneField.setError("请输入有效时区"); return;}
                ZonedDateTime localStart = start.toLocalDateTime().atZone(zone);
                ZonedDateTime localEnd = end.toLocalDateTime().atZone(zone);
                JSONArray repeat = new JSONArray();
                if (repeatTask.isChecked()) {
                    for (int i = 0; i < weekdays.length; i++) if (weekdays[i].isChecked()) repeat.put(i);
                    if (repeat.length() == 0 || !localStart.toLocalDate().equals(localEnd.toLocalDate())) {
                        repeatTask.setError("请选择星期，重复任务的起止必须在同一天"); return;
                    }
                }
                payload.put("start_at", localStart.format(DateTimeFormatter.ISO_OFFSET_DATE_TIME));
                payload.put("end_at", localEnd.format(DateTimeFormatter.ISO_OFFSET_DATE_TIME));
                payload.put("timezone", zone.getId());
                payload.put("repeat_weekdays", repeat);
                payload.put("workday_only", workday.isChecked());
                payload.put("skip_weekends", !repeatTask.isChecked() && skipWeekend.isChecked());
                String roomNumber = room.getText().toString().trim();
                String meetingUrl = meeting.getText().toString().trim();
                if (!meetingUrl.isEmpty() && !isWebUrl(meetingUrl)) {meeting.setError("请输入有效的 HTTP 或 HTTPS 链接"); return;}
                payload.put("room_number", roomNumber.isEmpty() ? JSONObject.NULL : roomNumber);
                payload.put("meeting_url", meetingUrl.isEmpty() ? JSONObject.NULL : meetingUrl);
                payload.put("location_url", map.getText().toString().trim().isEmpty() ? JSONObject.NULL : map.getText().toString().trim());
                JSONArray links = new JSONArray();
                for (String line : references.getText().toString().split("\n")) {
                    if (line.trim().isEmpty()) continue;
                    String[] parts = line.split("\\|", 2);
                    String url = parts[parts.length - 1].trim();
                    android.net.Uri uri = android.net.Uri.parse(url);
                    if (links.length() >= 20 || uri.getHost() == null ||
                            (!"http".equals(uri.getScheme()) && !"https".equals(uri.getScheme())) || uri.getUserInfo() != null) {
                        references.setError("请输入最多 20 条有效的 HTTP 或 HTTPS 链接"); return;
                    }
                    links.put(new JSONObject().put("title", parts.length == 2 ? parts[0].trim() : "").put("url", url));
                }
                payload.put("reference_links", links);
                payload.put("position", position.getText().toString().trim());
                payload.put("location_text", location.getText().toString().trim());
                payload.put("note", note.getText().toString().trim());
                if (rawInvitation != null && eventId == null) payload.put("raw_invitation", rawInvitation);
                if (sourceIdea != null && eventId == null) {
                    payload.put("source_idea_id", sourceIdea.getString("id"));
                }
                JSONArray choices = new JSONArray();
                for (int i = 0; i < checks.length; i++) if (checks[i].isChecked()) choices.put(offsets[i]);
                payload.put("reminder_offsets_minutes", choices);
                actions.save(eventId, payload);
            } catch (Exception error) { title.setError("无法保存日程"); }
        });
        button("返回", NativeButtons.Role.OUTLINED).setOnClickListener(v -> actions.back());
        scrollTo(0, 0);
    }

    private String referenceText(JSONObject initial) {
        JSONArray links = initial == null ? null : initial.optJSONArray("reference_links");
        StringBuilder result = new StringBuilder();
        for (int i = 0; links != null && i < links.length(); i++) {
            JSONObject link = links.optJSONObject(i);
            if (link == null) continue;
            if (!link.optString("title").isEmpty()) result.append(link.optString("title")).append(" | ");
            result.append(link.optString("url")).append('\n');
        }
        return result.toString();
    }

    private void renderDetail() {
        addHeading(event.optString("title", "日程详情"));
        if (!event.isNull("completed_at") && !event.optString("completed_at").isEmpty()) {
            TextView completed = text("✓ 已完成", 14, true);
            completed.setTextColor(Color.rgb(0, 122, 127));
            completed.setPadding(0, 0, 0, dp(12));
            body.addView(completed);
        }
        int index = 0;
        for (int i = 0; i < VALUES.length; i++) if (VALUES[i].equals(event.optString("type"))) index = i;
        body.addView(text(TYPES[index] + " · " + displayDate(parseDate(event.optString("start_at"), ZonedDateTime.now(ZONE))) +
                " — " + displayDate(parseDate(event.optString("end_at"), ZonedDateTime.now(ZONE))), 15, false));
        for (String key : new String[]{"position", "location_text", "note"}) {
            String value = event.optString(key);
            if (!value.isEmpty() && !"null".equals(value)) {
                addLabel(key.equals("position") ? "应聘岗位" : key.equals("location_text") ? "地点" : "备注");
                body.addView(text(value, 16, false));
            }
        }
        if (event.optBoolean("workday_only")) body.addView(text("仅在设定的上班时段显示", 14, false));
        if (event.optBoolean("skip_weekends")) body.addView(text("跳过周末", 14, false));
        addLink("查看地图", NativeWorkspace.value(event, "location_url"));
        String room = NativeWorkspace.value(event, "room_number");
        if (!room.isEmpty()) {addLabel("会议号"); body.addView(text(room, 16, false));}
        addLink("加入会议", NativeWorkspace.value(event, "meeting_url"));
        for (JSONObject link : NativeWorkspace.rows(event, "reference_links")) {
            String title = NativeWorkspace.value(link, "title");
            String url = NativeWorkspace.value(link, "url");
            addLink(title.isEmpty() ? url : title, url);
        }
        if ("interview".equals(event.optString("type")))
            button("查看当前简历", NativeButtons.Role.OUTLINED).setOnClickListener(v -> actions.openResume());
        if (eventId.contains("~")) {
            button("查看重复系列", NativeButtons.Role.PRIMARY).setOnClickListener(v -> actions.openSeries(eventId.split("~", 2)[0]));
        } else {
            button("编辑日程", NativeButtons.Role.PRIMARY).setOnClickListener(v -> showForm(event));
        }
        boolean done = !event.isNull("completed_at") && !event.optString("completed_at").isEmpty();
        boolean cancelled = !event.isNull("cancelled_at");
        if (!cancelled) button(done ? "恢复为未完成" : "标记为已完成", NativeButtons.Role.TONAL)
                .setOnClickListener(v -> actions.complete(eventId, !done));
        else body.addView(text("已废除", 14, true));
        JSONArray repeat = event.optJSONArray("repeat_weekdays");
        boolean series = repeat != null && repeat.length() > 0 && !eventId.contains("~");
        if (series && event.isNull("repeat_closed_on"))
            button("结束重复系列", NativeButtons.Role.OUTLINED).setOnClickListener(v ->
                    confirm("结束重复系列？", () -> actions.resolve(eventId, "close_repeat", null)));
        if (!done && !cancelled && !series && !eventId.contains("~") &&
                parseDate(event.optString("end_at"), ZonedDateTime.now(ZONE)).isBefore(ZonedDateTime.now(ZONE))) {
            button("延期", NativeButtons.Role.OUTLINED).setOnClickListener(v -> chooseDeadline());
            button("废除日程", NativeButtons.Role.DANGER).setOnClickListener(v ->
                    confirm("废除已到期日程？", () -> actions.resolve(eventId, "cancel", null)));
        }
        button("返回日历", NativeButtons.Role.OUTLINED).setOnClickListener(v -> actions.back());
        if (!eventId.contains("~")) {
            Button delete = button("删除日程", NativeButtons.Role.TEXT);
            delete.setTextColor(Color.rgb(174, 45, 64));
            delete.setOnClickListener(v -> actions.delete(eventId));
        }
        scrollTo(0, 0);
    }

    private void addLink(String title, String url) {
        if (!isWebUrl(url)) return;
        android.net.Uri link = android.net.Uri.parse(url);
        button(title, NativeButtons.Role.TEXT).setOnClickListener(view -> {
            try {getContext().startActivity(new android.content.Intent(android.content.Intent.ACTION_VIEW, link));}
            catch (android.content.ActivityNotFoundException ignored) {
                android.widget.Toast.makeText(getContext(), "无法打开链接", android.widget.Toast.LENGTH_SHORT).show();
            }
        });
    }

    private boolean isWebUrl(String url) {
        android.net.Uri link = android.net.Uri.parse(url);
        return ("https".equalsIgnoreCase(link.getScheme()) || "http".equalsIgnoreCase(link.getScheme()))
                && link.getHost() != null && link.getUserInfo() == null;
    }

    private void chooseDeadline() {
        ZonedDateTime initial = ZonedDateTime.now(ZONE).plusHours(1);
        new DatePickerDialog(getContext(), (picker, year, month, day) ->
                new TimePickerDialog(getContext(), (clock, hour, minute) -> {
                    ZonedDateTime deadline = ZonedDateTime.of(year, month + 1, day, hour, minute, 0, 0, ZONE);
                    if (!deadline.isAfter(ZonedDateTime.now(ZONE))) {
                        android.widget.Toast.makeText(getContext(), "新结束时间必须晚于当前时间", android.widget.Toast.LENGTH_SHORT).show();
                    } else actions.resolve(eventId, "defer", deadline);
                }, initial.getHour(), initial.getMinute(), true).show(),
                initial.getYear(), initial.getMonthValue() - 1, initial.getDayOfMonth()).show();
    }

    private void confirm(String title, Runnable action) {
        new android.app.AlertDialog.Builder(getContext()).setTitle(title).setNegativeButton("取消", null)
                .setPositiveButton("确认", (dialog, button) -> action.run()).show();
    }
}
