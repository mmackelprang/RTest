# Bluetooth Audio Input - Quick Summary

## Overview

This document provides a high-level summary of the Bluetooth audio input implementation plan. For complete details, see [BLUETOOTH_IMPLEMENTATION_PLAN.md](./BLUETOOTH_IMPLEMENTATION_PLAN.md).

## What It Does

Allows the Radio Console system to act as a **Bluetooth speaker** that phones, tablets, and computers can connect to wirelessly. Audio received via Bluetooth will:
- Play through the system's speakers
- Show up in real-time visualizations (spectrum, levels, waveform)
- Be fingerprinted to identify songs
- Route through all configured outputs (local, Chromecast, HTTP stream)

## Key Features

- ✅ **User-Configurable Name**: "Grandpa's Radio" or any custom name
- ✅ **Cross-Platform**: Works on Raspberry Pi (Linux) and Windows
- ✅ **Seamless Integration**: Bluetooth treated as a standard audio source
- ✅ **Full Pipeline**: Visualization, fingerprinting, ducking, outputs
- ✅ **Easy Connection**: Phone discovers device and connects like any Bluetooth speaker
- ✅ **Persistent Config**: Settings and paired devices saved across restarts

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────┐
│                    Bluetooth Device (Phone)                   │
└───────────────────────────┬──────────────────────────────────┘
                            │ A2DP Audio Stream
                            ▼
┌──────────────────────────────────────────────────────────────┐
│            Platform-Specific Bluetooth Service                │
│  ┌─────────────────────┐        ┌──────────────────────┐    │
│  │ Linux (Raspberry Pi)│        │  Windows (Dev PC)    │    │
│  │  - BlueZ via D-Bus  │   OR   │  - 32feet.NET        │    │
│  │  - PulseAudio       │        │  - Windows APIs      │    │
│  └─────────────────────┘        └──────────────────────┘    │
└───────────────────────────┬──────────────────────────────────┘
                            │ PCM Audio Samples
                            ▼
┌──────────────────────────────────────────────────────────────┐
│               BluetoothAudioSource (New)                      │
│         implements IPrimaryAudioSource                        │
└───────────────────────────┬──────────────────────────────────┘
                            │ SoundFlow AudioCaptureDevice
                            ▼
┌──────────────────────────────────────────────────────────────┐
│                 SoundFlowMasterMixer                          │
│                 (Existing Component)                          │
└───────────┬────────────────┬────────────────┬────────────────┘
            │                │                │
            ▼                ▼                ▼
    ┌───────────┐    ┌──────────────┐  ┌────────────┐
    │Visualizer │    │Fingerprinting│  │  Outputs   │
    │ Service   │    │   Service    │  │Local/Cast  │
    └───────────┘    └──────────────┘  └────────────┘
```

## Implementation Phases (9 Total)

| Phase | Focus | Duration | Key Deliverables |
|-------|-------|----------|------------------|
| 1 | Core Architecture | 2-3 days | Interfaces, enums, config classes |
| 2 | Platform Implementations | 5-7 days | Linux & Windows Bluetooth services |
| 3 | Audio Source | 3-4 days | `BluetoothAudioSource` implementation |
| 4 | Audio Manager Integration | 2-3 days | Source registration and switching |
| 5 | API & Control | 3-4 days | REST endpoints, SignalR events |
| 6 | Configuration | 2 days | Settings persistence, validation |
| 7 | Verification | 1-2 days | Visualization & fingerprinting tests |
| 8 | Testing | 4-5 days | Unit, integration, platform tests |
| 9 | Documentation | 2-3 days | User guides, API docs, setup guides |

**Total Time**: 24-35 days (≈3.5-5 weeks)

## Technical Stack

### Linux (Raspberry Pi)
- **Bluetooth Stack**: BlueZ (system daemon)
- **Communication**: D-Bus protocol
- **NuGet Package**: `Tmds.DBus` (v0.15+)
- **Audio Routing**: PulseAudio loopback → SoundFlow
- **Profile**: A2DP Sink (Bluetooth speaker mode)

### Windows
- **Bluetooth API**: Windows.Devices.Bluetooth or 32feet.NET
- **NuGet Package**: `InTheHand.Net.Bluetooth` (v4.1+)
- **Audio Routing**: Windows audio endpoint → SoundFlow
- **Profile**: A2DP Sink (Bluetooth speaker mode)

### Common
- **Audio Engine**: SoundFlow (existing)
- **Sample Rate**: 44.1kHz or 48kHz
- **Format**: 16-bit stereo PCM
- **Latency Target**: <200ms

## New Files to Create

### Core Layer (`src/Radio.Core/`)
```
Configuration/
  ├── BluetoothOptions.cs           # Configuration options
  └── BluetoothPreferences.cs       # User preferences
Interfaces/Audio/
  └── IBluetoothService.cs          # Platform abstraction
```

### Infrastructure Layer (`src/Radio.Infrastructure/`)
```
Platform/Bluetooth/                 # New directory
  ├── BluetoothServiceFactory.cs    # Platform detection
  ├── Linux/
  │   ├── LinuxBluetoothService.cs
  │   └── BlueZManager.cs
  └── Windows/
      ├── WindowsBluetoothService.cs
      └── WindowsAudioCapture.cs
Audio/Sources/Primary/
  └── BluetoothAudioSource.cs       # New audio source
```

### API Layer (`src/Radio.API/`)
```
Controllers/
  └── BluetoothController.cs        # REST API
