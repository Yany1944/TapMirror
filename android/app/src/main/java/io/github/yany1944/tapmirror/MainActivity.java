package io.github.yany1944.tapmirror;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
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
import android.widget.EditText;
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
        findViewById(R.id.btnReport).setOnClickListener(v -> new Thread(() -> {
            String report = BugReport.collect(this, p);
            runOnUiThread(() -> {
                getSystemService(ClipboardManager.class).setPrimaryClip(ClipData.newPlainText("TapMirror", report));
                Toast.makeText(this, R.string.report_copied, Toast.LENGTH_LONG).show();
            });
        }).start());
        findViewById(R.id.btnIssue).setOnClickListener(v -> open(REPO + "/issues/new/choose"));
        findViewById(R.id.btnUpdate).setOnClickListener(v -> update());
        findViewById(R.id.btnWhatsNew).setOnClickListener(v -> open(REPO + "/releases/tag/" + updateTag));
        findViewById(R.id.btnFind).setOnClickListener(v -> findPcs(p.pcIp()));
        findViewById(R.id.btnAddress).setOnClickListener(v -> askAddress());
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
        findViewById(R.id.btnCheckUpdates).setOnClickListener(v -> checkUpdateManual());
        findViewById(R.id.btnNotif).setOnClickListener(v -> {
            if (notificationsAllowed()) openNotificationSettings();
            else askNotifications();
        });
        ((TextView) findViewById(R.id.tvCommand)).setText(grantCommand());

        spinner(R.id.spMode, R.array.mode_entries, R.array.mode_values, "mode", p.mode());
        spinner(R.id.spSize, R.array.size_entries, R.array.size_values, "max_size", p.maxSize());
        spinner(R.id.spBitrate, R.array.bitrate_entries, R.array.bitrate_values, "bitrate", p.bitrate());
        spinner(R.id.spFps, R.array.fps_entries, R.array.fps_values, "max_fps", p.maxFps());
        spinner(R.id.spCodec, R.array.codec_entries, R.array.codec_values, "codec", p.codec());
        spinner(R.id.spAfter, R.array.after_entries, R.array.after_values, "after", p.after());
        Switch swVideo = findViewById(R.id.swVideo);
        Switch swAudio = findViewById(R.id.swAudio);
        swVideo.setChecked(p.video());
        swAudio.setChecked(p.audio());
        swVideo.setOnCheckedChangeListener((b, on) -> {
            if (!on && !swAudio.isChecked()) {
                swAudio.setChecked(true);
                p.put("audio", true);
            }
            p.put("video", on);
        });
        swAudio.setOnCheckedChangeListener((b, on) -> {
            if (!on && !swVideo.isChecked()) {
                swVideo.setChecked(true);
                p.put("video", true);
            }
            p.put("audio", on);
        });
        toggle(R.id.swScreenOff, "screen_off", p.screenOff());
        toggle(R.id.swAwake, "stay_awake", p.stayAwake());

        try {
            String v = getPackageManager().getPackageInfo(getPackageName(), 0).versionName;
            ((TextView) findViewById(R.id.tvVersionTop)).setText("v " + v);
            ((TextView) findViewById(R.id.tvVersion)).setText("TapMirror " + v);
        } catch (PackageManager.NameNotFoundException ignored) {
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        checkUpdate();
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
                String text = getString(R.string.st_mirroring, d);
                if (!p.video()) text += "\n" + getString(R.string.notif_no_video);
                status.setText(text);
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

        // no status for Developer options: Android reports them as off to apps either way

        boolean tile = p.sp.getBoolean("tile_added", false);
        mark(R.id.tvTile, tile, getString(tile ? R.string.tile_added : R.string.tile_not_added));
        findViewById(R.id.btnTile).setVisibility(Build.VERSION.SDK_INT >= 33 ? View.VISIBLE : View.GONE);

        // both are fine: with notifications off, the tile alone shows the state
        boolean notif = notificationsAllowed();
        mark(R.id.tvNotif, true, getString(notif ? R.string.notif_on : R.string.notif_tile_only));
        ((Button) findViewById(R.id.btnNotif)).setText(notif ? R.string.notif_turn_off : R.string.notif_turn_on);
    }

    private void mark(int id, boolean ok, String text) {
        TextView t = findViewById(id);
        t.setText(getString(ok ? R.string.ok : R.string.todo) + "  " + text);
        t.setTextColor(getColor(ok ? R.color.ok : R.color.todo));
    }

    private String grantCommand() {
        return "adb shell pm grant " + getPackageName() + " android.permission.WRITE_SECURE_SETTINGS";
    }

    /** For networks where searching can't work (some VPNs): the PC shows its address in the tray menu. */
    private void askAddress() {
        EditText input = new EditText(this);
        input.setHint("192.168.1.10");
        input.setInputType(android.text.InputType.TYPE_CLASS_PHONE);
        if (p.pcIp() != null) input.setText(p.pcIp());
        // the dialog gives a custom view no side margins of its own
        android.widget.FrameLayout box = new android.widget.FrameLayout(this);
        int pad = (int) (22 * getResources().getDisplayMetrics().density);
        box.setPadding(pad, 0, pad, 0);
        box.addView(input);
        new AlertDialog.Builder(this)
                .setTitle(R.string.address_title)
                .setMessage(R.string.address_body)
                .setView(box)
                .setPositiveButton(android.R.string.ok, (d, w) -> {
                    String ip = input.getText().toString().trim();
                    if (ip.matches("\\d{1,3}(\\.\\d{1,3}){3}")) findPcs(ip);
                })
                .setNegativeButton(android.R.string.cancel, null)
                .show();
    }

    /** Searches the network; {@code knownIp} is asked first, directly (works through VPNs). */
    private String updateTag;

    /** Shows the update card if GitHub has a newer release (asks GitHub at most once an hour). */
    private void checkUpdate() {
        new Thread(() -> {
            String tag;
            try {
                tag = Updater.newerTag(this, p);
            } catch (Exception e) {
                return; // offline or GitHub unreachable: try next time
            }
            runOnUiThread(() -> {
                updateTag = tag;
                findViewById(R.id.cardUpdate).setVisibility(tag != null ? View.VISIBLE : View.GONE);
                if (tag != null) {
                    ((TextView) findViewById(R.id.tvUpdate)).setText(getString(R.string.update_available,
                            Updater.version(tag), Updater.currentVersion(this)));
                }
            });
        }).start();
    }

    private void checkUpdateManual() {
        Button btn = findViewById(R.id.btnCheckUpdates);
        btn.setEnabled(false);
        Toast.makeText(this, R.string.checking_updates, Toast.LENGTH_SHORT).show();
        new Thread(() -> {
            String tag = null;
            boolean err = false;
            try {
                tag = Updater.newerTag(this, p, true);
            } catch (Exception e) {
                err = true;
            }
            final String finalTag = tag;
            final boolean finalErr = err;
            runOnUiThread(() -> {
                btn.setEnabled(true);
                if (finalErr) {
                    Toast.makeText(this, R.string.check_updates_failed, Toast.LENGTH_SHORT).show();
                    return;
                }
                updateTag = finalTag;
                findViewById(R.id.cardUpdate).setVisibility(finalTag != null ? View.VISIBLE : View.GONE);
                if (finalTag != null) {
                    ((TextView) findViewById(R.id.tvUpdate)).setText(getString(R.string.update_available,
                            Updater.version(finalTag), Updater.currentVersion(this)));
                } else {
                    Toast.makeText(this, getString(R.string.update_latest, Updater.currentVersion(this)),
                            Toast.LENGTH_SHORT).show();
                }
            });
        }).start();
    }

    private void update() {
        if (updateTag == null) return;
        if (MirrorService.state != MirrorService.IDLE) {
            Toast.makeText(this, R.string.update_stop_first, Toast.LENGTH_LONG).show();
            return;
        }
        if (!getPackageManager().canRequestPackageInstalls()) {
            Toast.makeText(this, R.string.update_allow, Toast.LENGTH_LONG).show();
            startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                    Uri.parse("package:" + getPackageName())));
            return;
        }
        Button btn = findViewById(R.id.btnUpdate);
        btn.setEnabled(false);
        String tag = updateTag;
        new Thread(() -> {
            try {
                Updater.install(this, tag, pct -> runOnUiThread(() -> btn.setText(getString(R.string.update_downloading, pct))));
            } catch (Exception e) {
                String msg = e.getMessage() != null ? e.getMessage() : e.toString();
                runOnUiThread(() -> Toast.makeText(this, getString(R.string.update_failed, msg), Toast.LENGTH_LONG).show());
            }
            runOnUiThread(() -> {
                btn.setEnabled(true);
                btn.setText(R.string.update);
            });
        }).start();
    }

    private void findPcs(String knownIp) {
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
                    pcs = Net.discover(this, s, wifi, p.phoneId(), knownIp);
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
        // the system asks only while the user hasn't refused for good; otherwise use its settings page
        if (Build.VERSION.SDK_INT >= 33 && shouldShowRequestPermissionRationale(Manifest.permission.POST_NOTIFICATIONS)
                || !p.sp.getBoolean("notif_asked", false)) {
            p.put("notif_asked", true);
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 1);
        } else {
            openNotificationSettings();
        }
    }

    private void openNotificationSettings() {
        startActivity(new Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS)
                .putExtra(Settings.EXTRA_APP_PACKAGE, getPackageName()));
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
