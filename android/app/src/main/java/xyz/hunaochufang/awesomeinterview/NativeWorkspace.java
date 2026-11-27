package xyz.hunaochufang.awesomeinterview;

import android.app.Activity;
import android.app.AlertDialog;
import android.net.Uri;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.time.LocalDate;
import java.time.LocalTime;
import java.time.ZoneId;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.function.Consumer;

/** Native account, plaza, interviews and experience screens. */
final class NativeWorkspace {
    static final String API = "/api/mobile/v1";
    static final String EXPERIENCES = "/my-schedule/experiences";
    private static final String[] CATEGORIES = {"project", "fundamentals", "algorithm", "other", "reverse"};
    private static final String[] CATEGORY_LABELS = {"项目问题", "八股问题", "算法题", "其他", "反问"};

    interface Actions {
        void request(String method, String path, Object body, Consumer<JSONObject> received);
        void openEvent(String id);
        void openResume();
        void exportPdf(String id, String title);
        void exportMarkdown(String id, String title);
        void shareMarkdown(String id, String title);
        JSONObject snapshot();
        void importFile(String path, Consumer<JSONObject> received);
        void experiencesChanged();
        void profileChanged(JSONObject profile);
    }

    private final Activity activity;
    private final Actions actions;
    private AlertDialog active;
    private boolean adminVisible;

    NativeWorkspace(Activity activity, Actions actions) {
        this.activity = activity;
        this.actions = actions;
    }

    void close() {
        if (active != null) active.dismiss();
        active = null;
        adminVisible = false;
    }

    void closeAdmin() {if (adminVisible) close();}

    void account() {
        get(API + "/profile", profile -> {
            Panel panel = new Panel();
            panel.text(value(profile, "email"));
            EditText displayName = panel.field("昵称", value(profile, "display_name"), false);
            EditText start = panel.field("上班时间（HH:mm）", value(profile, "workday_start"), false);
            EditText end = panel.field("下班时间（HH:mm）", value(profile, "workday_end"), false);
            panel.button("保存设置", () -> {
                try {
                    LocalTime from = LocalTime.parse(input(start));
                    LocalTime until = LocalTime.parse(input(end));
                    if (!from.isBefore(until)) {
                        end.setError("下班时间必须晚于上班时间");
                        return;
                    }
                } catch (java.time.DateTimeException error) {
                    end.setError("请用 HH:mm 格式填写工作时间");
                    return;
                }
                actions.request("PUT", API + "/profile", object("display_name", input(displayName),
                        "workday_start", input(start), "workday_end", input(end)), profileResult -> {
                    close();
                    actions.profileChanged(profileResult);
                });
            });
            show("个人设置", panel);
        });
    }

    void plaza(String company, String order, int page) {
        String path = new Uri.Builder().path(API + "/plaza").appendQueryParameter("company", company)
                .appendQueryParameter("order", order).appendQueryParameter("page", Integer.toString(page))
                .build().toString();
        get(path, data -> {
            Panel panel = new Panel();
            EditText filter = panel.field("公司", company, false);
            CheckBox oldest = panel.check("最早发布优先", order.equals("oldest"));
            panel.button("筛选", () -> plaza(input(filter), oldest.isChecked() ? "oldest" : "newest", 1));
            panel.text("共 " + data.optInt("total") + " 篇面经 · 第 " + page + " 页");
            for (JSONObject row : rows(data, "items")) {
                panel.button(value(row, "title"), () -> get(API + "/plaza/" + Uri.encode(row.optString("id")),
                        detail -> renderExperience(detail, false, () -> plaza(company, order, page))));
                panel.text(join(row, "company", "author"));
            }
            if (page > 1) panel.button("上一页", () -> plaza(company, order, page - 1));
            if (!data.isNull("next_page")) panel.button("下一页", () -> plaza(company, order, page + 1));
            show("面经广场", panel);
        });
    }

    void interviews(JSONObject snapshot, LocalDate day) {
        Panel panel = new Panel();
        panel.text(day.toString());
        panel.button("前一天", () -> interviews(snapshot, day.minusDays(1)));
        panel.button("后一天", () -> interviews(snapshot, day.plusDays(1)));
        panel.button("当前简历", actions::openResume);
        boolean empty = true;
        for (JSONObject event : rows(snapshot, "events")) {
            if (!"interview".equals(event.optString("event_type")) && !"interview".equals(event.optString("type"))) continue;
            if (!event.isNull("cancelled_at")) continue;
            try {
                var start = java.time.OffsetDateTime.parse(event.getString("start_at"))
                        .atZoneSameInstant(ZoneId.of("Asia/Shanghai"));
                if (!start.toLocalDate().equals(day)) continue;
                empty = false;
                panel.button(start.toLocalTime().toString() + " · " + value(event, "title"), () -> {
                    close();
                    actions.openEvent(event.optString("id"));
                });
            } catch (java.time.DateTimeException | JSONException ignored) {
                // Skip records without a valid timestamp.
            }
        }
        if (empty) panel.text("当天没有面试安排");
        show("我的面试", panel);
    }

