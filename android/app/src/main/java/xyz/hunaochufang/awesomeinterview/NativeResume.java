package xyz.hunaochufang.awesomeinterview;

import android.app.Activity;
import android.app.AlertDialog;
import android.graphics.Bitmap;
import android.graphics.pdf.PdfRenderer;
import android.os.ParcelFileDescriptor;
import android.webkit.CookieManager;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.concurrent.Executor;
import java.util.function.Supplier;

/** Versioned resume cache. The PDF remains encrypted at rest and renders off the UI thread. */
final class NativeResume {
    private static final int LIMIT = 20 * 1024 * 1024;
    private final Activity activity;
    private final Executor executor;
    private final String origin;
    private final Supplier<JSONObject> snapshot;
    private AlertDialog dialog;
    private TextView heading;
    private ImageView image;
    private android.widget.Button prev;
    private android.widget.Button next;
    private Runnable upload;
    private volatile int generation;
    private int page;
    private int pageCount;
    private Cached cached;

    NativeResume(Activity activity, Executor executor, String origin, Supplier<JSONObject> snapshot) {
        this.activity = activity;
        this.executor = executor;
        this.origin = origin;
        this.snapshot = snapshot;
    }

    void open(Runnable upload) {
        close();
        this.upload = upload;
        page = 0;
        var content = new LinearLayout(activity);
        content.setOrientation(LinearLayout.VERTICAL);
        int padding = Math.round(16 * activity.getResources().getDisplayMetrics().density);
        content.setPadding(padding, padding, padding, padding);
        heading = new TextView(activity);
        content.addView(heading);
        image = new ImageView(activity);
        image.setAdjustViewBounds(true);
        image.setContentDescription("简历预览");
        content.addView(image, new LinearLayout.LayoutParams(-1, -2));
        var row = new LinearLayout(activity);
        content.addView(row);
        prev = button(row, "上一页", () -> page(-1));
        next = button(row, "下一页", () -> page(1));
        var actions = new LinearLayout(activity);
        content.addView(actions);
        button(actions, "刷新", () -> load(false, null));
        button(actions, "清除缓存", () -> {
            generation++;
            cached = null;
            executor.execute(() -> {
                try {
                    OfflineStore.clearResume(activity);
                    activity.runOnUiThread(() -> {if (dialog != null) showStatus("已清除本机简历缓存");});
                } catch (Exception error) {
                    activity.runOnUiThread(() -> {if (dialog != null) showStatus("无法清除本机简历缓存");});
                }
            });
        });
        var scroll = new ScrollView(activity);
        scroll.addView(content);
        dialog = new AlertDialog.Builder(activity).setTitle("当前简历").setView(scroll)
                .setPositiveButton("上传 PDF", null).setNegativeButton("关闭", null).create();
        dialog.setOnDismissListener(ignored -> {
            generation++;
            cached = null;
            dialog = null;
        });
        dialog.show();
        dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(view -> this.upload.run());
        load(false, null);
    }

    void updated(JSONObject response) {if (dialog != null) load(true, response);}

    void close() {
        if (dialog != null) {
            dialog.setOnDismissListener(null);
            dialog.dismiss();
            dialog = null;
        }
        generation++;
        cached = null;
    }

    private void showStatus(String text) {
        heading.setText(text);
        image.setImageDrawable(null);
        prev.setEnabled(false);
        next.setEnabled(false);
    }

    private android.widget.Button button(LinearLayout row, String label, Runnable action) {
        var button = NativeButtons.make(activity, label, NativeButtons.Role.OUTLINED);
        row.addView(button, new LinearLayout.LayoutParams(0, -2, 1));
        button.setOnClickListener(view -> action.run());
        return button;
    }

    private void load(boolean replaced, JSONObject response) {
        int token = ++generation;
        page = 0;
        showStatus(replaced ? "替换成功，正在读取预览" : "正在读取简历");
        String cookie = CookieManager.getInstance().getCookie(origin);
        JSONObject account = snapshot.get();
        String owner = NativeWorkspace.value(account, "user_id");
        executor.execute(() -> {
            Cached local = null;
            try {local = Cached.read(activity, owner);} catch (Exception ignored) {}
            try {
                if (owner.isEmpty()) throw new IllegalStateException("请登录后查看简历");
                JSONObject current = response == null ? new JSONObject(new String(
                        fetch("/my-schedule/resume/current", cookie, 16 * 1024), StandardCharsets.UTF_8)) : response;
                JSONObject meta = current.optJSONObject("resume");
                if (meta == null) {
                    OfflineStore.clearResume(activity);
                    showMessage(token, "尚未上传简历");
                    return;
                }
                String scope = current.getString("cache_scope");
                String version = meta.getString("version");
                if (local == null || !version.equals(local.metadata.optString("version")) ||
                        !scope.equals(local.metadata.optString("scope"))) {
                    String path = meta.getString("pdf_url");
                    if (!path.matches("/resumes/[a-zA-Z0-9_-]+/pdf")) throw new IllegalStateException("简历下载地址无效");
                    byte[] pdf = fetch(path, cookie, LIMIT);
                    if (!isPdf(pdf)) throw new IllegalStateException("无法读取 PDF");
                    if (!sameSession(cookie, owner)) return;
                    JSONObject details = NativeWorkspace.object("owner", owner, "version", version, "scope", scope,
                            "name", meta.optString("file_name", "简历"));
                    local = new Cached(details, pdf);
                    local.save(activity);
                }
                if (sameSession(cookie, owner)) render(token, local, 0, false);
            } catch (AccessDenied error) {
                if (sameSession(cookie, owner)) {
                    OfflineStore.clearResume(activity);
                    showMessage(token, "简历不可用，请确认登录状态");
                }
            } catch (Exception error) {
                if (local != null && sameSession(cookie, owner)) render(token, local, 0, true);
                else showMessage(token, "无法读取简历，请稍后刷新");
            }
        });
    }

