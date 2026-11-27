package xyz.hunaochufang.awesomeinterview;

import android.content.Context;
import android.content.res.ColorStateList;
import android.annotation.SuppressLint;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.widget.CheckBox;
import android.widget.FrameLayout;
import android.widget.GridLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import org.json.JSONArray;
import org.json.JSONObject;

import java.time.DayOfWeek;
import java.time.LocalDate;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.time.temporal.TemporalAdjusters;
import java.util.ArrayList;
import java.util.List;

/** Native home and calendar backed by the same owner-scoped snapshot as offline reading. */
@SuppressLint("ViewConstructor") // Constructed only in Java with its action callbacks.
final class NativeDashboard extends ScrollView {
    interface Actions {
        void createTodo(LocalDate date);
        void completeTodo(String id, boolean completed);
        void openIdeas();
        void deleteTodo(String id, String title);
        void openEvent(String id);
    }

    private static final int INK = Color.rgb(23, 41, 61);
    private static final int MUTED = Color.rgb(97, 115, 131);
    private static final int TEAL = Color.rgb(0, 139, 143);
    private static final int PALE = Color.rgb(226, 247, 245);
    private static final int LINE = Color.rgb(222, 231, 235);
    private static final String[] WEEKDAYS = {"一", "二", "三", "四", "五", "六", "日"};
    private static final ZoneId ZONE = ZoneId.of("Asia/Shanghai");
    private final Actions actions;
    private final LinearLayout body;
    private JSONObject snapshot;
    private final java.util.Map<LocalDate, List<CalendarProjection.Segment>> segmentsByDay =
            new java.util.LinkedHashMap<>();
    private LocalDate selected = LocalDate.now(ZONE);
    private LocalDate visibleMonth = selected.withDayOfMonth(1);
    private int calendarView;
    private boolean showCompleted;
    private boolean calendar;
    private ScrollView dayScroll;
    private LocalDate shownDay;
    private View currentLine;

    NativeDashboard(Context context, Actions actions) {
        super(context);
        this.actions = actions;
        setFillViewport(true);
        setBackgroundColor(Color.rgb(248, 251, 252));
        setVerticalScrollBarEnabled(false);
        body = new LinearLayout(context);
        body.setOrientation(LinearLayout.VERTICAL);
        addView(body, new LayoutParams(-1, -2));
        render();
    }

    void setSnapshot(JSONObject value) {
        snapshot = value;
        segmentsByDay.clear();
        render();
    }

    void showCalendar(boolean value) {
        calendar = value;
        render();
        scrollTo(0, 0);
    }

    void selectDate(LocalDate day) {
        selected = day;
        visibleMonth = day.withDayOfMonth(1);
        render();
    }

    private int dp(float value) {
        return (int) (value * getResources().getDisplayMetrics().density + .5f);
    }

    private GradientDrawable background(int color, int radius, int stroke) {
        GradientDrawable shape = new GradientDrawable();
        shape.setColor(color);
        shape.setCornerRadius(dp(radius));
        if (stroke != Color.TRANSPARENT) shape.setStroke(dp(1), stroke);
        return shape;
    }

    private TextView text(String value, int size, int color, boolean bold) {
        TextView view = new TextView(getContext());
        view.setText(value);
        view.setTextColor(color);
        view.setTextSize(size);
        if (bold) view.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        view.setGravity(Gravity.CENTER_VERTICAL);
        return view;
    }

    private void render() {
        int dayOffset = dayScroll == null || !selected.equals(shownDay) ? -1 : dayScroll.getScrollY();
        dayScroll = null;
        currentLine = null;
        body.removeAllViews();
        if (calendar) renderCalendar(dayOffset);
        else renderHome();
        if (calendar) {
            long count = items("ideas").stream().filter(item -> item.isNull("arranged_event_id") &&
                    !"abandoned".equals(item.optString("status"))).count();
            TextView ideas = text(count == 0 ? "待安排" : "待安排 · " + count, 14, TEAL, true);
            NativeButtons.style(ideas, NativeButtons.Role.TONAL);
            ideas.setOnClickListener(v -> actions.openIdeas());
            body.addView(ideas, 0, new LinearLayout.LayoutParams(-1, dp(48)));
        }
    }

