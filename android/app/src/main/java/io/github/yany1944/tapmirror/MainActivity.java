package io.github.yany1944.tapmirror;

import android.Manifest;
import android.app.Activity;
import android.app.NotificationManager;
import android.app.StatusBarManager;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.ComponentName;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.drawable.Icon;
import android.net.Network;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.view.View;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.RadioButton;
import android.widget.RadioGroup;
import android.widget.Spinner;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

import java.net.DatagramSocket;
import java.util.Arrays;
import java.util.List;

/** One screen: status, a setup checklist that doubles as the guide, mirroring settings and help. */
public class MainActivity extends Activity {
    static final String REPO = "https://github.com/Yany1944/TapMirror";

    private Prefs p;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable poll = new Runnable() {
        @Override
        public void run() {
            renderSession();
            handler.postDelayed(this, 700);
        }
    };

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        setContentView(R.layout.activity_main);
        p = new Prefs(this);

        findViewById(R.id.btnToggle).setOnClickListener(v -> {
            if (MirrorService.state == MirrorService.IDLE) MirrorService.start(this);
            else MirrorService.stop(this);
        });
        findViewById(R.id.btnDownload).setOnClickListener(v -> open(REPO + "/releases/latest"));
        findViewById(R.id.btnGithub).setOnClickListener(v -> open(REPO));
        findViewById(R.id.btnFind).setOnClickListener(v -> findPcs());
        findViewById(R.id.btnRoot).setOnClickListener(v -> checkRoot());
        findViewById(R.id.btnCopy).setOnClickListener(v -> {
            getSystemService(ClipboardManager.class)
                    .setPrimaryClip(ClipData.newPlainText("adb", grantCommand()));
            Toast.makeText(this, R.string.copied, Toast.LENGTH_SHORT).show();
        });
        findViewById(R.id.btnDev).setOnClickListener(v -> {
            try {
                startActivity(new Intent(Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS));
            } catch (Exception e) {
                startActivity(new Intent(Settings.ACTION_DEVICE_INFO_SETTINGS));
            }
        });
        findViewById(R.id.btnTile).setOnClickListener(v -> addTile());
        findViewById(R.id.btnNotif).setOnClickListener(v -> askNotifications());
        ((TextView) findViewById(R.id.tvCommand)).setText(grantCommand());

        spinner(R.id.spMode, R.array.mode_entries, R.array.mode_values, "mode", p.mode());
        spinner(R.id.spSize, R.array.size_entries, R.array.size_values, "max_size", p.maxSize());
        spinner(R.id.spBitrate, R.array.bitrate_entries, R.array.bitrate_values, "bitrate", p.bitrate());
        spinner(R.id.spFps, R.array.fps_entries, R.array.fps_values, "max_fps", p.maxFps());
        spinner(R.id.spCodec, R.array.codec_entries, R.array.codec_values, "codec", p.codec());
        spinner(R.id.spAfter, R.array.after_entries, R.array.after_values, "after", p.after());
        toggle(R.id.swAudio, "audio", p.audio());
        toggle(R.id.swScreenOff, "screen_off", p.screenOff());
        toggle(R.id.swAwake, "stay_awake", p.stayAwake());

        try {
            String v = getPackageManager().getPackageInfo(getPackageName(), 0).versionName;
            ((TextView) findViewById(R.id.tvVersion)).setText("TapMirror " + v);
        } catch (PackageManager.NameNotFoundException ignored) {
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        refreshSetup();
        handler.post(poll);
    }

    @Override
    protected void onPause() {
        super.onPause();
        handler.removeCallbacks(poll);
    }

