package xyz.hunaochufang.awesomeinterview;

import android.content.Context;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.view.View;
import android.widget.Button;
import android.widget.ArrayAdapter;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;
import org.json.JSONArray;
import org.json.JSONObject;
import java.util.ArrayList;
import java.util.List;

/** Owner-scoped notes list rendered immediately from the encrypted snapshot. */
final class NativeExperienceList extends ScrollView {
    interface Actions {
        void edit(String id); void export(String id); void create(); void plaza();
        void load(String query, String visibility, String order, int page);
    }
    private final LinearLayout body;
    private final Actions actions;
    private final String[] visibilityValues = {"all", "public", "private"};
    private String query = "", visibility = "all", order = "newest";
    private int page = 1;
    private JSONObject snapshot, currentPage;
    NativeExperienceList(Context context, Actions actions) {
        super(context);
        this.actions = actions;
        setFillViewport(true);
        setBackgroundColor(Color.rgb(248, 251, 252));
        body = new LinearLayout(context);
        body.setOrientation(LinearLayout.VERTICAL);
        body.setPadding(dp(20), dp(20), dp(20), dp(24));
        addView(body);
        setSnapshot(null);
    }
    private int dp(int n) { return (int) (n * getResources().getDisplayMetrics().density + .5f); }
    private TextView text(String value, int size, boolean bold) {
        TextView view = new TextView(getContext());
        view.setText(value); view.setTextSize(size);
        view.setTextColor(bold ? Color.rgb(23, 41, 61) : Color.rgb(97, 115, 131));
        if (bold) view.setTypeface(null, Typeface.BOLD);
        view.setPadding(0, dp(4), 0, dp(8));
        return view;
    }
    void setSnapshot(JSONObject snapshot) {
        this.snapshot = snapshot;
        if (snapshot == null) {
            currentPage = null;
            query = "";
            visibility = "all";
            order = "newest";
            page = 1;
        }
        render();
    }
    void pageResult(JSONObject result) {
        currentPage = result;
        page = result.optInt("page", 1);
        render();
    }
    void reload() {actions.load(query, visibility, order, page);}
    private void render() {
        int scroll = getScrollY();
        body.removeAllViews();
        body.addView(text("我的面经", 24, true));
        body.addView(text("留住每次面试的收获", 14, false));
        LinearLayout tools = new LinearLayout(getContext());
        Button create = NativeButtons.make(getContext(), "＋ 新建面经", NativeButtons.Role.PRIMARY);
        Button plaza = NativeButtons.make(getContext(), "逛面经广场", NativeButtons.Role.TONAL);
        create.setOnClickListener(v -> actions.create());
        plaza.setOnClickListener(v -> actions.plaza());
        LinearLayout.LayoutParams left = new LinearLayout.LayoutParams(0, dp(48), 1);
        left.rightMargin = dp(8);
        tools.addView(create, left);
        tools.addView(plaza, new LinearLayout.LayoutParams(0, dp(48), 1));
        body.addView(tools);
        EditText search = new EditText(getContext());
        search.setSingleLine(true);
        search.setHint("搜索标题、公司或岗位");
        search.setText(query);
        search.addTextChangedListener(new android.text.TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int start, int count, int after) { }
            @Override public void onTextChanged(CharSequence s, int start, int before, int count) { query = s.toString(); }
            @Override public void afterTextChanged(android.text.Editable s) { }
        });
        body.addView(search);
        LinearLayout filters = new LinearLayout(getContext());
        Spinner visible = choices(new String[]{"全部", "公开", "私密"}, java.util.Arrays.asList(visibilityValues).indexOf(visibility), "可见性");
        Spinner sort = choices(new String[]{"最近更新", "最早更新"}, order.equals("oldest") ? 1 : 0, "排序");
        Button apply = NativeButtons.make(getContext(), "筛选", NativeButtons.Role.TONAL);
        apply.setOnClickListener(v -> {
            visibility = visibilityValues[visible.getSelectedItemPosition()];
            order = sort.getSelectedItemPosition() == 0 ? "newest" : "oldest";
            page = 1;
            reload();
        });
        filters.addView(visible, new LinearLayout.LayoutParams(0, dp(48), 1));
        filters.addView(sort, new LinearLayout.LayoutParams(0, dp(48), 1));
        filters.addView(apply, new LinearLayout.LayoutParams(-2, dp(48)));
        body.addView(filters);
        List<JSONObject> notes = new ArrayList<>();
        JSONArray items = currentPage != null ? currentPage.optJSONArray("items") : snapshot == null ? null : snapshot.optJSONArray("experiences");
        if (items != null) for (int i = 0; i < items.length(); i++) {
            JSONObject item = items.optJSONObject(i);
            if (item != null) notes.add(item);
        }
        if (currentPage == null) {
            notes.sort((a, b) -> b.optString("updated_at").compareTo(a.optString("updated_at")));
            if (notes.size() > 20) notes.subList(20, notes.size()).clear();
        }
        int total = currentPage == null ? notes.size() : currentPage.optInt("total");
        TextView summary = text(snapshot == null && currentPage == null ? "尚无本机缓存，联网登录后同步" : "共 " + total + " 篇", 14, false);
        summary.setPadding(0, dp(24), 0, dp(12));
        body.addView(summary);
        if (notes.isEmpty()) body.addView(text("从一场面试开始，记录问题与回答。", 16, false));
        for (JSONObject note : notes) {
            LinearLayout card = new LinearLayout(getContext());
            card.setOrientation(LinearLayout.VERTICAL);
            card.setPadding(dp(16), dp(12), dp(16), dp(12));
            GradientDrawable shape = new GradientDrawable();
            shape.setColor(Color.WHITE); shape.setCornerRadius(dp(16));
            shape.setStroke(dp(1), Color.rgb(222, 231, 235));
            card.setBackground(shape);
            TextView heading = text(note.optString("title"), 16, true);
            heading.setMinHeight(dp(48));
            heading.setOnClickListener(v -> actions.edit(note.optString("id")));
            card.addView(heading);
            JSONArray questions = note.optJSONArray("questions");
            String company = note.isNull("company") ? "公司待补充" : note.optString("company", "公司待补充");
            card.addView(text(company + " · " + note.optInt("question_count", questions == null ? 0 : questions.length()) + " 道问题 · " +
                    (note.optBoolean("is_public", true) ? "公开" : "私密"), 14, false));
            LinearLayout buttons = new LinearLayout(getContext());
            Button edit = NativeButtons.make(getContext(), "继续编辑", NativeButtons.Role.TEXT);
            Button export = NativeButtons.make(getContext(), "预览与导出", NativeButtons.Role.TONAL);
            edit.setOnClickListener(v -> actions.edit(note.optString("id")));
            export.setOnClickListener(v -> actions.export(note.optString("id")));
            buttons.addView(edit, new LinearLayout.LayoutParams(0, dp(48), 1));
            buttons.addView(export, new LinearLayout.LayoutParams(0, dp(48), 1));
            card.addView(buttons);
            LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(-1, -2);
            params.bottomMargin = dp(12);
            body.addView(card, params);
        }
        if (currentPage != null && currentPage.optInt("page_count", 1) > 1) {
            LinearLayout pages = new LinearLayout(getContext());
            Button previous = NativeButtons.make(getContext(), "上一页", NativeButtons.Role.TONAL);
            Button next = NativeButtons.make(getContext(), "下一页", NativeButtons.Role.TONAL);
            previous.setEnabled(page > 1);
            next.setEnabled(page < currentPage.optInt("page_count"));
            previous.setOnClickListener(v -> actions.load(query, visibility, order, page - 1));
            next.setOnClickListener(v -> actions.load(query, visibility, order, page + 1));
            pages.addView(previous, new LinearLayout.LayoutParams(0, dp(48), 1));
            pages.addView(next, new LinearLayout.LayoutParams(0, dp(48), 1));
            body.addView(pages);
        }
        post(() -> scrollTo(0, scroll));
    }

    private Spinner choices(String[] labels, int index, String description) {
        Spinner spinner = new Spinner(getContext());
        ArrayAdapter<String> adapter = new ArrayAdapter<>(getContext(), android.R.layout.simple_spinner_item, labels);
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        spinner.setAdapter(adapter);
        spinner.setSelection(Math.max(0, index));
        spinner.setContentDescription(description);
        return spinner;
    }
}