    private void renderHome() {
        LinearLayout hero = new LinearLayout(getContext());
        hero.setOrientation(LinearLayout.VERTICAL);
        hero.setPadding(dp(20), dp(17), dp(20), dp(18));
        hero.setBackgroundColor(Color.WHITE);
        TextView title = text("安排好每一天", 24, INK, true);
        hero.addView(title);
        hero.addView(text(selected.format(DateTimeFormatter.ofPattern("yyyy 年 M 月 d 日")) +
                "  ·  周" + WEEKDAYS[selected.getDayOfWeek().getValue() - 1], 13,
                MUTED, false));
        addWeekStrip(hero);
        body.addView(hero);

        LinearLayout content = vertical(18, 18, 18, 26);
        body.addView(content);
        LinearLayout heading = new LinearLayout(getContext());
        heading.setGravity(Gravity.CENTER_VERTICAL);
        heading.addView(text("待办事项", 19, INK, true), new LinearLayout.LayoutParams(0, dp(45), 1));
        TextView add = text("＋ 新建", 14, TEAL, true);
        NativeButtons.style(add, NativeButtons.Role.TONAL);
        add.setOnClickListener(v -> actions.createTodo(selected));
        heading.addView(add, new LinearLayout.LayoutParams(-2, dp(48)));
        content.addView(heading);
        TextView hint = text("勾选完成，长按可删除", 12, MUTED, false);
        hint.setPadding(0, 0, 0, dp(10));
        content.addView(hint);
        int count = 0;
        for (JSONObject item : todos()) {
            LocalDate due = date(item.optString("due_date"));
            if (due == null || !(due.equals(selected) ||
                    (selected.equals(LocalDate.now(ZONE)) && due.isBefore(selected) && item.isNull("completed_at")))) continue;
            content.addView(todoRow(item, due.isBefore(selected)));
            count++;
        }
        if (count == 0) content.addView(empty(snapshot == null
                ? "正在同步待办…" : "这一天还没有待办，点击“新建”添加。"));

        TextView agenda = text("当天日程", 19, INK, true);
        agenda.setPadding(0, dp(23), 0, dp(10));
        content.addView(agenda);
        addAgenda(content, selected);
        if (selected.equals(LocalDate.now(ZONE))) addHomeStatus(content);
    }

    private void addHomeStatus(LinearLayout parent) {
        java.time.Instant now = java.time.Instant.now();
        List<JSONObject> upcoming = new ArrayList<>(), overdue = new ArrayList<>(), finished = new ArrayList<>();
        java.util.Set<String> todayIds = new java.util.HashSet<>();
        for (CalendarProjection.Segment segment : segments(selected)) todayIds.add(segment.event.optString("id"));
        for (JSONObject event : events()) {
            if (todayIds.contains(event.optString("id")) || !event.isNull("cancelled_at")) continue;
            JSONArray recurring = event.optJSONArray("repeat_weekdays");
            if (recurring != null && recurring.length() > 0) continue;
            try {
                java.time.OffsetDateTime completion = event.isNull("completed_at")
                        ? null : java.time.OffsetDateTime.parse(event.optString("completed_at"));
                if (completion != null && completion.atZoneSameInstant(ZONE).toLocalDate().equals(selected)) {
                    finished.add(event);
                } else if (completion == null) {
                    var start = java.time.OffsetDateTime.parse(event.optString("start_at")).toInstant();
                    var end = java.time.OffsetDateTime.parse(event.optString("end_at")).toInstant();
                    if (!end.isAfter(now)) overdue.add(event);
                    else if (start.isAfter(now)) upcoming.add(event);
                }
            } catch (java.time.DateTimeException ignored) {}
        }
        addStatusSection(parent, "未完成日程", overdue);
        addStatusSection(parent, "接下来的安排", upcoming);
        addStatusSection(parent, "今日已完成", finished);
    }

    private void addStatusSection(LinearLayout parent, String title, List<JSONObject> events) {
        if (events.isEmpty()) return;
        TextView heading = text(title, 19, INK, true);
        heading.setPadding(0, dp(22), 0, dp(8));
        parent.addView(heading);
        for (JSONObject event : events.subList(0, Math.min(events.size(), 5))) {
            TextView row = text(event.optString("title"), 15, INK, false);
            NativeButtons.style(row, NativeButtons.Role.OUTLINED);
            row.setOnClickListener(view -> actions.openEvent(event.optString("id")));
            parent.addView(row, new LinearLayout.LayoutParams(-1, dp(48)));
        }
    }

