# Spotify Loopback - Testing Checklist

**Feature:** Spotify audio capture via loopback device  
**Status:** Implementation complete, ready for testing  
**Date:** January 2, 2026

---

## 🔨 Build Verification

### Step 1: Verify Build Compiles
- [ ] Run `scripts\build-solution.bat`
- [ ] Build completes with 0 errors
- [ ] All projects compile successfully

**Expected Output:**
```
✅ Build Successful!
```

**If build fails:**
- Review error messages
- Check BUILD_FIX_SUMMARY.md for known issues
- Verify all files are saved

---

## 🪟 Windows Testing

### Step 2: Install Prerequisites
- [ ] Download VB-Audio Virtual Cable from https://vb-audio.com/Cable/
- [ ] Install VB-Audio Cable (requires restart)
- [ ] Verify "CABLE Input" and "CABLE Output" appear in Sound settings
- [ ] Install Rust: `winget install Rustlang.Rust.GNU`

### Step 3: Build Librespot
- [ ] Clone librespot: `git clone https://github.com/librespot-org/librespot.git`
- [ ] Navigate to directory: `cd librespot`
- [ ] Build release: `cargo build --release`
- [ ] Verify executable exists: `target\release\librespot.exe`

### Step 4: Configure RadioConsole
- [ ] Open `src\Radio.API\appsettings.Development.json`
- [ ] Add or verify Spotify configuration:
```json
{
  "Devices": {
    "Spotify": {
      "Mode": "Loopback",
      "LoopbackDeviceName": "CABLE Output"
    }
  }
}
```
- [ ] Save file

### Step 5: Start Librespot
- [ ] Open PowerShell in librespot directory
- [ ] Run: `.\target\release\librespot.exe --name "RadioConsole" --backend rodio --device "CABLE Input"`
- [ ] Verify: "Using output device: CABLE Input" appears
- [ ] Leave PowerShell window open

### Step 6: Test Spotify Connection
- [ ] Open Spotify app (mobile or desktop)
- [ ] Tap "Connect to a device" (speaker icon)
- [ ] Verify "RadioConsole" appears in device list
- [ ] Connect to "RadioConsole"
- [ ] Play a test song
- [ ] Verify: Audio does NOT play directly from Spotify
- [ ] Verify: Librespot console shows "Track changed to..."

### Step 7: Test RadioConsole
- [ ] Start RadioConsole application
- [ ] Navigate to audio sources
- [ ] Select "Spotify" source
- [ ] Verify: Audio plays through RadioConsole
- [ ] Verify: **Visualization shows audio data** ⭐ (key feature)
- [ ] Verify: Metadata displays (track name, artist, album)

### Step 8: Test Controls
- [ ] Test Play/Pause button
- [ ] Test Next track button
- [ ] Test Previous track button
- [ ] Test Volume control
- [ ] Test Shuffle toggle
- [ ] Test Repeat mode

### Step 9: Performance Check
- [ ] Monitor CPU usage (Task Manager)
- [ ] Verify: < 15% CPU increase vs RemoteControl mode
- [ ] Check for audio dropouts or distortion
- [ ] Verify: Latency < 100ms (imperceptible)

---

## 🐧 Linux/Raspberry Pi Testing

### Step 10: Install Prerequisites
- [ ] Install raspotify: `curl -sL https://dtcooper.github.io/raspotify/install.sh | sh`
- [ ] Load ALSA loopback: `sudo modprobe snd-aloop`
- [ ] Make persistent: `echo "snd-aloop" | sudo tee -a /etc/modules`
- [ ] Verify device: `aplay -l | grep -i loopback`

### Step 11: Configure Raspotify
- [ ] Edit config: `sudo nano /etc/raspotify/conf`
- [ ] Set these values:
```bash
LIBRESPOT_NAME="RadioConsole"
LIBRESPOT_BACKEND="alsa"
LIBRESPOT_DEVICE="hw:Loopback,0,0"
LIBRESPOT_BITRATE="320"
LIBRESPOT_INITIAL_VOLUME="75"
```
- [ ] Save and exit (Ctrl+X, Y, Enter)
- [ ] Restart: `sudo systemctl restart raspotify`
- [ ] Verify: `sudo systemctl status raspotify` shows "active (running)"

### Step 12: Test ALSA Loopback
- [ ] Terminal 1: `speaker-test -D hw:Loopback,0,0 -c 2`
- [ ] Terminal 2: `arecord -D hw:Loopback,0,1 -f cd -d 5 test.wav`
- [ ] Stop speaker-test (Ctrl+C)
- [ ] Play recording: `aplay test.wav`
- [ ] Verify: You hear the test tone

### Step 13: Configure RadioConsole
- [ ] Edit `src\Radio.API\appsettings.Production.json`
- [ ] Add or verify Spotify configuration:
```json
{
  "Devices": {
    "Spotify": {
      "Mode": "Loopback",
      "LoopbackDeviceName": "hw:Loopback,0,1"
    }
  }
}
```
- [ ] Save file

