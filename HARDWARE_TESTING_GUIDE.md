# Hardware Testing Guide

This document clarifies which features can be tested in a development environment vs which require physical hardware.

**Last Updated:** 2026-01-04

---

## Testing Categories

### ✅ Can Test WITHOUT Hardware (Development Environment)

These features can be fully tested on a development machine (Windows/Linux) without special hardware:

#### Web UI & User Experience
- ✅ **All Web UI Pages:** Home, Queue, Spotify, File Browser, Visualizer, Radio, System Config, Metrics
- ✅ **Navigation & Layout:** MainLayout, navbar, now playing widget
- ✅ **Touch Targets:** Visual inspection of button sizes, spacing, touch-friendliness
- ✅ **Material 3 Design:** Color schemes, typography, animations, transitions
- ✅ **Responsive Design:** Test at 1920x576 resolution (target display size)
- ✅ **File Browser:** Local file system browsing, path entry, virtual keyboard
- ✅ **Queue Management:** Add/remove/reorder queue items, multi-select, duplicate prevention
- ✅ **Configuration UI:** All settings pages and configuration changes
- ✅ **Metrics Dashboard:** Display of metrics data (with mock/test data)

#### API & Backend Logic
- ✅ **REST API Endpoints:** All API controllers and endpoints
- ✅ **Configuration System:** JSON and SQLite configuration stores
- ✅ **Preference Persistence:** AudioPreferences, FilePlayerPreferences, SpotifyPreferences
- ✅ **Unit Tests:** All xUnit test suites
- ✅ **bUnit Tests:** Blazor component tests
- ✅ **Integration Tests:** Non-hardware API and service tests

#### Audio (Limited Testing)
- ✅ **FilePlayer Source:** Play local audio files (MP3, FLAC, etc.)
- ✅ **Volume & Balance Controls:** Software controls
- ✅ **Queue Auto-Advance:** Play through file queue
- ✅ **Metadata Display:** From local audio files
- ✅ **Audio Visualization:** Waveform, spectrum, VU meters (with FilePlayer audio)

---

### ⚠️ Requires SPECIFIC Hardware/Setup

These features need particular hardware or configurations but may be possible on some development machines:

#### Spotify (Loopback Mode)
**Requirements:**
- Virtual audio cable (VB-Audio Cable on Windows, PulseAudio virtual sink on Linux)
- librespot or raspotify installed and configured
- Spotify Premium account
- Spotify client (mobile or desktop app)

**What Can Be Tested:**
- Spotify device appears in Spotify Connect list
- Play/pause/skip controls via Spotify app
- Track metadata retrieval
- Audio capture and visualization
- Loopback audio routing

**Setup Guides:**
- `SPOTIFY_LOOPBACK_QUICKSTART.md` - 5-minute setup
- `SPOTIFY_LOOPBACK_SETUP.md` - Detailed configuration
- `SPOTIFY_LOOPBACK_TESTING.md` - Test procedures

#### USB Audio Sources
**Requirements:**
- USB audio input device (vinyl preamp, cassette deck, etc.)
- Proper audio drivers installed

**What Can Be Tested:**
- USB device detection and enumeration
- Audio capture from USB devices
- Real-time audio processing
- GenericUSBAudioSource functionality

**Note:** May work with built-in microphone as substitute for basic testing.

---

### 🔧 Requires PHYSICAL RASPBERRY PI Hardware

These features absolutely require Raspberry Pi 5 with specific hardware:

#### Phase 5: RTL-SDR Radio Audio Output
**Hardware Required:**
- Raspberry Pi 5
- RTL-SDR USB dongle (RTL2832U + R820T2)
- FM antenna
- Linux/Raspbian OS

**What Needs Testing:**
1. RTL-SDR device detection on Raspberry Pi
2. RadioReceiver initialization and tuning
3. FM demodulation quality
4. SDRAudioDataProvider audio flow
5. Audio output through SoundFlow mixer
6. Signal strength and frequency display
7. Station scanning and presets
8. Audio quality and latency

**Why Pi Required:**
- RTL-SDR drivers are Linux-specific
- GPIO and hardware timing requirements
- ALSA configuration for SoundFlow
- Performance testing on actual target hardware

**Status:** Code implemented in `SDRRadioAudioSource.cs`, needs hardware validation.

#### Vinyl Audio Source
**Hardware Required:**
- Raspberry Pi 5
- Vinyl turntable with preamp OR
- USB vinyl preamp
- Audio cables