    private void addWeekStrip(LinearLayout parent) {
        LinearLayout strip = new LinearLayout(getContext());
        strip.setPadding(0, dp(18), 0, 0);
        LocalDate monday = selected.with(TemporalAdjusters.previousOrSame(DayOfWeek.MONDAY));
        for (int i = 0; i < 7; i++) {
            LocalDate day = monday.plusDays(i);
            TextView cell = text("周" + WEEKDAYS[i] + "\n" + day.getDayOfMonth(), 13,
                    day.equals(selected) ? Color.WHITE : MUTED, day.equals(selected));
            cell.setGravity(Gravity.CENTER);
            cell.setMinHeight(dp(58));
            if (day.equals(selected)) cell.setBackground(background(TEAL, 16, Color.TRANSPARENT));
            cell.setContentDescription(day + (day.equals(selected) ? "，已选择" : "，查看当天安排"));
            cell.setOnClickListener(v -> { selected = day; visibleMonth = day.withDayOfMonth(1); render(); });
            strip.addView(cell, new LinearLayout.LayoutParams(0, dp(58), 1));
        }
        parent.addView(strip);
    }

    private View todoRow(JSONObject item, boolean overdue) {
        LinearLayout row = new LinearLayout(getContext());
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(dp(10), dp(6), dp(8), dp(6));
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(-1, dp(65));
        params.bottomMargin = dp(8);
        row.setLayoutParams(params);
        row.setBackground(background(Color.WHITE, 14, LINE));
        CheckBox check = new CheckBox(getContext());
        check.setButtonTintList(ColorStateList.valueOf(TEAL));
        boolean done = !item.isNull("completed_at");
        check.setChecked(done);
        check.setContentDescription("完成待办：" + item.optString("title"));
        check.setOnClickListener(v -> actions.completeTodo(item.optString("id"), item.isNull("completed_at")));
        row.addView(check, new LinearLayout.LayoutParams(dp(44), dp(48)));
        LinearLayout words = new LinearLayout(getContext());
        words.setOrientation(LinearLayout.VERTICAL);
        TextView label = text(item.optString("title"), 15, done ? MUTED : INK, !done);
        if (done) label.setPaintFlags(label.getPaintFlags() | Paint.STRIKE_THRU_TEXT_FLAG);
        label.setMaxLines(2);
        words.addView(label);
        if (overdue) words.addView(text("已逾期", 11, Color.rgb(188, 83, 57), false));
        if (item.optBoolean("awaiting_sync")) words.addView(text("待同步", 11, TEAL, false));
        row.addView(words, new LinearLayout.LayoutParams(0, -2, 1));
        row.setOnLongClickListener(v -> {
            actions.deleteTodo(item.optString("id"), item.optString("title"));
            return true;
        });
        return row;
    }