    void admin(String channel) {
        get("/my-schedule/admin/summary", summary -> get("/my-schedule/admin/performance?hours=24&channel=" + channel,
                performance -> {
                    Panel panel = new Panel();
                    panel.button("全部渠道", () -> admin("all"));
                    panel.button("Android 渠道", () -> admin("android"));
                    panel.text("近 24 小时 · " + (channel.equals("all") ? "全部渠道" : "Android"));
                    panel.text("注册用户 " + summary.optInt("total_users") + " · 面经 " + summary.optInt("total_notes"));
                    panel.text("公开 " + summary.optInt("total_public") + " · 私有 " + summary.optInt("total_private"));
                    panel.text("服务端请求 " + performance.optInt("server_count") + " · 错误 " + performance.optInt("errors"));
                    for (JSONObject row : rows(performance, "groups")) {
                        panel.text(join(row, "node", "metric_label") + "\nP95 " + row.optString("p95") +
                                " ms · 样本 " + row.optInt("count"));
                    }
                    show("管理员看板", panel);
                    adminVisible = true;
                }));
    }

    void experience(String id) {
        get(API + "/experiences/" + Uri.encode(id), detail -> renderExperience(detail, true, this::close));
    }

    void editExperience(JSONObject experience) {
        Panel panel = new Panel();
        String id = value(experience, "id");
        EditText title = panel.field("标题", value(experience, "title"), false);
        EditText company = panel.field("公司", value(experience, "company"), false);
        EditText position = panel.field("岗位", value(experience, "position"), false);
        EditText round = panel.field("轮次", value(experience, "interview_round"), false);
        ArrayList<String> interviewIds = new ArrayList<>();
        ArrayList<String> interviewTitles = new ArrayList<>();
        interviewIds.add("");
        interviewTitles.add("未关联面试");
        for (JSONObject event : rows(actions.snapshot(), "events")) {
            if (!"interview".equals(event.optString("type"))) continue;
            interviewIds.add(event.optString("id"));
            interviewTitles.add(value(event, "title"));
        }
        String currentInterview = value(experience, "linked_event_id");
        if (!currentInterview.isEmpty() && !interviewIds.contains(currentInterview)) {
            interviewIds.add(currentInterview);
            interviewTitles.add("保留当前关联");
        }
        android.widget.Spinner link = panel.choices("关联面试", interviewTitles.toArray(new String[0]));
        link.setSelection(Math.max(0, interviewIds.indexOf(currentInterview)));
        CheckBox visible = panel.check("公开到面经广场", experience != null && experience.optBoolean("is_public"));
        panel.button("保存面经", () -> {
            if (input(title).isEmpty()) {
                title.setError("请输入标题");
                return;
            }
            String eventId = interviewIds.get(link.getSelectedItemPosition());
            actions.request(id.isEmpty() ? "POST" : "PUT", API + "/experiences" +
                    (id.isEmpty() ? "" : "/" + Uri.encode(id)),
                    object("title", input(title), "company", input(company), "position", input(position),
                            "interview_round", input(round), "is_public", visible.isChecked(),
                            "linked_event_id", eventId.isEmpty() ? JSONObject.NULL : eventId,
                            "interviewed_at", fieldOrNull(experience, "interviewed_at")), result -> {
                        actions.experiencesChanged();
                        experience(result.optJSONObject("experience").optString("id"));
                    });
        });
        show(id.isEmpty() ? "新建面经" : "编辑面经", panel);
    }