**What Needs Testing:**
- Vinyl audio input capture
- RIAA equalization (if needed)
- Audio quality assessment
- Real-time playback performance

#### Full System Integration
**Hardware Required:**
- Raspberry Pi 5
- 12.5" × 3.75" touchscreen (1920x576)
- Speakers/amplifier
- Complete audio routing setup

**What Needs Testing:**
1. **Touch Interface Performance:**
   - Touch responsiveness on actual display
   - Multi-touch gestures (if supported)
   - Touch target accuracy
   - Swipe gestures (Phase 11.3)
   
2. **Audio Performance:**
   - All audio sources working simultaneously
   - Audio mixing and routing
   - Latency measurements
   - CPU/memory usage under load
   - SoundFlow performance on Pi hardware
   
3. **Visualization Performance:**
   - Frame rate/updates per second on Pi
   - Spectrum analyzer smoothness
   - Waveform rendering performance
   - VU meter responsiveness
   
4. **System Stability:**
   - Extended runtime testing
   - Source switching
   - Configuration changes
   - Error recovery
   - Auto-restart after failures

---

## Testing Strategy

### Phase 1: Development Environment Testing (Now - No Hardware Needed)

✅ **Complete these tests on development machine:**

1. **Build & Unit Tests:**
   ```bash
   dotnet build
   dotnet test
   ```

2. **Web UI Testing:**
   - Start Radio.Web project
   - Navigate through all pages
   - Test all UI controls
   - Verify responsive layout at 1920x576
   - Test file browser with local files
   - Test queue management
   - Test configuration changes

3. **FilePlayer Audio:**
   - Play local MP3/FLAC files
   - Test queue functionality
   - Verify visualizations work
   - Test volume/balance controls
   - Verify metadata displays correctly

4. **API Testing:**
   - Use Postman/curl to test endpoints
   - Verify configuration API
   - Test queue API operations
   - Check metrics endpoints

5. **Documentation Review:**
   - Verify all README files are accurate
   - Check setup instructions
   - Review API documentation
   - Validate troubleshooting guides

### Phase 2: Development Environment with Special Setup (Optional)

⚠️ **If you have access to virtual audio or USB audio devices:**

1. **Spotify Loopback (if setup available):**
   - Follow `SPOTIFY_LOOPBACK_QUICKSTART.md`
   - Install VB-Audio Cable (Windows) or configure PulseAudio (Linux)
   - Install librespot
   - Test Spotify integration
   - Verify audio visualization with Spotify

2. **USB Audio (if device available):**
   - Connect USB audio interface
   - Test GenericUSBAudioSource
   - Verify audio capture
   - Test USB device switching

### Phase 3: Raspberry Pi Hardware Testing (Requires Physical Hardware)

🔧 **Must be done on actual Raspberry Pi 5:**

1. **Initial Setup:**
   - Deploy application to Pi
   - Configure audio devices
   - Set up touchscreen
   - Install RTL-SDR drivers (if testing radio)

2. **Phase 5: RTL-SDR Radio Testing:**
   - Follow verification steps in DEBUG_AND_FIX_PLAN.md Phase 5
   - Test all radio functionality
   - Measure performance
   - Document any issues

3. **Phase 10: Spotify Loopback Final Validation:**
   - Follow `SPOTIFY_LOOPBACK_TESTING.md`
   - Install raspotify
   - Configure PulseAudio loopback
   - Test end-to-end Spotify integration
   - Verify visualization performance

4. **Full System Integration:**
   - Test all audio sources on Pi
   - Measure performance metrics
   - Test touchscreen interaction
   - Validate visualization frame rates
   - Run extended stability tests

---

## Current Status Summary

| Phase | Component | Dev Testing | Hardware Testing | Status |
|-------|-----------|-------------|------------------|--------|
| 1 | Global Now Playing Widget | ✅ Complete | N/A | ✅ Done |
| 2 | Queue Navbar Visibility | ✅ Complete | N/A | ✅ Done |
| 3 | Now Playing Metadata | ✅ Complete | ⚠️ Verify on Pi | ✅ Code Done |
| 4 | Fingerprinting Debug | ✅ Complete | ⚠️ Verify on Pi | ✅ Code Done |
| 5 | RTL-SDR Radio Audio | 🔧 Cannot Test | 🔧 Required | ⏸️ Needs Hardware |
| 6 | Queue Page Enhancements | ✅ Complete | N/A | ✅ Done |
| 7 | Visualizer Graphics | ✅ Complete | ⚠️ Verify on Pi | ✅ Code Done |
| 8 | Home Page Media Controls | ✅ Complete | N/A | ✅ Done |
| 9 | File Browser Network Access | ✅ Complete | ⚠️ Test on Pi | ✅ Done |
| 10 | Spotify Loopback | ⚠️ Needs Setup | 🔧 Final Validation | ✅ Code Done |
| 11 | Queue Touch UX | ✅ Partial | 🔧 Touch Testing | ✅ Mostly Done |
| 12 | Material 3 Design | ✅ Can Test | ⚠️ Verify on Pi | ⏸️ Pending |

