# Progress Log

## Session: 2026-03-05

### Phase 1: Analysis & Research

- [x] Extracted 151 AUDIO_DISTORTION_MARKER events from journalctl (14:05-15:42)
- [x] Mapped events to service PIDs / restart times (4 service instances in window)
- [x] Verified no clipping (all isClipping=False, peaks -6 to -11 dBFS)
- [x] Found PipeWire-pulse overrun events at 14:01:32 (MiniAudio output xrun)
- [x] Analyzed BufferedSoundGenerator stats — received > output (growing), no drops/compensations
- [x] Checked BT format: S24LE 48kHz 2ch (PipeWire converts to S16LE for our stream)
- [x] Read BufferedSoundGenerator code: lock contention between AddSamples + GenerateAudio
- [x] Read PipeWireNativeStream code: OnProcess on PW thread, Marshal.ReadInt16 per-sample conversion
- [x] Noted source state anomaly: 49% "Ready" during active audio playback
- [x] Wrote findings.md with ranked possible root causes

### Files Read (Research Only)
- `src/Radio.Infrastructure/Audio/SoundFlow/BufferedSoundGenerator.cs` (lines 90-327)
- `src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs` (lines 200-270)
- `src/Radio.Infrastructure/Audio/Sources/Primary/BluetoothAudioSource.cs` (lines 365-425)
- `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs` (lines 935-966)

### Files Modified
(none — research only)

### Test Results
(none — research only)