    private void renderCalendar(int dayOffset) {
        LinearLayout header = vertical(18, 18, 18, 16);
        header.setBackgroundColor(Color.WHITE);
        LinearLayout title = new LinearLayout(getContext());
        title.setGravity(Gravity.CENTER_VERTICAL);
        TextView back = text("‹", 28, TEAL, false);
        NativeButtons.style(back, NativeButtons.Role.TONAL);
        back.setTextSize(26);
        back.setContentDescription(calendarView == 0 ? "前一天" : calendarView == 1 ? "上一周" : "上个月");
        back.setOnClickListener(v -> movePeriod(-1));
        title.addView(back, new LinearLayout.LayoutParams(dp(48), dp(48)));
        TextView month = text(selected.format(DateTimeFormatter.ofPattern(calendarView == 0 ? "yyyy 年 M 月 d 日" : "yyyy 年 M 月")),
                21, INK, true);
        month.setGravity(Gravity.CENTER);
        month.setContentDescription("选择日期，当前 " + selected);
        month.setOnClickListener(view -> new android.app.DatePickerDialog(getContext(),
                (picker, year, chosenMonth, day) -> selectDate(LocalDate.of(year, chosenMonth + 1, day)),
                selected.getYear(), selected.getMonthValue() - 1, selected.getDayOfMonth()).show());
        title.addView(month, new LinearLayout.LayoutParams(0, dp(48), 1));
        TextView next = text("›", 28, TEAL, false);
        NativeButtons.style(next, NativeButtons.Role.TONAL);
        next.setTextSize(26);
        next.setContentDescription(calendarView == 0 ? "后一天" : calendarView == 1 ? "下一周" : "下个月");
        next.setOnClickListener(v -> movePeriod(1));
        title.addView(next, new LinearLayout.LayoutParams(dp(48), dp(48)));
        header.addView(title);
        LinearLayout switcher = new LinearLayout(getContext());
        switcher.setBackground(background(Color.rgb(236, 244, 245), 12, Color.TRANSPARENT));
        String[] views = {"日视图", "周视图", "月视图"};
        for (int index = 0; index < views.length; index++) {
            int mode = index;
            TextView option = text(views[mode], 14,
                    calendarView == mode ? Color.WHITE : MUTED, calendarView == mode);
            NativeButtons.style(option, calendarView == mode
                    ? NativeButtons.Role.PRIMARY : NativeButtons.Role.TEXT);
            option.setOnClickListener(v -> {calendarView = mode; render(); scrollTo(0, 0);});
            switcher.addView(option, new LinearLayout.LayoutParams(0, dp(48), 1));
        }
        LinearLayout.LayoutParams switchParams = new LinearLayout.LayoutParams(-1, dp(48));
        switchParams.topMargin = dp(12);
        header.addView(switcher, switchParams);
        LinearLayout options = new LinearLayout(getContext());
        TextView today = text("今天", 14, TEAL, true);
        NativeButtons.style(today, NativeButtons.Role.TEXT);
        today.setOnClickListener(view -> {shownDay = null; selectDate(LocalDate.now(ZONE));});
        options.addView(today, new LinearLayout.LayoutParams(dp(72), dp(48)));
        CheckBox completed = new CheckBox(getContext());
        completed.setText("展示已完成");
        completed.setChecked(showCompleted);
        completed.setOnCheckedChangeListener((view, checked) -> {showCompleted = checked; render();});
        options.addView(completed, new LinearLayout.LayoutParams(-2, dp(48)));
        header.addView(options);
        body.addView(header);
        if (calendarView == 0) {
            renderDayTimeline(dayOffset);
            return;
        }
        if (calendarView == 1) {
            renderWeekBoard();
            addSelectedAgenda();
            return;
        }

        SwipeContainer swipe = new SwipeContainer(getContext());
        swipe.setOrientation(LinearLayout.VERTICAL);
        swipe.setBackgroundColor(Color.WHITE);
        swipe.setPadding(dp(8), dp(6), dp(8), dp(12));
        GridLayout weekdays = new GridLayout(getContext());
        weekdays.setColumnCount(7);
        for (int i = 0; i < 7; i++) {
            TextView day = text("周" + WEEKDAYS[i], 12, MUTED, true);
            day.setGravity(Gravity.CENTER);
            GridLayout.LayoutParams spec = new GridLayout.LayoutParams(
                    GridLayout.spec(0), GridLayout.spec(i, 1f));
            spec.width = 0;
            spec.height = dp(32);
            weekdays.addView(day, spec);
        }
        swipe.addView(weekdays);
        GridLayout grid = new GridLayout(getContext());
        grid.setColumnCount(7);
        LocalDate first = visibleMonth.with(TemporalAdjusters.previousOrSame(DayOfWeek.MONDAY));
        for (int i = 0; i < 42; i++) {
            LocalDate day = first.plusDays(i);
            LinearLayout cell = dayCell(day, !day.getMonth().equals(visibleMonth.getMonth()));
            GridLayout.LayoutParams spec = new GridLayout.LayoutParams(
                    GridLayout.spec(i / 7), GridLayout.spec(i % 7, 1f));
            spec.width = 0;
            spec.height = dp(60);
            spec.setMargins(dp(1), dp(1), dp(1), dp(1));
            grid.addView(cell, spec);
        }
        swipe.addView(grid);
        body.addView(swipe);
        addSelectedAgenda();
    }

