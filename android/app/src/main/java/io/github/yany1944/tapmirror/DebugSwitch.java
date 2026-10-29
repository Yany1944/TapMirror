package io.github.yany1944.tapmirror;

import android.content.ContentResolver;
import android.content.Context;
import android.content.pm.PackageManager;
import android.provider.Settings;

import java.net.InetSocketAddress;
import java.net.Socket;
import java.util.concurrent.TimeUnit;

/**
 * Turns adb on for a mirroring session and puts it back afterwards.
 *
 * Root mode: classic adb over TCP on a fixed port (like "adb tcpip 5555").
 * Wireless mode (no root): Android's Wireless debugging, switched through
 * WRITE_SECURE_SETTINGS, which the PC grants once over adb. Its port is random,
 * so the PC finds it over mDNS.
 */
final class DebugSwitch {
    static final int ROOT_ADB_PORT = 5555;
    private static final String ADB = "adb_enabled";
    private static final String ADB_WIFI = "adb_wifi_enabled";

    private DebugSwitch() {}

    static boolean hasSecureSettings(Context c) {
        return c.checkSelfPermission(android.Manifest.permission.WRITE_SECURE_SETTINGS)
                == PackageManager.PERMISSION_GRANTED;
    }

    /** Runs "su -c id"; on Magisk this is also what shows the grant prompt. */
    static boolean hasRoot() {
        try {
            return su("id | grep -q uid=0");
        } catch (Exception e) {
            return false;
        }
    }

    /** Checks root and remembers the answer for the setup screen. */
    static boolean checkRoot(Prefs p) {
        boolean ok = hasRoot();
        p.sp.edit().putInt("root", ok ? 1 : 0).apply();
        return ok;
    }

    /** The mode to use now, or null if the phone isn't set up for either. Call off the main thread. */
    static String pickMode(Context c, Prefs p) {
        switch (p.mode()) {
            case Prefs.MODE_ROOT:
                return checkRoot(p) ? Prefs.MODE_ROOT : null;
            case Prefs.MODE_WIRELESS:
                return hasSecureSettings(c) ? Prefs.MODE_WIRELESS : null;
            default:
                // root first: fixed port, no per-network prompts
                if (checkRoot(p)) return Prefs.MODE_ROOT;
                return hasSecureSettings(c) ? Prefs.MODE_WIRELESS : null;
        }
    }

    /**
     * Turns adb on. Returns the TCP port for the PC, or 0 if the PC has to find it (Wireless debugging).
     */
    static int enable(Context c, Prefs p, String mode) throws Exception {
        ContentResolver cr = c.getContentResolver();
        boolean first = !p.sp.getBoolean("session_active", false);
        if (Prefs.MODE_ROOT.equals(mode)) {
            String out = suOutput("echo \"prev " + get(ADB) + " " + get(ADB_WIFI) + "\"; "
                    + put(ADB, 1) + "; setprop service.adb.tcp.port " + ROOT_ADB_PORT
                    + "; stop adbd; start adbd; echo done");
            if (!out.contains("done")) throw new Exception(c.getString(R.string.err_root));
            if (first) {
                // "prev <adb> <wifi>"; a setting that was never set reads as "null"
                String[] v = out.substring(out.indexOf("prev ")).split("\\s+");
                saveSession(p, mode, "1".equals(v[1]) ? 1 : 0, "1".equals(v[2]) ? 1 : 0);
            }
            for (int i = 0; i < 50; i++) {
                if (adbListening()) return ROOT_ADB_PORT;
                Thread.sleep(100);
            }
            throw new Exception(c.getString(R.string.err_adb_start));
        }
        // Apps read the debugging settings as 0 whatever they are, so the old state is unknown (-1)
        // and nothing can be checked here: if Wireless debugging isn't allowed on this Wi-Fi yet,
        // the PC doesn't find it over mDNS and reports that.
        if (first) saveSession(p, mode, -1, -1);
        Settings.Global.putInt(cr, ADB_WIFI, 1);
        return 0;
    }

    /** Saved before the first change only: after a crash the original values are still there. */
    private static void saveSession(Prefs p, String mode, int adb, int wifi) {
        p.sp.edit()
                .putInt("prev_adb", adb)
                .putInt("prev_wifi", wifi)
                .putString("session_mode", mode)
                .putBoolean("session_active", true)
                .commit();
    }

    /** Puts debugging back as the settings say. Safe to call when nothing was changed. */
    static void disable(Context c, Prefs p) {
        if (!p.sp.getBoolean("session_active", false)) return;
        boolean restore = Prefs.AFTER_RESTORE.equals(p.after());
        int adb = restore ? p.sp.getInt("prev_adb", 0) : 0;
        int wifi = restore ? p.sp.getInt("prev_wifi", 0) : 0;
        try {
            if (Prefs.MODE_ROOT.equals(p.sp.getString("session_mode", null))) {
                su("(resetprop -d service.adb.tcp.port || setprop service.adb.tcp.port 0) 2>/dev/null"
                        + "; input keyevent 224"
                        + "; " + put(ADB_WIFI, wifi) + "; " + put(ADB, adb)
                        // restart adbd so it stops listening on the port (it keeps running for USB)
                        + (adb == 1 ? "; stop adbd; start adbd" : ""));
            } else {
                // without root the old state is unknown, so Wireless debugging always goes off
                ContentResolver cr = c.getContentResolver();
                Settings.Global.putInt(cr, ADB_WIFI, wifi == 1 ? 1 : 0);
                if (!restore) Settings.Global.putInt(cr, ADB, 0);
            }
        } catch (Exception ignored) {
            // nothing better to do; the next session or a reboot tries again
        }
        p.sp.edit().putBoolean("session_active", false).commit();
    }

    // The "settings" tool hands its stdin/stdout to system_server, which may not use an app's
    // pipes (SELinux), and then fails. So it always gets /dev/null, and values come back
    // through the root shell's own command substitution.
    private static String get(String key) {
        return "$(settings get global " + key + " </dev/null 2>/dev/null)";
    }

    private static String put(String key, int value) {
        return "settings put global " + key + " " + value + " </dev/null >/dev/null 2>&1";
    }

    static boolean adbListening() {
        try (Socket s = new Socket()) {
            s.connect(new InetSocketAddress("127.0.0.1", ROOT_ADB_PORT), 300);
            return true;
        } catch (Exception e) {
            return false;
        }
    }

    static boolean su(String cmd) throws Exception {
        Process p = new ProcessBuilder("su", "-c", cmd).redirectErrorStream(true).start();
        p.getInputStream().readAllBytes();
        if (!p.waitFor(15, TimeUnit.SECONDS)) {
            p.destroy();
            return false;
        }
        return p.exitValue() == 0;
    }

    static String suOutput(String cmd) throws Exception {
        Process p = new ProcessBuilder("su", "-c", cmd).start();
        String out = new String(p.getInputStream().readAllBytes());
        p.waitFor(15, TimeUnit.SECONDS);
        return out;
    }
}
