package xyz.hunaochufang.awesomeinterview;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;

import org.json.JSONObject;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import java.security.KeyStore;
import java.util.Arrays;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** Encrypted, owner-scoped snapshot and pending operations on this device. */
final class OfflineStore {
    private static final String KEY_ALIAS = "awesome_interview_snapshot_v1";
    private static final String FILE_NAME = "private_snapshot.bin";
    private static final String RESUME_FILE = "private_resume.bin";
    private static final String PREFS = "offline_state";

    private OfflineStore() { }

    static SharedPreferences prefs(Context context) {
        return context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    private static File file(Context context) {
        return new File(context.getFilesDir(), FILE_NAME);
    }

    private static SecretKey key() throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        store.load(null);
        if (store.containsAlias(KEY_ALIAS)) {
            return (SecretKey) store.getKey(KEY_ALIAS, null);
        }
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build());
        return generator.generateKey();
    }

    static void save(Context context, JSONObject snapshot) throws Exception {
        String userId = snapshot.getString("user_id");
        String previous = prefs(context).getString("user_id", null);
        if (previous != null && !previous.equals(userId)) clear(context);
        saveEncrypted(file(context), snapshot.toString().getBytes(StandardCharsets.UTF_8));
        prefs(context).edit().putString("user_id", userId)
                .putString("synced_at", snapshot.getString("server_time")).apply();
    }

    static JSONObject load(Context context) throws Exception {
        JSONObject snapshot = new JSONObject(new String(loadEncrypted(file(context)), StandardCharsets.UTF_8));
        if (!snapshot.getString("user_id").equals(prefs(context).getString("user_id", null))) {
            throw new IllegalStateException("账号与缓存不匹配");
        }
        return snapshot;
    }

    static void clear(Context context) {
        File cached = file(context);
        if (cached.exists() && !cached.delete()) throw new IllegalStateException("无法清除本地缓存");
        clearResume(context);
        prefs(context).edit().clear().apply();
    }

    static void saveResume(Context context, byte[] content) throws Exception {
        saveEncrypted(new File(context.getFilesDir(), RESUME_FILE), content);
    }

    static byte[] loadResume(Context context) throws Exception {
        return loadEncrypted(new File(context.getFilesDir(), RESUME_FILE));
    }

    static void clearResume(Context context) {
        File cached = new File(context.getFilesDir(), RESUME_FILE);
        if (cached.exists() && !cached.delete()) throw new IllegalStateException("无法清除简历缓存");
    }

    private static void saveEncrypted(File destination, byte[] content) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key());
        byte[] nonce = cipher.getIV();
        byte[] encrypted = cipher.doFinal(content);
        File temporary = new File(destination.getParentFile(), destination.getName() + ".tmp");
        try {
            try (var stream = new java.io.FileOutputStream(temporary)) {
                stream.write(nonce);
                stream.write(encrypted);
                stream.getFD().sync();
            }
            Files.move(temporary.toPath(), destination.toPath(),
                    StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING);
        } finally {
            Files.deleteIfExists(temporary.toPath());
        }
    }

    private static byte[] loadEncrypted(File source) throws Exception {
        byte[] input = Files.readAllBytes(source.toPath());
        if (input.length < 29) throw new IllegalStateException("缓存不完整");
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, Arrays.copyOf(input, 12)));
        return cipher.doFinal(input, 12, input.length - 12);
    }
}