    private boolean sameSession(String cookie, String owner) {
        return java.util.Objects.equals(cookie, CookieManager.getInstance().getCookie(origin)) &&
                owner.equals(NativeWorkspace.value(snapshot.get(), "user_id"));
    }

    private void page(int step) {
        if (cached == null || page + step < 0 || page + step >= pageCount) return;
        int target = page + step, token = ++generation;
        Cached source = cached;
        boolean offline = heading.getText().toString().startsWith("本机缓存");
        executor.execute(() -> render(token, source, target, offline));
    }

    private void render(int token, Cached source, int pageIndex, boolean offline) {
        if (token != generation) return;
        File temporary = null;
        try {
            int count;
            Bitmap bitmap;
            temporary = File.createTempFile("resume-preview-", ".pdf", activity.getCacheDir());
            Files.write(temporary.toPath(), source.pdf);
            try (var file = ParcelFileDescriptor.open(temporary, ParcelFileDescriptor.MODE_READ_ONLY)) {
                Files.delete(temporary.toPath());
                try (var renderer = new PdfRenderer(file.dup()); var rendered = renderer.openPage(pageIndex)) {
                    count = renderer.getPageCount();
                    int height = Math.min(2000, 1100 * rendered.getHeight() / Math.max(1, rendered.getWidth()));
                    bitmap = Bitmap.createBitmap(1100, Math.max(1, height), Bitmap.Config.ARGB_8888);
                    bitmap.eraseColor(android.graphics.Color.WHITE);
                    rendered.render(bitmap, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);
                }
            }
            activity.runOnUiThread(() -> {
                if (dialog == null || token != generation) {bitmap.recycle(); return;}
                cached = source;
                page = pageIndex;
                pageCount = count;
                heading.setText((offline ? "本机缓存 · " : "") + source.metadata.optString("name") +
                        " · " + (pageIndex + 1) + "/" + count);
                image.setImageBitmap(bitmap);
                prev.setEnabled(pageIndex > 0);
                next.setEnabled(pageIndex + 1 < count);
            });
        } catch (Exception error) {
            showMessage(token, "无法预览 PDF");
        } finally {
            if (temporary != null) temporary.delete();
        }
    }

    private void showMessage(int token, String message) {
        activity.runOnUiThread(() -> {if (dialog != null && generation == token) showStatus(message);});
    }

    private byte[] fetch(String path, String cookie, int limit) throws Exception {
        HttpURLConnection connection = (HttpURLConnection) new URL(origin + path).openConnection();
        try {
            if (cookie != null) connection.setRequestProperty("Cookie", cookie);
            connection.setRequestProperty("Accept", path.endsWith("/pdf") ? "application/pdf" : "application/json");
            connection.setRequestProperty("X-Awesome-Client", "android");
            connection.setInstanceFollowRedirects(false);
            connection.setConnectTimeout(8000);
            connection.setReadTimeout(12000);
            int status = connection.getResponseCode();
            if (status == 303 || status == 401 || status == 403 || status == 404) throw new AccessDenied();
            if (status != 200) throw new java.io.IOException("简历请求失败");
            try (InputStream input = connection.getInputStream(); var bytes = new ByteArrayOutputStream()) {
                byte[] chunk = new byte[16 * 1024];
                int size;
                while ((size = input.read(chunk)) >= 0) {
                    if (bytes.size() + size > limit) throw new java.io.IOException("文件超过大小限制");
                    bytes.write(chunk, 0, size);
                }
                return bytes.toByteArray();
            }
        } finally {connection.disconnect();}
    }

    static boolean isPdf(byte[] bytes) {
        return bytes.length >= 5 && bytes[0] == '%' && bytes[1] == 'P' &&
                bytes[2] == 'D' && bytes[3] == 'F' && bytes[4] == '-';
    }

    private static final class AccessDenied extends java.io.IOException {}

    private static final class Cached {
        final JSONObject metadata;
        final byte[] pdf;

        Cached(JSONObject metadata, byte[] pdf) {this.metadata = metadata; this.pdf = pdf;}

        void save(Activity activity) throws Exception {
            byte[] info = metadata.toString().getBytes(StandardCharsets.UTF_8);
            byte[] data = ByteBuffer.allocate(4 + info.length + pdf.length)
                    .putInt(info.length).put(info).put(pdf).array();
            OfflineStore.saveResume(activity, data);
        }

        static Cached read(Activity activity, String owner) throws Exception {
            ByteBuffer data = ByteBuffer.wrap(OfflineStore.loadResume(activity));
            int size = data.getInt();
            if (size < 2 || size > 8192 || size > data.remaining()) return null;
            byte[] header = new byte[size];
            data.get(header);
            JSONObject metadata = new JSONObject(new String(header, StandardCharsets.UTF_8));
            if (owner.isEmpty() || !owner.equals(metadata.optString("owner")) || data.remaining() > LIMIT) return null;
            byte[] pdf = new byte[data.remaining()];
            data.get(pdf);
            return isPdf(pdf) ? new Cached(metadata, pdf) : null;
        }
    }
}
