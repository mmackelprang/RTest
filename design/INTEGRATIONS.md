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
4. Higher priority announcements can interrupt lower priority ones

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
