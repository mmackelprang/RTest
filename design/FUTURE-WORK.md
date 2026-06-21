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

**Status:** Display DPMS control implemented via GNOME ScreenSaver D-Bus in `SleepService`. UI power button works. Rotary encoder integration pending.
**Added:** 2026-03-16
**Priority:** Medium — integrate when rotary encoders are wired up

### What's Implemented

- `SleepService` (Radio.API) pauses audio, mutes, turns off display via GNOME ScreenSaver D-Bus, broadcasts via SignalR
- Web UI power button (`power_settings_new` icon in MainLayout topbar) triggers sleep
- Wake via API call (`POST /api/system/sleep { sleep: false }`) or touch screen (via JS idle-dimmer)
- Display DPMS on/off via `gdbus call --session --dest org.gnome.ScreenSaver ... SetActive true/false`

### What's Needed — Rotary Encoder

When rotary encoders are integrated via `RotaryEncoderActionRouter`:
1. **Sleep trigger:** Long-press (or dedicated button press) on a rotary encoder should call `ISleepService.EnterSleepAsync()`
2. **Wake trigger:** Any rotary encoder event (press or turn) while sleeping should call `ISleepService.WakeAsync("rotary-encoder")` BEFORE processing the encoder action
3. **Implementation:** In `RotaryEncoderActionRouter`, check `ISleepService.IsSleeping` at the top of each event handler. If sleeping, call `WakeAsync` and consume the event (don't pass it through as a source change or volume adjustment)

### Code Pointers

- `src/Radio.API/Services/SleepService.cs` — sleep/wake logic + DPMS control
- `src/Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs` — encoder event routing
- `src/Radio.Core/Interfaces/ISleepService.cs` — interface

### Gotchas

- GNOME ScreenSaver D-Bus requires the desktop session user (`mmack`) and session bus address — Radio.API runs `sudo -u mmack DBUS_SESSION_BUS_ADDRESS=... gdbus call`
- The `SetActive(true)` call may not reliably wake the display on all hardware — test with the actual touchscreen. If unreliable, fall back to `gnome-monitor-config` or `xdg-screensaver reset`
- On wake, turn on display FIRST (before unmuting/resuming) so the user sees the UI immediately

---

## 12. GV (Google Voice) Messages — UI-Local State, Send & Auth Seams

**Status:** PR1 foundation + PR2 voicemail surface + PR3 texts surface shipped (DTOs, read client, status poll, `/phone` unified Messages feed with call rows + voicemail rows + inline player + new-arrival path + **text thread rows interleaved into the feed** + master-detail conversation + bubbles + compose/new-recipient composer). **SMS send is feature-flagged OFF** (`RotaryPhone:Gv:SendEnabled=false`). The GV mark-read **endpoint** (the voicemail client seam is wired-but-no-op; the SMS thread mark-read sibling lands in GV-4) and the inter-service auth gate are deferred.
**Added:** 2026-06-20 (GV Messages PR1 — Foundation + IA shell); updated 2026-06-20 (PR2 — Voicemail surface).
**Updated:** 2026-06-21 (GV Messages PR3 — Texts surface reconciled onto PR2; texts wired into the `FeedItem` projection as `FeedKind.Text`, send + new-recipient composer flag-gated, on-screen entry reuses the existing global virtual keyboard).
**Priority:** Medium — read experience ships today; SMS send is one config flip + the endpoint. The seams below are wired OFF and ready to flip.

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
- UI-local mark-heard (`_locallyHeard` HashSet, in-memory per-circuit) — opening a voicemail flips it heard + decrements the badge.
- **GV mark-read client seam wired-but-no-op:** `GvBridgeApiService.MarkVoicemailReadAsync` is called on the heard path, gated on `RotaryPhone:Gv:MarkReadEnabled=false`. Today it returns `false` without hitting the network (fire-and-forget; never throws). Flip the flag + ship the route to light it up.

### What's Needed / Deferred

1. **UI-local voicemail/SMS read-state** — "heard"/"read" does NOT persist to Google Voice. A hard reload re-derives unread from the server's `isRead`/`hasUnread` fields. Missed-call badging is also UI-local (owner decision 2). To make read-state durable, RotaryPhone needs a **GV mark-read endpoint** (decision 4), then the client seam below becomes a real wire call.
   - **Gotcha:** because counts are UI-local, two browsers/circuits won't agree until a reload re-derives from the server fields.
2. **GV mark-read client seam** — voicemail side DONE in PR2: `GvBridgeApiService.MarkVoicemailReadAsync` is wired into the heard path and flagged off (`RotaryPhone:Gv:MarkReadEnabled=false`), no-op until RotaryPhone ships the endpoint. **Open thread to RotaryPhone:** request `POST /api/gvbridge/voicemail/{id}/read` be pulled forward (decision 4); confirm the route + verb before flipping the flag. The SMS-thread sibling (`MarkSmsThreadReadAsync` → `POST /api/gvbridge/sms/threads/{threadId}/read`) lands in **GV-4** (durable read-state); texts read-state is UI-local today (`_locallyReadThreads`).
   - **Constraint to hold:** keep the voicemail audio endpoint **unauthenticated** (ADR-022 §8.1) — the native `<audio src>` cannot send the auth header (see deferred item 4 gotcha).
3. **GV SMS send** — **built in PR3, flagged off** via `RotaryPhone:Gv:SendEnabled=false`. The whole compose/reply + new-recipient write path (optimistic → sending → sent → failed-with-preserved-text, 429/in-flight/degraded guardrails) is implemented behind `GvBridgeSendService` (`src/Radio.Web/Services/ApiClients/GvBridgeSendService.cs`); `SendAsync` throws `SendNotAvailableException` until the flag flips. Lights up via **one config flip** once `POST /api/gvbridge/sms/send` ships on RotaryPhone. **Confirm `SendSmsResponse` shape first** — it is marked **provisional** in the DTO and `SendAsync` reads `result.Message`; a shape mismatch silently fails the de-dupe.
4. **On-screen text entry — reuses the EXISTING global virtual keyboard.** The compose message field and the new-recipient field are ordinary `<input>`s; the app-wide keyboard (`wwwroot/js/virtual-keyboard.js`, loaded in `App.razor`) auto-shows on focus, and the recipient field opts into the numeric layout via `data-keyboard="numeric"`. **This supersedes the design spec's "build a touch keyboard" recommendation** — no new keyboard component was built or skinned. If explicit show/hide is ever needed, use `window.virtualKeyboardInterop.show(element)` / `.hide()`.
5. **`RotaryPhoneAuthHandler`** — header (`X-RotaryPhone-Auth`) injected only when `RotaryPhone:Gv:AuthKey` is non-empty; empty today (LAN-only no-auth posture). One place to flip on when the inter-service auth gate ships (ADR-022 §8.1). The `GvBridgeSendService` typed client carries this handler too, so send authenticates the moment the gate flips.
   - **Gotcha:** a native `<audio>` element CANNOT send the auth header. If the voicemail audio endpoint ever becomes auth-required, the direct-`<audio src>` approach (PR2) breaks — keep that endpoint unauthenticated or token-in-query (ADR-022 §8.1 / contract risk #4).
6. **Voicemail player Call back / Text back quick actions** — deferred (owner decision 3). The `VoicemailPlayer` carries a `@* fast-follow … *@` marker where these belong. Call back routes through the existing phone dial path; Text back opens/creates the GV text thread for the caller (the PR3 texts surface is now in place to host it). No UI shipped yet.

### Code Pointers

- `src/Radio.Web/Services/ApiClients/GvBridgeApiService.cs` — read methods + audio-URL builder + `MarkVoicemailReadAsync` flagged seam (GV-4 adds the SMS thread mark-read sibling).
- `src/Radio.Web/Services/ApiClients/GvBridgeSendService.cs` — **PR3 flagged send seam** (the only write path; 4 typed exceptions + in-flight/429/degraded guardrails).
- `src/Radio.Web/Components/Pages/PhoneTextsPanel.razor` — **PR3** thread list + conversation + compose + new-recipient composer.
- `src/Radio.Web/Components/Pages/MessageBubble.razor` — **PR3** inbound/outbound bubble + status glyph.
- `src/Radio.Web/wwwroot/js/phone-texts.js` — **PR3** auto-scroll-to-bottom helper for the conversation pane.
- `src/Radio.Web/Components/Pages/VoicemailRow.razor` / `VoicemailPlayer.razor` — voicemail row + inline player (PR2).
- `src/Radio.Web/wwwroot/js/voicemail-player.js` — seek + HTML5 `<audio>` event bridge (PR2).
- `src/Radio.Web/Services/Http/RotaryPhoneAuthHandler.cs` — the auth seam.
- `src/Radio.Web/Components/Pages/PhoneMessagesPanel.razor` — unified feed with the `FeedItem`/`FeedKind` projection: **PR2 added `Call`/`Voicemail`, PR3 added the `Text` case** + renders text thread rows interleaved newest-first + hosts the conversation in the detail pane.
- `design/decisions/2026-06-20-gvbridge-voicemail-sms-integration.md` — ADR-022 (decisions + risks).
- Config: `RotaryPhone:Gv:{SendEnabled,MarkReadEnabled,StatusPollSeconds,AuthKey}` in `appsettings.json` (+ `appsettings.Production.json` for per-machine `AuthKey`).
