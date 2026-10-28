package io.github.yany1944.tapmirror;

import android.service.quicksettings.Tile;
import android.service.quicksettings.TileService;

/** Quick settings tile: tap to start mirroring to the PC, tap again to stop. Long-press opens the app. */
public class MirrorTile extends TileService {

    @Override
    public void onTileAdded() {
        new Prefs(this).put("tile_added", true);
    }

    @Override
    public void onTileRemoved() {
        new Prefs(this).put("tile_added", false);
    }

    @Override
    public void onStartListening() {
        Tile tile = getQsTile();
        if (tile == null) return;
        int st = MirrorService.state;
        String sub;
        switch (st) {
            case MirrorService.SEARCHING:
            case MirrorService.CONNECTING:
                sub = getString(R.string.tile_connecting);
                break;
            case MirrorService.APPROVAL:
                sub = getString(R.string.tile_approval);
                break;
            case MirrorService.MIRRORING:
                sub = MirrorService.detail;
                break;
            default:
                // last error if the previous attempt failed, otherwise the chosen PC
                String err = MirrorService.detail;
                sub = err.isEmpty() ? new Prefs(this).pcName() : getString(R.string.tile_failed);
        }
        tile.setState(st == MirrorService.IDLE ? Tile.STATE_INACTIVE : Tile.STATE_ACTIVE);
        tile.setSubtitle(sub);
        tile.updateTile();
    }

    @Override
    public void onClick() {
        if (MirrorService.state == MirrorService.IDLE) {
            MirrorService.start(this);
        } else {
            MirrorService.stop(this);
        }
    }
}
