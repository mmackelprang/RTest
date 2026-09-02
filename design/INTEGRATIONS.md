# Integrations Setup Guide

This guide covers step-by-step setup for the three external integration systems: **Rotary Encoders**, **Phone Call Notifications**, and the **Announcement API**.

All integrations are **disabled by default** and opt-in via configuration. The Radio Console operates fully without them.

---

## 1. Rotary Encoders (USB HID)

Physical rotary encoders connected via a Raspberry Pi Pico running custom firmware, exposed as a USB HID device. Four encoders control Volume, Tuning, Source selection, and Visualization mode.

### Hardware Requirements

- **Raspberry Pi Pico** (or Pico W) with CircuitPython/C firmware
- **4x KY-040 rotary encoders** (with push buttons) wired to the Pico
- USB cable connecting the Pico to the Radio Console host

The Pico firmware must present itself as a USB HID device with:
- **Vendor ID:** `0xCAFE` (51966)
- **Product ID:** `0x4005` (16389)
- **Report format:** 8-byte reports
  - Byte 0: Report ID
  - Bytes 1-4: Signed encoder deltas (sbyte per encoder, positive = clockwise)
  - Byte 5: Button bitmask (bit N = encoder N button state, 1 = pressed)
  - Bytes 6-7: Reserved

### Encoder Mapping

| Encoder | Turn Action | Button Press |
|---------|------------|--------------|
| 0 — Volume | Volume up/down (configurable step %) | Mute toggle |
| 1 — Tuning | Frequency step up/down (when Radio source active) | Start/stop frequency scan |
| 2 — Source | Cycle through source selection | Switch to selected source |
| 3 — Visualizer | Cycle visualization modes | Toggle visualization on/off |

⚠ **The index→handler table above does not match the cabinet engraving, and that is deliberate.** The physical
order is VOLUME / SOURCE / PRESETS / TUNING. Index 0 is VOLUME under both, so the dangerous knob is correct; indices
1–3 are remapped by `ENC-5`/`ENC-7`, which introduce the handlers the remap points at.

### On-screen feedback — the `EncoderHud` (ENC-4)

Every knob movement puts a transient card on screen **in the screen quarter above the knob that produced it** —
centres 240 / 720 / 1200 / 1680 on the 1920 px panel. Geometry keys off the encoder index; the words key off whatever
the router's handler produced, so the card is in the right place before it says the right word.

One component, two hosts: `EncoderHud.razor` mounts in `MainLayout` for every normal route, and again inside
`Sleep.razor` with `Variant="Sleep"` (centred, dim amber, inside the anti-burn-in drift wrapper), because `/sleep` is
on `EmptyLayout` and `MainLayout` is not in that tree.

**Two behaviour changes worth knowing at the cabinet:**

- **The short button press now fires on RELEASE, not on press.** Firing on press would fire it on the way into every
  long-press hold.
- **A long press (600 ms) fires AT the threshold while the button is still held**, and the release afterwards does
  nothing. Only encoder 0 has a long action wired (→ Standby); a >600 ms hold on encoders 1–3 now performs **no
  action at all**, where it previously acted on press.

**Turning the volume knob while muted unmutes and applies the delta in the same frame** (`ENC-4b`).

The 600 ms threshold has **one definition**, `Radio.Core.Configuration.EncoderInteractionTimings`, shared by the
host-side synthesis in `Radio.Infrastructure` and by `RadioControlPanel` in `Radio.Web` — the two run in different
processes, so the value is promoted to Core rather than referenced across the boundary.

HUD broadcasts are coalesced to ≥ 50 ms (20 Hz), trailing-edge, always emitting the final value. **The audio action
is not throttled** — volume applies per event at full rate. The ear leads; the screen catches up.

### Setup Steps

**Step 1: Connect the Pico**

Plug the Pico into a USB port on the Radio Console host. Verify it appears as a HID device:

```bash
# Linux — check dmesg for the HID device
dmesg | tail -20

# List HID devices (look for VID:PID cafe:4005)
ls -la /dev/hidraw*
```

**Step 2: Set up HID permissions (Linux only)**

By default, `/dev/hidraw*` devices require root access. Create a udev rule so the `mmack` user (or whichever user runs `radio-api`) can access the device:

```bash
sudo tee /etc/udev/rules.d/99-radio-encoders.rules << 'EOF'
# Radio Console — Pico rotary encoder HID access
SUBSYSTEM=="hidraw", ATTRS{idVendor}=="cafe", ATTRS{idProduct}=="4005", MODE="0666"
EOF

sudo udevadm control --reload-rules
sudo udevadm trigger
```

Unplug and re-plug the Pico for the rule to take effect.

**Step 3: Enable in configuration**