    private void renderExperience(JSONObject detail, boolean editable, Runnable back) {
        JSONObject item = detail.optJSONObject("experience");
        if (item == null) return;
        String id = item.optString("id");
        Panel panel = new Panel();
        panel.text(join(item, "company", "position", "interview_round"));
        panel.text(value(detail, "author"));
        panel.text(value(item, "feeling"));
        if (editable) {
            panel.button("编辑面经信息", () -> editExperience(item));
            panel.button("添加问题", () -> editQuestion(id, null, ""));
            panel.button("导入面经", () -> importExperience(item, detail.optJSONArray("questions")));
        }
        for (JSONObject question : rows(detail, "questions")) {
            panel.text(value(question, "question_text"));
            panel.text(answer(question, "on_site_answer", "现场回答"));
            panel.text(answer(question, "review_answer", "复盘回答"));
            panel.text(answer(question, "interviewer_answer", "面试官回答"));
            if (editable) panel.button("编辑此题", () -> editQuestion(id, question, ""));
        }
        if (editable) {
            panel.button("导出 PDF", () -> {close(); actions.exportPdf(id, value(item, "title"));});
            panel.button("保存 Markdown", () -> actions.exportMarkdown(id, value(item, "title")));
            panel.button("分享 Markdown", () -> actions.shareMarkdown(id, value(item, "title")));
            panel.button("删除面经", () -> confirmDelete(EXPERIENCES + "/" + Uri.encode(id) + "/delete", () -> {
                close();
                actions.experiencesChanged();
            }));
        }
        panel.button("返回列表", back);
        show(value(item, "title"), panel);
    }

    private void editQuestion(String experienceId, JSONObject question, String parentId) {
        Panel panel = new Panel();
        String id = value(question, "id");
        EditText text = panel.field("题目", value(question, "question_text"), true);
        android.widget.Spinner category = panel.choices("题目分类", CATEGORY_LABELS);
        int index = java.util.Arrays.asList(CATEGORIES).indexOf(value(question, "category"));
        category.setSelection(Math.max(0, index));
        EditText onsite = panel.field("现场回答", value(question, "on_site_answer"), true);
        EditText review = panel.field("复盘回答", value(question, "review_answer"), true);
        EditText interviewer = panel.field("面试官回答（反问）", value(question, "interviewer_answer"), true);
        String path = EXPERIENCES + "/" + Uri.encode(experienceId) + "/questions" + (id.isEmpty() ? "" : "/" + Uri.encode(id));
        panel.button("保存题目", () -> actions.request("POST", path + (id.isEmpty() ? "" : "/edit"),
                form("question_text", input(text), "category", CATEGORIES[category.getSelectedItemPosition()],
                        "on_site_answer", input(onsite), "review_answer", input(review),
                        "interviewer_answer", input(interviewer), "parent_question_id", parentId),
                ignored -> questionChanged(experienceId)));
        if (!id.isEmpty()) {
            panel.button("添加追问", () -> editQuestion(experienceId, null, id));
            panel.button("上移", () -> moveQuestion(experienceId, path, "up"));
            panel.button("下移", () -> moveQuestion(experienceId, path, "down"));
            panel.button("删除题目", () -> confirmDelete(path + "/delete", () -> questionChanged(experienceId)));
        }
        show(id.isEmpty() ? "添加题目" : "编辑题目", panel);
    }

    private void questionChanged(String experienceId) {
        actions.experiencesChanged();
        experience(experienceId);
    }

    private void moveQuestion(String experienceId, String path, String direction) {
        actions.request("POST", path + "/move", form("direction", direction), ignored -> questionChanged(experienceId));
    }

    private void confirmDelete(String path, Runnable done) {
        new AlertDialog.Builder(activity).setTitle("确认删除？").setNegativeButton("取消", null)
                .setPositiveButton("删除", (dialog, button) ->
                        actions.request("POST", path, form(), ignored -> done.run())).show();
    }

    private void importExperience(JSONObject item, JSONArray existing) {
        Panel panel = new Panel();
        EditText source = panel.field("面经原文", value(item, "raw_text_draft"), true);
        String path = EXPERIENCES + "/" + Uri.encode(item.optString("id"));
        panel.button("解析原文", () -> actions.request("POST", path + "/parse", form("source", input(source)),
                parsed -> importPreview(item, existing, parsed, input(source))));
        panel.button("导入 Markdown 或 PDF", () -> actions.importFile(path + "/parse-file",
                parsed -> importPreview(item, existing, parsed, value(parsed, "source_text"))));
        show("导入面经", panel);
    }

    private void importPreview(JSONObject item, JSONArray existing, JSONObject parsed, String source) {
        Panel panel = new Panel();
        var imported = rows(parsed, "questions");
        for (JSONObject question : imported) panel.text(value(question, "question_text"));
        if (imported.isEmpty()) panel.text("未识别到题目");
        else panel.button("追加 " + imported.size() + " 道题", () -> {
            JSONArray questions = new JSONArray();
            if (existing != null) for (int index = 0; index < existing.length(); index++) questions.put(existing.opt(index));
            for (JSONObject question : imported) {
                try {
                    questions.put(new JSONObject(question.toString()).put("sibling_order", questions.length() + 1));
                } catch (JSONException error) {throw new IllegalStateException(error);}
            }
            JSONObject draft = object("version", value(item, "updated_at"), "questions", questions, "raw_text_draft", source);
            String id = item.optString("id");
            actions.request("POST", EXPERIENCES + "/" + Uri.encode(id) + "/draft",
                    form("payload", draft.toString()), ignored -> questionChanged(id));
        });
        show("导入预览", panel);
    }

