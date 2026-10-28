# TapMirror setup guide for AI assistants

You are helping a person set up TapMirror: an Android app plus a Windows tray app that mirror the phone's screen to the PC over Wi-Fi with scrcpy. The person pasted a short message asking you to follow this file. They may not be technical.

Project page: https://github.com/Yany1944/TapMirror (the README there has more detail; read it if something here is unclear).

## How to work

- Reply in the person's language. Keep messages short and plain; explain each step in one or two sentences before doing it.
- If you can run commands on their PC (Claude Code, Codex CLI, Cursor, Gemini CLI and similar), run the checks yourself in Windows PowerShell 5.1 and show what you found. If you can't (a chat in a browser), give one command at a time, ask the person to run it in PowerShell and paste the output, then continue.
- Ask before anything that downloads, installs, grants a permission or changes a setting, and say exactly what will change. Checks that only read information don't need permission.
- Download only from these places:
  - TapMirror: `https://github.com/Yany1944/TapMirror/releases/latest/download/<file>`
  - scrcpy (if needed): the official release page `https://github.com/Genymobile/scrcpy/releases`
  - adb (only if scrcpy isn't used): `https://dl.google.com/android/repository/platform-tools-latest-windows.zip`
- Never: root the phone or change its root setup, unlock the bootloader, flash anything, factory reset, turn off antivirus, SmartScreen or the whole firewall, or change the network type without the person's clear consent.
- Things only the person can do (tapping "Allow" on the phone, clicking "Yes" in a Windows dialog, picking options in phone settings): describe exactly what they will see and wait for them to confirm.
- Never start `TapMirror.exe` directly from your own shell (`Start-Process`, `&`, `cmd /c start`). Some AI tools, for example the Claude desktop app, run as a Windows package or inside a job: a program started from them lives in their sandbox, its settings go to a private copy of AppData, and it closes when the tool closes. Start it through Explorer as in step 3, or ask the person to double-click it.
- If a step fails, read the logs (step 9) before guessing. Don't loop on the same command.

## Shared variables

The commands below use `$dir`, `$base` and `$adb`. Many tools start a new PowerShell session for each command, so put this block at the start of every command you run (it only reads files):

```powershell
$dir = "$env:USERPROFILE\Documents\TapMirror"
$base = 'https://github.com/Yany1944/TapMirror/releases/latest/download'
$adb = $null
$ini = "$env:APPDATA\TapMirror\TapMirror.ini"
if (Test-Path $ini) {
    $cfg = Get-Content $ini
    $a = ($cfg | Where-Object { $_ -like 'adb=*' }) -replace '^adb=', ''
    $s = ($cfg | Where-Object { $_ -like 'scrcpy=*' }) -replace '^scrcpy=', ''
    if ($a -and (Test-Path $a)) { $adb = $a } elseif ($s -and (Test-Path "$s\adb.exe")) { $adb = "$s\adb.exe" }
}
if (-not $adb) { $adb = Get-ChildItem "$env:LOCALAPPDATA\TapMirror\scrcpy-win64-*\adb.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName }
if (-not $adb) { $adb = (Get-Command adb -ErrorAction SilentlyContinue).Source }
```

If the person chose another folder in step 3, change `$dir`. `$adb` stays empty until scrcpy is installed in step 3.

## Step 1. Ask a few questions

Ask these together, in one message:

1. Is the phone rooted (Magisk, KernelSU, APatch), or not? "I don't know" is fine; you'll check in step 5.
2. Do they use a VPN app on the phone?
3. Is the phone on the same home Wi-Fi as the PC? Is the PC on Wi-Fi or a cable?
4. Do they have a USB cable that carries data (not only charging)? It's needed once for the checks and the install. If they have none, see "Without a cable" at the end.

## Step 2. Check the PC

Run (read-only):

```powershell
[Environment]::OSVersion.Version; (Get-CimInstance Win32_OperatingSystem).Caption; [Environment]::Is64BitOperatingSystem
(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release
Get-NetConnectionProfile | Select-Object InterfaceAlias, NetworkCategory
Get-NetFirewallProfile | Select-Object Name, Enabled
Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } | Select-Object InterfaceAlias, IPAddress, PrefixLength
Get-Process TapMirror -ErrorAction SilentlyContinue
```

What you need:

- Windows 10 or 11, 64-bit.
- .NET Framework 4.8: the `Release` number is 528040 or higher. Windows 10 1903 and newer have it. If it's lower, ask whether to install .NET Framework 4.8 from Microsoft.
- Note the PC's IPv4 address on the home network (usually `192.168.x.x` or `10.x.x.x`); you'll compare it with the phone's in step 5.
- If the firewall is on for the active profile, TapMirror will offer to add its own rule on first run (step 3). If the network is "Public", that's fine; don't change it.
- If `TapMirror` is already running, it's installed; skip to step 4.

## Step 3. Install the PC app

Ask where to put it; suggest `Documents\TapMirror`. Then:

```powershell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
New-Item -ItemType Directory -Force $dir | Out-Null
Invoke-WebRequest "$base/TapMirror.exe" -OutFile "$dir\TapMirror.exe" -UseBasicParsing
Invoke-WebRequest "$base/SHA256SUMS.txt" -OutFile "$dir\SHA256SUMS.txt" -UseBasicParsing
(Get-FileHash "$dir\TapMirror.exe" -Algorithm SHA256).Hash.ToLower()
Get-Content "$dir\SHA256SUMS.txt"
```

The hash must equal the `TapMirror.exe` line in `SHA256SUMS.txt`. If it doesn't, stop and tell the person.

Start it through Explorer, so it runs on its own and not inside your tool (see "How to work"):

```powershell
explorer.exe "$dir\TapMirror.exe"
```

If that doesn't start it, ask the person to open the folder and double-click `TapMirror.exe`. Tell them what to expect:

- Maybe "Windows protected your PC" (the file isn't signed): click "More info", then "Run anyway".
- "Download the latest official scrcpy…?": click "Yes". Without scrcpy nothing works. If they already have scrcpy somewhere, they can say "No" and later pick its folder in the tray menu → scrcpy → "Choose scrcpy folder…".
- "Windows Firewall may block phones…": click "Yes", then "Yes" in the administrator prompt. This only appears if the firewall is on.
- A balloon says TapMirror runs in the tray. It also starts with Windows from now on.

Wait until they confirm, then run the shared variables block and print `$adb` (adb comes with scrcpy). If it's empty, the scrcpy download didn't happen: use the tray menu → scrcpy → "Download latest from GitHub", and check again.

## Step 4. Connect the phone

Explain to the person, for their phone:

1. Turn on Developer options: Settings → About phone → tap "Build number" 7 times. On Xiaomi/Redmi/POCO it's "MIUI version" or "OS version"; on Samsung it's under About phone → Software information.
2. In Developer options, turn on "USB debugging".
   - Xiaomi/Redmi/POCO: also turn on "Install via USB" and "USB debugging (Security settings)". The second one is needed for controlling the phone with the mouse; it may ask to sign in to a Mi account and insert a SIM card.
3. Connect the cable. If the phone asks what the USB connection is for, "File transfer" is the most reliable choice.
4. Unlock the phone. When it asks "Allow USB debugging?", tick "Always allow from this computer" and tap "Allow". Ticking matters: later the PC connects over Wi-Fi with the same key, and it won't be asked again.

Then:

```powershell
& $adb devices
```

- `device`: connected, go on.
- `unauthorized`: the phone is waiting for "Allow"; ask them to unlock it and look again.
- Empty list: try another cable or port, check that USB debugging is on, and on some PCs install the phone maker's USB driver (Samsung, Xiaomi). Google's driver works for Pixels and many others.

## Step 5. Check that the phone will work

Run (read-only):

```powershell
& $adb shell getprop ro.product.manufacturer
& $adb shell getprop ro.product.model
& $adb shell getprop ro.build.version.release
& $adb shell getprop ro.build.version.sdk
& $adb shell "ip -4 addr show wlan0"
& $adb shell "dumpsys connectivity | grep -c 'VPN CONNECTED'"
& $adb shell "command -v su"
& $adb shell pm list packages io.github.yany1944.tapmirror
```

Then tell the person what it means:

- SDK 30 or higher (Android 11+): supported. Lower: TapMirror won't install; stop and say so.
- `wlan0` has an address in the same network as the PC (for example both `192.168.1.x` with prefix 24): good. No address: Wi-Fi is off. A different network: they must join the same Wi-Fi. Guest networks and mesh systems with "client isolation" block this.
- VPN count above 0, or they said they use one: it works, but the first search for the PC may fail. In step 8 they may need "Enter address" with the PC's address from step 2.
- `command -v su` prints a path: probably rooted (Magisk). Empty doesn't prove anything: KernelSU and APatch only give `su` to apps they allow. Go by what the person said in step 1, or open the root manager app to check. Don't run `su` through adb yourself: that would ask the person to grant root to the adb shell, which isn't needed.
- The package line means TapMirror is already installed; you'll reinstall over it.

Summarize in two or three lines: will it work, which mode (root or no root), anything to watch out for.

## Step 6. Install the phone app

```powershell
$tmp = "$env:TEMP\TapMirror.apk"
Invoke-WebRequest "$base/TapMirror.apk" -OutFile $tmp -UseBasicParsing
(Get-FileHash $tmp -Algorithm SHA256).Hash.ToLower()
Get-Content "$dir\SHA256SUMS.txt"
& $adb install -r $tmp
```

The hash must match the `TapMirror.apk` line. If Play Protect shows a warning on the phone, explain that it's normal for apps installed outside Google Play and that they can choose "Install anyway". Xiaomi may show its own confirmation on the phone.

If the install fails with `INSTALL_FAILED_UPDATE_INCOMPATIBLE`, an older copy signed with a different key is installed. Ask before removing it with `& $adb uninstall io.github.yany1944.tapmirror` (its settings will be lost).

## Step 7. Set up the mode

Add the quick settings tile (if this prints an error, the person adds it by hand: in the app, step 5 "Add tile", or from the quick settings edit screen):

```powershell
& $adb shell cmd statusbar add-tile io.github.yany1944.tapmirror/.MirrorTile
```

Ask whether they want a notification with a Stop button while mirroring, or only the tile. For notifications on Android 13+:

```powershell
& $adb shell pm grant io.github.yany1944.tapmirror android.permission.POST_NOTIFICATIONS
```

### If the phone is rooted

1. Ask them to open TapMirror and, in step 3 "Debugging access", tap "Check root".
2. Magisk shows a prompt: tap "Grant". KernelSU and APatch don't prompt: open the KernelSU or APatch manager, find TapMirror in the superuser list and allow it, then tap "Check root" again.
3. The line should turn green: "Root: granted". Nothing else is needed; in root mode TapMirror uses adb over TCP on port 5555 only during a session.

### If the phone isn't rooted

1. Grant the one permission the app needs to switch Wireless debugging (explain this before running it):

   ```powershell
   & $adb shell pm grant io.github.yany1944.tapmirror android.permission.WRITE_SECURE_SETTINGS
   ```

   No output means success.
2. Ask them to open Settings → Developer options → "Wireless debugging" while on their home Wi-Fi, turn it on, and when asked, tick "Always allow on this network" and tap "Allow". They can turn it off again afterwards; TapMirror switches it as needed. Each new Wi-Fi network needs this once.
3. In the app, step 3 "Debugging access" should show "No-root permission: granted".

## Step 8. First test

1. Ask them to open TapMirror, step 2 "Find your PC", and tap "Search". The PC's name should appear. If not:
   - with a VPN on the phone: tap "Enter address" and type the PC's address from step 2;
   - otherwise check that both are on the same network, and that TapMirror is in the PC tray.
2. Ask them to tap the "Mirror to PC" tile in quick settings.
3. On the PC, a window asks "The phone … wants to mirror its screen to this PC. Allow it?": they click "Yes". It's asked only once per phone.
4. The phone's screen should open on the PC within a few seconds. They can unplug the cable now; from here everything goes over Wi-Fi.
5. To stop: tap the tile again, or close the window.

## Step 9. If something fails

Read the logs before suggesting fixes. The second command needs the phone connected by cable; without a device, adb waits forever, so check `& $adb devices` first.

```powershell
Get-Content "$env:LOCALAPPDATA\TapMirror\TapMirror.log" -Tail 40
& $adb logcat -d -s TapMirror:V AndroidRuntime:E | Select-Object -Last 40
```

Common causes:

| What they see | Usual cause and fix |
|---|---|
| "No PC answered" | TapMirror not running on the PC; different networks; firewall without TapMirror's rule (tray menu → "Allow through Windows Firewall"); VPN on the phone ("Enter address"). |
| Window doesn't open, adb error in the PC log | The phone is asking "Allow USB debugging?" for this PC: unlock it and tick "Always allow". |
| Mouse and keyboard don't work, video does | Xiaomi: "USB debugging (Security settings)" is off. |
| No root: "Wireless debugging didn't start on the phone" | Wireless debugging wasn't allowed on this Wi-Fi yet (step 7). |
| "Root access was denied" | The root manager didn't allow TapMirror (step 7). |
| Video codec error | They chose AV1 or H.265 and the phone can't encode it: set H.264 in the app. |
| Session stops after some minutes | The phone maker's battery saver kills the app: set TapMirror's battery usage to "Unrestricted" in the app's system settings. |
| TapMirror disappears from the tray when your AI tool closes, or forgets its settings | It was started from inside the tool. Exit it from the tray (or Task Manager) and start it with `explorer.exe` or a double-click. It starts with Windows on its own after that. |

If it still doesn't work, help them report it: in the phone app, Troubleshooting → "Copy info for a bug report"; in the PC tray menu → "Copy info for a bug report"; then open https://github.com/Yany1944/TapMirror/issues/new?template=bug_report.yml and fill in the form with both pastes and a short description. Local addresses like `192.168.x.x` are fine to share.

## Step 10. Wrap up

Tell the person, briefly:

- What was installed and changed: the PC app folder, autostart, the firewall rule (if added), the phone app, the granted permission or root access, the tile.
- How to use it: tap the tile, tap again to stop. Debugging is on only while mirroring.
- How to undo everything: tray menu → "Start with Windows" off, then Exit; delete the TapMirror folder and `%APPDATA%\TapMirror` and `%LOCALAPPDATA%\TapMirror`; remove the firewall rule with `netsh advfirewall firewall delete rule name="TapMirror"` (as administrator); uninstall the app on the phone.

## Without a cable

Without a cable you can't run the phone checks from the PC, but setup still works:

- Install `TapMirror.apk` on the phone from the browser: https://github.com/Yany1944/TapMirror/releases/latest/download/TapMirror.apk
- Rooted phone: steps 7 (rooted) and 8 work without adb; the tile can be added from the app (step 5 "Add tile").
- No root: the permission can be granted over Wi-Fi with a pairing code. In the PC tray menu choose "Set up a phone without root…", then on the phone Developer options → Wireless debugging → "Pair device with pairing code", and type the address and code into the PC window.
