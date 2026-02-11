# Future Work — Stubbed & Unimplemented Features

This document catalogs features that have been designed at the interface level but not yet fully implemented. Each entry includes the rationale for the stub, the platform APIs needed to complete it, and any known gotchas.

> **Rule:** When a feature is stubbed rather than implemented, it must be documented here with enough context for a future developer (or LLM) to pick it up without re-researching the problem.

---

## 1. Bluetooth AVRCP Volume Sync

**Status:** Interface defined, stubs in place, consumer not wired
**Added:** 2026-02-11 (Phase 3 of audio-latency-research-and-fixes)
**Priority:** Medium — Cast volume sync is functional; BT volume is a nice-to-have

### What Exists

| Layer | File | What's There |
|-------|------|-------------|
| Interface | `Radio.Core/Interfaces/Audio/IBluetoothService.cs` | `VolumeChanged` event, `DeviceVolume` property, `SetDeviceVolumeAsync()` method, `BluetoothVolumeChangedEventArgs` class |
| Windows stub | `Radio.Infrastructure/Platform/Bluetooth/WindowsBluetoothService.cs:229-250` | Event declared (CS0067 suppressed), `DeviceVolume` property (null), `SetDeviceVolumeAsync` logs debug and returns |
| Linux stub | `Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs:98-109` | Same pattern — event, null property, no-op method |
| Mock/Null | `MockBluetoothService.cs`, `BluetoothServiceFactory.cs` (NullBluetoothService) | Empty event handlers, null DeviceVolume, no-op SetDeviceVolumeAsync |
| Consumer | `Radio.API/Services/AudioStateUpdateService.cs` | **Not wired** — only Cast volume sync was connected to AudioManager. BT volume events are not subscribed. |

### What's Needed — Windows

Windows maps AVRCP absolute volume to the Bluetooth audio endpoint's system volume. The OS manages this transparently, so the approach is to monitor/control the endpoint volume directly.

**Read volume on connect:**
```csharp
// NAudio CoreAudioApi — find the BT audio endpoint
using NAudio.CoreAudioApi;
var enumerator = new MMDeviceEnumerator();
// Enumerate active render endpoints, find the one matching the BT device
// (match by device friendly name or device ID containing the BT address)
var btEndpoint = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
    .FirstOrDefault(d => d.FriendlyName.Contains(btDeviceName));
float volume = btEndpoint.AudioEndpointVolume.MasterVolumeLevelScalar; // 0.0-1.0
```

**Set volume:**
```csharp
btEndpoint.AudioEndpointVolume.MasterVolumeLevelScalar = volume; // 0.0-1.0
```

**Watch for changes (external volume knob on phone/headphones):**
```csharp
// NAudio AudioEndpointVolumeCallback
btEndpoint.AudioEndpointVolume.OnVolumeNotification += data =>
{
    // data.MasterVolume is 0.0-1.0, data.Muted is bool
    VolumeChanged?.Invoke(this, new BluetoothVolumeChangedEventArgs { Volume = data.MasterVolume });
};
```

**Gotchas:**
- Must be under `#if WINDOWS_TARGET` — NAudio.CoreAudioApi requires Windows
- The BT endpoint may not appear immediately after `AudioPlaybackConnection.Open()` — may need a short poll/delay
- If WASAPI loopback is active and the default endpoint is muted, the endpoint volume callbacks still fire but the actual audio is captured pre-mute
- `MMDeviceEnumerator` is COM — must be created on an STA thread or use `Task.Run` with marshalling
- The endpoint may change if the user switches BT codecs (SBC→AAC) — watch `DeviceStateChanged`

### What's Needed — Linux (Raspberry Pi Target)

BlueZ exposes AVRCP volume via D-Bus on the `org.bluez.MediaTransport1` interface.