    private void renderWeekBoard() {
        android.widget.HorizontalScrollView scroller = new android.widget.HorizontalScrollView(getContext());
        scroller.setHorizontalScrollBarEnabled(true);
        scroller.setContentDescription("本周安排，左右滑动查看更多日期");
        LinearLayout days = new LinearLayout(getContext());
        days.setOrientation(LinearLayout.HORIZONTAL);
        scroller.addView(days);
        LocalDate monday = selected.with(TemporalAdjusters.previousOrSame(DayOfWeek.MONDAY));
        List<List<CalendarProjection.Segment>> week = new ArrayList<>(7);
        java.util.LinkedHashSet<String> recurringRows = new java.util.LinkedHashSet<>();
        for (int index = 0; index < 7; index++) {
            List<CalendarProjection.Segment> daily = segments(monday.plusDays(index));
            week.add(daily);
            for (CalendarProjection.Segment item : daily)
                if (item.before || item.after) recurringRows.add(item.event.optString("id"));
        }
        for (int index = 0; index < 7; index++) {
            LocalDate date = monday.plusDays(index);
            LinearLayout column = vertical(4, 4, 4, 4);
            days.addView(column, new LinearLayout.LayoutParams(dp(120), -2));
            TextView title = text("周" + WEEKDAYS[index] + "  " + date.getDayOfMonth(), 14, INK, true);
            NativeButtons.style(title, date.equals(selected) ? NativeButtons.Role.TONAL : NativeButtons.Role.TEXT);
            title.setOnClickListener(view -> selectDate(date));
            column.addView(title, new LinearLayout.LayoutParams(-1, dp(48)));
            List<CalendarProjection.Segment> daily = week.get(index);
            for (String id : recurringRows) {
                CalendarProjection.Segment item = null;
                for (CalendarProjection.Segment segment : daily)
                    if (id.equals(segment.event.optString("id"))) {item = segment; break;}
                if (item == null) column.addView(new View(getContext()), new LinearLayout.LayoutParams(-1, dp(56)));
                else addWeekCard(column, item);
            }
            for (CalendarProjection.Segment item : daily)
                if (!recurringRows.contains(item.event.optString("id"))) addWeekCard(column, item);
        }
        body.addView(scroller);
        scroller.post(() -> scroller.scrollTo(Math.max(0, (selected.getDayOfWeek().getValue() - 2) * dp(120)), 0));
    }

    private void addWeekCard(LinearLayout column, CalendarProjection.Segment item) {
        String title = item.event.optString("title");
        String caption = (item.before ? "‹ " : "") + title + (item.after ? " ›" : "");
        TextView card = text(caption, 13, INK, false);
        card.setMaxLines(2);
        card.setEllipsize(android.text.TextUtils.TruncateAt.END);
        card.setContentDescription(title + "，" + formatTime(item.startMinute) + "至" + formatTime(item.endMinute));
        card.setPadding(dp(8), 0, dp(8), 0);
        card.setBackground(background(PALE, 8, LINE));
        card.setOnClickListener(view -> actions.openEvent(item.event.optString("id")));
        LinearLayout.LayoutParams position = new LinearLayout.LayoutParams(-1, dp(52));
        position.bottomMargin = dp(4);
        column.addView(card, position);
    }

    private void addSelectedAgenda() {
        LinearLayout agenda = vertical(18, 20, 18, 30);
        TextView heading = text(selected.format(DateTimeFormatter.ofPattern("M 月 d 日")) + " · 当天安排", 18, INK, true);
        agenda.addView(heading);
        addAgenda(agenda, selected);
        body.addView(agenda);
    }

    private LinearLayout dayCell(LocalDate day, boolean outside) {
        LinearLayout cell = new LinearLayout(getContext());
        cell.setOrientation(LinearLayout.VERTICAL);
        cell.setPadding(dp(3), dp(4), dp(2), 0);
        cell.setBackground(background(day.equals(selected) ? PALE : Color.WHITE, 5,
                day.equals(selected) ? TEAL : LINE));
        TextView number = text(Integer.toString(day.getDayOfMonth()), 14,
                outside ? Color.rgb(171, 184, 190) : day.equals(selected) ? TEAL : INK,
                day.equals(selected));
        cell.addView(number);
        int events = 0;
        for (CalendarProjection.Segment segment : segments(day)) {
            JSONObject item = segment.event;
            events++;
            if (events == 1) {
                String prefix = completed(item) ? "✓ " : segment.before ? "↖ " : "";
                TextView label = text(prefix + item.optString("title"), 10, eventColor(item.optString("type")), false);
                label.setSingleLine(true);
                label.setEllipsize(android.text.TextUtils.TruncateAt.END);
                label.setContentDescription(item.optString("title") + (completed(item) ? "，已完成" : ""));
                label.setPadding(dp(2), 0, dp(2), 0);
                label.setBackground(background(Color.rgb(235, 241, 245), 3, Color.TRANSPARENT));
                LinearLayout.LayoutParams layout = new LinearLayout.LayoutParams(-1, dp(19));
                layout.topMargin = dp(3);
                cell.addView(label, layout);
            }
        }
        if (events > 1) cell.addView(text("+" + (events - 1) + " 项", 10, TEAL, true));
        cell.setContentDescription(day + "，" + events + " 项日程，点击查看当天安排");
        cell.setOnClickListener(v -> {
            selected = day;
            visibleMonth = day.withDayOfMonth(1);
            render();
        });
        return cell;
    }

