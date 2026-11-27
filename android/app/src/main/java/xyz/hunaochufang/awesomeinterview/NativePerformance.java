package xyz.hunaochufang.awesomeinterview;

import android.os.SystemClock;
import android.webkit.CookieManager;

import org.json.JSONArray;
import org.json.JSONObject;

import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayDeque;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** In-memory, bounded, best-effort timings identified only by fixed node and metric. */
final class NativePerformance {
    private final String origin;
    private final ArrayDeque<JSONObject> samples = new ArrayDeque<>();
    private final ExecutorService sender = Executors.newSingleThreadExecutor();
    private boolean sending;
    private long nextAllowed;

    NativePerformance(String origin) {this.origin = origin;}

    synchronized void record(String node, String metric, long started, int status) {
        if (samples.size() == 100) samples.removeFirst();
        samples.addLast(NativeWorkspace.object("node", node, "metric", metric, "status", status,
                "duration", Math.min(120_000, SystemClock.elapsedRealtime() - started)));
    }

    synchronized void flush(String cookie, String csrf) {
        if (sending || samples.isEmpty() || csrf == null || sender.isShutdown() ||
                SystemClock.elapsedRealtime() < nextAllowed) return;
        sending = true;
        JSONArray batch = new JSONArray();
        while (!samples.isEmpty() && batch.length() < 20) batch.put(samples.removeFirst());
        sender.execute(() -> send(cookie, csrf, batch));
    }

    private void send(String cookie, String csrf, JSONArray batch) {
        HttpURLConnection connection = null;
        int status = 0;
        try {
            if (!cookie.equals(CookieManager.getInstance().getCookie(origin))) return;
            connection = (HttpURLConnection) new URL(origin + "/my-schedule/telemetry").openConnection();
            connection.setRequestMethod("POST");
            connection.setRequestProperty("X-Awesome-Client", "android");
            connection.setRequestProperty("Cookie", cookie);
            connection.setRequestProperty("X-CSRF-Token", csrf);
            connection.setRequestProperty("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
            connection.setInstanceFollowRedirects(false);
            connection.setConnectTimeout(4000);
            connection.setReadTimeout(4000);
            connection.setDoOutput(true);
            String encoded = "payload=" + URLEncoder.encode(NativeWorkspace.object("samples", batch).toString(), "UTF-8");
            try (var output = connection.getOutputStream()) {output.write(encoded.getBytes(StandardCharsets.UTF_8));}
            status = connection.getResponseCode();
        } catch (Exception ignored) {
            // Metrics are disposable and never hold up a user action.
        } finally {
            if (connection != null) connection.disconnect();
            synchronized (this) {
                sending = false;
                nextAllowed = SystemClock.elapsedRealtime() + (status == 429 ? 60_000 : 15_000);
            }
        }
    }

    synchronized void clear() {samples.clear();}

    synchronized void close() {clear(); sender.shutdownNow();}
}