**Read volume on connect:**
```csharp
// Using Tmds.DBus — find the MediaTransport1 object for the connected device
// Path pattern: /org/bluez/hci0/dev_XX_XX_XX_XX_XX_XX/fdN
var transport = Connection.System.CreateProxy<IMediaTransport1>(
    "org.bluez", transportPath);
byte volume = await transport.GetVolumeAsync(); // 0-127
float normalized = volume / 127f; // → 0.0-1.0
```

**Set volume:**
```csharp
await transport.SetVolumeAsync((byte)(volume * 127)); // 0.0-1.0 → 0-127
```

**Watch for changes:**
```csharp
// Subscribe to PropertiesChanged on the MediaTransport1 interface
await transport.WatchPropertiesAsync(changes =>
{
    if (changes.TryGetValue("Volume", out var vol))
    {
        var newVolume = (byte)vol / 127f;
        VolumeChanged?.Invoke(this, new BluetoothVolumeChangedEventArgs { Volume = newVolume });
    }
});
```

**Gotchas:**
- `MediaTransport1` only appears when A2DP transport is active (audio is streaming) — not just "connected"
- The transport path is dynamic (`/org/bluez/hci0/dev_.../fd0`, `fd1`, etc.) — must enumerate or watch `InterfacesAdded`
- Volume property may not exist if the remote device doesn't support AVRCP absolute volume (older devices)
- `Tmds.DBus` is only in the `net8.0` TFM ItemGroup — this code must be excluded from the Windows TFM via `Compile Remove`
- BlueZ volume (0-127) maps to AVRCP absolute volume; not all headphones support set (some are read-only)

### Wiring the Consumer

When either platform raises `VolumeChanged`, it needs to reach `IAudioManager.MasterVolume`. The Cast volume sync pattern in `AudioStateUpdateService` shows how:

```csharp
// In AudioStateUpdateService constructor (or similar coordinator):
_bluetoothService.VolumeChanged += OnBluetoothVolumeChanged;

private void OnBluetoothVolumeChanged(object? sender, BluetoothVolumeChangedEventArgs e)
{
    if (_audioManager == null) return;
    if (Math.Abs(_audioManager.MasterVolume - e.Volume) > 0.01f)
        _audioManager.MasterVolume = e.Volume;
}
```

The reverse direction (app volume → BT device) should be triggered when `MasterVolume` changes and the active source is Bluetooth:
```csharp
// When MasterVolume changes and active output is BT:
if (_audioManager.ActiveSource?.Type == AudioSourceType.Bluetooth)
    await _bluetoothService.SetDeviceVolumeAsync(_audioManager.MasterVolume);
```

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

## 5. SoundFlow Metadata Gaps (FileBrowser)

**Status:** Library limitation — SoundFlow's `SoundTags` doesn't expose all ID3 fields
**Added:** 2026-02-11
**Priority:** Low — FilePlayerAudioSource reads these via `SoundMetadataReader` which does expose them

### What Exists

| File | What's There |
|------|-------------|
| `Radio.Infrastructure/Audio/Services/FileBrowser.cs:403-405` | Returns `null` for track number, genre, and year with TODO comments |

### Context

`FileBrowser.GetFileMetadataAsync()` uses `SoundFlow.Providers.SoundMetadataReader.Read()` and accesses `formatInfo.Tags`. The `SoundTags` type exposes `Title`, `Artist`, `Album` but not `TrackNumber`, `Genre`, or `Year`.

However, `FilePlayerAudioSource.UpdateMetadataFromFile()` (line 1530) also uses `SoundMetadataReader.Read()` and DOES get `Tags.Genre`, `Tags.Year`, `Tags.TrackNumber` from `formatInfo.Tags`. This suggests the API may have been updated, or FileBrowser is using an older pattern.

**Resolution:** Check if `formatInfo.Tags.Genre`, `formatInfo.Tags.Year`, and `formatInfo.Tags.TrackNumber` are available in the current SoundFlow version and update FileBrowser accordingly. This may already work — the TODOs may be stale.
