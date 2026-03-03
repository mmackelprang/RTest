# Progress Log

## Session: 2026-03-03

### Completed
- Implemented plan: Replace Auto-Gain with AVRCP-Driven PipeWire Node Volume
- Deleted `SourceLevelLearningService.cs` (~190 lines)
- Removed auto-gain infrastructure from `AudioPreferencePersistence.cs` (~250 lines)
- Removed BT↔Master volume sync from `AudioStateUpdateService.cs` (~60 lines)
- Removed auto-gain endpoints from `AudioController.cs`
- Removed auto/manual UI from `NowPlayingPanel.razor` and `SystemConfigPage.razor`
- Removed `AutoGainInfoDto`, auto-gain API client methods
- Simplified `IAudioManager`/`AudioManager` — removed auto-gain methods
- Changed MaxGain 25→2 (per-source gain is now just user trim)
- Deployed to Ubuntu, verified BT playback works
- **Discovery**: PipeWire's bluez5 module manages AVRCP→node volume natively with cubic perceptual mapping
- Removed redundant pw-cli volume override (was being overwritten by PipeWire)
- Removed `BluetoothInputTrim` config (not needed — PipeWire handles AVRCP natively)
- Reset BT source gain from 2.0x (old auto-gain artifact) to 1.0x
- Total removal: ~600 lines of auto-gain infrastructure

### Key Findings
- PipeWire's bluez5 module converts AVRCP 0-127 to cubic volume automatically
  - AVRCP 81/127 (0.6378 linear) → PipeWire 0.259 (cubed)
- Old problem was forcing node volume to 1.0, defeating PipeWire's AVRCP management
- Fix: just don't override PipeWire's node volume — let it handle AVRCP natively
- Phone volume changes flow through PipeWire automatically (no app code needed)
- Console master volume stays independent of phone volume

### Errors / Blockers
- ObjectDisposedException on service restart (benign — old instance D-Bus shutdown race)