    private static String answer(JSONObject question, String key, String label) {
        String content = value(question, key);
        return content.isEmpty() ? "" : label + "\n" + content;
    }

    static String value(JSONObject object, String key) {
        return object == null || object.isNull(key) ? "" : object.optString(key);
    }

    private static Object fieldOrNull(JSONObject object, String key) {
        return object == null || object.isNull(key) ? JSONObject.NULL : object.opt(key);
    }

    static String join(JSONObject object, String... keys) {
        ArrayList<String> parts = new ArrayList<>();
        for (String key : keys) if (!value(object, key).isEmpty()) parts.add(value(object, key));
        return android.text.TextUtils.join(" · ", parts);
    }

    static ArrayList<JSONObject> rows(JSONObject object, String key) {
        var list = new ArrayList<JSONObject>();
        JSONArray rows = object == null ? null : object.optJSONArray(key);
        if (rows != null) for (int index = 0; index < rows.length(); index++) {
            JSONObject row = rows.optJSONObject(index);
            if (row != null) list.add(row);
        }
        return list;
    }

    static JSONObject object(Object... fields) {
        JSONObject object = new JSONObject();
        try {
            for (int index = 0; index < fields.length; index += 2) object.put((String) fields[index], fields[index + 1]);
            return object;
        } catch (JSONException error) {throw new IllegalArgumentException(error);}
    }

    private static Map<String, String> form(String... fields) {
        Map<String, String> form = new LinkedHashMap<>();
        for (int index = 0; index < fields.length; index += 2) form.put(fields[index], fields[index + 1]);
        return form;
    }

    private static String input(EditText field) {return field.getText().toString().trim();}
    private int dp(int amount) {return Math.round(amount * activity.getResources().getDisplayMetrics().density);}
    private void get(String path, Consumer<JSONObject> received) {actions.request("GET", path, null, received);}

    private void show(String title, Panel panel) {
        close();
        ScrollView scroller = new ScrollView(activity);
        scroller.addView(panel.layout);
        active = new AlertDialog.Builder(activity).setTitle(title).setView(scroller)
                .setNegativeButton("关闭", null).create();
        active.show();
    }

    private final class Panel {
        final LinearLayout layout = new LinearLayout(activity);

        Panel() {
            layout.setOrientation(LinearLayout.VERTICAL);
            layout.setPadding(dp(24), dp(8), dp(24), dp(16));
        }

        void text(String content) {
            if (content.isEmpty()) return;
            TextView text = new TextView(activity);
            text.setText(content);
            text.setTextIsSelectable(true);
            text.setPadding(0, dp(4), 0, dp(8));
            layout.addView(text);
        }

        EditText field(String label, String value, boolean multiline) {
            TextView name = new TextView(activity);
            name.setText(label);
            name.setPadding(0, dp(8), 0, 0);
            layout.addView(name);
            EditText input = new EditText(activity);
            input.setId(android.view.View.generateViewId());
            name.setLabelFor(input.getId());
            input.setSingleLine(!multiline);
            if (multiline) input.setMinLines(2);
            input.setText(value);
            input.setMinimumHeight(dp(48));
            layout.addView(input);
            return input;
        }

        CheckBox check(String label, boolean checked) {
            CheckBox check = new CheckBox(activity);
            check.setText(label);
            check.setChecked(checked);
            check.setMinHeight(dp(48));
            layout.addView(check);
            return check;
        }

        android.widget.Spinner choices(String label, String[] values) {
            TextView name = new TextView(activity);
            name.setText(label);
            name.setPadding(0, dp(8), 0, 0);
            layout.addView(name);
            android.widget.Spinner choices = new android.widget.Spinner(activity);
            choices.setId(android.view.View.generateViewId());
            name.setLabelFor(choices.getId());
            choices.setMinimumHeight(dp(48));
            choices.setAdapter(new android.widget.ArrayAdapter<>(activity, android.R.layout.simple_spinner_dropdown_item, values));
            layout.addView(choices);
            return choices;
        }

        void button(String label, Runnable callback) {
            var button = NativeButtons.make(activity, label, NativeButtons.Role.OUTLINED);
            button.setMinHeight(dp(48));
            button.setOnClickListener(view -> callback.run());
            layout.addView(button);
        }
    }
}
