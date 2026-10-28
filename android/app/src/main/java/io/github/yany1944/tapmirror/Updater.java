package io.github.yany1944.tapmirror;

import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageInstaller;
import android.content.pm.PackageManager;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.security.MessageDigest;

/**
 * Checks GitHub for a newer release and installs its APK over this one.
 * The APK must be signed with the same key, which every release is.
 */
final class Updater {
    private static final String API = "https://api.github.com/repos/Yany1944/TapMirror/releases/latest";
    private static final long CHECK_EVERY_MS = 60 * 60 * 1000; // at most once an hour, when the app opens

    interface Progress {
        void on(int percent);
    }

    private Updater() {}

    static String currentVersion(Context c) {
        try {
            return c.getPackageManager().getPackageInfo(c.getPackageName(), 0).versionName;
        } catch (PackageManager.NameNotFoundException e) {
            return "0";
        }
    }

    /** The tag of a newer release ("v0.1.2"), from the cache or from GitHub; null if there is none. */
    static String newerTag(Context c, Prefs p) throws Exception {
        long now = System.currentTimeMillis();
        if (now - p.sp.getLong("update_checked", 0) > CHECK_EVERY_MS) {
            JSONObject release = new JSONObject(new String(get(API), "UTF-8"));
            p.sp.edit().putString("update_tag", release.optString("tag_name", "")).putLong("update_checked", now).apply();
        }
        String tag = p.sp.getString("update_tag", "");
        return compare(tag, currentVersion(c)) > 0 ? tag : null;
    }

    static String version(String tag) {
        return tag.replaceFirst("^[vV]", "");
    }

    /** Compares "v0.1.10" with "0.1.9" by number; a "-beta" suffix is ignored. */
    static int compare(String a, String b) {
        String[] x = version(a).split("-")[0].split("\\."), y = version(b).split("-")[0].split("\\.");
        for (int i = 0; i < Math.max(x.length, y.length); i++) {
            int u = i < x.length ? parse(x[i]) : 0, v = i < y.length ? parse(y[i]) : 0;
            if (u != v) return Integer.compare(u, v);
        }
        return 0;
    }

    private static int parse(String s) {
        try {
            return Integer.parseInt(s.trim());
        } catch (NumberFormatException e) {
            return 0;
        }
    }

    /** Downloads the release's APK, checks it against SHA256SUMS.txt and hands it to the system installer. */
    static void install(Context c, String tag, Progress progress) throws Exception {
        String base = MainActivity.REPO + "/releases/download/" + tag + "/";
        File apk = new File(c.getCacheDir(), "update.apk");
        download(base + "TapMirror.apk", apk, progress);

        String expected = null;
        for (String line : new String(get(base + "SHA256SUMS.txt"), "UTF-8").split("\n")) {
            if (line.trim().endsWith("TapMirror.apk")) expected = line.trim().split("\\s+")[0].toLowerCase();
        }
        if (expected == null || !expected.equals(sha256(apk))) {
            apk.delete();
            throw new Exception(c.getString(R.string.update_bad_checksum));
        }

        PackageInstaller installer = c.getPackageManager().getPackageInstaller();
        PackageInstaller.SessionParams params = new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);
        params.setAppPackageName(c.getPackageName());
        int id = installer.createSession(params);
        try (PackageInstaller.Session session = installer.openSession(id)) {
            try (InputStream in = new FileInputStream(apk);
                 OutputStream out = session.openWrite("TapMirror.apk", 0, apk.length())) {
                copy(in, out, null, 0);
                session.fsync(out);
            }
            // the system replies to UpdateReceiver: first "needs the user's OK", then success or failure
            PendingIntent reply = PendingIntent.getBroadcast(c, 0, new Intent(c, UpdateReceiver.class),
                    PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_MUTABLE);
            session.commit(reply.getIntentSender());
        }
        apk.delete();
    }

    private static byte[] get(String url) throws Exception {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        HttpURLConnection conn = open(url);
        try (InputStream in = conn.getInputStream()) {
            copy(in, out, null, 0);
        } finally {
            conn.disconnect();
        }
        return out.toByteArray();
    }

    private static void download(String url, File to, Progress progress) throws Exception {
        HttpURLConnection conn = open(url);
        try (InputStream in = conn.getInputStream(); OutputStream out = new FileOutputStream(to)) {
            copy(in, out, progress, conn.getContentLengthLong());
        } finally {
            conn.disconnect();
        }
    }

    private static HttpURLConnection open(String url) throws Exception {
        HttpURLConnection conn = (HttpURLConnection) new URL(url).openConnection();
        conn.setConnectTimeout(10_000);
        conn.setReadTimeout(30_000);
        conn.setRequestProperty("User-Agent", "TapMirror-Android");
        conn.setInstanceFollowRedirects(true); // release files redirect to GitHub's file host
        if (conn.getResponseCode() != 200) throw new Exception("HTTP " + conn.getResponseCode() + " for " + url);
        return conn;
    }

    private static void copy(InputStream in, OutputStream out, Progress progress, long total) throws Exception {
        byte[] buf = new byte[64 * 1024];
        long done = 0;
        for (int n; (n = in.read(buf)) > 0; ) {
            out.write(buf, 0, n);
            done += n;
            if (progress != null && total > 0) progress.on((int) (done * 100 / total));
        }
    }

    private static String sha256(File f) throws Exception {
        MessageDigest md = MessageDigest.getInstance("SHA-256");
        try (InputStream in = new FileInputStream(f)) {
            byte[] buf = new byte[64 * 1024];
            for (int n; (n = in.read(buf)) > 0; ) md.update(buf, 0, n);
        }
        StringBuilder sb = new StringBuilder();
        for (byte b : md.digest()) sb.append(String.format("%02x", b));
        return sb.toString();
    }
}