### Step 14: Test Spotify Connection
- [ ] Open Spotify app (mobile or desktop)
- [ ] Tap "Connect to a device"
- [ ] Verify "RadioConsole" appears
- [ ] Connect to "RadioConsole"
- [ ] Play a test song
- [ ] Verify: Raspotify logs show activity: `sudo journalctl -u raspotify -f`

### Step 15: Test RadioConsole
- [ ] Start RadioConsole: `dotnet run --project src/Radio.API`
- [ ] Navigate to audio sources
- [ ] Select "Spotify" source
- [ ] Verify: Audio plays through RadioConsole
- [ ] Verify: **Visualization shows audio data** ⭐
- [ ] Verify: Metadata displays correctly

### Step 16: Performance Check (Raspberry Pi)
- [ ] Monitor CPU: `htop`
- [ ] Verify: < 20% CPU on Pi 5
- [ ] Check for audio dropouts
- [ ] Verify smooth visualization updates

---

## 🔄 Mode Switching Test

### Step 17: Test Remote Control Mode
- [ ] Change configuration:
```json
{
  "Devices": {
    "Spotify": {
      "Mode": "RemoteControl"
    }
  }
}
```
- [ ] Restart RadioConsole
- [ ] Play Spotify
- [ ] Verify: Audio plays directly from Spotify (not through RadioConsole)
- [ ] Verify: Visualization does NOT show data (expected)
- [ ] Verify: Remote controls still work (play/pause/next/prev)

### Step 18: Switch Back to Loopback
- [ ] Change configuration back to `"Mode": "Loopback"`
- [ ] Restart RadioConsole
- [ ] Verify: Loopback mode works again
- [ ] Verify: Visualization works

---

## 🐛 Error Handling Tests

### Step 19: Missing Loopback Device
- [ ] Stop librespot/raspotify
- [ ] Start RadioConsole with Loopback mode
- [ ] Expected: Helpful error message about device not found
- [ ] Verify: Error message lists available devices

### Step 20: Missing API Credentials
- [ ] Remove Spotify API credentials from config
- [ ] Start RadioConsole with Loopback mode
- [ ] Expected: Warning logged about missing credentials
- [ ] Verify: Audio still works (metadata limited)

### Step 21: Invalid Device Name
- [ ] Set `LoopbackDeviceName` to "INVALID_DEVICE"
- [ ] Start RadioConsole
- [ ] Expected: Clear error about device not found
- [ ] Verify: Suggests checking device names

---

## 📊 Documentation Verification

### Step 22: Review Documentation
- [ ] Quick Start guide is accurate
- [ ] Full Setup guide covers all steps
- [ ] Troubleshooting section addresses common issues
- [ ] Configuration examples match actual config files
- [ ] Scripts work as documented

---

## ✅ Acceptance Criteria

### Must Have (Required)
- [ ] ✅ Build succeeds with 0 errors
- [ ] ✅ Loopback mode captures audio on Windows
- [ ] ✅ Loopback mode captures audio on Linux
- [ ] ✅ **Visualization displays Spotify audio** ⭐
- [ ] ✅ Metadata updates from Spotify API
- [ ] ✅ Play/Pause/Next/Previous controls work
- [ ] ✅ Remote Control mode still works (backward compatible)

### Should Have (Important)
- [ ] ✅ Audio quality is good (no distortion)
- [ ] ✅ Latency is imperceptible (< 100ms)
- [ ] ✅ CPU usage is reasonable (< 20%)
- [ ] ✅ No audio dropouts
- [ ] ✅ Documentation is clear and complete
- [ ] ✅ Setup scripts work correctly

### Nice to Have (Optional)
- [ ] Auto-device detection
- [ ] GUI for mode switching
- [ ] Built-in librespot process manager
- [ ] Audio quality metrics

---

## 📝 Bug Reporting Template

If you encounter issues, use this template:

```markdown
**Platform:** Windows / Linux / Raspberry Pi
**Mode:** Loopback / RemoteControl
**Issue:** Brief description

**Steps to Reproduce:**
1. 
2. 
3. 

**Expected Behavior:**


**Actual Behavior:**


**Logs:**
```
[Paste relevant logs here]
```

**Configuration:**
```json
[Paste Spotify device config]
```

**Environment:**
- OS Version: 
- .NET Version: 
- RadioConsole Version: 
- Librespot/Raspotify Version: 
```

---

## 🎉 Success Indicators

You've successfully implemented Spotify loopback if:

1. ✅ **Visualization works** - Spectrum analyzer/waveform shows Spotify audio
2. ✅ Audio is clear and smooth - No dropouts or distortion
3. ✅ Controls work - Play/pause/next/previous respond correctly
4. ✅ Metadata displays - Track, artist, album info shown
5. ✅ Performance is good - CPU usage reasonable, no lag
6. ✅ Both modes work - Loopback and RemoteControl modes functional

---

**Testing Status:** ⏳ Pending  
**Expected Testing Time:** 2-3 hours  
**Testers Needed:** 1 Windows, 1 Linux/Pi

**Next:** Report results and any issues found
