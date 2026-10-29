package io.github.yany1944.tapmirror;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.net.Network;
import android.os.Build;
import android.os.IBinder;
import android.service.quicksettings.TileService;
import android.util.Log;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.SocketTimeoutException;
import java.security.SecureRandom;
import java.util.List;

/**
 * One mirroring session: find the PC, turn adb on, ask the PC to start scrcpy,
 * keep in touch with it, and turn adb off again when anything ends the session.
 * Runs as a foreground service so Android doesn't freeze it while the shade is closed.
 */
public class MirrorService extends Service {
    static final int IDLE = 0, SEARCHING = 1, CONNECTING = 2, APPROVAL = 3, MIRRORING = 4;

    static volatile int state = IDLE;
    /** PC name during a session; the error text after a failed one. */
    static volatile String detail = "";

    private static final String ACTION_START = "start";
    private static final String ACTION_STOP = "stop";
    private static final int NOTIF_SESSION = 1;
    private static final int NOTIF_ERROR = 2;

    private volatile boolean stopRequested;
    private volatile Thread worker;

    static void start(Context c) {
        c.startForegroundService(new Intent(c, MirrorService.class).setAction(ACTION_START));
    }

    static void stop(Context c) {
        try {
            c.startService(new Intent(c, MirrorService.class).setAction(ACTION_STOP));
        } catch (IllegalStateException ignored) {
            // not running
        }
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        NotificationManager nm = getSystemService(NotificationManager.class);
        nm.createNotificationChannel(new NotificationChannel("session",
                getString(R.string.channel_session), NotificationManager.IMPORTANCE_LOW));
        nm.createNotificationChannel(new NotificationChannel("error",
                getString(R.string.channel_error), NotificationManager.IMPORTANCE_DEFAULT));
        startForeground(NOTIF_SESSION, sessionNotification(), ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE);

        if (intent != null && ACTION_STOP.equals(intent.getAction())) {
            stopRequested = true;
            if (worker == null) finishService();
        } else if (worker == null) {
            stopRequested = false;
            nm.cancel(NOTIF_ERROR);
            worker = new Thread(this::session, "session");
            worker.start();
        }
        return START_NOT_STICKY;
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    private void session() {
        Prefs p = new Prefs(this);
        String error = null;
        DatagramSocket s = null;
        try {
            setState(SEARCHING, "");
            Network wifi = Net.wifi(this);
            if (wifi == null) throw new Exception(getString(R.string.err_no_wifi));
            s = Net.openSocket(wifi);

            List<Net.Pc> pcs = Net.discover(this, s, wifi, p.phoneId(), p.pcIp());
            if (stopRequested) return;
            if (pcs.isEmpty()) throw new Exception(getString(R.string.err_no_pc));
            Net.Pc pc = pcs.get(0);
            for (Net.Pc x : pcs) if (x.id.equals(p.pcId())) pc = x;
            // remember it, with its current address, for a faster search next time
            if (p.pcId() == null || p.pcId().equals(pc.id)) p.setPc(pc);
            setState(CONNECTING, pc.name);

            String mode = DebugSwitch.pickMode(this, p);
            if (mode == null) throw new Exception(getString(R.string.err_not_set_up));
            int port = DebugSwitch.enable(this, p, mode);
            if (stopRequested) return;

            String tap = Long.toString(new SecureRandom().nextLong() & Long.MAX_VALUE, 36);
            byte[] start = Net.Msg.build("START", "id", p.phoneId(), "tap", tap, "name", Build.MODEL,
                    "adb", String.valueOf(port), "size", p.maxSize(), "bitrate", p.bitrate(),
                    "fps", p.maxFps(), "codec", p.codec(), "video", flag(p.video()),
                    "audio", flag(p.audio()), "screenoff", flag(p.screenOff()),
                    "awake", flag(p.stayAwake()));
            talk(s, pc, tap, start);
        } catch (Exception e) {
            Log.w(Net.TAG, "session failed", e);
            error = e.getMessage() != null ? e.getMessage() : e.toString();
        } finally {
            if (s != null) s.close();
            if (p.screenOff()) wakeScreen();
            DebugSwitch.disable(this, p);
            if (error != null) showError(error);
            detail = error != null ? error : "";
            finishService();
        }
    }

    /** Talks to the PC until the session ends. Throws with a readable message on failure. */
    private void talk(DatagramSocket s, Net.Pc pc, String tap, byte[] start) throws Exception {
        long begun = System.currentTimeMillis();
        long lastHeard = 0, lastSent = 0;
        boolean accepted = false;
        byte[] buf = new byte[1500];
        s.setSoTimeout(250);
        while (true) {
            long now = System.currentTimeMillis();
            if (stopRequested) {
                byte[] stop = Net.Msg.build("STOP", "tap", tap);
                Net.send(s, pc.addr, stop);
                Net.send(s, pc.addr, stop);
                try { Thread.sleep(350); } catch (InterruptedException ignored) {}
                return;
            }
            if (!accepted && now - lastSent > 1500) {
                Net.send(s, pc.addr, start);
                lastSent = now;
            } else if (accepted && now - lastSent > 3000) {
                Net.send(s, pc.addr, Net.Msg.build("PING", "tap", tap));
                lastSent = now;
            }
            if (state == APPROVAL) {
                if (now - begun > 120_000) throw new Exception(getString(R.string.err_approval_timeout));
            } else if (!accepted) {
                if (now - begun > 8000) throw new Exception(getString(R.string.err_pc_no_answer, pc.name));
            } else if (now - lastHeard > 12_000) {
                throw new Exception(getString(R.string.err_pc_lost, pc.name));
            }

            Net.Msg m;
            try {
                DatagramPacket packet = new DatagramPacket(buf, buf.length);
                s.receive(packet);
                m = Net.Msg.parse(packet);
            } catch (SocketTimeoutException e) {
                continue;
            }
            if (m == null || !tap.equals(m.get("tap"))
                    || !m.from.getAddress().equals(pc.addr.getAddress())) continue;
            lastHeard = now;
            switch (m.type) {
                case "WAIT":
                    setState(APPROVAL, pc.name);
                    break;
                case "ACCEPT":
                    accepted = true;
                    if (state != MIRRORING) setState(CONNECTING, pc.name);
                    break;
                case "DENY":
                    throw new Exception(getString(R.string.err_denied, pc.name));
                case "RUNNING":
                    accepted = true;
                    setState(MIRRORING, pc.name);
                    break;
                case "PONG":
                    // the PC forgot the session (its ENDED message got lost)
                    if ("none".equals(m.get("state"))) return;
                    break;
                case "ENDED":
                    if ("error".equals(m.get("reason"))) {
                        throw new Exception(getString(R.string.err_pc_failed, m.get("msg")));
                    }
                    return;
            }
        }
    }

    private static String flag(boolean b) {
        return b ? "1" : "0";
    }

    private void setState(int st, String d) {
        state = st;
        detail = d;
        getSystemService(NotificationManager.class).notify(NOTIF_SESSION, sessionNotification());
        updateTile(this);
    }

    private void finishService() {
        state = IDLE;
        worker = null;
        updateTile(this);
        stopForeground(STOP_FOREGROUND_REMOVE);
        stopSelf();
    }

    static void updateTile(Context c) {
        TileService.requestListeningState(c, new ComponentName(c, MirrorTile.class));
    }

    private Notification sessionNotification() {
        String title;
        String text = null;
        Prefs p = new Prefs(this);
        switch (state) {
            case CONNECTING:
                title = getString(R.string.st_connecting, detail);
                break;
            case APPROVAL:
                title = getString(R.string.st_approval, detail);
                text = getString(R.string.st_approval_text);
                break;
            case MIRRORING:
                title = getString(R.string.st_mirroring, detail);
                break;
            default:
                title = getString(R.string.st_searching);
        }
        if (text == null && !p.video()) {
            text = getString(R.string.notif_no_video);
        }
        PendingIntent stop = PendingIntent.getService(this, 1,
                new Intent(this, MirrorService.class).setAction(ACTION_STOP), PendingIntent.FLAG_IMMUTABLE);
        // With the app's notifications turned off in system settings ("tile only"), Android
        // keeps the service running but doesn't show this anywhere except its task manager.
        return new Notification.Builder(this, "session")
                .setSmallIcon(R.drawable.ic_tile)
                .setContentTitle(title)
                .setContentText(text)
                .setOngoing(true)
                .setContentIntent(openApp())
                .addAction(new Notification.Action.Builder(null, getString(R.string.stop), stop).build())
                .build();
    }

    private void showError(String msg) {
        Notification n = new Notification.Builder(this, "error")
                .setSmallIcon(R.drawable.ic_tile)
                .setContentTitle(getString(R.string.err_title))
                .setContentText(msg)
                .setStyle(new Notification.BigTextStyle().bigText(msg))
                .setAutoCancel(true)
                .setContentIntent(openApp())
                .build();
        getSystemService(NotificationManager.class).notify(NOTIF_ERROR, n);
    }

    private PendingIntent openApp() {
        return PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class),
                PendingIntent.FLAG_IMMUTABLE);
    }

    private void wakeScreen() {
        try {
            android.os.PowerManager pm = (android.os.PowerManager) getSystemService(Context.POWER_SERVICE);
            if (pm != null) {
                android.os.PowerManager.WakeLock wl = pm.newWakeLock(
                        android.os.PowerManager.SCREEN_BRIGHT_WAKE_LOCK | android.os.PowerManager.ACQUIRE_CAUSES_WAKEUP,
                        "tapmirror:wake");
                wl.acquire(1500);
                wl.release();
            }
        } catch (Exception ignored) {}
        try {
            if (DebugSwitch.hasRoot()) {
                DebugSwitch.su("input keyevent 224");
            }
        } catch (Exception ignored) {}
    }
}