    private void addAgenda(LinearLayout parent, LocalDate day) {
        int count = 0;
        for (CalendarProjection.Segment segment : segments(day)) {
            JSONObject item = segment.event;
            count++;
            LinearLayout line = new LinearLayout(getContext());
            line.setGravity(Gravity.CENTER_VERTICAL);
            line.setPadding(dp(12), dp(11), dp(12), dp(11));
            LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(-1, -2);
            params.topMargin = dp(8);
            line.setLayoutParams(params);
            line.setBackground(background(Color.WHITE, 12, LINE));
            View stripe = new View(getContext());
            stripe.setBackground(background(eventColor(item.optString("type")), 4, Color.TRANSPARENT));
            line.addView(stripe, new LinearLayout.LayoutParams(dp(4), dp(38)));
            LinearLayout words = new LinearLayout(getContext());
            words.setPadding(dp(12), 0, 0, 0);
            words.setOrientation(LinearLayout.VERTICAL);
            String status = item.optBoolean("awaiting_sync") ? " · 待同步" : !item.isNull("cancelled_at") ? " · 已废除" : "";
            TextView title = text((completed(item) ? "✓ " : "") + item.optString("title") + status, 15,
                    completed(item) ? MUTED : INK, true);
            words.addView(title);
            String when = String.format(java.util.Locale.ROOT, "%02d:%02d–%02d:%02d",
                    segment.startMinute / 60, segment.startMinute % 60, segment.endMinute / 60, segment.endMinute % 60);
            if (segment.before) when += " · 从前一天延续";
            if (segment.after) when += " · 延续到次日";
            words.addView(text(when, 12, MUTED, false));
            line.addView(words, new LinearLayout.LayoutParams(0, -2, 1));
            line.setOnClickListener(v -> actions.openEvent(item.optString("id")));
            parent.addView(line);
        }
        if (count == 0) parent.addView(empty(snapshot == null
                ? "正在同步日程…" : "这一天没有日程。"));
    }