Edit `appsettings.json` (or `appsettings.Production.json` for per-machine overrides):

```json
{
  "RotaryEncoder": {
    "Enabled": true,
    "VendorId": 51966,
    "ProductId": 16389,
    "DevicePath": "",
    "PollIntervalMs": 10,
    "VolumeStepPercent": 2,
    "TuningStepKHz": 10,
    "ReconnectDelayMs": 2000
  }
}
```

Configuration fields:
| Field | Description | Default |
|-------|-------------|---------|
| `Enabled` | Master switch for the encoder service | `false` |
| `VendorId` | USB HID Vendor ID (decimal) | `51966` (0xCAFE) |
| `ProductId` | USB HID Product ID (decimal) | `16389` (0x4005) |
| `DevicePath` | Explicit `/dev/hidrawN` path (empty = auto-detect by VID/PID) | `""` |
| `PollIntervalMs` | Delay between HID report reads in ms | `10` |
| `VolumeStepPercent` | Volume change per encoder click (0-100) | `2` |
| `TuningStepKHz` | Radio frequency step per click in kHz | `10` |
| `ReconnectDelayMs` | Delay before retrying after device disconnect | `2000` |

You can also configure these from the Web UI: **System Config → Integrations → Rotary Encoders**.

**Step 4: Restart the API service**

```bash
sudo systemctl restart radio-api
```

**Step 5: Verify**

Check the service logs for connection status:

```bash
journalctl -u radio-api --no-pager --since "1 min ago" | grep -i encoder
```

You should see:
```
Rotary encoder service started
Encoder device connected: <device name>
```

Or open **System Config → Integrations → Rotary Encoders** in the Web UI and check the status chip shows "Connected".

### Troubleshooting

- **"Disconnected" in UI but Pico is plugged in:** Check udev permissions, verify VID/PID match with `lsusb | grep -i cafe`
- **Encoder turns in wrong direction:** The delta sign depends on wiring. If clockwise decreases volume, swap the A/B encoder pins on the Pico, or negate the delta in firmware
- **No response on button press:** Verify byte 5 bitmask in the Pico firmware. The service uses bit N for encoder N (bit 0 = encoder 0)
- **Service starts but can't find device:** Try setting `DevicePath` explicitly to `/dev/hidraw0` (or whichever device the Pico is)

### ⚠ The screen is dark and will not come back — recovery

Read this first if you are at the cabinet and the panel is black. **You need SSH; the panel itself cannot
help you.** `ssh mmack@radio` (never the bare IP — the SSH identity is hostname-bound).

**Primary — set the display power mode directly.** This is the one that works in both dark states and
holds:

```bash
gdbus call --session --dest org.gnome.Mutter.DisplayConfig \
  --object-path /org/gnome/Mutter/DisplayConfig \
  --method org.freedesktop.DBus.Properties.Set \
  org.gnome.Mutter.DisplayConfig PowerSaveMode "<0>"
```

**Secondary — deactivate the screensaver.** Only use this if the primary is unavailable:

```bash
gdbus call --session --dest org.gnome.ScreenSaver --object-path /org/gnome/ScreenSaver \
  --method org.gnome.ScreenSaver.SetActive false
```

**The order matters, and it was measured.** The screensaver route does **not** reach DPMS-off. On
2026-09-02 the panel was found dark with `dpms=Off` while the screensaver reported `GetActive=(false,)` —
`SetActive false` is a no-op there, because the screensaver is already inactive. `PowerSaveMode` read `3`
(DRM DPMS Off) at the same moment, and setting it to `0` is what actually restored the panel. Where the
screensaver cycle did work it bought about 13 seconds before the panel went down again.

Both commands need the graphical session environment. From a plain SSH shell, import it first or every
call fails in a way that looks like the interface is missing — the incantation is in `CLAUDE.md` under
"Remote UI driving".

Check state with `cat /sys/class/drm/card1-DP-1/dpms` and the `PowerSaveMode` property — `status`, `dpms`
and `enabled` are reported independently and do not always agree.

> **Why screen blanking is switched off, and must stay off.** `ENC-15` established on the box that **the
> touchscreen is powered by the panel and leaves the USB bus when the panel blanks** — so touch cannot wake
> it, because no input device exists to generate the event. The rotary encoder cannot wake it either: it
> exposes `/dev/hidraw3` and **no evdev node at all**, so it is invisible to the compositor and cannot even
> reset the idle timer. A knob wake would work only through `radio-api` reading hidraw and calling the
> unblank itself, which makes that service a single point of failure in the only remaining wake path.
> Full write-up: [`docs/uat/2026-09-02-enc15-touch-wake-gate/REPORT.md`](../docs/uat/2026-09-02-enc15-touch-wake-gate/REPORT.md).

