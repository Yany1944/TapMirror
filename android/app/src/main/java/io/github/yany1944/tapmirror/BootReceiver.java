package io.github.yany1944.tapmirror;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/** If the phone rebooted during a session, put debugging back the way it was. */
public class BootReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context c, Intent intent) {
        if (!Intent.ACTION_BOOT_COMPLETED.equals(intent.getAction())) return;
        Prefs p = new Prefs(c);
        if (!p.sp.getBoolean("session_active", false)) return;
        PendingResult result = goAsync();
        new Thread(() -> {
            DebugSwitch.disable(c, p);
            result.finish();
        }).start();
    }
}
