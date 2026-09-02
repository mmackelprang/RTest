# Future Work — Stubbed & Unimplemented Features

This document catalogs features that have been designed at the interface level but not yet fully implemented. Each entry includes the rationale for the stub, the platform APIs needed to complete it, and any known gotchas.

> **Rule:** When a feature is stubbed rather than implemented, it must be documented here with enough context for a future developer (or LLM) to pick it up without re-researching the problem.

---

## 1. Bluetooth AVRCP Volume Sync — Windows

**Status:** Linux fully implemented; Windows still stubbed
**Added:** 2026-02-11 (Phase 3 of audio-latency-research-and-fixes)
**Updated:** 2026-02-12 — Linux implementation complete
**Priority:** Low — Linux (Pi target) is done; Windows is dev-only

### What's Implemented (Linux)

Bidirectional AVRCP volume sync via BlueZ `MediaTransport1` D-Bus interface:
- `LinuxBluetoothService.AttachMediaTransportAsync()` — attaches to `MediaTransport1` when A2DP transport appears
- `OnTransportPropertiesChanged()` — fires `VolumeChanged` event when phone volume changes
- `SetDeviceVolumeAsync()` — sets BlueZ volume (0-127) via D-Bus property
- `AudioStateUpdateService` — subscribes to `VolumeChanged`, syncs to `MasterVolume`, and pushes console volume changes back to the BT device

### What's Needed — Windows

Windows stub (`WindowsBluetoothService`) still needs implementation using NAudio CoreAudioApi:

```csharp
// NAudio CoreAudioApi — find the BT audio endpoint
using NAudio.CoreAudioApi;
var enumerator = new MMDeviceEnumerator();
var btEndpoint = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
    .FirstOrDefault(d => d.FriendlyName.Contains(btDeviceName));
float volume = btEndpoint.AudioEndpointVolume.MasterVolumeLevelScalar; // 0.0-1.0
```

**Gotchas:**
- Must be under `#if WINDOWS_TARGET` — NAudio.CoreAudioApi requires Windows
- The BT endpoint may not appear immediately after `AudioPlaybackConnection.Open()`
- `MMDeviceEnumerator` is COM — must be created on an STA thread
- The endpoint may change if the user switches BT codecs (SBC→AAC)

---

## 2. Radio Device Switching (API)

**Status:** Validates request, returns device info, does not switch
**Added:** 2026-02-11
**Priority:** Low — RF320 is the only radio hardware; SDR radio uses a separate source

### What Exists

| File | What's There |
|------|-------------|
| `Radio.API/Controllers/RadioController.cs:845-858` | `POST /api/radio/device/select` — validates device type, checks availability via `_radioFactory.IsDeviceAvailable()`, returns `RadioDeviceInfoDto` with `IsActive = false` |

### What's Needed

The controller needs to call `AudioManager.GetOrCreateSourceAsync(AudioSourceType.Radio)` with the selected device type. This requires:

1. `IRadioFactory.CreateRadioSource(deviceType)` already exists and works
2. The `AudioManager` would need a way to recreate the Radio source with a different device type (currently the source is cached — switching device type requires evicting the cached source)
3. Consider: should switching radio device type stop the current radio and create a new one, or hot-swap?

**Gotchas:**
- The current `_sourceCache` in AudioManager uses `AudioSourceType` as key — both RTL-SDR and RF320 are `AudioSourceType.Radio`. Switching between them requires cache eviction and source disposal.
- RF320 requires physical power — software can't start it. SDR can be started/stopped programmatically.

---

## 3. RF320 Radio Software Control

**Status:** Fully stubbed — hardware limitation, not a software gap
**Added:** 2026-02-11
**Priority:** None — this is a hardware constraint, not future work

### What Exists

`Radio.Infrastructure/Audio/Sources/Primary/RadioAudioSource.cs` — The RF320BT is a vintage Bluetooth-controlled radio with USB audio output. It has no software-controllable tuner. All control methods (`SetFrequencyAsync`, `StepFrequencyUpAsync`, `SetBandAsync`, `StartScanAsync`, `TogglePowerStateAsync`, etc.) are no-ops that log warnings.

**This is intentional and permanent.** The RF320 can only be controlled via its physical knobs and the RaddyRF320BT Bluetooth protocol (separate git submodule). If SDR radio support is the active device, these methods are handled by `SDRRadioAudioSource` which fully implements them.

No further action needed unless a new radio hardware type is added.

---

## 4. TTS Audio Cache

**Status:** Cache key computed, lookup not implemented
**Added:** 2026-02-11
**Priority:** Low — TTS is used infrequently (announcements, alerts)

### What Exists

| File | What's There |
|------|-------------|
| `Radio.Infrastructure/Audio/Services/TTSFactory.cs:91-102` | Computes `cacheKey = $"{engine}_{voice}_{text.GetHashCode()}"`, sets `isCached = false` always, increments cache hit/miss metrics |

### What's Needed

1. Define a cache directory (e.g., `./data/tts-cache/`)
2. On synthesis, save the audio stream to `{cacheDirectory}/{cacheKey}.wav` (or `.mp3`)
3. On lookup, check if `{cacheDirectory}/{cacheKey}.wav` exists and return it
4. Add TTL-based cleanup (similar to `AlbumArtCacheService` pattern — SHA256 content-addressed, 7-day TTL)
5. Consider cache invalidation when voice settings change

**Gotchas:**
- `text.GetHashCode()` is not deterministic across .NET processes/versions — use SHA256 of the text instead
- Cache should include voice parameters (speed, pitch) in the key, not just engine+voice+text
- Audio format should match what the TTS source expects (PCM WAV at engine sample rate)

---

## 5. Kiosk Mode (Fullscreen Browser)

**Status:** COMPLETE — running on Ubuntu x64 touchscreen (1920x720)
**Added:** 2026-02-13
**Priority:** Medium — needed for the final console radio experience, but deferred until all testing/debugging/validation is complete on the Pi

### What Exists

The Radio.Web Blazor Server UI runs as a separate systemd service (`radio-web.service`) on port 5002. The Pi's touchscreen display (1920x576) can access it via any browser.

### What's Needed