Models/
  ├── BluetoothStatusDto.cs
  ├── BluetoothDeviceDto.cs
  └── BluetoothSettingsDto.cs
```

### Tests
```
tests/Radio.Core.Tests/Configuration/
  └── BluetoothOptionsTests.cs
tests/Radio.Infrastructure.Tests/Audio/Sources/
  └── BluetoothAudioSourceTests.cs
tests/Radio.Infrastructure.Tests/Platform/
  └── BluetoothServiceTests.cs
tests/Radio.API.Tests/Controllers/
  └── BluetoothControllerTests.cs
```

### Documentation
```
design/
  ├── BLUETOOTH_SETUP.md            # Setup guide
  └── BLUETOOTH_ARCHITECTURE.md     # Technical details
```

## Configuration Example

```json
{
  "Bluetooth": {
    "DeviceName": "Grandpa's Radio",
    "AutoAcceptConnections": true,
    "RequirePairing": false,
    "EnableOnStartup": true,
    "AutoSwitchOnConnect": true,
    "AudioQuality": "High"
  }
}
```

## API Endpoints (New)

```
GET    /api/bluetooth/status              - Get Bluetooth status
POST   /api/bluetooth/start               - Start Bluetooth adapter
POST   /api/bluetooth/stop                - Stop Bluetooth adapter
POST   /api/bluetooth/discovery/start     - Start device discovery
POST   /api/bluetooth/discovery/stop      - Stop device discovery
POST   /api/bluetooth/pair                - Pair with device
DELETE /api/bluetooth/unpair/{address}    - Unpair device
POST   /api/bluetooth/connect             - Connect device
POST   /api/bluetooth/disconnect          - Disconnect device
PUT    /api/bluetooth/settings            - Update settings
GET    /api/bluetooth/devices/paired      - List paired devices
GET    /api/bluetooth/devices/discovered  - List discovered devices
```

## User Experience Flow

### Initial Setup
1. User navigates to Settings > Bluetooth
2. Sets device name to "Grandpa's Radio"
3. Enables "Auto-accept connections"
4. Clicks "Start Bluetooth"
5. System advertises as Bluetooth speaker

### Daily Use
1. User opens Bluetooth on phone
2. Phone discovers "Grandpa's Radio"
3. User taps to connect
4. System auto-switches to Bluetooth source (optional)
5. Audio plays through Radio Console
6. Visualizations show in real-time
7. Songs are fingerprinted and identified
8. Metadata updates on display

### Disconnection
1. User walks away with phone
2. Bluetooth disconnects automatically
3. System returns to previous audio source
4. No manual intervention needed

## Benefits

### For Users
- ✅ Play Spotify, YouTube, podcasts from phone through vintage radio
- ✅ No cables or physical connection needed
- ✅ Works like any Bluetooth speaker
- ✅ See song information even without app integration
- ✅ Seamless switching between sources

### For System
- ✅ Bluetooth is just another audio source
- ✅ No special handling needed for outputs
- ✅ Reuses existing visualization infrastructure
- ✅ Fingerprinting works automatically
- ✅ Ducking for notifications works
- ✅ Full metrics and logging

### For Developers
- ✅ Clean abstraction for platform differences
- ✅ Testable with mocked interfaces
- ✅ Follows existing patterns
- ✅ Well-documented
- ✅ Extensible for future enhancements

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| **BlueZ complexity** | Use proven D-Bus library, prototype early |
| **Audio latency** | Optimize buffers, test on real hardware |
| **Platform differences** | Strong abstraction, separate test plans |
| **Device compatibility** | Test with multiple phone models |
| **Pairing confusion** | Clear UI, automatic pairing option |

## Success Criteria

### Must Have ✅
- Bluetooth adapter starts and advertises device name
- Phone can discover and connect to system
- Audio plays with <200ms latency
- Visualization shows Bluetooth audio
- Fingerprinting identifies songs
- Works on both Pi and Windows
- Configuration persists

### Nice to Have 🎯
- AVRCP controls (play/pause from phone)
- Multiple simultaneous connections
- Codec selection (AAC, aptX)
- Connection history logging

## Next Steps After Approval

1. ✅ Create GitHub issues for each phase
2. ✅ Set up project board with milestones
3. ✅ Begin Phase 1: Core Architecture
4. ✅ Weekly progress reviews
5. ✅ UAT after Phase 7
6. ✅ Staging deployment after Phase 8
7. ✅ Production rollout after Phase 9

## Questions to Consider

Before starting implementation, consider:

1. **Device Name**: Should it default to "Grandpa's Radio" or "Radio Console"?
2. **Pairing**: Allow all devices or require manual approval?
3. **Auto-Switch**: Automatically switch to Bluetooth when device connects?
4. **Multiple Devices**: Allow multiple phones to connect (future enhancement)?
5. **UI Priority**: Should Bluetooth management be in Web UI or just API?

## Resources

- **Full Plan**: [BLUETOOTH_IMPLEMENTATION_PLAN.md](./BLUETOOTH_IMPLEMENTATION_PLAN.md)
- **BlueZ Documentation**: https://www.bluez.org/
- **32feet.NET**: https://github.com/inthehand/32feet
- **A2DP Specification**: Bluetooth SIG A2DP Profile v1.3
- **SoundFlow Docs**: https://lsxprime.github.io/soundflow-docs/

---

**Status**: 📋 Plan Complete - Ready for Review  
**Created**: 2026-02-06  
**Version**: 1.0