    @Override
    public void onRequestPermissionsResult(int code, String[] perms, int[] results) {
        if (!notificationsAllowed()) {
            // denied for good: only the system settings page can change it now
            startActivity(new Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS)
                    .putExtra(Settings.EXTRA_APP_PACKAGE, getPackageName()));
        }
        refreshSetup();
    }

    private void renderSession() {
        TextView status = findViewById(R.id.tvStatus);
        Button btn = findViewById(R.id.btnToggle);
        String d = MirrorService.detail;
        switch (MirrorService.state) {
            case MirrorService.SEARCHING:
                status.setText(R.string.st_searching);
                break;
            case MirrorService.CONNECTING:
                status.setText(getString(R.string.st_connecting, d));
                break;
            case MirrorService.APPROVAL:
                status.setText(getString(R.string.st_approval, d) + "\n" + getString(R.string.st_approval_text));
                break;
            case MirrorService.MIRRORING:
                status.setText(getString(R.string.st_mirroring, d));
                break;
            default:
                status.setText(d.isEmpty() ? getString(R.string.st_idle) : getString(R.string.st_last_error, d));
        }
        btn.setText(MirrorService.state == MirrorService.IDLE ? R.string.start : R.string.stop);
    }

    private void refreshSetup() {
        String pc = p.pcName();
        mark(R.id.tvPc, pc != null, pc != null ? getString(R.string.find_result, pc) : getString(R.string.find_none));

        int root = p.sp.getInt("root", -1);
        mark(R.id.tvRoot, root == 1, getString(root == 1 ? R.string.access_root_ok
                : root == 0 ? R.string.access_root_no : R.string.access_root_unknown));
        boolean wss = DebugSwitch.hasSecureSettings(this);
        mark(R.id.tvWss, wss, getString(wss ? R.string.access_wss_ok : R.string.access_wss_no));

        boolean dev = Settings.Global.getInt(getContentResolver(), Settings.Global.DEVELOPMENT_SETTINGS_ENABLED, 0) == 1;
        mark(R.id.tvDev, dev, getString(dev ? R.string.dev_on : R.string.dev_off));

        boolean tile = p.sp.getBoolean("tile_added", false);
        mark(R.id.tvTile, tile, getString(tile ? R.string.tile_added : R.string.tile_not_added));
        findViewById(R.id.btnTile).setVisibility(Build.VERSION.SDK_INT >= 33 ? View.VISIBLE : View.GONE);

        boolean notif = notificationsAllowed();
        mark(R.id.tvNotif, notif, getString(notif ? R.string.notif_on : R.string.notif_off));
        findViewById(R.id.btnNotif).setVisibility(notif ? View.GONE : View.VISIBLE);
    }

    private void mark(int id, boolean ok, String text) {
        TextView t = findViewById(id);
        t.setText(getString(ok ? R.string.ok : R.string.todo) + "  " + text);
        t.setTextColor(getColor(ok ? R.color.ok : R.color.todo));
    }

    private String grantCommand() {
        return "adb shell pm grant " + getPackageName() + " android.permission.WRITE_SECURE_SETTINGS";
    }

    private void findPcs() {
        TextView tv = findViewById(R.id.tvPc);
        tv.setText(R.string.find_searching);
        new Thread(() -> {
            List<Net.Pc> pcs = null;
            String error = null;
            Network wifi = Net.wifi(this);
            if (wifi == null) {
                error = getString(R.string.err_no_wifi);
            } else {
                try (DatagramSocket s = Net.openSocket(wifi)) {
                    pcs = Net.discover(this, s, wifi, p.phoneId(), p.pcIp());
                } catch (Exception e) {
                    error = e.toString();
                }
            }
            List<Net.Pc> found = pcs;
            String err = error;
            runOnUiThread(() -> showPcs(found, err));
        }).start();
    }

    private void showPcs(List<Net.Pc> pcs, String error) {
        RadioGroup group = findViewById(R.id.pcGroup);
        group.setOnCheckedChangeListener(null);
        group.removeAllViews();
        if (error != null) Toast.makeText(this, error, Toast.LENGTH_LONG).show();
        if (pcs == null || pcs.isEmpty()) {
            if (error == null) Toast.makeText(this, R.string.err_no_pc, Toast.LENGTH_LONG).show();
            refreshSetup();
            return;
        }
        Net.Pc chosen = pcs.get(0);
        for (Net.Pc x : pcs) if (x.id.equals(p.pcId())) chosen = x;
        p.setPc(chosen);
        for (int i = 0; i < pcs.size(); i++) {
            Net.Pc pc = pcs.get(i);
            RadioButton rb = new RadioButton(this);
            rb.setId(View.generateViewId());
            rb.setText(pc.name + "  (" + pc.addr.getAddress().getHostAddress() + ")");
            rb.setTag(pc);
            group.addView(rb);
            if (pc.id.equals(p.pcId())) rb.setChecked(true);
        }
        group.setOnCheckedChangeListener((g, checkedId) -> {
            p.setPc((Net.Pc) g.findViewById(checkedId).getTag());
            refreshSetup();
        });
        refreshSetup();
    }

    private void checkRoot() {
        new Thread(() -> {
            DebugSwitch.checkRoot(p);
            runOnUiThread(this::refreshSetup);
        }).start();
    }

    private void addTile() {
        if (Build.VERSION.SDK_INT < 33) return;
        getSystemService(StatusBarManager.class).requestAddTileService(
                new ComponentName(this, MirrorTile.class), getString(R.string.tile_label),
                Icon.createWithResource(this, R.drawable.ic_tile), getMainExecutor(), result -> {
                    if (result == StatusBarManager.TILE_ADD_REQUEST_RESULT_TILE_ADDED
                            || result == StatusBarManager.TILE_ADD_REQUEST_RESULT_TILE_ALREADY_ADDED) {
                        p.put("tile_added", true);
                    }
                    refreshSetup();
                });
    }

    private boolean notificationsAllowed() {
        return getSystemService(NotificationManager.class).areNotificationsEnabled();
    }

    private void askNotifications() {
        if (Build.VERSION.SDK_INT >= 33
                && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 1);
        } else {
            startActivity(new Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS)
                    .putExtra(Settings.EXTRA_APP_PACKAGE, getPackageName()));
        }
    }

    private void open(String url) {
        startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)));
    }

    private void spinner(int id, int entries, int values, String key, String current) {
        Spinner s = findViewById(id);
        ArrayAdapter<CharSequence> a = ArrayAdapter.createFromResource(this, entries, android.R.layout.simple_spinner_item);
        a.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        s.setAdapter(a);
        String[] vals = getResources().getStringArray(values);
        s.setSelection(Math.max(0, Arrays.asList(vals).indexOf(current)), false);
        s.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override
            public void onItemSelected(AdapterView<?> parent, View view, int pos, long rowId) {
                p.put(key, vals[pos]);
            }

            @Override
            public void onNothingSelected(AdapterView<?> parent) {
            }
        });
    }

    private void toggle(int id, String key, boolean current) {
        Switch sw = findViewById(id);
        sw.setChecked(current);
        sw.setOnCheckedChangeListener((b, on) -> p.put(key, on));
    }
}