A kiosk mode setup that launches Chromium in fullscreen `--app` mode pointing at the local Web UI, auto-starting on boot.

#### Implementation Steps

1. **Auto-login for the display user** (e.g., `pi` or a dedicated `kiosk` user):

```bash
# /etc/systemd/system/getty@tty1.service.d/autologin.conf
[Service]
ExecStart=
ExecStart=-/sbin/agetty --autologin pi --noclear %I $TERM
```

2. **Chromium kiosk systemd user service** (`~/.config/systemd/user/radio-kiosk.service`):

```ini
[Unit]
Description=Radio Console Kiosk Browser
After=graphical-session.target
Wants=graphical-session.target

[Service]
Type=simple
Environment=DISPLAY=:0
ExecStartPre=/bin/sleep 5
ExecStart=/usr/bin/chromium-browser --kiosk --app=http://localhost:5002 --noerrdialogs --disable-infobars --disable-session-crashed-bubble --check-for-update-interval=31536000
Restart=on-failure
RestartSec=5

[Install]
WantedBy=graphical-session.target
```

3. **Disable screen blanking / power management:**

```bash
# /etc/xdg/lxsession/LXDE-pi/autostart (add these lines)
@xset s off
@xset -dpms
@xset s noblank
```

4. **Hide mouse cursor** (for touchscreen):

```bash
apt install unclutter
# Add to autostart: @unclutter -idle 0.1 -root
```

#### Gotchas

- Chromium `--kiosk` vs `--app`: `--kiosk` is true fullscreen (no window chrome, no way to exit without keyboard). `--app` shows minimal chrome but allows window management. For a console radio, `--kiosk` is preferred.
- The `ExecStartPre=/bin/sleep 5` ensures the X server and radio-web service are fully up before Chromium launches. May need adjustment.
- On Raspberry Pi OS Bookworm with Wayland (labwc), the approach differs — use `wlr-randr` for display config and ensure Chromium runs under Wayland.
- If using X11 (default on Pi OS Bookworm desktop), ensure LightDM auto-login is configured in `/etc/lightdm/lightdm.conf` under `[Seat:*]`.
- The 1920x576 ultrawide touchscreen may need custom display configuration via `xrandr` or `/boot/config.txt` HDMI settings.
- Consider adding a health-check loop that restarts Chromium if it crashes or if the Web UI becomes unresponsive.

---

## 6. Google Cast Custom Web Receiver — Low-Latency Streaming

**Status:** COMPLETE — registered, deployed, and tested with DirectChannel streaming
**Added:** 2026-02-16 (Phase 11.2 Cast latency reduction)
**Priority:** Medium — eliminates 10-15s Cast buffering delay; code-side changes already reduce ceremony from ~15s to ~8s

### What Exists

| File | What's There |
|------|-------------|
| `docs/receiver.html` | Complete CAF Custom Web Receiver with low-latency buffer config (deployed via GitHub Pages) |
| `Radio.Core/Configuration/AudioOutputOptions.cs` | `GoogleCastOutputOptions.ApplicationId` — configurable app ID (default: `CC1AD845`) |
| `Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs` | Uses `_options.ApplicationId` for all `LaunchApplicationAsync()` calls |
| `Radio.API/appsettings.json` | `AudioOutput.GoogleCast.ApplicationId` setting |

### How the Custom Receiver Reduces Latency

The Default Media Receiver (CC1AD845) has a hardcoded 10-15 second audio prefill buffer. For live radio streams, this creates unacceptable delay. The custom receiver uses CAF (Cast Application Framework) with:

- `initialPlaybackWatermark = 0.2` — start playback after just 200ms of buffered audio (vs 10-15s default)
- `resumePlaybackWatermark = 0.5` — resume after rebuffer with only 500ms
- `disableIdleTimeout: true` — keeps receiver alive for continuous streams
- `StreamType.LIVE` already enforced by our sender code

### Step-by-Step Setup

#### Step 1: Host `receiver.html` on HTTPS

Google Cast requires the receiver URL to be served over HTTPS. The simplest free option is GitHub Pages:

1. In your GitHub repo, go to **Settings → Pages**
2. Under "Source", select **Deploy from a branch**
3. Choose the `main` branch and `/deploy/cast-receiver` folder (or push `receiver.html` to a `docs/` folder on `main` if your repo structure requires it)
4. Click **Save** — GitHub will deploy to `https://<username>.github.io/<repo>/receiver.html`
5. Verify the URL loads in a browser — you should see a dark page with "Radio Console" text

Alternative: If GitHub Pages doesn't suit your setup, any static HTTPS host works (Firebase Hosting free tier, Netlify, Cloudflare Pages, etc.).

#### Step 2: Create a Google Cast Developer Account

