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

**Status:** Deferred — features existed in the old `/queue` page but were excluded from the `FileBrowserDialog` consolidation (PR #263)
**Added:** 2026-03-02
**Priority:** Low — current home-page `QueueHistoryPanel` + `FileBrowserDialog` covers primary use cases

### What Was Removed

The dedicated `/queue` page had several features not carried over:

1. **Drag-reorder queue items** — The QueuePage had a toggle for drag-and-drop reordering using `MudDropContainer`. The `QueueHistoryPanel` does not support drag reorder. The API endpoint `POST /api/queue/move` exists and works.

2. **Drive selector** — The QueuePage file browser had a drive dropdown (`FileApiService.GetDrivesAsync()`). Not needed on the single-drive Linux kiosk target, but useful if NAS mounts are added later.

3. **Custom path entry + virtual keyboard** — The QueuePage allowed typing an arbitrary filesystem path in a text field. Not needed when the media root is configured via `fileplayer.RootDirectory` in system config.

### What's Needed to Re-add

- **Drag-reorder**: Add `MudDropContainer` to `QueueHistoryPanel` queue list, wire up `QueueApiService.MoveItemAsync(fromIndex, toIndex)`. CSS for `.queue-item-draggable` / `.queue-item-dragging` already exists in `design-system.css`.
- **Drive selector**: Add drive dropdown to `FileBrowserDialog` toolbar, call `FileApiService.GetDrivesAsync()`. Only show when multiple drives are mounted.
- **Path entry**: Add a text input mode to `FileBrowserDialog` breadcrumbs for manual path entry.

### Gotchas

- Drag-and-drop in MudBlazor requires `MudDropContainer` + `MudDropZone` with explicit item tracking — the queue index changes after each move, so refresh from API after each reorder
- Touch drag on the kiosk needs `touch-action: none` on draggable items
