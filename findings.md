# Findings & Decisions

## Implementation Status Assessment: ~85% Structurally Complete, Audio Pipeline Non-Functional

### CRITICAL BUG: Audio Capture Pipeline Broken on Both Platforms

**BluetoothAudioSource.InitializeAsync()** (line 57-77) does:
```csharp
var capture = await _bluetoothService.GetAudioCaptureDeviceAsync(ct);
if (capture is AudioCaptureDevice audioCapture) { SoundComponent = audioCapture; }
else { State = AudioSourceState.Error; }
```

**Neither platform returns an AudioCaptureDevice:**
- **Linux**: Returns `null` (TODO comment, no implementation)
- **Windows**: `FindCaptureDeviceByName()` returns a `string` (device name), NOT an `AudioCaptureDevice`
  - The `is AudioCaptureDevice` check always fails → source always goes to Error state

**Root cause:** `SoundFlowDeviceManager.FindCaptureDeviceByName()` returns `match.Name` (string) not an `AudioCaptureDevice` instance. The method needs to create/return an actual SoundFlow `AudioCaptureDevice` from the found device info.

### Windows Bluetooth Service Issues
1. **GetAudioCaptureDeviceAsync** returns wrong type (string vs AudioCaptureDevice)
2. **No AVRCP/metadata support** — no media player events, no track info from connected device
3. **InTheHand.Net** is a Bluetooth Classic library — it handles pairing/discovery but NOT A2DP sink role or audio capture directly
4. **Windows doesn't easily support A2DP Sink** (acting as a speaker) — Windows typically acts as A2DP Source (sends audio TO speakers). The "Bluetooth speaker" scenario requires the phone to stream audio, which Windows receives via its standard audio endpoint system, not via InTheHand.

### Linux Bluetooth Service Issues
1. **GetAudioCaptureDeviceAsync** returns null (unimplemented)
2. **Pairing/Unpairing** are stubs (return false)
3. **DisconnectAsync** is a no-op
4. **D-Bus metadata watcher** is partially implemented but untested
5. **SoundFlowDeviceManager not injected** — constructor only takes logger + options

### BluetoothAudioSource Issues
1. Calls `_bluetoothService.StartAsync(Name, ct)` with `Name` = "Bluetooth Audio" instead of the configured device name from BluetoothOptions
2. Doesn't subscribe to `DeviceConnected`/`DeviceDisconnected` events
3. No `NeedsFingerprintingLookup` flag (unlike FilePlayer/SDR sources)
4. Missing album art URL in metadata propagation

### What IS Working
- IBluetoothService interface design (comprehensive, includes MetadataChanged events)
- BluetoothOptions/Preferences config classes
- BluetoothController (all endpoints implemented)
- BluetoothDtos
- DI registration (factory pattern with platform detection)
- AudioManager integration (factory, auto-switch on connect, cleanup)
- MockBluetoothService (for testing)
- Test structure (unit + integration tests exist)

### Web UI Status
- **No Bluetooth management page exists** — only PlayHistoryPage references "Bluetooth" (for source type display)
- DeviceManagementPage handles Cast/USB devices but not Bluetooth

### Metadata Pipeline
- IBluetoothService has MetadataChanged, PlaybackStatusChanged, PositionChanged events
- BluetoothPlaybackMetadata class has: Title, Artist, Album, Duration (NO AlbumArtUrl)
- BluetoothAudioSource subscribes to MetadataChanged → updates StandardMetadataKeys
- Linux has partial D-Bus media player property watcher (MPRIS/BlueZ MediaPlayer1)
- Windows has NO metadata support at all

### HFP Compatibility Assessment
- Current architecture cleanly separates A2DP from other profiles
- IBluetoothService is profile-agnostic (methods are about device management, not audio-specific)
- Future HFP would be a separate service or extend IBluetoothService
- No blockers for future RotaryPhone HFP integration

### Key Architectural Decision: How Audio Capture Works
On both platforms, when a phone connects via Bluetooth and streams audio:
- **Linux**: BlueZ + PulseAudio/PipeWire creates an audio source automatically. We need to find and capture from it.
- **Windows**: Windows audio system creates a virtual audio endpoint for the Bluetooth device. We capture from it via standard audio APIs (MiniAudio/SoundFlow).

In both cases, the approach is:
1. Platform service finds the OS audio device corresponding to the Bluetooth connection
2. Create a SoundFlow `AudioCaptureDevice` from that OS device
3. Return it to BluetoothAudioSource for pipeline integration

This is similar to how USB audio sources work — hence why BluetoothAudioSource extends USBAudioSourceBase.