---

## 2. Phone Call Integration (RotaryPhone)

Connects to an external **RotaryPhone** server via SignalR to receive incoming call notifications. When a call comes in, the Radio Console:
1. Ducks the current audio
2. Plays a ring sound (if configured)
3. Announces the caller name/number via TTS

### Prerequisites

- A running **RotaryPhone** server instance with:
  - A SignalR hub at `/hubs/phone`
  - (Optional) A contacts REST API for caller name lookup

### Setup Steps

**Step 1: Note the RotaryPhone server address**

The RotaryPhone server exposes two endpoints:
- **SignalR Hub:** `http://<phone-server>:5555/hubs/phone`
- **Contacts API:** `http://<phone-server>:5555`

Replace `<phone-server>` with the actual hostname or IP.

**Step 2: (Optional) Place a ring sound file**

If you want a ring sound before the TTS announcement, place a WAV or MP3 file on the Radio Console host:

```bash
# Example: copy a ring sound to the media directory
cp phone-ring.wav /opt/radio-console/media/sounds/phone-ring.wav
```

**Step 3: Enable in configuration**

Edit `appsettings.json` (or `appsettings.Production.json`):

```json
{
  "PhoneIntegration": {
    "Enabled": true,
    "HubUrl": "http://<phone-server>:5555/hubs/phone",
    "ContactsApiBaseUrl": "http://<phone-server>:5555",
    "RingSoundPath": "media/sounds/phone-ring.wav",
    "RingPriority": 9,
    "AnnouncementPriority": 8,
    "ReconnectBaseDelayMs": 2000,
    "ReconnectMaxDelayMs": 30000
  }
}
```

Configuration fields:
| Field | Description | Default |
|-------|-------------|---------|
| `Enabled` | Master switch for phone integration | `false` |
| `HubUrl` | RotaryPhone SignalR hub URL | `http://radio:5004/hub` |
| `ContactsApiBaseUrl` | RotaryPhone contacts REST API base URL | `http://radio:5004` |
| `PlayRingSound` | Play ring sound through radio speakers (disable when physical phone rings) | `false` |
| `RingSoundPath` | Path to ring sound file (relative to app root, or absolute) | `media/sounds/phone-ring.wav` |
| `RingPriority` | Audio ducking priority for the ring sound (1-10) | `9` |
| `AnnouncementPriority` | Audio ducking priority for TTS caller announcement (1-10) | `8` |
| `ReconnectBaseDelayMs` | Initial reconnection delay in ms (doubles on each retry) | `2000` |
| `ReconnectMaxDelayMs` | Maximum reconnection delay cap in ms | `30000` |

You can also configure these from the Web UI: **System Config → Integrations → Phone Integration**.

**Step 4: Restart the API service**

```bash
sudo systemctl restart radio-api
```

**Step 5: Verify connection**

```bash
journalctl -u radio-api --no-pager --since "1 min ago" | grep -i phone
```

Success:
```
Connecting to RotaryPhone hub at http://<phone-server>:5555/hubs/phone
Connected to RotaryPhone hub
```

If the RotaryPhone server isn't running yet:
```
Could not connect to RotaryPhone hub at http://... Will retry.
```

This is safe — the client retries with exponential backoff and will connect automatically when the server becomes available.

### SignalR Hub Protocol

The Radio Console expects the RotaryPhone hub to invoke the following client method:

```
CallStateChanged(string state, string phoneNumber)
CallStateChanged(string state, string phoneNumber, string callerName)
```

Both overloads are registered. The `state` parameter is parsed leniently:

| State Values | Maps To |
|-------------|---------|
| `"ringing"`, `"ring"`, `"incoming"` | Ringing |
| `"incall"`, `"in_call"`, `"active"`, `"answered"` | InCall |
| `"ended"`, `"hangup"`, `"idle"` | Ended |
| Anything else | Idle |

### Contacts REST API Protocol

The optional contacts API is called when a caller name isn't provided in the SignalR event:

```
GET {ContactsApiBaseUrl}/api/contacts/lookup?phone={phoneNumber}
```

Expected response (200 OK):
```json
{
  "Name": "John Smith",
  "PhoneNumber": "+15551234567"
}
```

If the API is unavailable or returns no match, the raw phone number is used in the announcement.

### PBAP Contact Sync

Radio.API can download contacts from the connected phone's phonebook via Bluetooth PBAP (Phone Book Access Profile). These contacts are used for caller ID resolution during incoming calls.

