<p align="center"><img src="docs/icon.png" width="96" alt=""></p>

<h1 align="center">TapMirror</h1>

<p align="center">One tap in Android quick settings, and your phone's screen opens on your Windows PC.<br>
Low latency, mouse and keyboard control, over Wi-Fi, no console. Built on <a href="https://github.com/Genymobile/scrcpy">scrcpy</a>.</p>

<p align="center"><a href="README.ru.md">Русская версия</a></p>

---

## What you get

- **A "Mirror to PC" tile** in quick settings: tap to start, tap again to stop. A notification shows the status and a Stop button.
- **scrcpy quality**: real-time video, control with mouse and keyboard, phone audio on the PC.
- **Wi-Fi from anywhere in the house**, no cable, no commands.
- **Debugging is on only while you mirror.** The phone turns adb on for the session and back off afterwards. This matters for banking and government apps that refuse to run while USB debugging is on.
- **Root or no root.** With root it just works. Without root you grant one permission from the PC once.
- **A tiny PC side**: a tray app that uses no CPU while it waits, starts with Windows and can download scrcpy for you.
- **New phones need approval** on the PC the first time.
- **Settings on the phone**: resolution, bitrate, frame rate, codec, audio, turn the phone screen off, keep it awake.
- English and Russian.

## How it works

```
 phone (tile)                                   PC (tray app)
 ──────────────                                 ──────────────
 HELLO  ── UDP broadcast, port 27199 ─────────►  HI (name)
 turn adb on for this session
   root:    adb over TCP, port 5555
   no root: Wireless debugging
 START (options) ─────────────────────────────►  first time: "Allow this phone?"
                                                 adb connect, start scrcpy
          ◄──────────────────────────────────── RUNNING      (window is open)
 PING every 3 s ──────────────────────────────►  PONG
          ◄──────────────────────────────────── ENDED        (window closed)
 turn adb back off
```

If anything goes away (window closed, tile tapped, PC asleep, Wi-Fi lost), the phone turns debugging back off within seconds. Details: [docs/protocol.md](docs/protocol.md).

## Requirements

- Android 11 or newer.
- Windows 10 or 11, 64-bit (uses the .NET Framework 4.8 built into Windows).
- Phone and PC on the same network, able to see each other (not a guest network, no "AP/client isolation").
- Either root (Magisk, KernelSU, APatch) or a PC with adb once for the no-root permission.

Tested on a Pixel 7 Pro (Android 17 beta, Magisk) with Windows 11, in both root and no-root mode.

## Install

### 1. PC

1. Download **TapMirror.exe** from [Releases](https://github.com/Yany1944/TapMirror/releases/latest). Put it in a folder of its own (for example `Documents\TapMirror`) and run it.
2. On the first run it offers to:
   - download the latest official **scrcpy** from GitHub (it checks the SHA-256);
   - add a **Windows Firewall** rule for UDP port 27199 (asks for administrator rights).

   Say yes to both. TapMirror now lives in the tray and starts with Windows.

### 2. Phone

1. Download **TapMirror.apk** from [Releases](https://github.com/Yany1944/TapMirror/releases/latest) and install it.
2. Open the app. Its first screen is a checklist that walks you through the setup:
   1. TapMirror on the PC;
   2. find the PC (the Search button);
   3. debugging access: **root**: tap "Check root" and grant it. **No root**: see below;
   4. Developer options on;
   5. add the quick settings tile;
   6. allow notifications.
3. Tap the tile. The first time, the PC asks whether to allow this phone.

### No root: one permission, once

Without root the app switches Android's **Wireless debugging** itself. For that it needs `WRITE_SECURE_SETTINGS`, which only a computer can grant. In the PC tray menu choose **"Set up a phone without root…"** and pick one of:

- **USB**: turn on USB debugging, connect the cable, tap Allow on the phone, click "Grant over USB".
- **No cable**: on the phone open Developer options → Wireless debugging → "Pair device with pairing code", type the address and code into the window, click "Pair and grant".

Or, if you already use adb:

```
adb shell pm grant io.github.yany1944.tapmirror android.permission.WRITE_SECURE_SETTINGS
```

Then, on each Wi-Fi network you use, open Wireless debugging once and allow it ("Always allow on this network").

Android doesn't let ordinary apps read the debugging settings, so without root TapMirror can't tell whether Wireless debugging was on before. It always turns Wireless debugging off after a session. USB debugging is left alone (or turned off, if you choose "Always turn debugging off").

|  | Root | No root |
|---|---|---|
| adb is turned on by | `su`: adb over TCP, port 5555 | Wireless debugging, through `WRITE_SECURE_SETTINGS` |
| One-time setup | grant root to the app | grant the permission from a PC |
| New Wi-Fi network | nothing | allow Wireless debugging there once |
| The PC finds adb by | the fixed port | mDNS |

## Settings

On the phone (TapMirror app → Mirroring):

| Setting | Default |
|---|---|
| Max resolution | full |
| Video bitrate | 8 Mbps |
| Max frame rate | no limit |
| Video codec | H.264 |
| Play phone audio on the PC | on |
| Turn the phone screen off while mirroring | off |
| Keep the phone awake while mirroring | on |
| After mirroring | put debugging back as it was / always turn it off |

On the PC, `%APPDATA%\TapMirror\TapMirror.ini` holds the scrcpy folder, an optional adb path, extra scrcpy options for every session (`args=`, for example `args=--window-borderless`), and the allowed/blocked phones. The tray menu covers the common cases. Logs: `%LOCALAPPDATA%\TapMirror\TapMirror.log` (tray menu → Open log).

## Troubleshooting

| Problem | Fix |
|---|---|
| "No PC answered" | Is TapMirror in the tray? Same network? Tray menu → "Allow through Windows Firewall" (shown only when the firewall may block it). Guest networks and AP isolation block devices from seeing each other. |
| Window doesn't open, error about adb | The phone may be asking "Allow USB debugging?" for this PC: tick "Always allow". |
| No root: "Wireless debugging didn't start on the phone" | Open Developer options → Wireless debugging on this network once and allow it ("Always allow on this network"). |
| Banking app complains about debugging | Set "After mirroring" to "Always turn debugging off". |
| Lag | Lower the resolution or bitrate; use 5 GHz Wi-Fi. A PC on Ethernet helps. |
| AV1 fails | Most phones have no AV1 encoder; use H.264 or H.265. |

## Build from source

- **PC**: run `windows\build.cmd`. It uses the C# compiler that ships with Windows, so no SDK is needed. Output: `windows\bin\TapMirror.exe`.
- **Android**: JDK 17+ and the Android SDK (platform 35). Run `cd android` then `gradlew assembleDebug`. For a signed release, create `android\keystore.properties` (see `android/app/build.gradle.kts`) and run `gradlew assembleRelease`.
- `build.cmd` in the root builds both into `dist\`.
- The icon comes from `tools/make_icon.py` (Pillow).

```
android/   phone app (Java, no dependencies): tile, session service, setup checklist and settings
windows/   tray app (C#, .NET Framework 4.8): UDP listener, scrcpy launcher, no-root setup wizard
docs/      protocol
```

## Credits

TapMirror only starts [scrcpy](https://github.com/Genymobile/scrcpy) by Genymobile (Apache License 2.0), which does all the hard work. TapMirror is not affiliated with Genymobile.

## License

[MIT](LICENSE)
