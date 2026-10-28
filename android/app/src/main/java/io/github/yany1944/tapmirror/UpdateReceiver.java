package io.github.yany1944.tapmirror;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageInstaller;
import android.widget.Toast;

/** Gets the system installer's answers for an update started by {@link Updater#install}. */
public class UpdateReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context c, Intent intent) {
        int status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE);
        if (status == PackageInstaller.STATUS_PENDING_USER_ACTION) {
            // the system's "Do you want to update this app?" screen
            Intent confirm = intent.getParcelableExtra(Intent.EXTRA_INTENT); // the typed variant needs Android 13
            if (confirm != null) c.startActivity(confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        } else if (status != PackageInstaller.STATUS_SUCCESS) {
            // on success the new version has replaced this one and the app was closed; nothing to say
            String msg = intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE);
            Toast.makeText(c, c.getString(R.string.update_failed, msg != null ? msg : "status " + status),
                    Toast.LENGTH_LONG).show();
        }
    }
}