**How it works:**
1. A Python helper script (`pbap_download.py`) manages the D-Bus session bus connection to BlueZ's obexd
2. It creates an OBEX PBAP session, selects the internal phonebook, and downloads all contacts as a VCF file
3. The VCF is parsed (vCard 2.1/3.0 with quoted-printable decoding) and stored in SQLite
4. Phone number lookup uses exact match first, then last-7-digit suffix matching for fuzzy resolution
5. After sync, the BT connection is automatically restored (OBEX teardown causes a temporary disconnect)

**Prerequisites:**
- `bluez-obexd` installed and enabled as a user service: `systemctl --user enable --now obex`
- `python3` with `dbus-python` and `PyGObject` packages (standard on Ubuntu)
- `DBUS_SESSION_BUS_ADDRESS` environment variable set in the radio-api systemd service
- Phone must be paired and have granted PBAP access (Android prompts on first access)

**Configuration** (`appsettings.json` → `Bluetooth:Pbap`):

| Field | Description | Default |
|-------|-------------|---------|
| `AutoSyncOnConnect` | Automatically sync contacts when a phone connects | `true` |
| `SyncStaleThresholdHours` | Hours before contacts are considered stale and re-synced | `24` |
| `TransferTimeoutSeconds` | Max seconds to wait for PBAP transfer to complete | `30` |

**REST API** (`/api/bluetooth/pbap`):

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/sync?deviceAddress=XX:XX:XX:XX:XX:XX` | Trigger manual sync (uses connected device if omitted) |
| `GET` | `/contacts?deviceAddress=XX:XX:XX:XX:XX:XX` | List synced contacts for a device |
| `GET` | `/lookup?phoneNumber=5551234567` | Look up a contact by phone number |
| `GET` | `/status` | Sync status for all devices |

**Operational notes:**
- PBAP sync causes a brief BT audio interruption (~5 seconds) as OBEX uses a separate RFCOMM channel
- The service uses `PrivateTmp=true` in systemd, so temp files are written to `/opt/radio-console/data/` (not `/tmp`) to avoid mount namespace mismatch with obexd
- A `SemaphoreSlim` prevents concurrent sync attempts from auto-sync and manual API calls
- On first connect from a new phone, Android will prompt to allow phonebook access — the user must approve on the phone

### How It Works

When an incoming call is detected (`Ringing` state):

1. The `PhoneCallIntegrationService` looks up the caller name:
   - First checks PBAP contacts (local SQLite, synced from phone's phonebook)
   - Falls back to the RotaryPhone contacts REST API
2. If a ring sound file exists at `RingSoundPath`, it plays the sound followed by a TTS announcement (e.g., "Incoming call from John Smith")
3. If no ring sound, just the TTS announcement plays
4. Audio ducking lowers the main audio during the announcement
5. The resolved caller name is reported back to RotaryPhone via SignalR (`ReportCallerResolved`)
6. When the call ends (`Ended` or `Idle`), the announcement stops and audio returns to normal
7. Call state changes are broadcast to the Web UI in real-time via SignalR

### Troubleshooting

- **"Disconnected" but server is running:** Check firewall rules, verify the hub URL is reachable from the Radio Console host with `curl http://<phone-server>:5555/hubs/phone/negotiate`
- **No announcement on ring:** Check TTS engine availability in **System Config → Event Sources**. Ensure at least one TTS engine (ESpeak, Google, or Azure) is configured
- **Wrong caller name:** Verify the contacts API response shape matches the expected schema above
- **Ring sound doesn't play:** Verify the file exists at the configured path and is a valid WAV or MP3

### Google Voice (gvbridge) Messages

The same RotaryPhone service exposes a Google Voice bridge that the Web UI consumes for the unified **Messages** feed on `/phone` (voicemail + texts). Like the rest of the phone integration, Radio Console consumes it over REST/SignalR **only** — no GV backend runs in-process.

**Base host:** `radio:5004` (same RotaryPhone service as the call integration above).

**REST (read), under `/api/gvbridge/*`:**

| Method | Route | Returns |
|--------|-------|---------|
| GET | `/api/gvbridge/status` | `{ available, activeMode, sipRegistered?, cookiesValid? }` — drives the reconnecting banner + Send gate |
| GET | `/api/gvbridge/voicemail?count=&pageToken=` | `VoicemailListDto` (paged) |
| GET | `/api/gvbridge/voicemail/{id}` | `VoicemailItemDto` |
| GET | `/api/gvbridge/voicemail/{id}/audio` | voicemail recording (bind an **absolute** URL — see gotcha) |
| GET | `/api/gvbridge/sms/threads?count=` | `SmsThreadListDto` |
| GET | `/api/gvbridge/sms/threads/{threadId}?count=` | `SmsThreadMessagesDto` |
| POST | `/api/gvbridge/sms/send` | `SendSmsResponse` — **shipped** on RotaryPhone, dark behind their `GVBridge:EnableSmsSend=false` (returns `409` + `Code:"send_disabled"`). Gated on our side by `RotaryPhone:Gv:SendEnabled` (PR3). **Response shapes have diverged — reconcile before flipping** (see FUTURE-WORK §12 item 2) |

