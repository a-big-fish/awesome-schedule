package xyz.hunaochufang.awesomeinterview;

import android.Manifest;
import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.AlarmManager;
import android.app.AlertDialog;
import android.app.KeyguardManager;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Bitmap;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.RippleDrawable;
import android.content.res.ColorStateList;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.graphics.Typeface;
import android.view.Gravity;
import android.view.View;
import android.view.WindowInsets;
import android.webkit.CookieManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;
import android.window.OnBackInvokedDispatcher;
import android.text.method.ScrollingMovementMethod;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.time.OffsetDateTime;
import java.time.LocalDate;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

public final class MainActivity extends Activity {
    @Override protected void attachBaseContext(android.content.Context base) {
        // The current product provides Chinese UI; localize native dialogs as well.
        android.content.res.Configuration config = new android.content.res.Configuration(base.getResources().getConfiguration());
        config.setLocales(new android.os.LocaleList(java.util.Locale.SIMPLIFIED_CHINESE));
        super.attachBaseContext(base.createConfigurationContext(config));
    }
    private static final String ORIGIN = BuildConfig.SERVER_ORIGIN;
    private static final String BASE = "/my-schedule";
    private static final int UNLOCK_REQUEST = 11;
    private static final int NOTICE_REQUEST = 12;
    private static final int DOWNLOAD_REQUEST = 13;
    private static final int PAGE_IMAGE_REQUEST = 14;
    private static final int EXPORT_PDF_REQUEST = 15;
    private static final int NATIVE_UPLOAD_REQUEST = 16;

    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final ExecutorService localWrites = Executors.newSingleThreadExecutor();
    private final Object nativeStateLock = new Object();
    private final NativePerformance performance = new NativePerformance(ORIGIN);
    private TextView offline;
    private LinearLayout navigation;
    private NativeDashboard dashboard;
    private NativeEventScreen eventScreen;
    private NativeExportScreen exportScreen;
    private NativeExperienceList experienceList;
    private NativeWorkspace workspace;
    private NativeResume resume;
    private String nativeUploadPath;
    private java.util.function.Consumer<JSONObject> nativeUploadResult;
    private boolean administrator;
    private int workspaceGeneration;
    private boolean workspaceBusy;
    private int experienceGeneration;
    private String exportExperienceId;
    private int exportGeneration;
    private final TextView[] tabs = new TextView[5];
    private volatile JSONObject currentSnapshot;
    private String sessionCsrf;
    private String sessionCookie;
    private String sessionOwner;
    private AlertDialog authDialog;
    private int activeTab;
    private boolean initialHomePending = true;
    private Button retry;
    private String downloadUrl;
    private long lastSyncAttempt;
    private final AtomicBoolean snapshotSyncQueued = new AtomicBoolean();
    private final AtomicBoolean snapshotRefreshPending = new AtomicBoolean();
    private final AtomicBoolean fullNativeRefresh = new AtomicBoolean();
    private final AtomicBoolean resumePendingNow = new AtomicBoolean();
    private boolean offlineUnlockOffered;
    private boolean offlineVisible;
    private String pendingEventId;
    private String failedEventOperation;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().setStatusBarColor(Color.rgb(246, 248, 252));
        getWindow().setNavigationBarColor(Color.WHITE);
        getWindow().getDecorView().setSystemUiVisibility(
                View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR | View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR);
        if (Build.VERSION.SDK_INT >= 30) getWindow().setDecorFitsSystemWindows(false);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(Color.WHITE);
        root.setOnApplyWindowInsetsListener((view, insets) -> {
            int top;
            int bottom;
            if (Build.VERSION.SDK_INT >= 30) {
                android.graphics.Insets bars = insets.getInsets(
                        WindowInsets.Type.statusBars() | WindowInsets.Type.navigationBars() |
                                WindowInsets.Type.displayCutout());
                top = bars.top;
                bottom = Math.max(bars.bottom, insets.getInsets(WindowInsets.Type.ime()).bottom);
                if (eventScreen != null && navigation != null && eventScreen.getVisibility() == View.VISIBLE)
                    navigation.setVisibility(insets.isVisible(WindowInsets.Type.ime()) ? View.GONE : View.VISIBLE);
            } else {
                // Before API 30 the decor already fits content inside system bars.
                top = 0;
                bottom = 0;
            }
            view.setPadding(0, top, 0, bottom);
            return insets;
        });
        setContentView(root);
        LinearLayout top = new LinearLayout(this);
        top.setGravity(Gravity.CENTER_VERTICAL);
        top.setPadding(dp(16), 0, dp(12), 0);
        top.setBackgroundColor(Color.WHITE);
        top.setElevation(0);
        TextView brand = new TextView(this);
        brand.setText(R.string.app_name);
        brand.setTextColor(Color.rgb(18, 33, 64));
        brand.setTextSize(16);
        brand.setSingleLine(true);
        brand.setEllipsize(android.text.TextUtils.TruncateAt.END);
        brand.setTypeface(null, Typeface.BOLD);
        brand.setOnClickListener(v -> new AlertDialog.Builder(this)
                .setTitle("前往")
                .setItems(administrator ? new String[]{"面经广场", "我的面试", "个人设置", "管理员看板"} :
                                new String[]{"面经广场", "我的面试", "个人设置"},
                        (dialog, which) -> {
                            if (which == 0) workspace.plaza("", "newest", 1);
                            else if (which == 1) workspace.interviews(currentSnapshot,
                                    LocalDate.now(java.time.ZoneId.of("Asia/Shanghai")));
                            else if (which == 2) workspace.account();
                            else workspace.admin("all");
                        }).show());
        top.addView(brand, new LinearLayout.LayoutParams(0, dp(54), 1));
        android.widget.ImageButton more = new android.widget.ImageButton(this);
        more.setImageResource(R.drawable.nav_more);
        more.setBackground(NativeButtons.make(this, "", NativeButtons.Role.TEXT).getBackground());
        more.setPadding(dp(12), dp(12), dp(12), dp(12));
        more.setContentDescription("更多选项：本机同步状态、退出登录");
        more.setOnClickListener(v -> new AlertDialog.Builder(this).setTitle("更多选项")
                .setItems(new String[]{"本机与同步", "待同步修改", "退出登录"}, (dialog, which) -> {
                    if (which == 0) showDeviceStatus();
                    else if (which == 1) showPendingChanges();
                    else confirmLogout();
                }).show());
        top.addView(more, new LinearLayout.LayoutParams(dp(48), dp(48)));
        root.addView(top);
        FrameLayout content = new FrameLayout(this);
        root.addView(content, new LinearLayout.LayoutParams(-1, 0, 1));
        CookieManager.getInstance().setAcceptCookie(true);
        dashboard = new NativeDashboard(this, new NativeDashboard.Actions() {
            @Override public void createTodo(LocalDate date) { promptCreateTodo(date); }
            @Override public void completeTodo(String id, boolean completed) { writeTodo("completion", id, null, null, completed); }
            @Override public void openIdeas() { showIdeas(false); }
            @Override public void deleteTodo(String id, String title) {
                new AlertDialog.Builder(MainActivity.this)
                        .setTitle("删除待办？")
                        .setMessage(title)
                        .setNegativeButton("取消", null)
                        .setPositiveButton("删除", (dialog, which) -> writeTodo("delete", id, null, null, false))
                        .show();
            }
            @Override public void openEvent(String id) {
                activeTab = 2;
                updateNavigation();
                showEventFromSnapshot(id);
            }
        });
        dashboard.setVisibility(View.GONE);
        content.addView(dashboard, new FrameLayout.LayoutParams(-1, -1));
        eventScreen = new NativeEventScreen(this, new NativeEventScreen.Actions() {
            @Override public void openSeries(String id) { showEventFromSnapshot(id); }
            @Override public void save(String id, JSONObject payload) { writeEvent(id, payload); }
            @Override public void parse(String rawText) { parseInvitation(rawText); }
            @Override public void delete(String id) { confirmDeleteEvent(id); }
            @Override public void complete(String id, boolean completed) {
                try { writeEventRequest("COMPLETE", id, new JSONObject().put("completed", completed)); }
                catch (Exception ignored) { }
            }
            @Override public void resolve(String id, String action, java.time.ZonedDateTime end) {
                JSONObject snapshot = currentSnapshot;
                if (snapshot == null) return;
                JSONObject command = NativeWorkspace.object("action", action, "event_id", id);
                if (end != null) try {command.put("end_at", end.toOffsetDateTime().toString());}
                catch (org.json.JSONException error) {throw new IllegalStateException(error);}
                sendCalendarCommand(snapshot.optString("user_id"), java.util.UUID.randomUUID().toString(), command, id);
            }
            @Override public void openResume() {resume.open(() -> pickNativeFile(BASE + "/resume", resume::updated));}
            @Override public void back() {
                failedEventOperation = null;
                activeTab = 1;
                updateNavigation();
                showNative(true);
            }
            @Override public java.time.LocalTime workTime(boolean starting) {
                JSONObject snapshot = currentSnapshot;
                JSONObject profile = snapshot == null ? null : snapshot.optJSONObject("profile");
                return NativeDashboard.workTime(profile, starting ? "workday_start" : "workday_end",
                        starting ? "08:00" : "18:00");
            }
        });
        eventScreen.setVisibility(View.GONE);
        content.addView(eventScreen, new FrameLayout.LayoutParams(-1, -1));
        exportScreen = new NativeExportScreen(this, new NativeExportScreen.Actions() {
            @Override public void refresh() { loadExportPdf(); }
            @Override public void savePage() { saveExportPage(); }
            @Override public void savePdf() { saveExportPdf(); }
            @Override public void back() { leaveExport(); }
        });
        exportScreen.setVisibility(View.GONE);
        content.addView(exportScreen, new FrameLayout.LayoutParams(-1, -1));
        resume = new NativeResume(this, executor, ORIGIN, () -> currentSnapshot);
        workspace = new NativeWorkspace(this, new NativeWorkspace.Actions() {
            @Override public void request(String method, String path, Object body,
                                          java.util.function.Consumer<JSONObject> received) {
                workspaceRequest(method, path, body, received);
            }
            @Override public void openEvent(String id) {showEventFromSnapshot(id);}
            @Override public void openResume() {resume.open(() -> pickNativeFile(BASE + "/resume", resume::updated));}
            @Override public void exportPdf(String id, String title) {showNativeExport(id);}
            @Override public void exportMarkdown(String id, String title) {
                downloadUrl = ORIGIN + NativeWorkspace.EXPERIENCES + "/" + Uri.encode(id) + "/export.md";
                Intent save = new Intent(Intent.ACTION_CREATE_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE)
                        .setType("text/markdown").putExtra(Intent.EXTRA_TITLE, title + ".md");
                startActivityForResult(save, DOWNLOAD_REQUEST);
            }
            @Override public void shareMarkdown(String id, String title) {shareNativeMarkdown(id, title);}
            @Override public JSONObject snapshot() {return currentSnapshot;}
            @Override public void importFile(String path, java.util.function.Consumer<JSONObject> received) {
                pickNativeFile(path, received);
            }
            @Override public void experiencesChanged() {experienceList.reload();}
            @Override public void profileChanged(JSONObject profile) {
                executor.execute(() -> {
                    JSONObject base = currentSnapshot;
                    if (base == null || !base.optString("user_id").equals(profile.optString("user_id"))) return;
                    try {
                        synchronized (nativeStateLock) {
                            JSONObject current = currentSnapshot;
                            if (current == null || !base.optString("user_id").equals(current.optString("user_id"))) return;
                            persistNative(new JSONObject(current.toString()).put("profile", profile));
                        }
                    } catch (Exception error) {syncSnapshot(true);}
                });
            }
        });
        experienceList = new NativeExperienceList(this, new NativeExperienceList.Actions() {
            @Override public void edit(String id) { workspace.experience(id); }
            @Override public void export(String id) { showNativeExport(id); }
            @Override public void create() { workspace.editExperience(null); }
            @Override public void plaza() { workspace.plaza("", "newest", 1); }
            @Override public void load(String query, String visibility, String order, int page) {
                loadExperiencePage(query, visibility, order, page);
            }
        });
        experienceList.setVisibility(View.GONE);
        content.addView(experienceList, new FrameLayout.LayoutParams(-1, -1));
        offline = new TextView(this);
        offline.setTextColor(Color.rgb(25, 40, 70));
        offline.setTextSize(15);
        offline.setPadding(24, 24, 24, 24);
        offline.setMovementMethod(ScrollingMovementMethod.getInstance());
        offline.setVisibility(View.GONE);
        content.addView(offline, new FrameLayout.LayoutParams(-1, -1));
        retry = NativeButtons.make(this, "重试连接", NativeButtons.Role.PRIMARY);
        retry.setVisibility(View.GONE);
        retry.setOnClickListener(v -> retryConnection());
        root.addView(retry);
        navigation = new LinearLayout(this);
        navigation.setGravity(Gravity.CENTER);
        navigation.setPadding(dp(8), dp(5), dp(8), dp(5));
        navigation.setBackgroundColor(Color.WHITE);
        navigation.setElevation(dp(8));
        String[] labels = {"首页", "日历", "添加", "面经", "我的"};
        int[] icons = {R.drawable.nav_home, R.drawable.nav_calendar, R.drawable.nav_add,
                R.drawable.nav_notes, R.drawable.nav_profile};
        for (int i = 0; i < labels.length; i++) {
            final int index = i;
            TextView tab = new TextView(this);
            tab.setText(labels[i]);
            tab.setTextSize(11);
            tab.setGravity(Gravity.CENTER);
            tab.setCompoundDrawablesWithIntrinsicBounds(0, icons[i], 0, 0);
            tab.setCompoundDrawablePadding(dp(4));
            tab.setMinHeight(dp(58));
            tab.setClickable(true);
            tab.setFocusable(true);
            tab.setOnClickListener(v -> selectTab(index));
            tabs[i] = tab;
            LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(0, dp(58), 1);
            params.setMargins(dp(2), 0, dp(2), 0);
            navigation.addView(tab, params);
        }
        updateNavigation();
        navigation.setVisibility(View.GONE);
        root.addView(navigation);
        if (Build.VERSION.SDK_INT >= 33) {
            getOnBackInvokedDispatcher().registerOnBackInvokedCallback(
                    OnBackInvokedDispatcher.PRIORITY_DEFAULT, this::handleBack);
        }
        String eventId = getIntent().getStringExtra("event_id");
        if (eventId != null) {
            initialHomePending = false;
            activeTab = 1;
            pendingEventId = eventId;
            updateNavigation();
        }
        if (BuildConfig.DEBUG && showAcceptanceFixture(getIntent().getStringExtra("acceptance_screen"))) return;
        initialHomePending = false;
        if (!hasSessionCookie(CookieManager.getInstance().getCookie(ORIGIN))) showNativeAuth("login");
        else {
            showNative(false);
            syncSnapshot(true);
        }
    }

    private void showNativeAuth(String mode) {
        if (authDialog != null) authDialog.dismiss();
        boolean register = mode.equals("register"), forgot = mode.equals("forgot-password");
        boolean reset = mode.equals("reset-password");
        LinearLayout fields = new LinearLayout(this);
        fields.setOrientation(LinearLayout.VERTICAL);
        fields.setPadding(dp(20), dp(8), dp(20), 0);
        EditText identifier = new EditText(this), password = new EditText(this), confirm = new EditText(this), invitation = new EditText(this);
        identifier.setSingleLine(true);
        identifier.setHint(reset ? "邮件中的重置链接或令牌" : register || forgot ? "邮箱" : "邮箱或用户名");
        fields.addView(identifier);
        if (!forgot) {
            password.setHint("密码");
            password.setInputType(129);
            fields.addView(password);
        }
        if (register || reset) {
            confirm.setHint("再次输入密码");
            confirm.setInputType(129);
            fields.addView(confirm);
        }
        if (register) {
            invitation.setHint("邀请码（按站点要求填写）");
            fields.addView(invitation);
        }
        if (!register && !forgot && !reset) {
            android.widget.Button recovery = NativeButtons.make(this, "忘记密码", NativeButtons.Role.TEXT);
            recovery.setOnClickListener(v -> showNativeAuth("forgot-password"));
            fields.addView(recovery);
        }
        if (forgot) {
            Button link = NativeButtons.make(this, "我已收到邮件", NativeButtons.Role.TEXT);
            link.setOnClickListener(view -> showNativeAuth("reset-password"));
            fields.addView(link);
        }
        boolean signingIn = !register && !forgot && !reset;
        AlertDialog form = new AlertDialog.Builder(this).setTitle(register ? "创建账号" : forgot ? "找回密码" : reset ? "重置密码" : "登录")
                .setView(fields).setNegativeButton("取消", null)
                .setPositiveButton(register ? "注册" : forgot ? "发送邮件" : reset ? "保存新密码" : "登录", null)
                .setNeutralButton(signingIn ? "注册" : "返回登录", (view, button) -> showNativeAuth(signingIn ? "register" : "login"))
                .create();
        authDialog = form;
        form.setOnDismissListener(dialog -> {if (authDialog == form) authDialog = null;});
        form.setOnShowListener(v -> form.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(button -> {
            String name = identifier.getText().toString().trim();
            if (name.isEmpty()) {identifier.setError(reset ? "请输入重置链接或令牌" : "请填写账号"); return;}
            String token = reset && name.contains("://") && Uri.parse(name).isHierarchical()
                    ? Uri.parse(name).getQueryParameter("token") : name;
            if (reset && (token == null || !token.matches("[A-Za-z0-9_-]+"))) {
                identifier.setError("重置链接或令牌格式无效");
                return;
            }
            String secret = password.getText().toString();
            String confirmation = confirm.getText().toString();
            String invite = invitation.getText().toString();
            if ((register || reset) && !secret.equals(confirmation)) {confirm.setError("两次密码不一致"); return;}
            button.setEnabled(false);
            executor.execute(() -> {
                try {
                    JSONObject config = authForm("/auth/form-config", null);
                    String data = "csrf_token=" + formValue(config.getString("csrf_token")) + "&" +
                            (reset ? "token=" : register || forgot ? "email=" : "identifier=") + formValue(token);
                    if (!forgot) data += "&password=" + formValue(secret);
                    if (register || reset) data += "&password_confirm=" + formValue(confirmation);
                    if (register) data += "&invite_code=" + formValue(invite);
                    JSONObject reply = authForm("/auth/" + mode, data);
                    if (reply.has("next")) {
                        sessionCsrf = null;
                        runOnUiThread(() -> {
                            form.dismiss();
                            showNative(false);
                            resumePendingNow.set(true);
                            syncSnapshot(true);
                        });
                    } else {
                        String message = reply.optString("detail", "请求完成");
                        runOnUiThread(() -> new AlertDialog.Builder(this).setMessage(message)
                                .setPositiveButton("好", (view, which) -> {if (reset) showNativeAuth("login");}).show());
                    }
                } catch (Exception error) {
                    runOnUiThread(() -> identifier.setError(error.getMessage()));
                } finally {
                    runOnUiThread(() -> button.setEnabled(true));
                }
            });
        }));
        form.show();
    }

    private static boolean hasSessionCookie(String cookies) {
        if (cookies == null) return false;
        for (String cookie : cookies.split(";"))
            if (cookie.trim().startsWith("schedule_session=")) return true;
        return false;
    }

    private static String formValue(String value) throws Exception {
        return URLEncoder.encode(value, StandardCharsets.UTF_8.name());
    }

    private JSONObject authForm(String path, String data) throws Exception {
        long started = android.os.SystemClock.elapsedRealtime();
        int metricStatus = 0;
        HttpURLConnection connection = (HttpURLConnection) new URL(ORIGIN + path).openConnection();
        CookieManager cookies = CookieManager.getInstance();
        try {
            connection.setRequestProperty("Accept", "application/json");
            connection.setRequestProperty("X-Awesome-Client", "android");
            String cookie = cookies.getCookie(ORIGIN);
            if (cookie != null) connection.setRequestProperty("Cookie", cookie);
            connection.setConnectTimeout(8000);
            connection.setReadTimeout(10000);
            connection.setInstanceFollowRedirects(false);
            if (data != null) {
                connection.setRequestMethod("POST");
                connection.setDoOutput(true);
                connection.setRequestProperty("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
                try (OutputStream stream = connection.getOutputStream()) {stream.write(data.getBytes(StandardCharsets.UTF_8));}
            }
            int status = connection.getResponseCode();
            metricStatus = status;
            for (java.util.Map.Entry<String, java.util.List<String>> header : connection.getHeaderFields().entrySet())
                if ("Set-Cookie".equalsIgnoreCase(header.getKey()))
                    for (String value : header.getValue()) cookies.setCookie(ORIGIN, value);
            cookies.flush();
            if (status == 303 && path.endsWith("/logout")) return new JSONObject();
            JSONObject result = new JSONObject(readResponse(status < 400 ? connection.getInputStream() : connection.getErrorStream()));
            if (status >= 400) throw new IllegalStateException(result.optString("detail", "请求失败"));
            return result;
        } finally {
            connection.disconnect();
            if (data != null) {
                String node = path.endsWith("/login") ? "login" : path.endsWith("/register") ? "register" : "account";
                performance.record(node, "request", started, metricStatus);
            }
        }
    }

    private boolean showAcceptanceFixture(String screen) {
        if ("native-outbox".equals(screen)) {
            android.content.Context files = createDeviceProtectedStorageContext();
            try {
                String owner = "synthetic-acceptance";
                JSONObject state = NativeWorkspace.object("user_id", owner, "server_time", "2026-11-19T00:00:00Z");
                JSONObject command = NativeWorkspace.object("action", "create", "todo_id", "fixture-todo",
                        "title", "离线保存的合成待办", "due_date", LocalDate.now(CalendarProjection.DISPLAY_ZONE).toString());
                NativeOutbox.enqueue(state, "fixture-operation", "todos", command);
                OfflineStore.save(files, state);
                JSONObject restored = OfflineStore.load(files);
                JSONObject restoredOp = NativeOutbox.next(restored);
                if (restoredOp == null || !restoredOp.getJSONObject("command").toString().equals(command.toString()) ||
                        !restored.getJSONArray("todos").getJSONObject(0).optBoolean("awaiting_sync"))
                    throw new IllegalStateException("Expected optimistic todo and durable outbox");
                JSONObject accepted = new JSONObject(restored.toString());
                NativeOutbox.acknowledge(accepted, restoredOp, NativeWorkspace.object("todo",
                        NativeWorkspace.object("id", "fixture-todo", "title", command.getString("title"),
                                "due_date", command.getString("due_date"))));
                if (NativeOutbox.next(accepted) != null || accepted.getJSONArray("todos").length() != 1 ||
                        accepted.getJSONArray("todos").getJSONObject(0).optBoolean("awaiting_sync"))
                    throw new IllegalStateException("Expected one confirmed todo after acknowledgement");
                android.util.Log.i("NativeOutboxContract", "PASS: optimistic write, Keystore reload and receipt");
                initialHomePending = false;
                currentSnapshot = restored;
                dashboard.setSnapshot(restored);
                showNative(false);
                return true;
            } catch (Exception error) {
                android.util.Log.e("NativeOutboxContract", "FAIL: durable outbox contract", error);
                return false;
            } finally {OfflineStore.clear(files);}
        }
        if ("calendar-contract".equals(screen)) {
            try {
                JSONObject fixture = new JSONObject(readResponse(getAssets().open("calendar_projection_v1.json")));
                JSONArray input = fixture.getJSONArray("events");
                java.util.List<JSONObject> events = new java.util.ArrayList<>();
                for (int i = 0; i < input.length(); i++) events.add(input.getJSONObject(i));
                JSONObject days = fixture.getJSONObject("days");
                java.util.Iterator<String> keys = days.keys();
                while (keys.hasNext()) {
                    String key = keys.next();
                    JSONArray actual = new JSONArray();
                    for (CalendarProjection.Segment segment : CalendarProjection.forDay(events, LocalDate.parse(key),
                            java.time.LocalTime.parse(fixture.getString("workday_start")),
                            java.time.LocalTime.parse(fixture.getString("workday_end")))) {
                        actual.put(new JSONArray().put(segment.event.getString("id")).put(segment.startMinute)
                                .put(segment.endMinute).put(!segment.event.isNull("completed_at")));
                    }
                    if (!actual.toString().equals(days.getJSONArray(key).toString()))
                        throw new IllegalStateException("Calendar contract mismatch: " + key + " " + actual);
                }
                fixture.put("profile", new JSONObject().put("workday_start", fixture.getString("workday_start"))
                        .put("workday_end", fixture.getString("workday_end")));
                initialHomePending = false;
                activeTab = 1;
                updateNavigation();
                currentSnapshot = fixture;
                dashboard.setSnapshot(fixture);
                dashboard.selectDate(LocalDate.parse("2026-11-13"));
                showNative(true);
                android.util.Log.i("CalendarContract", "PASS: shared synthetic calendar fixture");
                return true;
            } catch (Exception error) {
                android.util.Log.e("CalendarContract", "FAIL: shared calendar fixture", error);
                return false;
            }
        }
        if ("native-experiences".equals(screen)) {
            try {
                currentSnapshot = new JSONObject("{\"experiences\":[{\"id\":\"demo\",\"title\":\"后端开发二面复盘\",\"company\":\"示例公司\",\"is_public\":true,\"questions\":[{},{}]},{\"id\":\"demo-private\",\"title\":\"我的下一场面试准备\",\"company\":\"示例团队\",\"is_public\":false,\"questions\":[]}]}");
                showExperienceList();
                return true;
            } catch (Exception ignored) { return false; }
        }
        if ("dashboard-home".equals(screen) || "dashboard-calendar".equals(screen) ||
                "dashboard-next-day".equals(screen)) {
            initialHomePending = false;
            activeTab = "dashboard-home".equals(screen) ? 0 : 1;
            updateNavigation();
            try {
                LocalDate today = LocalDate.now(java.time.ZoneId.of("Asia/Shanghai"));
                String events = "{\"events\":[" +
                        "{\"id\":\"acceptance-overnight\",\"title\":\"跨天面试准备\"," +
                        "\"type\":\"personal\",\"start_at\":\"" + today + "T23:00:00+08:00\"," +
                        "\"end_at\":\"" + today.plusDays(1) + "T01:00:00+08:00\",\"completed_at\":null}," +
                        "{\"id\":\"acceptance-done\",\"title\":\"已完成的简历检查\"," +
                        "\"type\":\"personal\",\"start_at\":\"" + today + "T10:00:00+08:00\"," +
                        "\"end_at\":\"" + today + "T11:00:00+08:00\"," +
                        "\"completed_at\":\"" + today + "T11:15:00+08:00\"}]}";
                dashboard.setSnapshot(new JSONObject(events));
            } catch (Exception error) { return false; }
            showNative("dashboard-calendar".equals(screen));
            if ("dashboard-next-day".equals(screen)) {
                dashboard.showCalendar(true);
                dashboard.selectDate(LocalDate.now(java.time.ZoneId.of("Asia/Shanghai")).plusDays(1));
            }
            return true;
        }
        if ("event-detail".equals(screen)) {
            initialHomePending = false;
            activeTab = 2;
            updateNavigation();
            try {
                eventScreen.showEvent(new JSONObject("{\"id\":\"acceptance-event\",\"title\":\"示例面试\"," +
                        "\"type\":\"interview\",\"start_at\":\"2026-10-12T10:00:00+08:00\"," +
                        "\"end_at\":\"2026-10-12T11:00:00+08:00\"}"));
            } catch (Exception error) { return false; }
            showEventScreen();
            return true;
        }
        if ("event".equals(screen) || "event-reminders".equals(screen)) {
            initialHomePending = false;
            activeTab = 2;
            updateNavigation();
            eventScreen.showNew();
            showEventScreen();
            if ("event-reminders".equals(screen))
                eventScreen.post(() -> eventScreen.scrollTo(0, eventScreen.getChildAt(0).getHeight()));
            return true;
        }
        if (!"export".equals(screen) && !"export-page2".equals(screen) &&
                !"export-options".equals(screen)) return false;
        try {
            File fixture = File.createTempFile("acceptance-export-", ".pdf", getCacheDir());
            try (InputStream input = getAssets().open("acceptance-export.pdf");
                 OutputStream output = new FileOutputStream(fixture)) { copy(input, output); }
            initialHomePending = false;
            activeTab = 3;
            updateNavigation();
            dashboard.setVisibility(View.GONE);
            eventScreen.setVisibility(View.GONE);
            exportScreen.setVisibility(View.VISIBLE);
            navigation.setVisibility(View.VISIBLE);
            exportScreen.open(fixture);
            if ("export-page2".equals(screen)) exportScreen.showPage(2);
            if ("export-options".equals(screen)) exportScreen.showOptions();
            return true;
        } catch (Exception error) {
            Toast.makeText(this, "验收示例无法打开", Toast.LENGTH_LONG).show();
            return false;
        }
    }

    private int dp(float value) {
        return (int) (value * getResources().getDisplayMetrics().density + .5f);
    }

    private RippleDrawable tabBackground(boolean active) {
        GradientDrawable shape = new GradientDrawable();
        shape.setColor(active ? Color.rgb(221, 245, 243) : Color.TRANSPARENT);
        shape.setCornerRadius(dp(15));
        return new RippleDrawable(ColorStateList.valueOf(0x28007A7F), shape, null);
    }

    private void updateNavigation() {
        for (int i = 0; i < tabs.length; i++) {
            TextView tab = tabs[i];
            if (tab == null) continue;
            boolean active = i == activeTab;
            tab.setBackground(tabBackground(active));
            int color = active ? Color.rgb(0, 122, 127) : Color.rgb(92, 108, 122);
            tab.setTextColor(color);
            tab.setCompoundDrawableTintList(ColorStateList.valueOf(color));
            tab.setTypeface(null, active ? Typeface.BOLD : Typeface.NORMAL);
            tab.setContentDescription(tab.getText().toString() +
                    (active ? "，当前页面" : ""));
        }
    }

    private void selectTab(int index) {
        if (exportScreen.getVisibility() == View.VISIBLE) leaveExport();
        activeTab = index;
        updateNavigation();
        if (index <= 1) {
            showNative(index == 1);
            syncSnapshot(false);
            return;
        }
        if (index == 2) {
            failedEventOperation = null;
            eventScreen.showNew();
            showEventScreen();
            return;
        }
        if (index == 3) {
            showExperienceList();
            experienceList.reload();
            return;
        }
        workspace.account();
    }

    private void showNative(boolean calendar) {
        long started = android.os.SystemClock.elapsedRealtime();
        if (experienceList != null) experienceList.setVisibility(View.GONE);
        offlineVisible = false;
        offline.setVisibility(View.GONE);
        retry.setVisibility(View.GONE);
        eventScreen.setVisibility(View.GONE);
        exportScreen.setVisibility(View.GONE);
        dashboard.setVisibility(View.VISIBLE);
        dashboard.showCalendar(calendar);
        if (currentSnapshot != null) dashboard.post(() ->
                performance.record(calendar ? "calendar" : "home", "cache_render", started, 200));
        navigation.setVisibility(View.VISIBLE);
    }

    private void showEventScreen() {
        if (experienceList != null) experienceList.setVisibility(View.GONE);
        offlineVisible = false;
        offline.setVisibility(View.GONE);
        retry.setVisibility(View.GONE);
        dashboard.setVisibility(View.GONE);
        eventScreen.setVisibility(View.VISIBLE);
        exportScreen.setVisibility(View.GONE);
        navigation.setVisibility(View.VISIBLE);
    }

    private void showEventFromSnapshot(String id) {
        failedEventOperation = null;
        String[] occurrence = id.split("~", 2);
        JSONArray events = currentSnapshot == null ? null : currentSnapshot.optJSONArray("events");
        if (events != null) for (int i = 0; i < events.length(); i++) {
            JSONObject event = events.optJSONObject(i);
            if (event != null && occurrence[0].equals(event.optString("id"))) {
                if (occurrence.length == 2) {
                    try {event = CalendarProjection.occurrence(event, LocalDate.parse(occurrence[1]));}
                    catch (Exception error) {event = null;}
                    if (event == null) break;
                }
                eventScreen.showEvent(event);
                showEventScreen();
                return;
            }
        }
        Toast.makeText(this, "日程正在同步，请稍后重试", Toast.LENGTH_SHORT).show();
        syncSnapshot(true);
    }

    private void confirmDeleteEvent(String id) {
        new AlertDialog.Builder(this).setTitle("删除日程？")
                .setMessage("删除后无法恢复")
                .setNegativeButton("取消", null)
                .setPositiveButton("删除", (dialog, which) -> writeEventRequest("DELETE", id, null))
                .show();
    }

    private void writeEvent(String id, JSONObject payload) {
        writeEventRequest(id == null ? "POST" : "PUT", id, payload);
    }

    private void parseInvitation(String rawText) {
        String cookie = CookieManager.getInstance().getCookie(ORIGIN);
        if (cookie == null || cookie.isEmpty()) {
            Toast.makeText(this, "请先登录", Toast.LENGTH_SHORT).show();
            return;
        }
        executor.execute(() -> {
            HttpURLConnection connection = null;
            try {
                String csrf = currentCsrf(cookie);
                if (csrf == null) throw new IllegalStateException("登录已过期");
                connection = (HttpURLConnection) new URL(ORIGIN + "/api/mobile/v1/events/parse").openConnection();
                connection.setRequestMethod("POST");
                connection.setRequestProperty("Cookie", cookie);
                connection.setRequestProperty("X-CSRF-Token", csrf);
                connection.setRequestProperty("Content-Type", "application/json; charset=UTF-8");
                connection.setRequestProperty("X-Awesome-Client", "android");
                connection.setConnectTimeout(8000);
                connection.setReadTimeout(10000);
                connection.setDoOutput(true);
                try (OutputStream output = connection.getOutputStream()) {
                    output.write(new JSONObject().put("raw_text", rawText).toString()
                            .getBytes(StandardCharsets.UTF_8));
                }
                if (connection.getResponseCode() != 200)
                    throw new IllegalStateException("邀请解析失败，请检查内容或手动填写");
                JSONObject parsed = new JSONObject(readResponse(connection.getInputStream()));
                runOnUiThread(() -> {
                    eventScreen.applyParsed(parsed, rawText);
                    Toast.makeText(this, "已填入解析结果，请核对后保存", Toast.LENGTH_LONG).show();
                });
            } catch (Exception error) {
                runOnUiThread(() -> Toast.makeText(this, error.getMessage(), Toast.LENGTH_LONG).show());
            } finally {
                if (connection != null) connection.disconnect();
            }
        });
    }

    private void writeEventRequest(String method, String id, JSONObject payload) {
        JSONObject snapshot = currentSnapshot;
        if (snapshot == null || snapshot.isNull("user_id")) {
            Toast.makeText(this, "请先同步日程", Toast.LENGTH_SHORT).show();
            return;
        }
        try {
            String owner = snapshot.getString("user_id");
            String operation = java.util.UUID.randomUUID().toString();
            JSONObject command = new JSONObject();
            if ("POST".equals(method) || "PUT".equals(method)) {
                JSONObject input = new JSONObject(payload.toString());
                input.put("type", input.remove("event_type"));
                Object raw = input.remove("raw_invitation");
                if (raw != null) {
                    input.put("raw_text", raw);
                    input.put("source", "invitation");
                }
                copyEventSettings(id, input, snapshot);
                command.put("event", input);
                command.put("action", id == null ? "create" : "edit");
            } else {
                command.put("action", "DELETE".equals(method) ? "delete" : "completion");
                if ("COMPLETE".equals(method)) command.put("completed", payload.getBoolean("completed"));
            }
            String[] target = id == null ? null : id.split("~", 2);
            JSONObject event = command.optJSONObject("event");
            String source = event == null ? "" : event.optString("source_idea_id", "");
            String key = source.isEmpty() ? "calendar:" + owner + ":" + operation : "idea:" + owner + ":" + source;
            command.put("event_id", target == null ? deterministicId(key) : target[0]);
            if (target != null && target.length == 2 && "COMPLETE".equals(method))
                command.put("occurrence_day", target[1]);
            sendCalendarCommand(owner, operation, command, id);
        } catch (Exception error) {
            Toast.makeText(this, "无法准备日程修改", Toast.LENGTH_SHORT).show();
        }
    }

    private void copyEventSettings(String id, JSONObject input, JSONObject snapshot) throws Exception {
        if (id == null) return;
        JSONArray events = snapshot.getJSONArray("events");
        for (int i = 0; i < events.length(); i++) {
            JSONObject saved = events.getJSONObject(i);
            if (!id.split("~", 2)[0].equals(saved.optString("id"))) continue;
            for (String key : new String[]{"skip_weekends", "workday_only", "repeat_weekdays", "reference_links", "location_url"})
                if (saved.has(key) && !input.has(key)) input.put(key, saved.get(key));
            break;
        }
    }

    private static String deterministicId(String operation) throws Exception {
        java.util.UUID namespace = java.util.UUID.fromString("6ba7b811-9dad-11d1-80b4-00c04fd430c8");
        java.nio.ByteBuffer buffer = java.nio.ByteBuffer.allocate(16).putLong(namespace.getMostSignificantBits())
                .putLong(namespace.getLeastSignificantBits());
        java.security.MessageDigest digest = java.security.MessageDigest.getInstance("SHA-1");
        digest.update(buffer.array());
        byte[] hash = digest.digest(operation.getBytes(StandardCharsets.UTF_8));
        hash[6] = (byte) ((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte) ((hash[8] & 0x3f) | 0x80);
        java.nio.ByteBuffer result = java.nio.ByteBuffer.wrap(hash);
        return new java.util.UUID(result.getLong(), result.getLong()).toString();
    }

    private void sendCalendarCommand(String owner, String operation, JSONObject command, String occurrenceId) {
        String replacing = failedEventOperation;
        enqueueNative(owner, operation, "events", command, replacing, () -> {
            if (java.util.Objects.equals(replacing, failedEventOperation)) failedEventOperation = null;
            if ("completion".equals(command.optString("action")) && occurrenceId != null) {
                showEventFromSnapshot(occurrenceId);
            } else {
                JSONObject event = command.optJSONObject("event");
                if (event != null) dashboard.selectDate(OffsetDateTime.parse(event.optString("start_at"))
                        .atZoneSameInstant(CalendarProjection.DISPLAY_ZONE).toLocalDate());
                activeTab = 1;
                updateNavigation();
                showNative(true);
            }
            Toast.makeText(this, "已保存到本机，等待同步", Toast.LENGTH_SHORT).show();
        });
    }

    private void enqueueNative(String owner, String operation, String resource, JSONObject command, Runnable displayed) {
        enqueueNative(owner, operation, resource, command, null, displayed);
    }

    private void enqueueNative(String owner, String operation, String resource, JSONObject command, String replacing, Runnable displayed) {
        localWrites.execute(() -> {
            try {
                synchronized (nativeStateLock) {
                    JSONObject base = currentSnapshot;
                    if (base == null || !owner.equals(base.optString("user_id"))) return;
                    JSONObject next = new JSONObject(base.toString());
                    if (replacing != null) NativeOutbox.remove(next, replacing);
                    NativeOutbox.enqueue(next, operation, resource, command);
                    persistNative(next);
                }
                runOnUiThread(() -> {
                    JSONObject current = currentSnapshot;
                    if (current != null && owner.equals(current.optString("user_id"))) displayed.run();
                });
                pushNativeChanges();
            } catch (Exception error) {
                runOnUiThread(() -> Toast.makeText(this, error.getMessage(), Toast.LENGTH_LONG).show());
            }
        });
    }

    private void persistNative(JSONObject updated) throws Exception {
        OfflineStore.save(this, updated);
        currentSnapshot = updated;
        runOnUiThread(() -> {
            if (currentSnapshot == updated) dashboard.setSnapshot(updated);
        });
        try {ReminderScheduler.reschedule(this, updated);}
        catch (Exception error) {android.util.Log.w("NativeReminder", "Cannot reconcile reminders", error);}
    }

    private boolean flushNative(String cookie) throws Exception {
        JSONObject base = currentSnapshot;
        if (base == null || base.optJSONArray("outbox") == null || base.getJSONArray("outbox").length() == 0) return true;
        JSONObject session = sessionCsrf != null && cookie.equals(sessionCookie)
                ? NativeWorkspace.object("user_id", sessionOwner, "csrf_token", sessionCsrf)
                : readNativeJson("/api/mobile/v1/session", cookie);
        String owner = base.getString("user_id");
        if (!cookie.equals(CookieManager.getInstance().getCookie(ORIGIN))) return false;
        if (!owner.equals(session.optString("user_id"))) {
            runOnUiThread(() -> new AlertDialog.Builder(this).setTitle("账号已切换")
                    .setMessage("上一个账号有未同步修改。可以返回原账号继续同步，也可以清除旧缓存。")
                    .setNegativeButton("登录原账号", (view, button) -> showNativeAuth("login"))
                    .setPositiveButton("清除旧缓存", (view, button) -> {
                        clearAccountDisplay();
                        executor.execute(() -> {
                            ReminderScheduler.cancelAll(this);
                            OfflineStore.clear(this);
                            syncSnapshot(true);
                        });
                    }).show());
            return false;
        }
        String csrf = session.getString("csrf_token");
        sessionCookie = cookie;
        sessionOwner = owner;
        sessionCsrf = csrf;
        boolean restart = resumePendingNow.getAndSet(false);
        while (cookie.equals(CookieManager.getInstance().getCookie(ORIGIN))) {
            JSONObject current = currentSnapshot;
            if (current == null || !owner.equals(current.optString("user_id"))) return false;
            JSONObject op = NativeOutbox.next(current);
            if (op == null) return current.getJSONArray("outbox").length() == 0;
            long delay = op.optLong("next_attempt") - System.currentTimeMillis();
            if (!restart && delay > 0) {scheduleNativeRetry(delay); return false;}
            String operation = op.getString("id");
            String resource = op.getString("kind");
            JSONObject command = op.getJSONObject("command");
            HttpURLConnection connection = null;
            int status = 0;
            String result = "";
            try {
                String prefix = resource.equals("todos") ? "/api/mobile/v1/todo-sync/" :
                        BASE + (resource.equals("events") ? "/calendar-sync/" : "/idea-sync/");
                connection = (HttpURLConnection) new URL(ORIGIN + prefix + operation).openConnection();
                connection.setRequestMethod("POST");
                connection.setRequestProperty("Cookie", cookie);
                connection.setRequestProperty("X-CSRF-Token", csrf);
                connection.setRequestProperty("X-Awesome-Client", "android");
                connection.setRequestProperty("Content-Type", "application/json; charset=UTF-8");
                connection.setConnectTimeout(8000);
                connection.setReadTimeout(10000);
                connection.setInstanceFollowRedirects(false);
                connection.setDoOutput(true);
                try (OutputStream output = connection.getOutputStream()) {
                    output.write(command.toString().getBytes(StandardCharsets.UTF_8));
                }
                status = connection.getResponseCode();
                result = readResponse(status == 200 ? connection.getInputStream() : connection.getErrorStream());
            } catch (java.io.IOException ignored) {
                status = 0;
            } finally {
                if (connection != null) connection.disconnect();
            }
            synchronized (nativeStateLock) {
                if (!cookie.equals(CookieManager.getInstance().getCookie(ORIGIN)) || currentSnapshot == null ||
                        !owner.equals(currentSnapshot.optString("user_id"))) return false;
                JSONObject next = new JSONObject(currentSnapshot.toString());
                JSONObject sent = NativeOutbox.next(next);
                if (sent == null || !operation.equals(sent.optString("id"))) continue;
                if (status == 200) {
                    JSONObject receipt = new JSONObject(result);
                    if (!owner.equals(receipt.optString("user_id")) || !operation.equals(receipt.optString("operation_id")))
                        throw new java.io.IOException("回执与操作不匹配");
                    NativeOutbox.acknowledge(next, sent, receipt);
                    persistNative(next);
                    continue;
                }
                if (status == 401 || status == 403 || status == 303) {
                    sessionCsrf = null;
                    sent.put("state", "auth").put("error", "需要重新登录");
                    persistNative(next);
                    runOnUiThread(() -> {
                        clearAccountDisplay();
                        showNativeAuth("login");
                    });
                    return false;
                }
                if (status == 400 || status == 404 || status == 409 || status == 422) {
                    String reason = "";
                    try {
                        JSONObject failure = new JSONObject(result);
                        reason = failure.optString("detail");
                        JSONArray conflicts = failure.optJSONArray("conflicts");
                        if (conflicts != null) for (int i = 0; i < conflicts.length(); i++) reason += "\n" + conflicts.optString(i);
                    } catch (org.json.JSONException ignored) { }
                    NativeOutbox.fail(next, sent, status, reason);
                    persistNative(next);
                    continue;
                }
                int attempts = sent.optInt("attempts") + 1;
                long backoff = Math.min(300000, 1000L << Math.min(attempts - 1, 9));
                sent.put("attempts", attempts).put("next_attempt", System.currentTimeMillis() + backoff);
                persistNative(next);
                scheduleNativeRetry(backoff);
                return false;
            }
        }
        return false;
    }

    private final android.os.Handler syncHandler = new android.os.Handler(android.os.Looper.getMainLooper());
    private final Runnable retryNative = () -> { if (!isDestroyed()) pushNativeChanges(); };

    private void pushNativeChanges() {
        String cookie = CookieManager.getInstance().getCookie(ORIGIN);
        if (!hasSessionCookie(cookie)) return;
        executor.execute(() -> {
            try {flushNative(cookie);}
            catch (NativeSessionExpired error) {runOnUiThread(() -> showNativeAuth("login"));}
            catch (Exception error) {scheduleNativeRetry(30_000);}
        });
    }

    private void scheduleNativeRetry(long delay) {
        syncHandler.removeCallbacks(retryNative);
        syncHandler.postDelayed(retryNative, delay);
    }

    private void showPendingChanges() {
        JSONArray queue = currentSnapshot == null ? null : currentSnapshot.optJSONArray("outbox");
        if (queue == null || queue.length() == 0) {
            new AlertDialog.Builder(this).setMessage("没有待同步修改").setPositiveButton("关闭", null).show();
            return;
        }
        String[] labels = new String[queue.length()];
        for (int i = 0; i < queue.length(); i++) {
            JSONObject op = queue.optJSONObject(i), command = op.optJSONObject("command");
            JSONObject event = command.optJSONObject("event");
            String title = event == null ? command.optString("title", "本地修改") : event.optString("title");
            labels[i] = title + " · " + op.optString("error", "待同步");
        }
        new AlertDialog.Builder(this).setTitle("待同步修改").setNegativeButton("关闭", null)
                .setItems(labels, (dialog, which) -> managePending(queue.optJSONObject(which))).show();
    }

    private void managePending(JSONObject selected) {
        boolean failed = "failed".equals(selected.optString("state"));
        AlertDialog.Builder dialog = new AlertDialog.Builder(this).setTitle("本地修改")
                .setMessage(selected.optString("error", "修改已保存在本机"))
                .setNegativeButton("返回", (view, button) -> showPendingChanges());
        boolean conflict = selected.optInt("status") == 409 && selected.optJSONObject("command").has("event");
        dialog.setPositiveButton(conflict ? "确认重叠并重试" : "重试", (view, button) ->
                changePending(selected.optString("id"), false, conflict));
        if (failed) {
            JSONObject command = selected.optJSONObject("command");
            JSONObject draft = command.optJSONObject("event");
            if (draft == null) dialog.setNeutralButton("放弃本次修改", (view, button) ->
                    changePending(selected.optString("id"), true, false));
            else dialog.setNeutralButton("管理草稿", (view, button) -> new AlertDialog.Builder(this)
                    .setTitle(draft.optString("title"))
                    .setItems(new String[]{"修改草稿", "放弃本次修改"}, (options, index) -> {
                        if (index == 1) changePending(selected.optString("id"), true, false);
                        else {
                            failedEventOperation = selected.optString("id");
                            eventScreen.showDraft(draft, "create".equals(command.optString("action")) ? null : command.optString("event_id"));
                            showEventScreen();
                        }
                    }).show());
        }
        dialog.show();
    }

    private void changePending(String id, boolean discard, boolean confirmOverlap) {
        executor.execute(() -> {
            try {
                JSONObject base = currentSnapshot;
                if (base == null) return;
                JSONObject next = new JSONObject(base.toString());
                JSONArray queue = NativeOutbox.operations(next);
                for (int i = 0; i < queue.length(); i++) {
                    JSONObject op = queue.getJSONObject(i);
                    if (!id.equals(op.getString("id"))) continue;
                    if (discard) queue.remove(i);
                    else {
                        if (confirmOverlap) {
                            JSONObject command = op.getJSONObject("command");
                            String newId = java.util.UUID.randomUUID().toString();
                            if ("create".equals(command.optString("action")) && command.getJSONObject("event").isNull("source_idea_id"))
                                command.put("event_id", deterministicId("calendar:" + next.getString("user_id") + ":" + newId));
                            op.put("id", newId);
                            command.put("confirm_overlap", true);
                        }
                        op.put("state", "pending").put("attempts", 0).put("next_attempt", 0).remove("error");
                    }
                    NativeOutbox.project(next);
                    persistNative(next);
                    pushNativeChanges();
                    break;
                }
            } catch (Exception error) {
                runOnUiThread(() -> Toast.makeText(this, "无法更新本机修改", Toast.LENGTH_SHORT).show());
            }
        });
    }

    private final android.net.ConnectivityManager.NetworkCallback networkCallback = new android.net.ConnectivityManager.NetworkCallback() {
        @Override public void onAvailable(android.net.Network network) {
            syncHandler.post(() -> {
                if (!isDestroyed()) {
                    resumePendingNow.set(true);
                    syncSnapshot(true);
                }
            });
        }
        @Override public void onLost(android.net.Network network) {
            syncHandler.post(() -> {
                if (!isDestroyed()) {administrator = false; workspace.closeAdmin();}
            });
        }
    };

    @Override protected void onStart() {
        super.onStart();
        getSystemService(android.net.ConnectivityManager.class).registerDefaultNetworkCallback(networkCallback);
    }

    @Override protected void onStop() {
        getSystemService(android.net.ConnectivityManager.class).unregisterNetworkCallback(networkCallback);
        super.onStop();
    }

    private void showIdeas(boolean abandoned) {
        JSONObject snapshot = currentSnapshot;
        if (snapshot == null) return;
        JSONArray ideas = snapshot.optJSONArray("ideas");
        java.util.List<JSONObject> items = new java.util.ArrayList<>();
        java.util.List<String> labels = new java.util.ArrayList<>();
        if (ideas != null) for (int i = ideas.length() - 1; i >= 0; i--) {
            JSONObject item = ideas.optJSONObject(i);
            if (item == null || abandoned != "abandoned".equals(item.optString("status"))) continue;
            items.add(item);
            String linked = linkedEvent(snapshot, item);
            labels.add(item.optString("title") + (!linked.isEmpty() ? " · 已创建日程" :
                    item.optBoolean("awaiting_sync") ? " · 待同步" : ""));
        }
        AlertDialog.Builder dialog = new AlertDialog.Builder(this).setTitle((abandoned ? "已放弃" : "待安排") + " · " + items.size())
                .setNeutralButton(abandoned ? "返回待安排" : "已放弃", (view, button) -> showIdeas(!abandoned))
                .setNegativeButton("关闭", null);
        if (!items.isEmpty()) dialog.setItems(labels.toArray(new String[0]), (view, index) -> ideaActions(items.get(index), abandoned));
        if (!abandoned) dialog.setPositiveButton("记录想法", (view, button) -> editIdea(null));
        dialog.show();
    }

    private void ideaActions(JSONObject idea, boolean abandoned) {
        AlertDialog.Builder dialog = new AlertDialog.Builder(this).setTitle(idea.optString("title")).setNegativeButton("返回", (view, button) -> showIdeas(abandoned));
        if (abandoned) {dialog.setMessage(idea.optString("note")).show(); return;}
        String linked = linkedEvent(currentSnapshot, idea);
        boolean alreadyArranged = !linked.isEmpty();
        dialog.setItems(new String[]{alreadyArranged ? "查看关联日程" : "正式安排时间", "修改想法", "放弃"}, (view, index) -> {
            if (index == 0) {
                if (alreadyArranged) showEventFromSnapshot(linked);
                else {failedEventOperation = null; eventScreen.arrangeIdea(idea); showEventScreen();}
            } else if (index == 1) editIdea(idea);
            else submitIdea("abandon", idea);
        }).show();
    }

    private String linkedEvent(JSONObject snapshot, JSONObject idea) {
        if (snapshot == null) return "";
        String linked = NativeWorkspace.value(idea, "arranged_event_id");
        try {
            if (linked.isEmpty()) linked = deterministicId("idea:" + snapshot.getString("user_id") + ":" + idea.getString("id"));
            for (JSONObject event : NativeWorkspace.rows(snapshot, "events"))
                if (linked.equals(event.optString("id"))) return linked;
        } catch (Exception ignored) {}
        return "";
    }

    private void editIdea(JSONObject original) {
        LinearLayout fields = new LinearLayout(this);
        fields.setOrientation(LinearLayout.VERTICAL);
        fields.setPadding(dp(20), dp(8), dp(20), 0);
        EditText title = new EditText(this), note = new EditText(this), links = new EditText(this);
        title.setHint("标题"); title.setSingleLine(true);
        note.setHint("备注");
        links.setHint("参考链接，每行一条");
        if (original != null) {
            title.setText(original.optString("title")); note.setText(original.optString("note"));
            StringBuilder text = new StringBuilder();
            JSONArray existing = original.optJSONArray("reference_links");
            if (existing != null) for (int i = 0; i < existing.length(); i++) {
                JSONObject link = existing.optJSONObject(i);
                if (link == null) continue;
                if (!link.optString("title").isEmpty()) text.append(link.optString("title")).append(" | ");
                text.append(link.optString("url")).append("\n");
            }
            links.setText(text.toString());
        }
        fields.addView(title); fields.addView(note); fields.addView(links);
        AlertDialog dialog = new AlertDialog.Builder(this).setTitle(original == null ? "记录想法" : "修改想法")
                .setView(fields).setNegativeButton("取消", null).setPositiveButton("保存", null).create();
        dialog.setOnShowListener(v -> dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(button -> {
            String name = title.getText().toString().trim();
            if (name.isEmpty() || name.length() > 200) {title.setError("标题须为 1 至 200 个字符"); return;}
            try {
                JSONArray references = new JSONArray();
                for (String row : links.getText().toString().split("\n")) {
                    if (row.trim().isEmpty()) continue;
                    String[] parts = row.split("\\|", 2);
                    String url = parts[parts.length - 1].trim();
                    String scheme = Uri.parse(url).getScheme();
                    if ((! "https".equals(scheme) && ! "http".equals(scheme)) || references.length() >= 20) {
                        links.setError("最多 20 条 HTTP 或 HTTPS 链接"); return;
                    }
                    references.put(new JSONObject().put("title", parts.length == 2 ? parts[0].trim() : "").put("url", url));
                }
                JSONObject item = new JSONObject().put("title", name).put("note", note.getText().toString())
                        .put("reference_links", references);
                if (original != null) item.put("id", original.getString("id"));
                submitIdea(original == null ? "create" : "edit", item);
                dialog.dismiss();
            } catch (Exception error) {title.setError("无法保存事项");}
        }));
        dialog.show();
    }

    private void submitIdea(String action, JSONObject item) {
        try {
            String owner = currentSnapshot.getString("user_id");
            String operation = java.util.UUID.randomUUID().toString();
            String id = "create".equals(action) ? deterministicId("idea-create:" + owner + ":" + operation) : item.getString("id");
            JSONObject command = new JSONObject().put("idea_id", id).put("action", action);
            if (!"abandon".equals(action))
                for (String key : new String[]{"title", "note", "reference_links"}) command.put(key, item.get(key));
            enqueueNative(owner, operation, "ideas", command, () -> showIdeas(false));
        } catch (Exception error) {Toast.makeText(this, "无法保存事项", Toast.LENGTH_SHORT).show();}
    }

    private static String readResponse(InputStream input) throws Exception {
        if (input == null) return "";
        try (InputStream stream = input; ByteArrayOutputStream buffer = new ByteArrayOutputStream()) {
            copy(stream, buffer);
            return buffer.toString(StandardCharsets.UTF_8.name());
        }
    }

    private void promptCreateTodo(LocalDate date) {
        EditText title = new EditText(this);
        title.setSingleLine(true);
        title.setHint("例如：准备面试问题");
        title.setPadding(dp(22), dp(10), dp(22), dp(10));
        new AlertDialog.Builder(this).setTitle("新建待办 · " + date)
                .setView(title)
                .setNegativeButton("取消", null)
                .setPositiveButton("保存", (dialog, which) -> {
                    String value = title.getText().toString().trim();
                    if (value.isEmpty()) {
                        Toast.makeText(this, "请输入待办内容", Toast.LENGTH_SHORT).show();
                        return;
                    }
                    writeTodo("create", null, value, date, false);
                }).show();
    }

    private void writeTodo(String action, String id, String title, LocalDate date, boolean completed) {
        if (currentSnapshot == null) return;
        try {
            String owner = currentSnapshot.getString("user_id");
            String operation = java.util.UUID.randomUUID().toString();
            JSONObject command = new JSONObject().put("action", action)
                    .put("todo_id", id == null ? deterministicId("todo:" + owner + ":" + operation) : id);
            if (action.equals("create")) command.put("title", title).put("due_date", date.toString());
            if (action.equals("completion")) command.put("completed", completed);
            enqueueNative(owner, operation, "todos", command,
                    () -> Toast.makeText(this, "待办已保存到本机", Toast.LENGTH_SHORT).show());
        } catch (Exception error) {
            Toast.makeText(this, "无法保存待办", Toast.LENGTH_LONG).show();
        }
    }

    private String currentCsrf(String cookie) throws Exception {
        if (sessionCsrf != null && cookie.equals(sessionCookie)) return sessionCsrf;
        HttpURLConnection connection = (HttpURLConnection) new URL(ORIGIN + "/api/mobile/v1/session").openConnection();
        try {
            connection.setRequestProperty("Cookie", cookie);
            connection.setRequestProperty("X-Awesome-Client", "android");
                connection.setConnectTimeout(8000);
            connection.setReadTimeout(10000);
            connection.setInstanceFollowRedirects(false);
            if (connection.getResponseCode() != 200) return null;
            ByteArrayOutputStream buffer = new ByteArrayOutputStream();
            try (InputStream input = connection.getInputStream()) { copy(input, buffer); }
            JSONObject session = new JSONObject(buffer.toString(StandardCharsets.UTF_8.name()));
            if (currentSnapshot != null && !session.getString("user_id").equals(currentSnapshot.optString("user_id")))
                return null;
            sessionCsrf = session.getString("csrf_token");
            sessionCookie = cookie;
            sessionOwner = session.getString("user_id");
            performance.flush(cookie, sessionCsrf);
            return sessionCsrf;
        } finally {
            connection.disconnect();
        }
    }

    private void syncSnapshot(boolean force) {
        long now = System.currentTimeMillis();
        if (!force && now - lastSyncAttempt < 30_000 &&
                (currentSnapshot == null || currentSnapshot.optJSONArray("outbox") == null ||
                currentSnapshot.optJSONArray("outbox").length() == 0)) return;
        String cookie = CookieManager.getInstance().getCookie(ORIGIN);
        if (!hasSessionCookie(cookie)) return;
        if (!snapshotSyncQueued.compareAndSet(false, true)) {
            if (force) snapshotRefreshPending.set(true);
            return;
        }
        lastSyncAttempt = now;
        executor.execute(() -> {
            HttpURLConnection connection = null;
            long started = android.os.SystemClock.elapsedRealtime();
            int metricStatus = 0;
            try {
                if (currentSnapshot == null && OfflineStore.prefs(this).contains("user_id")) {
                    JSONObject session = readNativeJson("/api/mobile/v1/session", cookie);
                    if (!cookie.equals(CookieManager.getInstance().getCookie(ORIGIN))) return;
                    try {
                        JSONObject cached = OfflineStore.load(this);
                        if (session.getString("user_id").equals(cached.optString("user_id"))) {
                            currentSnapshot = cached;
                            runOnUiThread(() -> {if (currentSnapshot == cached) dashboard.setSnapshot(cached);});
                        } else if (cached.optJSONArray("outbox") != null && cached.getJSONArray("outbox").length() > 0) {
                            // Keep the previous account's commands locked until the user chooses how to proceed.
                            runOnUiThread(() -> new AlertDialog.Builder(this).setTitle("上一个账号仍有未同步修改")
                                    .setNegativeButton("切换回原账号", (view, button) -> showNativeAuth("login"))
                                    .setPositiveButton("清除旧缓存", (view, button) -> executor.execute(() -> {
                                        ReminderScheduler.cancelAll(this);
                                        OfflineStore.clear(this);
                                        syncSnapshot(true);
                                    })).show());
                            return;
                        }
                    } catch (Exception ignored) { /* A damaged cache can be replaced by a verified snapshot. */ }
                }
                if (!flushNative(cookie)) return;
                JSONObject base = currentSnapshot;
                boolean full = base == null || fullNativeRefresh.getAndSet(false);
                String resource = full ? "snapshot" : "calendar";
                connection = (HttpURLConnection) new URL(ORIGIN + "/api/mobile/v1/" + resource).openConnection();
                connection.setRequestProperty("Cookie", cookie);
                connection.setRequestProperty("Accept", "application/json");
                connection.setRequestProperty("X-Awesome-Client", "android");
                connection.setConnectTimeout(8000);
                connection.setReadTimeout(10000);
                connection.setInstanceFollowRedirects(false);
                int status = connection.getResponseCode();
                metricStatus = status;
                if (status == 401 || status == 403 || status == 303) {
                    runOnUiThread(() -> {
                        if (!cookie.equals(CookieManager.getInstance().getCookie(ORIGIN))) return;
                        clearAccountDisplay();
                        showNativeAuth("login");
                    });
                    return;
                }
                if (status != 200) throw new java.io.IOException("日历读取失败");
                String body;
                try (InputStream stream = connection.getInputStream()) {
                    ByteArrayOutputStream buffer = new ByteArrayOutputStream();
                    copy(stream, buffer);
                    body = buffer.toString(StandardCharsets.UTF_8.name());
                }
                JSONObject response = new JSONObject(body);
                if (response.getInt("schema_version") != 1 || !cookie.equals(CookieManager.getInstance().getCookie(ORIGIN))) return;
                boolean changedOwner = !full && !base.getString("user_id").equals(response.getString("user_id"));
                JSONObject snapshot = full || changedOwner ? response : new JSONObject(base.toString());
                if (changedOwner) snapshot.put("experiences", new JSONArray());
                else if (!full) {
                    for (String key : new String[]{"events", "todos", "ideas", "profile"}) snapshot.put(key, response.get(key));
                    snapshot.put("is_admin", response.optBoolean("is_admin"));
                }
                if (full && base != null && base.optString("user_id").equals(snapshot.optString("user_id")) &&
                        base.has("experience_cache")) snapshot.put("experience_cache", base.get("experience_cache"));
                snapshot.remove("confirmed_events");
                snapshot.remove("confirmed_ideas");
                snapshot.remove("confirmed_todos");
                snapshot.put("calendar_server_time", response.getString("server_time"));
                String previousOwner = OfflineStore.prefs(this).getString("user_id", null);
                if (previousOwner != null && !previousOwner.equals(snapshot.getString("user_id"))) {
                    ReminderScheduler.cancelAll(this);
                    sessionCsrf = null;
                }
                synchronized (nativeStateLock) {
                    if (currentSnapshot != base) return;
                    OfflineStore.save(this, snapshot);
                    currentSnapshot = snapshot;
                }
                if (full && sessionCsrf == null) {
                    // The optional timing upload waits for a verified session on its own execution path.
                    executor.execute(() -> {try {currentCsrf(cookie);} catch (Exception ignored) {}});
                }
                runOnUiThread(() -> {
                    if (!cookie.equals(CookieManager.getInstance().getCookie(ORIGIN))) return;
                    if (currentSnapshot != snapshot) return;
                    administrator = snapshot.optBoolean("is_admin");
                    if (!administrator) workspace.closeAdmin();
                    dashboard.setSnapshot(snapshot);
                    if (offlineVisible) showNative(activeTab == 1);
                    if (experienceList != null && experienceList.getVisibility() == View.VISIBLE)
                        experienceList.setSnapshot(snapshot);
                    if (pendingEventId != null) {
                        String requested = pendingEventId;
                        pendingEventId = null;
                        activeTab = 2;
                        updateNavigation();
                        showEventFromSnapshot(requested);
                    }
                });
                int count = ReminderScheduler.reschedule(this, snapshot);
                if (count > 0 && Build.VERSION.SDK_INT >= 33 &&
                        checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                    runOnUiThread(() -> requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, NOTICE_REQUEST));
                }
                if (count > 0 && Build.VERSION.SDK_INT >= 31) {
                    AlarmManager alarms = (AlarmManager) getSystemService(ALARM_SERVICE);
                    if (!alarms.canScheduleExactAlarms() &&
                            !OfflineStore.prefs(this).getBoolean("exact_prompted", false)) {
                        OfflineStore.prefs(this).edit().putBoolean("exact_prompted", true).apply();
                        runOnUiThread(() -> new AlertDialog.Builder(this)
                                .setTitle("提醒可能延迟")
                                .setMessage("系统尚未允许精确提醒。当前已设置普通系统提醒，触发时间可能延迟。")
                                .setNegativeButton("稍后", null)
                                .setPositiveButton("打开设置", (dialog, which) -> {
                                    try {
                                        startActivity(new Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM,
                                                Uri.parse("package:" + getPackageName())));
                                    } catch (Exception ignored) {
                                        Toast.makeText(this, "请在系统设置中允许精确提醒", Toast.LENGTH_LONG).show();
                                    }
                                }).show());
                    }
                }
            } catch (NativeSessionExpired error) {
                runOnUiThread(() -> {
                    if (!cookie.equals(CookieManager.getInstance().getCookie(ORIGIN))) return;
                    clearAccountDisplay();
                    showNativeAuth("login");
                });
            } catch (Exception ignored) {
                runOnUiThread(() -> {
                    if (currentSnapshot == null && OfflineStore.prefs(this).contains("user_id") && !offlineUnlockOffered) {
                        offlineUnlockOffered = true;
                        offerOffline();
                    } else if (currentSnapshot == null && !OfflineStore.prefs(this).contains("user_id")) {
                        showOfflineMessage("无法连接服务器，本机暂无可用缓存。");
                    }
                });
            } finally {
                if (connection != null) connection.disconnect();
                if (connection != null) performance.record("mobile", "request", started, metricStatus);
                if (cookie.equals(sessionCookie)) performance.flush(cookie, sessionCsrf);
                snapshotSyncQueued.set(false);
                if (snapshotRefreshPending.getAndSet(false)) syncSnapshot(true);
            }
        });
    }

    private void nativeLogout() {
        executor.execute(() -> {
            try {
                String cookie = CookieManager.getInstance().getCookie(ORIGIN);
                String token = cookie == null ? null : currentCsrf(cookie);
                authForm("/auth/logout", "csrf_token=" + formValue(token == null ? "" : token));
                signOutLocal();
                runOnUiThread(() -> showNativeAuth("login"));
            } catch (Exception error) {
                runOnUiThread(() -> Toast.makeText(this, "暂时无法退出，请检查网络后重试", Toast.LENGTH_LONG).show());
            }
        });
    }

    private void confirmLogout() {
        JSONObject snapshot = currentSnapshot;
        JSONArray operations = snapshot == null ? null : snapshot.optJSONArray("outbox");
        int count = operations == null ? 0 : operations.length();
        new AlertDialog.Builder(this).setTitle("退出登录？")
                .setMessage(count == 0 ? "退出会清除本机缓存。" : "还有 " + count + " 条本机修改。退出会舍弃这些修改并清除缓存。")
                .setNegativeButton("取消", null)
                .setPositiveButton("退出", (confirmation, button) -> nativeLogout()).show();
    }

    private void signOutLocal() {
        runOnUiThread(() -> {
            clearAccountDisplay();
            CookieManager.getInstance().removeAllCookies(null);
        });
        executor.execute(() -> {
            ReminderScheduler.cancelAll(this);
            OfflineStore.clear(this);
        });
    }

    private void clearAccountDisplay() {
        workspaceGeneration++;
        workspaceBusy = false;
        workspace.close();
        resume.close();
        performance.clear();
        administrator = false;
        experienceGeneration++;
        exportGeneration++;
        exportExperienceId = null;
        exportScreen.close();
        synchronized (nativeStateLock) {currentSnapshot = null;}
        sessionCsrf = null;
        sessionCookie = null;
        sessionOwner = null;
        dashboard.setSnapshot(null);
        dashboard.setVisibility(View.GONE);
        eventScreen.setVisibility(View.GONE);
        exportScreen.setVisibility(View.GONE);
        experienceList.setSnapshot(null);
        experienceList.setVisibility(View.GONE);
        navigation.setVisibility(View.GONE);
    }

    private void offerOffline() {
        runOnUiThread(() -> {
            KeyguardManager guard = (KeyguardManager) getSystemService(KEYGUARD_SERVICE);
            if (!guard.isDeviceSecure()) {
                showOfflineMessage("无法连接服务器。本机未设置锁屏，离线内容暂不可查看。");
                return;
            }
            Intent unlock = guard.createConfirmDeviceCredentialIntent("查看离线内容", "请验证设备身份");
            if (unlock != null) startActivityForResult(unlock, UNLOCK_REQUEST);
        });
    }

    private void displayOffline() {
        try {
            JSONObject snapshot = OfflineStore.load(this);
            currentSnapshot = snapshot;
            dashboard.setSnapshot(snapshot);
            showNative(false);
            Toast.makeText(this, "已打开本机缓存，修改将在联网后同步", Toast.LENGTH_LONG).show();
        } catch (Exception error) {
            showOfflineMessage("无法连接服务器，且本机暂无可用的离线内容。");
        }
    }

    private void showOfflineMessage(String message) {
        offlineVisible = true;
        dashboard.setVisibility(View.GONE);
        eventScreen.setVisibility(View.GONE);
        exportScreen.setVisibility(View.GONE);
        offline.setVisibility(View.VISIBLE);
        offline.setText(message);
        retry.setVisibility(View.VISIBLE);
        navigation.setVisibility(View.GONE);
    }

    private void retryConnection() {
        if (hasSessionCookie(CookieManager.getInstance().getCookie(ORIGIN))) {
            offlineUnlockOffered = false;
            syncSnapshot(true);
        } else showNativeAuth("login");
    }

    private void showNativeExport(String id) {
        experienceList.setVisibility(View.GONE);
        exportExperienceId = id;
        activeTab = 3;
        updateNavigation();
        dashboard.setVisibility(View.GONE);
        eventScreen.setVisibility(View.GONE);
        exportScreen.setVisibility(View.VISIBLE);
        navigation.setVisibility(View.VISIBLE);
        loadExportPdf();
    }

    private void leaveExport() {
        exportGeneration++;
        exportExperienceId = null;
        exportScreen.close();
        showExperienceList();
    }

    private void shareNativeMarkdown(String id, String title) {
        String cookie = CookieManager.getInstance().getCookie(ORIGIN);
        if (!hasSessionCookie(cookie)) {showNativeAuth("login"); return;}
        executor.execute(() -> {
            HttpURLConnection connection = null;
            try {
                String path = NativeWorkspace.EXPERIENCES + "/" + Uri.encode(id) + "/export.md";
                connection = (HttpURLConnection) new URL(ORIGIN + path).openConnection();
                connection.setRequestProperty("Cookie", cookie);
                connection.setRequestProperty("X-Awesome-Client", "android");
                connection.setConnectTimeout(8000);
                connection.setReadTimeout(15000);
                connection.setInstanceFollowRedirects(false);
                if (connection.getResponseCode() != 200) throw new java.io.IOException("导出失败");
                String content = readResponse(connection.getInputStream());
                if (content.length() > 250_000) throw new java.io.IOException("内容过长，请保存为 Markdown 文件");
                runOnUiThread(() -> {
                    if (!cookie.equals(CookieManager.getInstance().getCookie(ORIGIN)) || isFinishing()) return;
                    startActivity(Intent.createChooser(new Intent(Intent.ACTION_SEND).setType("text/plain")
                            .putExtra(Intent.EXTRA_SUBJECT, title).putExtra(Intent.EXTRA_TEXT, content), "分享面经"));
                });
            } catch (Exception error) {
                runOnUiThread(() -> Toast.makeText(this, "无法分享，可重试或保存 Markdown 文件", Toast.LENGTH_LONG).show());
            } finally {if (connection != null) connection.disconnect();}
        });
    }

    private void showExperienceList() {
        showNative(false);
        dashboard.setVisibility(View.GONE);
        experienceList.setSnapshot(currentSnapshot);
        experienceList.setVisibility(View.VISIBLE);
        activeTab = 3;
        updateNavigation();
    }

    private void loadExperiencePage(String query, String visibility, String order, int page) {
        int generation = ++experienceGeneration;
        String path = new Uri.Builder().path("/api/mobile/v1/experiences")
                .appendQueryParameter("q", query).appendQueryParameter("visibility", visibility)
                .appendQueryParameter("order", order).appendQueryParameter("page", Integer.toString(page)).build().toString();
        JSONObject cached = cachedExperience(path);
        if (cached != null) experienceList.pageResult(cached);
        String cookie = CookieManager.getInstance().getCookie(ORIGIN);
        if (!hasSessionCookie(cookie)) return;
        String owner = NativeWorkspace.value(currentSnapshot, "user_id");
        executor.execute(() -> {
            try {
                JSONObject result = readNativeJson(path, cookie);
                cacheExperience(owner, path, result);
                runOnUiThread(() -> {
                    if (generation == experienceGeneration && activeTab == 3 &&
                            cookie.equals(CookieManager.getInstance().getCookie(ORIGIN))) experienceList.pageResult(result);
                });
            } catch (Exception error) {
                runOnUiThread(() -> {
                    if (generation == experienceGeneration && activeTab == 3)
                        Toast.makeText(this, "暂时无法刷新，已保留当前列表", Toast.LENGTH_SHORT).show();
                });
            }
        });
    }

    private JSONObject cachedExperience(String path) {
        JSONObject snapshot = currentSnapshot;
        JSONArray cache = snapshot == null ? null : snapshot.optJSONArray("experience_cache");
        for (int index = 0; cache != null && index < cache.length(); index++) {
            JSONObject row = cache.optJSONObject(index);
            if (row != null && path.equals(row.optString("path"))) return row.optJSONObject("value");
        }
        return null;
    }

    private void cacheExperience(String owner, String path, JSONObject payload) {
        if (owner.isEmpty()) return;
        localWrites.execute(() -> {
            final int maxCachedChars = 2_000_000;
            int characters = payload.toString().length();
            if (characters > 500_000) return;
            synchronized (nativeStateLock) {
                JSONObject base = currentSnapshot;
                if (base == null || !owner.equals(base.optString("user_id"))) return;
                try {
                    JSONObject next = new JSONObject(base.toString());
                    JSONArray previous = next.optJSONArray("experience_cache"), updated = new JSONArray();
                    updated.put(NativeWorkspace.object("path", path, "value", payload, "fetched_at", java.time.Instant.now().toString()));
                    for (int i = 0; previous != null && i < previous.length() && updated.length() < 30; i++) {
                        JSONObject entry = previous.optJSONObject(i);
                        if (entry == null || path.equals(entry.optString("path"))) continue;
                        int length = entry.toString().length();
                        if (characters + length <= maxCachedChars) {
                            updated.put(entry);
                            characters += length;
                        }
                    }
                    next.put("experience_cache", updated);
                    OfflineStore.save(this, next);
                    currentSnapshot = next;
                } catch (Exception error) {android.util.Log.w("NativeExperience", "Cannot update experience cache", error);}
            }
        });
    }

    private void pickNativeFile(String path, java.util.function.Consumer<JSONObject> received) {
        nativeUploadPath = path;
        nativeUploadResult = received;
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType(path.endsWith("/resume") ? "application/pdf" : "*/*");
        if (!path.endsWith("/resume")) intent.putExtra(Intent.EXTRA_MIME_TYPES,
                new String[]{"text/markdown", "text/plain", "application/pdf", "application/octet-stream"});
        startActivityForResult(intent, NATIVE_UPLOAD_REQUEST);
    }

    private void workspaceRequest(String method, String path, Object body,
                                  java.util.function.Consumer<JSONObject> received) {
        if (workspaceBusy) return;
        boolean ownDetail = "GET".equals(method) && path.startsWith(NativeWorkspace.API + "/experiences/");
        JSONObject cached = ownDetail ? cachedExperience(path) : null;
        String cookie = CookieManager.getInstance().getCookie(ORIGIN);
        if (!hasSessionCookie(cookie)) {
            if (cached != null) received.accept(cached);
            else showNativeAuth("login");
            return;
        }
        String owner = NativeWorkspace.value(currentSnapshot, "user_id");
        workspaceBusy = true;
        int generation = ++workspaceGeneration;
        android.app.ProgressDialog upload = body instanceof Uri ? new android.app.ProgressDialog(this) : null;
        if (upload != null) {
            upload.setTitle(path.endsWith("/resume") ? "上传简历" : "解析面经文件");
            upload.setMessage("正在读取文件");
            upload.setProgressStyle(android.app.ProgressDialog.STYLE_HORIZONTAL);
            upload.setMax(100);
            upload.setCancelable(false);
            upload.show();
        }
        executor.execute(() -> {
            HttpURLConnection connection = null;
            long started = android.os.SystemClock.elapsedRealtime();
            int metricStatus = 0;
            try {
                connection = (HttpURLConnection) new URL(ORIGIN + path).openConnection();
                connection.setRequestMethod(method);
                connection.setRequestProperty("Cookie", cookie);
                connection.setRequestProperty("Accept", "application/json");
                connection.setRequestProperty("X-Awesome-Client", "android");
                connection.setConnectTimeout(8000);
                connection.setReadTimeout(body instanceof Uri ? 65000 : 12000);
                connection.setInstanceFollowRedirects(false);
                if (body != null) {
                    String csrf = currentCsrf(cookie);
                    if (csrf == null) throw new IllegalStateException("请重新登录");
                    connection.setRequestProperty("X-CSRF-Token", csrf);
                    connection.setDoOutput(true);
                    if (body instanceof Uri) {
                        sendWorkspaceFile(connection, (Uri) body, path.endsWith("/resume"), upload);
                    } else {
                        String encoded;
                        if (body instanceof java.util.Map) {
                            var builder = new Uri.Builder();
                            ((java.util.Map<?, ?>) body).forEach((key, value) ->
                                    builder.appendQueryParameter(key.toString(), value.toString()));
                            encoded = builder.build().getEncodedQuery();
                            if (encoded == null) encoded = "";
                            connection.setRequestProperty("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
                        } else {
                            encoded = body.toString();
                            connection.setRequestProperty("Content-Type", "application/json; charset=UTF-8");
                        }
                        try (OutputStream stream = connection.getOutputStream()) {
                            stream.write(encoded.getBytes(StandardCharsets.UTF_8));
                        }
                    }
                }
                int status = connection.getResponseCode();
                metricStatus = status;
                String redirect = connection.getHeaderField("Location");
                JSONObject result;
                if (status == 303 && redirect != null && redirect.startsWith(NativeWorkspace.EXPERIENCES)) {
                    result = new JSONObject();
                } else if (status >= 200 && status < 300) {
                    result = new JSONObject(readResponse(connection.getInputStream()));
                } else if (status == 401 || status == 403 || status == 303) {
                    throw new IllegalStateException("登录已失效或无权访问");
                } else {
                    String response = readResponse(connection.getErrorStream());
                    Object detail = response.startsWith("{") ? new JSONObject(response).opt("detail") : null;
                    throw new IllegalStateException(detail instanceof String ? (String) detail : "请求失败，请检查填写内容");
                }
                if (ownDetail) cacheExperience(owner, path, result);
                runOnUiThread(() -> {
                    if (upload != null) upload.dismiss();
                    if (generation != workspaceGeneration) return;
                    workspaceBusy = false;
                    if (path.startsWith(BASE + "/admin/") && !administrator) return;
                    if (cookie.equals(CookieManager.getInstance().getCookie(ORIGIN))) received.accept(result);
                });
            } catch (Exception error) {
                runOnUiThread(() -> {
                    if (upload != null) upload.dismiss();
                    if (generation != workspaceGeneration) return;
                    workspaceBusy = false;
                    if (cached != null && error instanceof java.io.IOException &&
                            cookie.equals(CookieManager.getInstance().getCookie(ORIGIN))) {
                        received.accept(cached);
                        Toast.makeText(this, "已打开本机缓存", Toast.LENGTH_SHORT).show();
                        return;
                    }
                    String message = error instanceof IllegalStateException ? error.getMessage() : "暂时无法连接，请重试";
                    Toast.makeText(this, message, Toast.LENGTH_LONG).show();
                });
            } finally {
                if (connection != null) connection.disconnect();
                if (body instanceof Uri) performance.record(path.endsWith("/resume") ? "resume_upload" : "parse",
                        "request", started, metricStatus);
                if (cookie.equals(sessionCookie)) performance.flush(cookie, sessionCsrf);
            }
        });
    }

    private void sendWorkspaceFile(HttpURLConnection connection, Uri uri, boolean isResume,
                                   android.app.ProgressDialog progress) throws Exception {
        int limit = (isResume ? 20 : 5) * 1024 * 1024;
        String filename = isResume ? "resume.pdf" : "面经.md";
        try (android.database.Cursor cursor = getContentResolver().query(uri,
                new String[]{android.provider.OpenableColumns.DISPLAY_NAME}, null, null, null)) {
            if (cursor != null && cursor.moveToFirst() && !cursor.isNull(0)) filename = cursor.getString(0);
        }
        String safeName = filename.replaceAll("[\\r\\n\"\\\\/]", "_");
        String lower = safeName.toLowerCase(java.util.Locale.ROOT);
        boolean pdf = lower.endsWith(".pdf") || "application/pdf".equals(getContentResolver().getType(uri));
        if (isResume && !pdf || !isResume && !pdf && !lower.endsWith(".md") && !lower.endsWith(".markdown"))
            throw new IllegalStateException(isResume ? "请选择 PDF 文件" : "请选择 Markdown 或 PDF 文件");
        if (pdf && !lower.endsWith(".pdf")) safeName += ".pdf";
        byte[] data;
        try (InputStream stream = getContentResolver().openInputStream(uri);
             ByteArrayOutputStream buffer = new ByteArrayOutputStream()) {
            if (stream == null) throw new IllegalStateException("无法读取文件");
            byte[] chunk = new byte[16 * 1024];
            int count;
            while ((count = stream.read(chunk)) >= 0) {
                if (buffer.size() + count > limit) throw new IllegalStateException("文件超过 " + limit / (1024 * 1024) + " MB");
                buffer.write(chunk, 0, count);
            }
            data = buffer.toByteArray();
        }
        if (pdf && !NativeResume.isPdf(data)) throw new IllegalStateException("PDF 文件格式无效");
        String boundary = "awesome-" + java.util.UUID.randomUUID();
        byte[] header = ("--" + boundary + "\r\nContent-Disposition: form-data; name=\"" +
                (isResume ? "resume" : "file") + "\"; filename=\"" + safeName +
                "\"\r\nContent-Type: " + (pdf ? "application/pdf" : "text/markdown") +
                "\r\n\r\n").getBytes(StandardCharsets.UTF_8);
        byte[] tail = ("\r\n--" + boundary + "--\r\n").getBytes(StandardCharsets.UTF_8);
        connection.setRequestProperty("Content-Type", "multipart/form-data; boundary=" + boundary);
        connection.setFixedLengthStreamingMode(header.length + data.length + tail.length);
        runOnUiThread(() -> progress.setMessage("正在上传"));
        try (OutputStream stream = connection.getOutputStream()) {
            stream.write(header);
            int last = -1;
            for (int offset = 0; offset < data.length; offset += 16 * 1024) {
                int size = Math.min(16 * 1024, data.length - offset);
                stream.write(data, offset, size);
                int percent = (offset + size) * 100 / data.length;
                if (percent != last) runOnUiThread(() -> progress.setProgress(percent));
                last = percent;
            }
            stream.write(tail);
        }
        runOnUiThread(() -> progress.setMessage("服务器正在解析"));
    }

    private JSONObject readNativeJson(String path, String cookie) throws Exception {
        HttpURLConnection connection = (HttpURLConnection) new URL(ORIGIN + path).openConnection();
        try {
            connection.setRequestProperty("Cookie", cookie);
            connection.setRequestProperty("Accept", "application/json");
            connection.setRequestProperty("X-Awesome-Client", "android");
            connection.setConnectTimeout(8000);
            connection.setReadTimeout(10000);
            connection.setInstanceFollowRedirects(false);
            int status = connection.getResponseCode();
            if (status == 401 || status == 303) throw new NativeSessionExpired();
            if (status != 200) throw new java.io.IOException("无法读取数据");
            return new JSONObject(readResponse(connection.getInputStream()));
        } finally {
            connection.disconnect();
        }
    }

    private static final class NativeSessionExpired extends java.io.IOException {}

    private void loadExportPdf() {
        if (exportExperienceId == null) return;
        String id = exportExperienceId;
        String query = exportScreen.query();
        int generation = ++exportGeneration;
        exportScreen.setLoading();
        String cookie = CookieManager.getInstance().getCookie(ORIGIN);
        if (cookie == null || cookie.isEmpty()) {
            exportScreen.setError("请先登录后导出");
            return;
        }
        executor.execute(() -> {
            HttpURLConnection connection = null;
            File file = null;
            try {
                file = File.createTempFile("experience-export-", ".pdf", getCacheDir());
                String source = ORIGIN + BASE + "/experiences/" + Uri.encode(id) + "/export.pdf?" + query;
                connection = (HttpURLConnection) new URL(source).openConnection();
                connection.setRequestProperty("Cookie", cookie);
                connection.setRequestProperty("X-Awesome-Client", "android");
                connection.setConnectTimeout(8000);
                connection.setReadTimeout(20000);
                connection.setInstanceFollowRedirects(false);
                if (connection.getResponseCode() != 200 ||
                        connection.getContentType() == null ||
                        !connection.getContentType().startsWith("application/pdf"))
                    throw new IllegalStateException("导出失败，请确认登录状态后重试");
                try (InputStream input = connection.getInputStream();
                     OutputStream output = new FileOutputStream(file)) { copy(input, output); }
                File ready = file;
                runOnUiThread(() -> {
                    if (generation != exportGeneration || exportScreen.getVisibility() != View.VISIBLE) {
                        ready.delete();
                        return;
                    }
                    try { exportScreen.open(ready); }
                    catch (Exception error) {
                        ready.delete();
                        exportScreen.setError("PDF 无法预览，请重试");
                    }
                });
            } catch (Exception error) {
                if (file != null) file.delete();
                runOnUiThread(() -> {
                    if (generation == exportGeneration)
                        exportScreen.setError(error.getMessage() == null ? "导出失败" : error.getMessage());
                });
            } finally {
                if (connection != null) connection.disconnect();
            }
        });
    }

    private void saveExportPage() {
        if (exportScreen.bitmap() == null) return;
        Intent save = new Intent(Intent.ACTION_CREATE_DOCUMENT);
        save.addCategory(Intent.CATEGORY_OPENABLE);
        save.setType("image/png");
        save.putExtra(Intent.EXTRA_TITLE, "面经-第" + exportScreen.pageNumber() + "页.png");
        startActivityForResult(save, PAGE_IMAGE_REQUEST);
    }

    private void saveExportPdf() {
        if (exportScreen.pdfFile() == null) return;
        Intent save = new Intent(Intent.ACTION_CREATE_DOCUMENT);
        save.addCategory(Intent.CATEGORY_OPENABLE);
        save.setType("application/pdf");
        save.putExtra(Intent.EXTRA_TITLE, "面经.pdf");
        startActivityForResult(save, EXPORT_PDF_REQUEST);
    }

    private void showDeviceStatus() {
        String synced = OfflineStore.prefs(this).getString("synced_at", "尚未同步");
        String calendarTime = currentSnapshot == null ? synced : currentSnapshot.optString("calendar_server_time", synced);
        String until = OfflineStore.prefs(this).getString("reminder_until", "尚未排期");
        try {until = OffsetDateTime.parse(until).atZoneSameInstant(CalendarProjection.DISPLAY_ZONE).toLocalDate().toString();}
        catch (java.time.DateTimeException ignored) { }
        String limited = OfflineStore.prefs(this).getBoolean("reminder_limited", false) ? "（达到 500 条上限）" : "";
        String notices = Build.VERSION.SDK_INT < 33 ||
                checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED
                ? "允许" : "未允许";
        String exact = "系统允许";
        if (Build.VERSION.SDK_INT >= 31) {
            AlarmManager alarms = (AlarmManager) getSystemService(ALARM_SERVICE);
            exact = alarms.canScheduleExactAlarms() ? "系统允许" : "未允许，提醒可能延迟";
        }
        new AlertDialog.Builder(this)
                .setTitle("本机同步与提醒")
                .setMessage("日历最近同步：" + calendarTime + "\n通知权限：" + notices + "\n精确提醒：" + exact +
                        "\n提醒覆盖至：" + until + limited)
                .setNeutralButton("系统权限", (dialog, which) -> startActivity(new Intent(
                        Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                        Uri.parse("package:" + getPackageName()))))
                .setNegativeButton("关闭", null)
                .setPositiveButton("重新同步", (dialog, which) -> {
                    fullNativeRefresh.set(true);
                    syncSnapshot(true);
                })
                .show();
    }

    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request == NATIVE_UPLOAD_REQUEST) {
            String path = nativeUploadPath;
            var received = nativeUploadResult;
            nativeUploadPath = null;
            nativeUploadResult = null;
            if (result == RESULT_OK && data != null && data.getData() != null && path != null && received != null)
                workspaceRequest("POST", path, data.getData(), received);
        } else if (request == UNLOCK_REQUEST && result == RESULT_OK) {
            displayOffline();
        } else if (request == DOWNLOAD_REQUEST && result == RESULT_OK && data != null && downloadUrl != null) {
            Uri destination = data.getData();
            String source = downloadUrl;
            downloadUrl = null;
            String cookie = CookieManager.getInstance().getCookie(ORIGIN);
            executor.execute(() -> {
                HttpURLConnection connection = null;
                try {
                    connection = (HttpURLConnection) new URL(source).openConnection();
                    connection.setRequestProperty("Cookie", cookie == null ? "" : cookie);
                    connection.setInstanceFollowRedirects(false);
                    if (connection.getResponseCode() != 200) return;
                    try (InputStream input = connection.getInputStream();
                         OutputStream output = getContentResolver().openOutputStream(destination)) {
                        if (output != null) copy(input, output);
                    }
                } catch (Exception ignored) {
                    // The export remains available online for another attempt.
                } finally {
                    if (connection != null) connection.disconnect();
                }
            });
        } else if (request == PAGE_IMAGE_REQUEST && result == RESULT_OK && data != null) {
            Bitmap bitmap = exportScreen.bitmap();
            if (bitmap != null) try (OutputStream output = getContentResolver().openOutputStream(data.getData())) {
                if (output == null || !bitmap.compress(Bitmap.CompressFormat.PNG, 100, output))
                    throw new IllegalStateException("图片保存失败");
                Toast.makeText(this, "本页图片已保存", Toast.LENGTH_SHORT).show();
            } catch (Exception error) {
                Toast.makeText(this, "图片保存失败", Toast.LENGTH_LONG).show();
            }
        } else if (request == EXPORT_PDF_REQUEST && result == RESULT_OK && data != null) {
            File source = exportScreen.pdfFile();
            if (source != null) executor.execute(() -> {
                try (InputStream input = new java.io.FileInputStream(source);
                     OutputStream output = getContentResolver().openOutputStream(data.getData())) {
                    if (output == null) throw new IllegalStateException();
                    copy(input, output);
                    runOnUiThread(() -> Toast.makeText(this, "PDF 已保存", Toast.LENGTH_SHORT).show());
                } catch (Exception error) {
                    runOnUiThread(() -> Toast.makeText(this, "PDF 保存失败", Toast.LENGTH_LONG).show());
                }
            });
        }
    }

    @Override public void onRequestPermissionsResult(int request, String[] permissions, int[] results) {
        super.onRequestPermissionsResult(request, permissions, results);
        if (request == NOTICE_REQUEST && (results.length == 0 || results[0] != PackageManager.PERMISSION_GRANTED)) {
            Toast.makeText(this, "通知权限未开启，本机不会显示日程提醒", Toast.LENGTH_LONG).show();
        }
    }

    private static void copy(InputStream input, OutputStream output) throws Exception {
        byte[] block = new byte[8192];
        int read;
        while ((read = input.read(block)) >= 0) output.write(block, 0, read);
    }

    @SuppressLint("GestureBackNavigation")
    @Override public void onBackPressed() {
        handleBack();
    }

    private void handleBack() {
        if (experienceList.getVisibility() == View.VISIBLE) {
            activeTab = 0;
            updateNavigation();
            showNative(false);
        } else if (exportScreen.getVisibility() == View.VISIBLE) {
            leaveExport();
        } else if (eventScreen.getVisibility() == View.VISIBLE) {
            activeTab = 1;
            updateNavigation();
            showNative(true);
        } else if (offlineVisible) {
            retryConnection();
        } else if (activeTab == 1) {
            activeTab = 0;
            updateNavigation();
            showNative(false);
        } else if (activeTab == 0 && dashboard.getVisibility() == View.VISIBLE) {
            finish();
        } else {
            finish();
        }
    }

    @Override protected void onDestroy() {
        syncHandler.removeCallbacks(retryNative);
        workspace.close();
        resume.close();
        performance.close();
        exportScreen.close();
        executor.shutdown();
        localWrites.shutdown();
        super.onDestroy();
    }

    @Override protected void onResume() {
        super.onResume();
        if (!initialHomePending && currentSnapshot != null) {
            resumePendingNow.set(true);
            syncSnapshot(false);
        }
    }

    @Override protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        String eventId = intent.getStringExtra("event_id");
        if (eventId != null) {
            pendingEventId = eventId;
            syncSnapshot(true);
        }
    }
}