    private void renderDayTimeline(int previousOffset) {
        List<CalendarProjection.Segment> day = segments(selected);
        SwipeContainer timeline = new SwipeContainer(getContext());
        timeline.setOrientation(LinearLayout.VERTICAL);
        timeline.setPadding(dp(12), 0, dp(12), dp(16));
        boolean first = true;
        for (CalendarProjection.Segment item : day) {
            if (!item.before && !item.after) continue;
            if (first) {timeline.addView(text("跨天安排", 14, MUTED, true)); first = false;}
            TextView summary = text((item.before ? "← " : "") + item.event.optString("title") +
                    (item.after ? " →" : ""), 14, INK, false);
            NativeButtons.style(summary, NativeButtons.Role.OUTLINED);
            summary.setOnClickListener(view -> actions.openEvent(item.event.optString("id")));
            timeline.addView(summary, new LinearLayout.LayoutParams(-1, dp(48)));
        }
        FrameLayout axes = new FrameLayout(getContext());
        axes.setBackgroundColor(Color.WHITE);
        ScrollView hours = new ScrollView(getContext()) {
            private float startX, startY;

            @Override public boolean dispatchTouchEvent(MotionEvent event) {
                if (event.getActionMasked() == MotionEvent.ACTION_DOWN) {
                    startX = event.getX();
                    startY = event.getY();
                } else if (event.getActionMasked() == MotionEvent.ACTION_MOVE &&
                        Math.abs(event.getY() - startY) > Math.abs(event.getX() - startX)) {
                    getParent().requestDisallowInterceptTouchEvent(true);
                }
                return super.dispatchTouchEvent(event);
            }
        };
        hours.setNestedScrollingEnabled(true);
        hours.addView(axes, new ScrollView.LayoutParams(-1, dp(24 * 54 + 20)));
        timeline.addView(hours, new LinearLayout.LayoutParams(-1, dp(345)));
        dayScroll = hours;
        shownDay = selected;
        for (int hour = 0; hour <= 24; hour++) {
            TextView time = text(String.format(java.util.Locale.ROOT, "%02d:00", hour), 12, MUTED, false);
            FrameLayout.LayoutParams hourLabel = new FrameLayout.LayoutParams(dp(45), dp(20));
            hourLabel.topMargin = dp(hour * 54);
            axes.addView(time, hourLabel);
            View rule = new View(getContext());
            rule.setBackgroundColor(LINE);
            FrameLayout.LayoutParams ruleLayout = new FrameLayout.LayoutParams(-1, dp(1));
            ruleLayout.leftMargin = dp(45);
            ruleLayout.topMargin = dp(hour * 54 + 9);
            axes.addView(rule, ruleLayout);
        }
        List<Integer> laneEnds = new ArrayList<>();
        List<Integer> lanes = new ArrayList<>();
        List<View> cards = new ArrayList<>();
        for (CalendarProjection.Segment item : day) {
            int lane = 0;
            int start = timelinePosition(item.startMinute);
            while (lane < laneEnds.size() && laneEnds.get(lane) > start) lane++;
            int end = Math.max(start + dp(34), timelinePosition(item.endMinute));
            if (lane == laneEnds.size()) laneEnds.add(end);
            else laneEnds.set(lane, end);
            lanes.add(lane);
        }
        axes.addOnLayoutChangeListener((view, left, top, right, bottom, oldLeft, oldTop, oldRight, oldBottom) -> {
            if (right - left == oldRight - oldLeft || laneEnds.isEmpty()) return;
            int width = Math.max(1, (right - left - dp(48)) / laneEnds.size());
            for (int index = 0; index < day.size(); index++) {
                View card = cards.get(index);
                FrameLayout.LayoutParams params = (FrameLayout.LayoutParams) card.getLayoutParams();
                params.width = Math.max(1, width - dp(4));
                params.leftMargin = dp(48) + width * lanes.get(index);
                card.setLayoutParams(params);
            }
        });
        for (CalendarProjection.Segment item : day) {
            TextView event = text(item.event.optString("title"), 13, INK, true);
            event.setPadding(dp(6), 0, dp(6), 0);
            event.setBackground(background(PALE, 6, eventColor(item.event.optString("type"))));
            event.setOnClickListener(view -> actions.openEvent(item.event.optString("id")));
            event.setContentDescription(item.event.optString("title") + "，" + formatTime(item.startMinute) + "至" + formatTime(item.endMinute));
            int start = timelinePosition(item.startMinute);
            FrameLayout.LayoutParams position = new FrameLayout.LayoutParams(dp(48),
                    Math.max(dp(34), timelinePosition(item.endMinute) - start));
            position.topMargin = start;
            axes.addView(event, position);
            cards.add(event);
        }
        if (selected.equals(LocalDate.now(ZONE))) {
            var time = java.time.LocalTime.now(ZONE);
            View now = new View(getContext());
            now.setBackgroundColor(Color.rgb(192, 63, 77));
            now.setContentDescription("当前时间 " + formatTime(time.getHour() * 60 + time.getMinute()));
            FrameLayout.LayoutParams position = new FrameLayout.LayoutParams(-1, dp(2));
            position.leftMargin = dp(45);
            position.topMargin = timelinePosition(time.getHour() * 60 + time.getMinute());
            axes.addView(now, position);
            currentLine = now;
        }
        body.addView(timeline);
        int initialMinute = selected.equals(LocalDate.now(ZONE))
                ? Math.max(0, minuteOfDay() - 95)
                : day.isEmpty() ? 8 * 60 : Math.max(0, day.get(0).startMinute - 30);
        hours.post(() -> hours.scrollTo(0, previousOffset < 0 ? timelinePosition(initialMinute) - dp(9) : previousOffset));
    }

    private int timelinePosition(int minute) {return dp(9 + minute * 54f / 60);}

    private int minuteOfDay() {
        java.time.LocalTime now = java.time.LocalTime.now(ZONE);
        return now.getHour() * 60 + now.getMinute();
    }

    private final Runnable tickTime = new Runnable() {
        @Override public void run() {
            if (currentLine != null) {
                var position = (FrameLayout.LayoutParams) currentLine.getLayoutParams();
                int minute = minuteOfDay();
                position.topMargin = timelinePosition(minute);
                currentLine.setLayoutParams(position);
                currentLine.setContentDescription("当前时间 " + formatTime(minute));
            }
            postDelayed(this, 60_000 - System.currentTimeMillis() % 60_000);
        }
    };

    @Override protected void onAttachedToWindow() {super.onAttachedToWindow(); post(tickTime);}

    @Override protected void onDetachedFromWindow() {removeCallbacks(tickTime); super.onDetachedFromWindow();}

    private String formatTime(int minute) {
        return String.format(java.util.Locale.ROOT, "%02d:%02d", minute / 60, minute % 60);
    }