**REST (mark-read / durable read-state, PR4 — ADR-024):** gated by `RotaryPhone:Gv:MarkReadEnabled` (consumer flag); body `{ "isRead": true }`; returns the updated DTO; `200`→DTO, `404`→gone (null), `502`/non-2xx→null but the UI keeps its optimistic flip and reconciles on the next list/poll/push; **no client-side auto-retry**.

| Method | Route | Returns |
|--------|-------|---------|
| POST | `/api/gvbridge/voicemail/{id}/read` | updated `VoicemailItemDto` (`isRead` authoritative) |
| POST | `/api/gvbridge/sms/threads/{threadId}/read` | updated `SmsThreadDto` (`hasUnread` authoritative) |

**SignalR push:** rides the existing `/hub` RotaryHub connection (consumed by `PhoneHubService`):
- `SmsReceived` → `PhoneHubService.GvSmsReceived` (`SmsMessageDto`)
- `VoicemailReceived` → `PhoneHubService.GvVoicemailReceived` (`VoicemailItemDto`)
- `ReadStateChanged` → `PhoneHubService.ReadStateChanged` (`ReadStateChangedDto { Kind, Id, ThreadId, IsRead, ChangedAtUtc }`, PR4) — unified read-state change. RotaryPhone broadcasts **unconditionally, including back to the originator**, so consumers MUST de-dupe by `(id-or-threadId + isRead)` (see the `ReadStateReconciler` gotcha below). Unknown `Kind` is ignored defensively.

> **GV SMS is NOT the same product as VoIP.ms trunk SMS.** Trunk SMS rides `GvTrunkHubService.SmsReceived` on `/hubs/gvtrunk` with a different payload. The C# event for GV SMS is deliberately named `GvSmsReceived` so the two can't be confused. Do not merge or rename them. (ADR-022 §4.3.)

**Config keys** (`appsettings.json`; per-machine overrides in `appsettings.Production.json`):

```json
"RotaryPhone": {
  "ApiBaseUrl": "http://radio:5004",
  "HubUrl": "http://radio:5004/hub",
  "Gv": {
    "SendEnabled": false,        // flip true when POST /sms/send ships (PR3)
    "MarkReadEnabled": false,    // flip true when the two POST .../read routes ship (PR4)
    "StatusPollSeconds": 10,     // GvBridgeStatusService poll interval
    "AuthKey": ""                // set to enable X-RotaryPhone-Auth header (OFF today)
  }
}
```