**Legend:**
- ✅ Complete - Fully tested and working
- ⚠️ Needs Verification - Code done, hardware testing recommended
- 🔧 Required - Must have hardware to test
- ⏸️ Pending - Not yet implemented or tested

---

## Hardware Test Session Checklist

When you have access to Raspberry Pi hardware, use this checklist:

### Pre-Session Setup
- [ ] Raspberry Pi 5 powered on and accessible
- [ ] Touchscreen connected and calibrated
- [ ] Speakers/amplifier connected
- [ ] Network connection established
- [ ] Latest code deployed to Pi
- [ ] All dependencies installed

### Phase 5: RTL-SDR Testing
- [ ] RTL-SDR dongle connected
- [ ] Antenna attached
- [ ] Driver installation verified: `rtl_test -t`
- [ ] Select "SDR Radio" in UI
- [ ] Tune to known FM station
- [ ] Verify audio output
- [ ] Test frequency scanning
- [ ] Check signal strength indicator
- [ ] Measure CPU usage
- [ ] Document any issues

### Phase 10: Spotify Testing  
- [ ] raspotify installed: `systemctl status raspotify`
- [ ] PulseAudio loopback configured
- [ ] Spotify credentials configured
- [ ] Select "Spotify" source in UI
- [ ] Verify device appears in Spotify app
- [ ] Play track from mobile app
- [ ] Verify audio quality
- [ ] Test visualization
- [ ] Check metadata display
- [ ] Test playback controls
- [ ] Measure performance metrics

### Touch Interface Testing
- [ ] Test all button interactions
- [ ] Verify touch target sizes feel right
- [ ] Test gestures (if implemented)
- [ ] Test virtual keyboard
- [ ] Check for accidental taps
- [ ] Verify responsiveness
- [ ] Test in landscape orientation

### Performance Testing
- [ ] Measure CPU usage at idle
- [ ] Measure CPU during playback
- [ ] Measure CPU during visualization
- [ ] Check memory usage
- [ ] Monitor temperature
- [ ] Test for thermal throttling
- [ ] Measure visualization FPS
- [ ] Check audio latency

### Stability Testing
- [ ] Run for 1 hour continuously
- [ ] Switch between all audio sources
- [ ] Test configuration changes
- [ ] Verify persistence after restart
- [ ] Test error recovery
- [ ] Check logs for warnings/errors

---

## Recommendations

### For Current Session (No Hardware)
Focus on maximizing what can be tested in development:
1. ✅ Run all unit tests and bUnit tests
2. ✅ Build and run Radio.Web locally
3. ✅ Test all UI pages and features
4. ✅ Play local audio files through FilePlayer
5. ✅ Test queue management thoroughly
6. ✅ Review and update documentation
7. ✅ Verify Material 3 design compliance
8. ✅ Test responsive layout at target resolution

### For Future Hardware Session
When hardware becomes available:
1. 🔧 Schedule dedicated time for Pi testing
2. 🔧 Prepare checklist of hardware tests
3. 🔧 Have debugging tools ready
4. 🔧 Document all findings in detail
5. 🔧 Create video recordings of issues
6. 🔧 Measure all performance metrics
7. 🔧 Update documentation based on results

---

## Contact & Support

If you encounter issues or have questions:
- See `SPOTIFY_LOOPBACK_TROUBLESHOOTING.md` for Spotify issues
- See `DEBUG_AND_FIX_PLAN.md` for detailed task information
- See `SESSION_SUMMARY.md` for current progress
- Check logs in `{RootDir}/logs/` for runtime errors

---

**Remember:** The vast majority of functionality (80%+) can be tested without special hardware. The Pi-specific features (RTL-SDR, final performance validation) are important but not blockers for continued development and testing.