    private TextView empty(String message) {
        TextView view = text(message, 14, MUTED, false);
        view.setPadding(dp(14), dp(16), dp(14), dp(16));
        view.setBackground(background(Color.WHITE, 12, LINE));
        return view;
    }

    private LinearLayout vertical(int left, int top, int right, int bottom) {
        LinearLayout view = new LinearLayout(getContext());
        view.setOrientation(LinearLayout.VERTICAL);
        view.setPadding(dp(left), dp(top), dp(right), dp(bottom));
        return view;
    }

    private void movePeriod(int direction) {
        if (calendarView == 0) selected = selected.plusDays(direction);
        else if (calendarView == 1) selected = selected.plusWeeks(direction);
        else selected = visibleMonth.plusMonths(direction).withDayOfMonth(1);
        visibleMonth = selected.withDayOfMonth(1);
        render();
    }

    private List<JSONObject> events() {
        List<JSONObject> result = items("events");
        result.sort((left, right) -> left.optString("start_at").compareTo(right.optString("start_at")));
        return result;
    }
    private List<JSONObject> todos() { return items("todos"); }

    private List<JSONObject> items(String key) {
        List<JSONObject> result = new ArrayList<>();
        JSONArray array = snapshot == null ? null : snapshot.optJSONArray(key);
        if (array != null) for (int i = 0; i < array.length(); i++) {
            JSONObject item = array.optJSONObject(i);
            if (item != null) result.add(item);
        }
        return result;
    }

    private static LocalDate date(String value) {
        try { return LocalDate.parse(value); } catch (Exception ignored) { return null; }
    }

    private List<CalendarProjection.Segment> segments(LocalDate day) {
        if (segmentsByDay.size() >= 90) segmentsByDay.clear();
        List<CalendarProjection.Segment> result = segmentsByDay.computeIfAbsent(day, date -> {
            JSONObject profile = snapshot == null ? null : snapshot.optJSONObject("profile");
            return CalendarProjection.forDay(events(), date, workTime(profile, "workday_start", "08:00"),
                    workTime(profile, "workday_end", "18:00"));
        });
        if (!calendar || showCompleted) return result;
        List<CalendarProjection.Segment> active = new ArrayList<>(result);
        active.removeIf(segment -> completed(segment.event));
        return active;
    }

    static java.time.LocalTime workTime(JSONObject profile, String field, String fallback) {
        try { return java.time.LocalTime.parse(profile == null ? fallback : profile.optString(field, fallback)); }
        catch (java.time.DateTimeException error) { return java.time.LocalTime.parse(fallback); }
    }

    private static boolean completed(JSONObject item) {
        return !item.isNull("completed_at") && !item.optString("completed_at").isEmpty();
    }

    private static int eventColor(String type) {
        switch (type) {
            case "interview": return Color.rgb(70, 99, 218);
            case "exam": return Color.rgb(202, 100, 62);
            case "course": return Color.rgb(124, 91, 176);
            case "meeting": return Color.rgb(0, 139, 143);
            default: return Color.rgb(91, 112, 128);
        }
    }

    @SuppressLint("ViewConstructor")
    private final class SwipeContainer extends LinearLayout {
        private float downX;
        private float downY;

        SwipeContainer(Context context) {
            super(context);
            setClickable(true);
            setContentDescription("左右滑动切换日期");
        }

        @Override public boolean onInterceptTouchEvent(MotionEvent event) {
            if (event.getActionMasked() == MotionEvent.ACTION_DOWN) {
                downX = event.getX();
                downY = event.getY();
            } else if (event.getActionMasked() == MotionEvent.ACTION_MOVE &&
                    Math.abs(event.getX() - downX) > dp(25) &&
                    Math.abs(event.getX() - downX) > Math.abs(event.getY() - downY) * 1.2f) {
                return true;
            }
            return super.onInterceptTouchEvent(event);
        }

        @Override public boolean onTouchEvent(MotionEvent event) {
            if (event.getActionMasked() == MotionEvent.ACTION_UP) {
                float dx = event.getX() - downX;
                if (Math.abs(dx) > dp(60)) {
                    post(() -> movePeriod(dx < 0 ? 1 : -1));
                    return true;
                }
                if (Math.abs(dx) < dp(10) && Math.abs(event.getY() - downY) < dp(10))
                    return performClick();
                return true;
            }
            return true;
        }

        @Override public boolean performClick() {
            super.performClick();
            return true;
        }
    }
}