**Gotchas:**
- **Voicemail audio URL must be absolute.** The DTO's relative `AudioUrl` resolves against the Web origin (`:5002`) and 404s — always rebuild it against the API base via `GvBridgeApiService.GetVoicemailAudioUrl(id)` (→ `http://radio:5004/...`). Never bind the relative `AudioUrl` (ADR-022 D4).
- **A failed conversation load is NOT an empty conversation (GV-8 / UAT F-1).** `GetSmsThreadMessagesAsync` returns `GvResult<SmsThreadMessagesDto>` — `Success` / `HttpError` (with `StatusCode` + RotaryPhone's `error`/`code` discriminator) / `Timeout` / `Transport` / `Malformed` — precisely because it used to map all of them to `null`, which `PhonePage` then coalesced with `?? new()` into an empty list. A 502 and a genuinely empty thread became byte-identical, and the pane rendered **"Start the conversation below."** with no error, no spinner and no retry. The conversation pane now branches **skeleton → error → empty → list** in that order, so the empty copy is reachable only for a real zero-message result. Two consequences worth holding: **(a)** a group thread (id containing `/`) returns a genuine **HTTP 200 with `messages: []`** because RotaryPhone never decodes the `%2F` — from our side that IS an empty, and rendering it as one is correct, not a bug (their fix is tracked as a cross-repo item); **(b)** the failure is **invisible to browser instrumentation** — Blazor Server fetches server-side over SignalR, so the UAT saw 0 console errors and 0 failed requests. The only probe is `journalctl -u radio-web --since '-30min' | grep 'Failed to get GV SMS thread'` — keep it bounded with `--since`, do not tail; the box is an Intel N100 and heavy journald reads compete with the audio pipeline. **`GvResult<T>` is the reusable shape** — GV-6 adopts it for the mark-read methods rather than inventing a second mechanism.
- **`psidtsAgeSeconds` is the ONLY trustworthy field on `GET :5004/api/gvbridge/status` — and it is a live blackout clock.** Twice-confirmed (2026-07-31 root-cause pass and the GV-8 UAT). Google's PSIDTS cookie is good for ~11 minutes; RotaryPhone's CDP refresh only fires every ~20, with no reactive refresh on 401 — so GV auth is dead for roughly **9 minutes out of every 20**, on a wall clock that is independent of anything we do. Read it as: **`< 660` healthy · `660–1200` blackout (expect HTTP 502 on every GV read) · resets at ~1200.** The sibling fields **lie** — `{"available": true, "degraded": false, "cookiesValid": true, "psidtsAgeSeconds": 707}` was captured while both SMS endpoints were returning hard 502s, which is exactly why our "Google Voice is reconnecting" banner (`PhoneMessagesPanel.razor:14-20`) never fires in the window it exists for. **Practical consequence: any test of this surface that does not record `psidtsAgeSeconds` (or wall-clock time) produces results that look random.** That is how a previous pass came to hypothesise throttling — a hypothesis the logs then falsified three ways (401 never 429; failure tracks wall-clock not request volume; recovery lands on fixed boundaries). Prefer this one-shot probe over reading journals; the box is an Intel N100 and heavy journald reads compete with the audio pipeline. _Both the refresh interval and the dishonest health fields are RotaryPhone-side items, tracked in `docs/BUILDER_QUEUE.md` § Cross-repo handoffs #6._

  ```bash
  curl -s http://192.168.86.50:5004/api/gvbridge/status
  ```

- **Read-state is durable via GV write-through (PR4 / ADR-024), behind `RotaryPhone:Gv:MarkReadEnabled`.** When the flag is ON, heard/read persists to Google Voice (the single source of truth); RadioConsole keeps **no local read-state store** — list endpoints' `isRead`/`hasUnread` are authoritative on every (re)load. When the flag is OFF (today), read-state is per-circuit optimistic only and any list reload — hard reload, the Refresh button, or error-retry — re-derives from `isRead`/`hasUnread`. Missed calls count toward the topbar `/phone` badge (owner decision 2).
- **The de-dupe invariant (ADR-024 §9) — the one correctness rule.** Every mark yields ≥2 signals: the mark route's returned DTO **and** the echoed `ReadStateChanged` broadcast (RotaryPhone re-broadcasts unconditionally, including back to the originator); a 502 adds a third "keep optimistic, reconcile later" path. All collapse to one badge state via `ReadStateReconciler`, keyed by `(id-or-threadId + isRead)` — applying an already-applied mark is a no-op (no re-render, no flicker).
- **Two-flag distinction.** `RotaryPhone:Gv:MarkReadEnabled` is **our consumer flag** (gates whether our seam calls the route). RotaryPhone's server-side `GVBridge:EnableMarkRead` is **their config flag** (gates whether their already-shipped route acts or returns the dark rejection). Both default `false` and are independent — **flip theirs first**, confirm the route no longer rejects, then flip ours. Their `GVBridge:AllowMarkUnread` (also `false`) separately gates `isRead:false`, which we never send.
- **Mark-read auth is auto-covered.** The two `POST .../read` routes ride the `/api/gvbridge/*` prefix gate and reuse `RotaryPhone:Gv:AuthKey` via the existing `RotaryPhoneAuthHandler` — no new auth key (ADR-024 §7).
- **`RotaryPhoneAuthHandler` is OFF** until `Gv:AuthKey` is set. ~~A native `<audio>` element can't send that header, so an auth-gated audio endpoint would break the direct-`<audio>` approach (ADR-022 §8.1).~~ **Superseded by [ADR-029](decisions/2026-08-03-gv-audio-through-engine.md) (2026-08-03):** voicemail audio no longer goes to the browser at all — Radio.API fetches it server-side through `GvMediaClient`, which *can* attach the header. **The "keep the audio endpoint unauthenticated" constraint is dissolved and the standing cross-repo ask should be withdrawn** (`docs/BUILDER_QUEUE.md` § Carried risks #3). Note the closure depends on Radio.API holding its own `GvMedia:AuthKey` — it would *not* hold under an opaque-URL design (ADR-029 §10.1).

#### Second consumer: the kiosk desktop launcher (`radio-console-open`)

Since 2026-08-18 the Web UI is **not** the only thing in this repo that leans on the GV bridge.
`deploy/debian-x64/kiosk/bin/radio-console-open` — the "Radio Console" desktop icon, installed to
`/usr/local/bin` by `setup-kiosk.sh` — probes the bridge and, when it is down, **invokes**
`gv-bridge-ensure.sh` to bring it back.

- **`gv-bridge-ensure.sh` is RotaryPhone-owned, and this is invoke-and-probe only.** The launcher
  calls it and reads its exit code, and does nothing else with it: it never writes, installs,
  edits or owns bridge startup, never touches the watchdog or nightly timers, and never `pkill`s
  the bridge. **The entire contract is two things — a path and an exit code.** The path is
  resolved from a candidate list at runtime (`~/bin/` → `/usr/local/bin/` →
  `/opt/rotary-phone/bin/`) because RotaryPhone is bringing the script under version control and
  may relocate it; a relocation then degrades to a *reported* failure rather than a wrong one.
- **The launcher checks liveness, never auth**, and it reads `psidtsAgeSeconds` by exactly the
  rule above plus one thing this doc had not needed to spell out before: **`> 1200`, or the field
  absent or null, is the launcher's dead-session test.** The counter resets at ~1200 in every
  healthy cycle, so a value beyond it means the refresh cycle itself never fired — which is what
  makes a single stateless probe sufficient, with no timestamp file and no history.
- **The `660–1200` trough is deliberately never reported.** It is the appliance working as
  designed, it clears itself inside ten minutes, and the launcher can do nothing about it.
  Surfacing it would fire a dialog on roughly 45% of taps, and a status surface that cries wolf
  is worse than no status surface: by the time something is genuinely broken it is already being
  dismissed unread. The in-app `/phone` banner owns transient auth decay; the launcher does not.
- The bridge process is identified by its profile directory
  (`--user-data-dir=~/.config/gv-bridge-chrome`), never by matching `chrome`. **Never widen any
  kill or match to `chrome`** — that is what used to take the bridge down.

---

## 3. Notification / Announcement API

A REST endpoint that any external service can call to trigger a TTS announcement with audio ducking. No setup required beyond having the Radio Console API running.

### Endpoint

```
POST http://<radio-console>:5000/api/notifications/announce
Content-Type: application/json

{
  "Message": "Dinner is ready!",
  "Priority": 8
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Message` | string | Yes | Text to announce via TTS |
| `Priority` | int | No | Audio ducking priority 1-10 (default: 8). Higher = more important |

### Response

**Success (200):**
```json
{ "message": "Announcement played" }
```

**Validation error (400):**
```json
{ "error": "Message is required" }
```

### Usage Examples

**curl:**
```bash
curl -X POST http://radio:5000/api/notifications/announce \
  -H "Content-Type: application/json" \
  -d '{"Message": "The washing machine is done", "Priority": 7}'
```

**Home Assistant automation:**
```yaml
action:
  - service: rest_command.radio_announce
    data:
      message: "Motion detected at the front door"
      priority: 9
```

With a REST command configured as:
```yaml
rest_command:
  radio_announce:
    url: "http://radio:5000/api/notifications/announce"
    method: POST
    content_type: "application/json"
    payload: '{"Message": "{{ message }}", "Priority": {{ priority | default(8) }}}'
```

**Python:**
```python
import requests

requests.post("http://radio:5000/api/notifications/announce", json={
    "Message": "Package delivered",
    "Priority": 6
})
```

### Testing from the Web UI

Open **System Config → Integrations → Notifications**, type a message, set the priority, and click **Send Test**. You'll hear the TTS announcement through the Radio Console speakers.

### How Audio Ducking Works

When an announcement is triggered:
1. The current audio (music, radio, etc.) is smoothly lowered in volume
2. The TTS announcement plays at the specified priority level
3. Once the announcement finishes, the original audio volume is restored
4. ~~Higher priority announcements can interrupt lower priority ones~~ — **this is not true today.** Verified 2026-08-03 while writing [ADR-029](decisions/2026-08-03-gv-audio-through-engine.md): ducking is **binary and reference-counted**, not priority-weighted. The first event to start fades the primary source to the fixed global `Audio:DuckingPercentage` (20); **every subsequent concurrent event changes nothing** (`DuckingService.cs:138-143`), and full volume is restored only when the *last* event leaves. Concurrent events **mix** — none stops, preempts or queues behind another — and `GetPriority` is read in exactly one place, inside `GetActiveEventsByPriority`, which together with `StopAllDuckingAsync` has **zero non-test callers**. So the `Priority` field below is currently accepted, validated, stored, and then **ignored**. ADR-029 §6 introduces the first load-bearing use of it (attended playback preempted at ≥ 8); until that ships, treat the guidance table as intent rather than behavior.

Priority guidelines:
| Priority | Use Case |
|----------|----------|
| 1-3 | Low importance (informational, timers) |
| 4-6 | Medium importance (smart home events, reminders) |
| 7-8 | High importance (doorbell, notifications) |
| 9-10 | Critical (phone calls, alarms, security alerts) |

---

## Web UI Management

All three integrations can be monitored and configured from the Web UI at:

**System Config → Integrations** (8th tab)

The Integrations tab has three sub-tabs:

1. **Rotary Encoders** — Connection status, VID/PID, encoder mapping reference, full configuration editor
2. **Phone Integration** — Hub connection status, current call state, caller info, full configuration editor
3. **Notifications** — Test announcement form for sending TTS announcements

Status indicators update in real-time via SignalR — no page refresh needed.

> **Note:** Configuration changes made in the Web UI are saved to the configuration store but require a service restart to take effect (the `Enabled` flag and connection parameters are read at startup).

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        Radio.API                                │
│                                                                 │
│  RotaryEncoderHostedService ← IRotaryEncoderService (HID)       │
│    └→ RotaryEncoderActionRouter → IAudioManager                 │
│                                 → VisualizationModeService      │
│                                                                 │
│  PhoneCallIntegrationService ← IPhoneIntegrationService (SignalR)│
│    └→ PhoneContactLookupService (PBAP SQLite + REST fallback)   │
│                                                                 │
│  PbapSyncService ← IPbapSyncService (D-Bus OBEX via Python)     │
│    └→ PbapContactRepository (SQLite)                            │
│    └→ VCardParser                                               │
│    └→ IAnnouncementService → ITTSFactory + IDuckingService      │
│                                                                 │
│  NotificationsController                                        │
│    └→ IAnnouncementService → ITTSFactory + IDuckingService      │
│                                                                 │
│  IntegrationsController (status endpoints for Web UI)           │
│    └→ IRotaryEncoderService.IsConnected                         │
│    └→ IPhoneIntegrationService.IsConnected/CurrentState         │
│                                                                 │
│  AudioStateUpdateService                                        │
│    └→ Broadcasts EncoderConnectionChanged via SignalR            │
│    └→ Broadcasts PhoneCallStateChanged via SignalR               │
└─────────────────────────────────────────────────────────────────┘
         ↕ SignalR                    ↕ HTTP
┌─────────────────────────────────────────────────────────────────┐
│                        Radio.Web                                │
│                                                                 │
│  SystemConfigPage → Integrations tab (3 sub-tabs)               │
│    └→ IntegrationsApiService (status polling)                   │
│    └→ ConfigurationApiService (config load/save)                │
│    └→ AudioStateHubService (real-time events)                   │
└─────────────────────────────────────────────────────────────────┘
```

### Key Files

| Layer | File | Purpose |
|-------|------|---------|
| Core | `Interfaces/Input/IRotaryEncoderService.cs` | Encoder service contract |
| Core | `Interfaces/External/IPhoneIntegrationService.cs` | Phone service contract |
| Core | `Interfaces/Audio/IAnnouncementService.cs` | TTS announcement contract |
| Core | `Configuration/RotaryEncoderOptions.cs` | Encoder config options |
| Core | `Configuration/PhoneIntegrationOptions.cs` | Phone config options |
| Infrastructure | `Platform/Input/HidRotaryEncoderService.cs` | USB HID reader + event firing |
| Infrastructure | `Platform/Input/RotaryEncoderActionRouter.cs` | Maps encoder events → audio actions |
| Infrastructure | `External/PhoneCallClient.cs` | SignalR client for RotaryPhone hub |
| Infrastructure | `External/PhoneContactLookupService.cs` | PBAP + REST contact lookup |
| Infrastructure | `Bluetooth/PbapSyncService.cs` | PBAP sync service (D-Bus OBEX) |
| Infrastructure | `Bluetooth/PbapContactRepository.cs` | SQLite contact storage |
| Infrastructure | `Bluetooth/VCardParser.cs` | vCard 2.1/3.0 parser |
| Infrastructure | `Bluetooth/pbap_download.py` | Python D-Bus OBEX helper script |
| Infrastructure | `Audio/Services/AnnouncementService.cs` | TTS + ducking orchestration |
| Infrastructure | `Audio/Services/VisualizationModeService.cs` | Viz mode cycling for encoder 3 |
| API | `Controllers/NotificationsController.cs` | `POST /api/notifications/announce` |
| API | `Controllers/IntegrationsController.cs` | `GET /api/integrations/{encoder,phone}/status` |
| API | `Services/RotaryEncoderHostedService.cs` | Encoder lifecycle management |
| API | `Services/PhoneCallIntegrationService.cs` | Phone call event handling + caller ID |
| API | `Controllers/PbapController.cs` | PBAP sync/contacts/lookup REST endpoints |
| Web | `Services/ApiClients/IntegrationsApiService.cs` | HTTP client for status endpoints |
| Web | `Components/Pages/SystemConfigPage.razor` | Integrations tab UI |
