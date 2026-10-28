package io.github.yany1944.tapmirror;

import android.app.NotificationManager;
import android.content.Context;
import android.content.pm.PackageManager;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.Build;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.util.ArrayDeque;

/** Collects what a bug report needs: versions, setup state, the last error and this app's recent log. */
final class BugReport {
    private BugReport() {}

    static String collect(Context c, Prefs p) {
        StringBuilder sb = new StringBuilder();
        String version = "?";
        try {
            version = c.getPackageManager().getPackageInfo(c.getPackageName(), 0).versionName;
        } catch (PackageManager.NameNotFoundException ignored) {
        }
        int root = p.sp.getInt("root", -1);
        sb.append("TapMirror app: ").append(version).append('\n')
                .append("Device: ").append(Build.MANUFACTURER).append(' ').append(Build.MODEL)
                .append(", Android ").append(Build.VERSION.RELEASE).append(" (SDK ").append(Build.VERSION.SDK_INT).append(")\n")
                .append("Mode setting: ").append(p.mode())
                .append(", root: ").append(root == 1 ? "yes" : root == 0 ? "no" : "not checked")
                .append(", no-root permission: ").append(DebugSwitch.hasSecureSettings(c) ? "yes" : "no").append('\n')
                .append("VPN active: ").append(vpnActive(c) ? "yes" : "no")
                .append(", PC found before: ").append(p.pcId() != null ? "yes" : "no")
                .append(", notifications: ").append(c.getSystemService(NotificationManager.class)
                        .areNotificationsEnabled() ? "on" : "off").append('\n');
        if (MirrorService.state == MirrorService.IDLE && !MirrorService.detail.isEmpty()) {
            sb.append("Last error: ").append(MirrorService.detail).append('\n');
        }
        sb.append("\nLog:\n").append(log());
        return sb.toString();
    }

    private static boolean vpnActive(Context c) {
        ConnectivityManager cm = c.getSystemService(ConnectivityManager.class);
        for (Network n : cm.getAllNetworks()) {
            NetworkCapabilities nc = cm.getNetworkCapabilities(n);
            if (nc != null && nc.hasTransport(NetworkCapabilities.TRANSPORT_VPN)) return true;
        }
        return false;
    }

    /** The last lines this app logged (apps can read their own log without any permission). */
    private static String log() {
        ArrayDeque<String> lines = new ArrayDeque<>();
        try {
            Process proc = new ProcessBuilder("logcat", "-d", "-v", "time", "-t", "500").start();
            try (BufferedReader r = new BufferedReader(new InputStreamReader(proc.getInputStream()))) {
                for (String l; (l = r.readLine()) != null; ) {
                    if (!l.contains(Net.TAG) && !l.contains("AndroidRuntime")) continue;
                    lines.add(l);
                    if (lines.size() > 80) lines.removeFirst();
                }
            }
        } catch (Exception e) {
            return "(can't read the log: " + e + ")\n";
        }
        if (lines.isEmpty()) return "(nothing logged since the app started; try mirroring once, then copy again)\n";
        return String.join("\n", lines) + "\n";
    }
}
