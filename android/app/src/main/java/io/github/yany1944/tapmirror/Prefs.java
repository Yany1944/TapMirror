package io.github.yany1944.tapmirror;

import android.content.Context;
import android.content.SharedPreferences;

import java.security.SecureRandom;

/** All app settings in one place. */
final class Prefs {
    static final String MODE_AUTO = "auto";
    static final String MODE_ROOT = "root";
    static final String MODE_WIRELESS = "wireless";

    static final String AFTER_RESTORE = "restore";
    static final String AFTER_OFF = "off";

    final SharedPreferences sp;

    Prefs(Context c) {
        sp = c.getSharedPreferences("tapmirror", Context.MODE_PRIVATE);
    }

    /** Random id the PC uses to remember that this phone is allowed. */
    String phoneId() {
        String id = sp.getString("phone_id", null);
        if (id == null) {
            byte[] b = new byte[8];
            new SecureRandom().nextBytes(b);
            StringBuilder sb = new StringBuilder();
            for (byte x : b) sb.append(String.format("%02x", x));
            id = sb.toString();
            sp.edit().putString("phone_id", id).apply();
        }
        return id;
    }

    String mode() { return sp.getString("mode", MODE_AUTO); }
    String after() { return sp.getString("after", AFTER_RESTORE); }

    String pcId() { return sp.getString("pc_id", null); }
    String pcName() { return sp.getString("pc_name", null); }
    String pcIp() { return sp.getString("pc_ip", null); }
    void setPc(Net.Pc pc) {
        sp.edit().putString("pc_id", pc.id).putString("pc_name", pc.name)
                .putString("pc_ip", pc.addr.getAddress().getHostAddress()).apply();
    }

    // scrcpy options, sent to the PC with each request
    String maxSize() { return sp.getString("max_size", "0"); }
    String bitrate() { return sp.getString("bitrate", "8"); }
    String maxFps() { return sp.getString("max_fps", "0"); }
    String codec() { return sp.getString("codec", "h264"); }
    boolean video() { return sp.getBoolean("video", true); }
    boolean audio() { return sp.getBoolean("audio", true); }
    boolean screenOff() { return sp.getBoolean("screen_off", false); }
    boolean stayAwake() { return sp.getBoolean("stay_awake", true); }

    void put(String key, String value) { sp.edit().putString(key, value).apply(); }
    void put(String key, boolean value) { sp.edit().putBoolean(key, value).apply(); }
}
