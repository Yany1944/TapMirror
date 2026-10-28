# TapMirror protocol

Phone and PC talk over UDP port **27199** on the local network. Each message is one datagram of UTF-8 text:

```
TAPMIRROR/1 <TYPE> key=value key=value ...
```

Values are URL-encoded (`+` or `%20` for a space). Unknown keys are ignored, so both sides can add fields.

## Messages

| From | Type | Keys | Meaning |
|---|---|---|---|
| phone → broadcast | `HELLO` | `id`, `name` | Who is there? Sent to the subnet broadcast and 255.255.255.255. |
| PC → phone | `HI` | `pc`, `name` | Reply to `HELLO`. `pc` is a random id that stays the same for this PC. |
| phone → PC | `START` | `id`, `tap`, `name`, `adb`, options | Start mirroring. `tap` is new for every tap of the tile. `adb` is the TCP port of adb on the phone, or `0` if the PC has to find it over mDNS (Wireless debugging). |
| PC → phone | `WAIT` | `tap` | Unknown phone; the PC is asking its user to allow it. |
| PC → phone | `ACCEPT` | `tap` | Allowed; the PC is connecting. |
| PC → phone | `DENY` | `tap` | This phone is blocked on the PC. |
| PC → phone | `RUNNING` | `tap` | The scrcpy window is open. |
| PC → phone | `ENDED` | `tap`, `reason` (`closed`/`error`), `msg` | The session is over. Sent 3 times. |
| phone → PC | `PING` | `tap` | Every 3 s while a session runs. |
| PC → phone | `PONG` | `tap`, `state` (`connecting`/`running`/`none`) | `none` means the PC has no such session. |
| phone → PC | `STOP` | `tap` | The user stopped from the phone. |

The PC answers a unicast message from the address it was sent to (it has one socket per local address), because a VPN on the phone drops answers from any other address. Answers to broadcasts can't pass such a VPN at all, so behind one the phone needs the PC's address: the last one it saw, one typed in by the user, or a sweep of the local /24.

The phone repeats `START` every 1.5 s until it gets an answer. The PC treats a repeated `START` with the same `tap` as the same request.

### START options

| Key | Values | scrcpy option |
|---|---|---|
| `size` | 0 (full) or 320–8192 | `--max-size` |
| `bitrate` | 1–200 (Mbps) | `--video-bit-rate` |
| `fps` | 0 (no limit) or 1–240 | `--max-fps` |
| `codec` | `h264`, `h265`, `av1` | `--video-codec` |
| `audio` | `0`/`1` | `--no-audio` when 0 |
| `screenoff` | `0`/`1` | `--turn-screen-off` |
| `awake` | `0`/`1` | `--stay-awake` |

The PC only passes these checked values to scrcpy, never raw text from the network.

## Session end

The phone turns adb off when any of these happens:

- it gets `ENDED`, or `PONG state=none`;
- the user taps the tile again or presses Stop in the notification (the phone sends `STOP`);
- nothing comes back from the PC for 12 s (PC asleep, Wi-Fi lost);
- the PC doesn't answer `START` within 8 s, or nobody approves the phone within 2 minutes.

If the phone reboots during a session, the app restores the debugging settings at boot.

## Security notes

- Messages are not authenticated. Approval on the PC is tied to the phone's random `id`, which other devices on the same network can see and copy. The worst they can do with it is make your PC open a scrcpy window that connects to *their* device.
- The PC never runs commands on the phone except `adb connect` and scrcpy itself; it ignores everything in `START` except the options above.
- adb on the phone is reachable on the network only during a session, and it still asks for (or remembers) approval of the PC's adb key as usual.