1. Go to the [Google Cast SDK Developer Console](https://cast.google.com/publish/)
2. Sign in with your Google account
3. Pay the **one-time $5 registration fee** (non-refundable)
4. **Important**: The email address cannot be changed after account creation — use the account you want to own this long-term

#### Step 3: Register the Custom Receiver Application

1. From the Developer Console, click **Add New Application**
2. Select **Custom Receiver**
3. Fill in the form:
   - **Name**: `Radio Console` (displayed briefly while the receiver loads on Cast devices)
   - **URL**: Your HTTPS receiver URL from Step 1 (e.g., `https://yourname.github.io/RTest/receiver.html`)
   - **Relay casting**: Leave **disabled** (not needed for local network streaming)
   - **Audio-only devices**: **Enable** — this allows Cast to audio-only devices like speakers and smart displays in audio mode
4. Click **Save**
5. **Copy the Application ID** shown (e.g., `A1B2C3D4`) — you'll need this for configuration

#### Step 4: Register Your Cast Device for Testing

Unpublished apps only work on devices whose serial numbers are registered in the console.

1. In the Developer Console, click **Add New Device**
2. Find your Chromecast's serial number:
   - **Chromecast/Chromecast Audio**: Printed on the back of the device, or:
     - Open the Google Home app → tap your device → tap the gear icon → scroll to "Cast firmware version" area where serial is shown
   - **Android TV / Google TV**: Settings → System → About → Status → Serial number (use the **Cast serial number**, not hardware serial)
   - **Smart speakers/displays**: Open the Google Home app → tap your device → gear icon → scroll to serial number
3. Enter the **serial number** and a **description** (e.g., "Living Room Chromecast")
4. Click **OK**
5. **Wait 15 minutes** for registration to propagate
6. **Reboot your Cast device** — unplug power, wait 10 seconds, plug back in
7. The device status in the console should change to **"Ready for Testing"**

#### Step 5: Configure Radio Console to Use the Custom Receiver

Update `appsettings.json` (or the Pi's deployed config) with your new Application ID:

```json
"AudioOutput": {
  "GoogleCast": {
    "ApplicationId": "A1B2C3D4"
  }
}
```

Replace `A1B2C3D4` with the actual ID from Step 3. Then restart the Radio.API service.

#### Step 6: Verify It Works

1. Start Radio.API and Radio.Web
2. Open the Web UI → go to the Cast/Output section
3. Select your Cast device and connect
4. Play any audio source — audio should start on the Cast device within **1-3 seconds** instead of the 10-15 second delay with the default receiver
5. If something goes wrong, revert `ApplicationId` to `"CC1AD845"` to fall back to the default media receiver

#### Step 7: Publish (Optional)

Once tested and working, you can publish so the app works on **all** Cast devices without serial number registration:

1. In the Developer Console, click **Edit** on your application
2. Fill in the required listing details:
   - **Category**: Music & Audio
   - **Title**: `Radio Console` (≤50 chars)
   - **Description**: `Low-latency audio streaming for Radio Console` (≤80 chars)
   - **Icon**: Upload a 512×512 PNG icon
3. Click **Save**, then **Publish**
4. The app becomes available to all Cast devices worldwide (takes a few minutes to propagate)

### Gotchas

- **HTTPS required**: Cast receivers must be served over HTTPS. `http://` URLs are rejected by the Cast SDK.
- **Test device registration**: Unpublished custom receivers only work on devices registered in the Cast SDK Console. After registering, **reboot the Chromecast** for the change to take effect.
- **Registration propagation**: New app IDs can take 5-15 minutes to propagate to Cast devices after registration.
- **CAF v3 only**: The receiver uses CAF v3 (`cast_receiver_framework.js`). Do not use the deprecated v2 receiver SDK.
- **Fallback**: If the custom receiver has issues, revert `ApplicationId` to `"CC1AD845"` to use the default media receiver (with higher latency).

---

## 7. Google Cast — WebSocket + Web Audio API (Sub-500ms Latency)

**Status:** Researched & tested — blocked by mixed content policy
**Added:** 2026-02-19
**Priority:** Low — current MP3 approach (~3-10s latency) is acceptable for music; this only matters for intercom/doorbell use cases

### Concept

Replace the standard CAF media pipeline (`<audio>` element) with a fully custom receiver that:
1. Connects back to the C# app via WebSocket
2. Receives PCM audio chunks wrapped in 44-byte WAV headers
3. Decodes via `AudioContext.decodeAudioData()`
4. Schedules playback via `BufferSource.start()` with a tiny jitter buffer (~100ms)

This bypasses Chrome's 10-20s buffering entirely, achieving sub-500ms theoretical latency.

### What Would Be Needed

**C# Server Side:**
- WebSocket server (e.g., Fleck or `System.Net.WebSockets`) on a LAN port
- Audio capture in small chunks (50-100ms) from the existing `TappedOutputStream`
- Each chunk wrapped in a 44-byte WAV header (RIFF + fmt + data)
- Custom Cast namespace messages to send the WebSocket URL to the receiver

**Custom Receiver (JavaScript):**
- `AudioContext` + `decodeAudioData()` for chunk decoding
- Jitter buffer with scheduled playback via `BufferSource.start()`
- WebSocket reconnection logic
- No CAF media player — fully custom audio pipeline

### Why It's Blocked

**Mixed content policy** prevents the HTTPS-served receiver from connecting to local LAN servers:

| Protocol | Result | Notes |
|----------|--------|-------|
| `ws://` (plain WebSocket) | **BLOCKED** | Chrome 92 on Cast devices enforces mixed content. No TCP connection attempted. |
| `wss://` (self-signed cert) | **BLOCKED** | Chrome rejects self-signed certs from HTTPS context. No TLS handshake attempted. |

Both were tested on 2026-02-19 with a TCP/TLS listener on the Pi (port 9999) — zero connections received in 120s for each test.

### Possible Workarounds

1. **Trusted CA certificate** — Use Let's Encrypt with a real domain (e.g., via DuckDNS) pointing to the LAN IP. The receiver would connect to `wss://radio.duckdns.org:8080/audio`. Requires: domain registration, cert renewal automation, DNS configuration.

2. **Cast custom message channel** — Route audio data through the Cast SDK's custom namespace messaging (`urn:x-cast:com.radioconsole.audio`). This stays within the Cast protocol and avoids mixed content entirely. Downsides: message size limits, higher overhead, unknown latency characteristics for binary audio data.

3. **HTTP-served receiver** — Google Cast technically requires HTTPS for registered receivers, but some older Chromecast firmware may accept HTTP. Not reliable and not future-proof.

### Current Approach (For Reference)

The existing HTTP MP3 streaming approach achieves ~3-10s latency:
- Server delivers MP3 at ~1.1x real-time via throttled HTTP progressive download
- Custom receiver (`docs/receiver.html`) forces `play()` after 3s buffer
- `maxAheadSeconds=10` provides deep buffer for stable playback
- `lagSeconds=5` gives initial burst on connection

This is adequate for music playback and significantly simpler than the WebSocket approach.

---

## 8. Google Cast — Pre-loaded Event Sounds (Zero-Latency Alerts)

**Status:** Not implemented — documented for future use
**Added:** 2026-02-19
**Priority:** Low — only relevant if TTS/alert sounds need instant Cast playback

### Concept

Pre-load static audio files (doorbell, phone ring, TTS announcements) on the Cast receiver at startup. When the C# app needs to play an alert, it sends a lightweight JSON message via the Cast custom namespace — the receiver plays the pre-loaded audio instantly (no buffering, no streaming).

### What Would Be Needed

**Custom Receiver Changes:**
```javascript
// Pre-load sounds into browser memory
const sounds = {
  doorbell: new Audio('https://your-server.com/sounds/doorbell.mp3'),
  alert: new Audio('https://your-server.com/sounds/alert.mp3')
};
Object.values(sounds).forEach(s => s.load());

// Listen for custom namespace messages
const NAMESPACE = 'urn:x-cast:com.radioconsole.alerts';
context.addCustomMessageListener(NAMESPACE, (event) => {
  const msg = JSON.parse(event.data);
  if (sounds[msg.sound]) {
    sounds[msg.sound].currentTime = 0;
    sounds[msg.sound].play();
  }
});
```

**C# Sender Changes:**
- Add a custom namespace channel to the Cast connection
- Send JSON trigger messages: `{"sound": "doorbell"}`
- Sound files must be hosted on HTTPS (same server as receiver, or any HTTPS CDN)

### Gotchas

- Sound files must be served over HTTPS (same mixed content restriction)
- Pre-loading too many large files increases receiver startup time
- `new Audio()` + `.load()` may not work on all Cast device types (smart speakers vs Chromecast)
- The existing `AudioFileEventSource` in our pipeline handles event sounds locally — this would be an additional output path specifically for Cast devices

---

## 9. Queue UI — Deferred Features from /queue Page Removal

**Status:** Partially implemented — drive selector and path entry added; drag-reorder still deferred
**Added:** 2026-03-02
**Updated:** 2026-03-02 — Drive selector + path entry implemented in `FileBrowserDialog`
**Priority:** Low — current home-page `QueueHistoryPanel` + `FileBrowserDialog` covers primary use cases

### What Was Removed

The dedicated `/queue` page had several features not carried over:

1. **Drag-reorder queue items** — The QueuePage had a toggle for drag-and-drop reordering using `MudDropContainer`. The `QueueHistoryPanel` does not support drag reorder. The API endpoint `POST /api/queue/move` exists and works.

2. ~~**Drive selector**~~ — **IMPLEMENTED** in `FileBrowserDialog` location dropdown. Loads drives via `GET /api/files/drives`, supports browsing absolute paths via `GET /api/files?absolutePath=X`.

3. ~~**Custom path entry + virtual keyboard**~~ — **IMPLEMENTED** as editable path bar in `FileBrowserDialog`. Click the edit icon to enter an absolute path, press Enter to navigate.

### What's Still Needed

- **Drag-reorder**: Add `MudDropContainer` to `QueueHistoryPanel` queue list, wire up `QueueApiService.MoveItemAsync(fromIndex, toIndex)`. CSS for `.queue-item-draggable` / `.queue-item-dragging` already exists in `design-system.css`.

### Gotchas

- Drag-and-drop in MudBlazor requires `MudDropContainer` + `MudDropZone` with explicit item tracking — the queue index changes after each move, so refresh from API after each reorder
- Touch drag on the kiosk needs `touch-action: none` on draggable items

---

## 10. Rotary Encoders — Pico HID Report Format Verification

**Status:** Code implemented, HID report parsing assumed
**Added:** 2026-03-03
**Priority:** High — must verify on first hardware connection

### What Exists

| File | What's There |
|------|-------------|
| `Radio.Infrastructure/Platform/Input/HidRotaryEncoderService.cs` | HID reader, report parsing, event firing |
| `Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs` | Maps encoder events → volume/tune/source/viz |
| `Radio.API/Services/RotaryEncoderHostedService.cs` | BackgroundService, gated by `RotaryEncoder:Enabled` |
| `Radio.Core/Configuration/RotaryEncoderOptions.cs` | VID=0xCAFE, PID=0x4005, step sizes |

### What Needs Verification

The HID report format is **assumed** based on typical KY-040 Pico implementations:
- **Bytes 1-4**: signed encoder deltas (`sbyte` per encoder)
- **Byte 5**: button bitmask (bit N = encoder N)
- **Report size**: 8 bytes

**On first hardware connection:**
1. Enable logging, connect Pico, verify report bytes match assumed format
2. Adjust `ParseReport()` if the actual Pico firmware uses different byte offsets
3. Verify VID/PID match (`0xCAFE`/`0x4005` are TinyUSB defaults — may differ)
4. Linux: add udev rule `SUBSYSTEM=="hidraw", ATTRS{idVendor}=="cafe", ATTRS{idProduct}=="4005", MODE="0666"` for hidraw access

### Gotchas

- The Pico firmware is a separate project — both sides may need adjustments
- HidSharp on Linux needs hidraw permissions (udev rule or running as root)
- Encoder direction (CW = positive/negative) depends on wiring — may need to negate delta

---

## 11. Phone Call Integration — RotaryPhone Hub Protocol Verification

**Status:** Code implemented, hub protocol assumed
**Added:** 2026-03-03
**Priority:** High — must verify when RotaryPhone server is first available

### What Exists

| File | What's There |
|------|-------------|
| `Radio.Infrastructure/External/PhoneCallClient.cs` | SignalR client connecting to RotaryPhone hub |
| `Radio.Infrastructure/External/PhoneContactLookupService.cs` | REST client for contacts API |
| `Radio.API/Services/PhoneCallIntegrationService.cs` | BackgroundService orchestrating ring + TTS |
| `Radio.Core/Configuration/PhoneIntegrationOptions.cs` | Hub URL, ring sound path, priorities |

### What Needs Verification

**SignalR hub contract** (assumed):
- Hub URL: `http://radio:5004/hub`
- Server method: `CallStateChanged(string state, string phoneNumber)` — may also send caller name as 3rd param
- State values: `"Ringing"`, `"InCall"`, `"Ended"`, `"Idle"` — lenient parser handles variants

**Contacts REST API** (assumed):
- Endpoint: `GET {baseUrl}/api/contacts/lookup?phone={number}`
- Response: `{ "Name": "...", "PhoneNumber": "..." }`

**On first integration:**
1. Start RotaryPhone server, check actual hub URL path
2. Verify `CallStateChanged` method signature matches (param count, types)
3. Verify contacts API response shape
4. Test ring sound file exists at configured path (`media/sounds/phone-ring.wav`)
5. Provide a placeholder ring sound WAV

### Gotchas

- PhoneCallClient registers two `CallStateChanged` overloads (2 and 3 params) for resilience
- The call state parser is lenient (`"ringing"`, `"ring"`, `"incoming"` all map to Ringing)
- If RotaryPhone server isn't running, the client logs a warning and retries with backoff — no crash

---

## 7. Sleep Mode — Rotary Encoder Wake/Sleep Button

**Status:** ⚠ **CORRECTED 2026-09-02 by the `ENC-15` gate result — the previous status overclaimed in two
ways.** (1) It said *"Display DPMS control implemented … in `SleepService`"*; the display-power calls at
`SleepService.cs:84-87` and `:114-115` are **commented out**, so nothing in the running service turns the
panel off today. (2) It named `org.gnome.ScreenSaver SetActive` as the DPMS control; that route **does not
reach DPMS-off** — see the recovery section in `design/INTEGRATIONS.md` §1 and the full write-up at
[`docs/uat/2026-09-02-enc15-touch-wake-gate/REPORT.md`](../docs/uat/2026-09-02-enc15-touch-wake-gate/REPORT.md).
Sleep/wake as a *UI state* works; **panel blanking does not ship** and this section is the record of why.
**Added:** 2026-03-16
**Priority:** Medium for the encoder wake integration. ⛔ **Blanking itself: do not implement** — see the
blocker below.

### ⛔ Blocker — the panel has no usable wake path, measured on the box

`ENC-15` set out to confirm that touch could wake a blanked panel. It cannot, and the reason is
structural rather than a configuration problem:

- **The touchscreen is powered by the panel.** When the panel powers down the touch controller leaves the
  USB bus (`usb 3-1: USB disconnect` about a second after the blank, re-enumerating about a second after
  the unblank). There is no input device left to generate a touch event, at any layer.
- **The rotary encoder is not a compositor input device.** `cafe:4005` exposes `/dev/hidraw3` and **zero
  evdev nodes**, so it cannot reset the GNOME idle timer or wake a blank by itself. A knob wake works only
  if `radio-api` reads hidraw and *itself* issues the unblank — **making that service a single point of
  failure in the only remaining wake path**, which is precisely the condition under which somebody most
  wants the screen back.

That is one application-mediated wake path, not the two the design assumed. Blanking therefore stays off.
**What has not been tested is panel-firmware wake-on-touch** — a panel that watches its own digitizer while
powered down. If that exists on this hardware the conclusion changes; a confirmatory script was staged at
`/tmp/enc15-touchtest.sh` (note `/tmp` does not survive a reboot).

### What's Implemented

- `SleepService` (Radio.API) pauses audio, mutes, and broadcasts via SignalR. ⚠ **The display-power half is
  commented out** — the service does not blank the panel.
- Web UI power button (`power_settings_new` icon in MainLayout topbar) triggers sleep
- Wake via API call (`POST /api/system/sleep { sleep: false }`), or by touching the screen while it is still
  **lit** (the JS idle-dimmer dims in-page; it does not blank the panel). Touch on a *blanked* panel is a
  different case and does not work — see the blocker.

### What's Needed — Rotary Encoder

When rotary encoders are integrated via `RotaryEncoderActionRouter`:
1. **Sleep trigger:** Long-press (or dedicated button press) on a rotary encoder should call `ISleepService.EnterSleepAsync()`
2. **Wake trigger:** Any rotary encoder event (press or turn) while sleeping should call `ISleepService.WakeAsync("rotary-encoder")` BEFORE processing the encoder action. ⚠ This is an **application-mediated** wake, not a hardware one — it only functions while `radio-api` is running and reading hidraw.
3. **Implementation:** In `RotaryEncoderActionRouter`, check `ISleepService.IsSleeping` at the top of each event handler. If sleeping, call `WakeAsync` and consume the event (don't pass it through as a source change or volume adjustment)
4. ⚠ **The wake latch is a real defect here:** `TryWakeFromSleep` calls `WakeAsync` fire-and-forget (`:121`) and returns true, so with a 10 ms poll several more events arrive and are silently discarded before `IsSleeping` flips. Tracked on `ENC-6`.

### Code Pointers

- `src/Radio.API/Services/SleepService.cs` — sleep/wake logic; display-power calls present but commented out
- `src/Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs` — encoder event routing
- `src/Radio.Core/Interfaces/ISleepService.cs` — interface

### Gotchas

- GNOME ScreenSaver D-Bus requires the desktop session user (`mmack`) and session bus address — Radio.API runs `sudo -u mmack DBUS_SESSION_BUS_ADDRESS=... gdbus call`
- ⚠ **`org.gnome.ScreenSaver SetActive false` is a no-op when the panel is held down by DPMS rather than by the screensaver** — a state observed on this box, with `GetActive=(false,)` while `dpms=Off`. The control that works in both states is `org.gnome.Mutter.DisplayConfig PowerSaveMode`, set to `0`. The recovery commands, in the order to try them, are in `design/INTEGRATIONS.md` §1.
- An earlier revision of this section warned that *"`SetActive(true)` may not reliably wake the display"*. `SetActive(true)` **blanks**; `SetActive(false)` wakes. The real unreliability is the DPMS/screensaver split above.

## 12. GV (Google Voice) Messages — Durable Read-State, Send & Auth Seams

**Status:** PR1 foundation + PR2 voicemail surface + PR3 texts surface + PR4 durable mark-read shipped (DTOs, read client, status poll, `/phone` unified Messages feed with call rows + voicemail rows + inline player + new-arrival path + **text thread rows interleaved into the feed** + master-detail conversation + bubbles + compose/new-recipient composer + **durable read-state via GV write-through**). **SMS send is feature-flagged OFF** (`RotaryPhone:Gv:SendEnabled=false`). **Mark-read is now durable (GV write-through, ADR-024)** behind `RotaryPhone:Gv:MarkReadEnabled=false`. RotaryPhone's routes are **shipped** and dark behind their own `GVBridge:EnableMarkRead=false`, so lighting it up is a **two-side config flip** (theirs first), not a build wait. The inter-service auth gate remains deferred.
**Added:** 2026-06-20 (GV Messages PR1 — Foundation + IA shell); updated 2026-06-20 (PR2 — Voicemail surface).
**Updated:** 2026-06-21 (GV Messages PR3 — Texts surface reconciled onto PR2; texts wired into the `FeedItem` projection as `FeedKind.Text`, send + new-recipient composer flag-gated, on-screen entry reuses the existing global virtual keyboard); 2026-06-21 (PR4 — durable read-state: GV mark-read routes wired, unified `ReadStateChanged` event, idempotent reconciler).
**Priority:** Medium — read experience (incl. durable read-state) ships today behind flags; SMS send is one config flip + the endpoint. The seams below are wired OFF and ready to flip.

### What's Implemented (PR1)

- DTOs: `VoicemailItemDto`/`VoicemailListDto`, `SmsMessageDto`/`SmsThreadDto`/`SmsThreadListDto`/`SmsThreadMessagesDto`, `SendSmsRequest`/`SendSmsResponse`, `GvDirection` helper (`src/Radio.Web/Models/ApiModels.cs`).
- `GvBridgeApiService` read methods: voicemail list/item + **absolute** audio-URL builder, SMS threads/messages (`src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs`).
- `PhoneHubService.GvSmsReceived` / `GvVoicemailReceived` push events on the existing `/hub` connection.
- `GvBridgeStatusService` — single ~10s `/api/gvbridge/status` poll → reconnecting banner + (future) Send gate.
- `RotaryPhoneAuthHandler` — header seam, OFF.
- `PhoneUnreadState` — UI-local unread sum surfaced to the topbar `/phone` pill.
- `PhoneMessagesPanel` — unified feed shell (segmented filter + call rows + detail pane + reconnecting banner).

### What's Implemented (PR2)

- `VoicemailRow` + `VoicemailPlayer` components (`src/Radio.Web/Components/Pages/`) — row (chip + unread dot + caller + transcript-preview + duration) and inline accordion player (seekable Range-backed scrubber via `wwwroot/js/voicemail-player.js`, transcript present/pending/absent, 502 audio-error state). `<audio src>` is the **absolute** `radio:5004` URL from `GvBridgeApiService.GetVoicemailAudioUrl` (never the relative DTO field).
- Unified-feed `FeedItem`/`FeedKind` projection in `PhoneMessagesPanel` — interleaves calls + voicemail by timestamp under **All**; single-stream under **Voicemail**. **PR3 (texts) extends this projection** (add `FeedKind.Text` + project threads; the shape is intentionally trivial to extend).
- `GvVoicemailReceived` new-arrival path in `PhonePage` — prepend + animate row, calm `NotificationSeverity.Info` toast only (never modal, never pauses audio per the hard rule), badge ++.
- Mark-heard on the voicemail-open path — opening a voicemail flips it heard (optimistic) + decrements the badge. **PR4 made this durable** (see below): the optimistic flip is now a presentation-only bridge to the authoritative GV write-through.

### What's Implemented (PR4 — durable read-state, ADR-024)

- **GV mark-read routes wired** (`GvBridgeApiService`): `MarkVoicemailReadAsync` → `POST /api/gvbridge/voicemail/{id}/read`, `MarkSmsThreadReadAsync` → `POST /api/gvbridge/sms/threads/{threadId}/read`. Body `{ "isRead": true }`; both return the updated DTO (`VoicemailItemDto?` / `SmsThreadDto?`). `200`→DTO, `404`→null (item gone), `502`/non-2xx→null but the caller keeps the optimistic flip. **No client-side auto-retry** — one attempt per user action. Gated on `RotaryPhone:Gv:MarkReadEnabled=false`.
- **Unified `ReadStateChanged` event** on the existing `/hub` (`PhoneHubService.ReadStateChanged`, payload `ReadStateChangedDto { Kind, Id, ThreadId, IsRead, ChangedAtUtc }`). Defensive: unknown `Kind` ignored at Debug. RotaryPhone broadcasts **unconditionally, including back to the originator**.
- **Idempotent reconciler** (`ReadStateReconciler`, plain unit-tested class) keyed by `(id-or-threadId + isRead)` — the mark route's returned DTO, the echoed broadcast, and the 502-deferred reconcile all collapse to one badge state with no flicker (no-op when already in state).
- **No local read-state store** — list endpoints' `isRead`/`hasUnread` are source-of-truth on (re)load. The GV-2 `_locallyHeard` / GV-3 `_locallyReadThreads` seeding was removed; the per-circuit optimistic flip is presentation-only and is never replayed onto fresh list data.
- **Single write path** for voicemail mark-read: `PhonePage.OnVoicemailHeard` (the player bubbles via `OnHeard`; it no longer calls the route directly). **No unread toggle** (ADR-024 §6).

### What's Needed / Deferred

1. **Durable read-state — SHIPPED behind `MarkReadEnabled` (PR4 / ADR-024).** "heard"/"read" now persists to Google Voice via GV write-through; Google is the single source of truth and RadioConsole keeps no local read-state store. **RotaryPhone's side is shipped too** (no longer owner-HELD): `GvVoicemailController.MarkRead`, `GvSmsController.MarkThreadRead`, and the unconditional `ReadStateChanged` broadcast all exist, with route templates matching our call sites verbatim. **Both sides are dark by config only** — theirs `GVBridge:EnableMarkRead=false`, ours `RotaryPhone:Gv:MarkReadEnabled=false` — so lighting up is a **two-side config flip, not a build wait**: flip theirs first, confirm the routes stop returning the dark rejection, then set `RotaryPhone:Gv:MarkReadEnabled=true` in `appsettings.Production.json` (deploy overwrites `appsettings.json`). Until both are flipped the routes are never called and read-state is per-circuit optimistic only (re-derived from `isRead`/`hasUnread` on the next list load). Missed-call badging remains UI-local (owner decision 2).
   - **Behavior while our flag is off:** the optimistic flip is no longer replayed onto refetched lists (the GV-2/GV-3 `_locallyHeard`/`_locallyReadThreads` seeding is gone by design — ADR-024 §2). So hearing a voicemail and then pressing **Refresh** in the same session restores the unread marker, because we never wrote the read through. Lists reload only on initial load, the explicit Refresh button, and error-retry — no background poll refetches them — so this is the full extent of the difference. It resolves the moment both flags are on.
   - **Deferred (informational, not blocking) — unread support:** their `GVBridge:AllowMarkUnread` is `false`, so `isRead:false` returns `400 unread_unsupported`; our UI never sends `false` and the toggle stays hidden (ADR-024 §6). One UI change if unread is ever wanted, no contract change.
   - **Deferred (informational, not blocking) — path (b), phone→kiosk LIVE push:** RotaryPhone's poller-flip fast-follow. Until it ships, externally-originated reads (phone/GV-web) reconcile on our **next list refresh / poll**, not as an instant push. The SAME `ReadStateChanged` handler covers both — no GV-4-side change when it lands.
   - **Constraint to hold:** keep the voicemail audio endpoint **unauthenticated** (ADR-022 §8.1) — the native `<audio src>` cannot send the auth header (see deferred item 3 gotcha).
2. **GV SMS send** — **built in PR3, flagged off** via `RotaryPhone:Gv:SendEnabled=false`. The whole compose/reply + new-recipient write path (optimistic → sending → sent → failed-with-preserved-text, 429/in-flight/degraded guardrails) is implemented behind `GvBridgeSendService` (`src/Radio.Web/Services/ApiClients/GvBridgeSendService.cs`); `SendAsync` throws `SendNotAvailableException` until the flag flips. **`POST /api/gvbridge/sms/send` has now SHIPPED on RotaryPhone** (dark behind their `GVBridge:EnableSmsSend=false`), so the endpoint is no longer the blocker — but **do not flip `RotaryPhone:Gv:SendEnabled` until the response shape is reconciled.** The shapes have diverged: ours is `SendSmsResponse(SmsMessageDto? Message, string? Error)` (still marked **provisional**); theirs is `SendSmsResponse(bool Queued, string Code, string? ThreadId, string? Error, SmsMessageDto? Message)`. `Message`/`Error` still bind by name, so this fails *quietly* rather than loudly — we silently drop **`Queued`** and the **`Code`** taxonomy (`queued` | `invalid_number` | `rate_limited` | `auth_unavailable` | `upstream_error` | `timeout` | `send_disabled` | `error`) that `GvBridgeSendService`'s 429/degraded guardrails need to distinguish a retryable rate-limit from a hard failure. Their dark response is `409` + `Code:"send_disabled"`. Needs its own queue row.
3. **On-screen text entry — reuses the EXISTING global virtual keyboard.** The compose message field and the new-recipient field are ordinary `<input>`s; the app-wide keyboard (`wwwroot/js/virtual-keyboard.js`, loaded in `App.razor`) auto-shows on focus, and the recipient field opts into the numeric layout via `data-keyboard="numeric"`. **This supersedes the design spec's "build a touch keyboard" recommendation** — no new keyboard component was built or skinned. If explicit show/hide is ever needed, use `window.virtualKeyboardInterop.show(element)` / `.hide()`.
4. **`RotaryPhoneAuthHandler`** — header (`X-RotaryPhone-Auth`) injected only when `RotaryPhone:Gv:AuthKey` is non-empty; empty today (LAN-only no-auth posture). One place to flip on when the inter-service auth gate ships (ADR-022 §8.1). The `GvBridgeSendService` typed client carries this handler too, so send authenticates the moment the gate flips. **GV-4 mark-read routes ride the same `RotaryPhone:Gv:AuthKey` seam** — the `/api/gvbridge/*` prefix gate auto-covers the two POST routes; no new auth key (ADR-024 §7).
   - **Gotcha:** a native `<audio>` element CANNOT send the auth header. If the voicemail audio endpoint ever becomes auth-required, the direct-`<audio src>` approach (PR2) breaks — keep that endpoint unauthenticated or token-in-query (ADR-022 §8.1 / contract risk #4).
5. **Voicemail player Call back / Text back quick actions** — deferred (owner decision 3). The `VoicemailPlayer` carries a `@* fast-follow … *@` marker where these belong. Call back routes through the existing phone dial path; Text back opens/creates the GV text thread for the caller (the PR3 texts surface is now in place to host it). No UI shipped yet.

### Code Pointers

- `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs` — read methods + audio-URL builder + **PR4 durable mark-read** (`MarkVoicemailReadAsync` / `MarkSmsThreadReadAsync` → the two `POST .../read` routes; 200→DTO, 404→null, 502→null-keep-optimistic, no retry; flag-gated). **GV-8:** `GetSmsThreadMessagesAsync` is the one read method that returns `GvResult<T>` rather than `T?`; the others still return `T?` because their callers already handle `null` correctly (the thread list keeps its last good list and toasts).
- `src/Radio.Web/Services/ApiClients/GvResult.cs` — **GV-8** outcome type (`Success` / `HttpError` / `Timeout` / `Transport` / `Malformed`, plus `StatusCode` and RotaryPhone's `error`/`code` discriminator). Exists because collapsing every failure to `null` let a 502 render as an empty conversation (UAT F-1). **GV-6 adopts this same type** for the two mark-read methods — the two rows share the idiom, not the PR (see `docs/BUILDER_QUEUE.md` § Dependency / ordering notes).
- `src/Radio.Web/Services/ReadStateReconciler.cs` — **PR4** idempotent `(id-or-threadId + isRead)` reconciler (the ADR-024 §9 invariant; plain unit-tested class, no Blazor dep).
- `src/Radio.Web/Services/Hub/PhoneHubService.cs` — **PR4** `ReadStateChanged` event on the existing `/hub` (defensive Kind guard), alongside `GvVoicemailReceived` / `GvSmsReceived`.
- `src/Radio.Web/Services/ApiClients/GvBridgeSendService.cs` — **PR3 flagged send seam** (the only write path; 4 typed exceptions + in-flight/429/degraded guardrails).
- `src/Radio.Web/Components/Pages/PhoneTextsPanel.razor` — **PR3** thread list + conversation + compose + new-recipient composer.
- `src/Radio.Web/Components/Pages/MessageBubble.razor` — **PR3** inbound/outbound bubble + status glyph.
- `src/Radio.Web/wwwroot/js/phone-texts.js` — **PR3** auto-scroll-to-bottom helper for the conversation pane.
- `src/Radio.Web/Components/Pages/VoicemailRow.razor` / `VoicemailPlayer.razor` — voicemail row + inline player (PR2).
- `src/Radio.Web/wwwroot/js/voicemail-player.js` — seek + HTML5 `<audio>` event bridge (PR2).
- `src/Radio.Web/Services/Http/RotaryPhoneAuthHandler.cs` — the auth seam.
- `src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor` — unified feed with the `FeedItem`/`FeedKind` projection: **PR2 added `Call`/`Voicemail`, PR3 added the `Text` case** + renders text thread rows interleaved newest-first + hosts the conversation in the detail pane.
- `design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md` — ADR-022 (decisions + risks).
- `design/decisions/2026-06-20-gv-mark-read-durable-readstate.md` — ADR-024 (durable read-state contract + the de-dupe invariant).
- Config: `RotaryPhone:Gv:{SendEnabled,MarkReadEnabled,StatusPollSeconds,AuthKey}` in `appsettings.json` (+ `appsettings.Production.json` for per-machine `AuthKey`). `MarkReadEnabled` is the **consumer** flag (gates whether our seam calls the route); distinct from RotaryPhone's server-side `GVBridge:EnableMarkRead` config flag (gates whether their already-shipped route acts or returns the dark rejection). Flip theirs first.

---

## 13. Bell-Failure Surfacing — Live-Call States (blocked on RotaryPhone)

**Status:** Persistent signals shipped; the live-call + sticky-note states are stubbed pending a backend contract
**Added:** 2026-07-29 (PR "surface bell failures")
**Priority:** Medium — the shipped half already covers the "notice it at all" case; this half closes the 60-second window

### Why this exists

A call arrived, `/phone` showed RINGING in 96px amber with the pulse running, and the physical
rotary phone stayed silent for the full 60-second timeout. Nothing on screen indicated a
problem — the screen was not merely unhelpful, it was confidently wrong. Root cause was a
stale ring-INVITE target inside RotaryPhone (fixed there). Full spec, including the ASCII
mockups, copy deck and edge-case table, is in
`docs/design-handoffs/HANDOFF-bell-failure-surfacing.md`.

### What Exists

- `Radio.Web/Models/BellHealth.cs` — the `Ok` / `Suspect` / `Failed` / `Unknown` enum plus
  `BellHealthRules` (derivation, pill mapping, nav-pill accessible names).
- `Radio.Web/Services/BellHealthService.cs` — singleton `IHostedService` polling
  `GET /api/phone/system-status` app-wide (default 15s, `RotaryPhone:BellHealthPollSeconds`)
  so the topbar badge works from any route. `PhonePage` also `Publish()`es into it.
- Tri-state BELL pill in the System Status card, the `HT801 ATA` -> `BELL` relabel, the
  degraded idle hero (State C), and the crossed-bell nav-pill badge.

### What's Stubbed

**`BellHealth.Failed` has no producer.** It is modelled and handled everywhere a value is
consumed, but nothing ever sets it. Three UI states therefore do not exist yet:

1. **State B (§3.2)** — the live `.phone-hero-alert` strip during `Ringing`, the
   `· BELL SILENT` label suffix, and withdrawal of `.ring-pulse`.
2. **The predictive-degrade rule (§2)** — render the strip immediately at `Ringing` onset when
   health is already `Suspect`, because `BellInviteFailed` lands ~5s *after* `Ringing` and the
   hero would otherwise lie for that whole window.
3. **State D (§3.4)** — the sticky amber "A call at 2:14 PM didn't ring the phone." note that
   survives the call ending, plus its dismissal.

### What's Needed — from RotaryPhone (handoff §6)

| Item | Status | Needed for |
|---|---|---|
| `BellInviteFailed` hub event (§6.1, single-DTO shape) | REQUIRED | States B + D |
| `lastBellFailure` on `GET /api/phone/status` (§6.4) | REQUIRED | State D surviving a reload |
| `Ht801LastCheckedUtc` on `PhoneSystemStatusDto` (§6.3) | REQUIRED | The `last checked 14:32` sub-line, currently omitted |
| Recovery signal (§6.2) | REQUIRED | Clearing a fault without waiting for the next call |
| `PhoneCallStateDto.CallId` (§6.5) | Strongly requested | Correlating a failure to a specific call |
| `POST /api/phone/bell-failure/ack` (§6.6) | Requested | Durable dismissal across restarts |
| `POST /api/phone/bell/probe` (§6.7) | Requested | Making "Check again" a real probe rather than a status re-read |

File the request at `D:\prj\RotaryPhone\docs\prompts\radioconsole-bell-failure-request.md`,
following the convention set by `radioconsole-gv-markread-readstate-request.md`.

### Gotchas

- **Radio.Web is UI-only for phone functionality.** Consume RotaryPhone.API over REST/SignalR;
  never register RotaryPhone backend services here.
- **`Ht801Reachable == null` must never alarm** (§7m). It means "not probed / cannot
  determine". The original bug was rendering it as a red **Offline** on every cold load;
  `PhoneDashboardPanelTests.BellPill_NullReachable_RendersUnknown_NotOffline` pins this.
- **Non-obvious edge cases** the naive implementation gets wrong — handoff §7 (f), (h), (i),
  (m): a failure arriving after the call ended must NOT show a live strip; a later successful
  ring is the strongest recovery evidence; `[ Dismiss ]` must not silence a live fault; and
  absence of evidence is not a fault.
- **Reduced motion makes the glyph load-bearing.** `design-system.css:1675` zeroes all
  animation globally, so withdrawing `.ring-pulse` communicates nothing to those users. The
  crossed-bell glyph and text are mandatory channels, not redundancy (§4, §8.3).
- **`role="alert"` for the live strip only** (§8.1). It must be keyed stably or it re-announces
  on every hero re-render — the hero re-renders on each `InCall` duration tick.
- Remaining CSS for those states is specified verbatim in handoff §10:
  `.phone-hero-alert.is-warn` and `.phone-hero-state-label-fault`. Both were deliberately left
  out of the first PR rather than shipped unused.
