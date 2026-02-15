# Progress Log

## Session: 2026-02-13 — Cast Audio & BT Fixes

### Pre-planning fixes completed:
- PR #192 (merged): BT capture bridge, DI factories, visualization tap, codec pinning, album art fix
- PR #193 (merged): Metrics transaction/connection mismatch fix
- PR #194 (merged): Cast audio — reader lag, LAME Flush fix, reduced tap latency

### Planning phase:
- Wrote 5-phase plan (see task_plan.md)
- Phases 1-4 implemented and tested (1266 tests pass, 0 warnings)

### Phase 1-4 Implementation:
- All code changes committed as PR #195 (merged)

---

## Session: 2026-02-14 — Pi Hardware Testing

### Dual-service deployment:
- PR #196: Split radio-console.service into radio-api + radio-web
- PipeWire/WirePlumber BT A2DP sink config
- ALSA direct hardware access for radio system user

### Pi debugging — BT audio pipeline:
1. Discovered arecord subprocess runs and captures real audio data (strace confirmed non-zero 24KB writes)
2. Found Serilog `Default: Warning` was hiding all audio pipeline logs — added `Radio: Information` override
3. Identified race condition in `GetAudioCaptureDeviceAsync` — two concurrent handlers with 0-timeout semaphore
4. Fixed with 30s timeout + `_activeGenerator` cache
5. Discovered `SwitchPlaybackDevice` bug — orphans source components after device switch
6. Fixed with `PlaybackDeviceSwitched` event + subscriber re-attachment in `SoundFlowPlaybackService`
7. Set device to playback-12 (bcm2835 Headphones), confirmed audio plays through soundbar
8. All fixes committed as PR #197

### Verified on Pi:
- [x] BT connect → arecord → generator → mixer → playback in <1 second
- [x] AVRCP metadata flowing (title/artist)
- [x] AVRCP volume sync (68% from phone)
- [x] Play history updated with real track info
- [x] Audio output to soundbar via 3.5mm jack
- [x] Device preference saved to config store

### Still pending verification:
- [ ] Restart preference restore (playback-12 auto-selected)
- [ ] BT album art display in Web UI
- [ ] Cast output with BT source
- [ ] Cast latency measurement
- [ ] Volume persistence across restart
- [ ] Fingerprint skip after identification
- [ ] BT progress bar in Web UI
- [ ] BT next/previous buttons in Web UI
- [ ] Sample drop rate after fixes
