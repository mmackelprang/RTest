# How to Verify Audio is Reaching SoundFlow

## Quick Visual Check (30 seconds)

### Step 1: Check CABLE Output is receiving audio
1. Right-click **speaker icon** in system tray
2. Click **"Open Sound settings"**
3. Scroll down, click **"More sound settings"**
4. Go to **"Recording"** tab
5. Find **"CABLE Output (VB-Audio Virtual Cable)"**
6. **Play a song in Spotify** (should already be playing from your test)
7. **Watch for GREEN BARS** moving on the CABLE Output device

**Result:**
- ✅ **Green bars moving** = Audio is flowing through loopback! Continue to Step 2
- ❌ **No green bars** = Audio is NOT reaching loopback. See "Troubleshooting" below

---

### Step 2: Check RadioConsole is capturing audio
1. Open **RadioConsole web UI** (usually http://localhost:5000)
2. Go to **audio sources** or **visualization** page
3. **Select "Spotify" as the active source**
4. Look for:
   - **Spectrum analyzer** - Should show frequency bars moving
   - **Waveform** - Should show audio waveform
   - **VU meters** - Should show level indicators moving

**Result:**
- ✅ **Visualization is moving** = SoundFlow is receiving audio! You're all set! 🎉
- ❌ **Visualization is static/frozen** = SoundFlow is NOT receiving audio. See "Troubleshooting" below

---

## Detailed Verification (5 minutes)

### Run the verification script:
```powershell
.\scripts\Verify-AudioLoopback.ps1
```

This will:
1. Check CABLE Output device exists
2. Verify RadioConsole configuration
3. Check librespot output settings
4. Optionally record test audio from CABLE Output
5. Point you to relevant log files

---

## Manual Log Check

### Check RadioConsole logs for these messages:

**Good signs (✅):**
```
[INFO] Initializing Spotify in Loopback mode
[INFO] Initializing Spotify loopback capture from device: CABLE Output
[INFO] Spotify loopback mode initialized successfully
[INFO] USB capture initialized on device: CABLE Output
```

**Bad signs (❌):**
```
[ERROR] Loopback device not configured for Spotify
[ERROR] Audio device manager not available for loopback mode
[ERROR] Could not find USB capture device for port CABLE Output
[ERROR] Failed to initialize Spotify audio capture
```

**Where to find logs:**
- Console output where RadioConsole is running
- `src/Radio.API/Logs/` directory
- Look for files named like `log-20260102.txt`

---

## Troubleshooting

### Problem: No green bars on CABLE Output

**Cause:** Librespot is NOT outputting to CABLE Input

**Fix:**
1. Check librespot console window
2. Look for line: `Using output device: CABLE Input`
3. If you see a different device:
   - Stop librespot (Ctrl+C)
   - Restart with correct device:
   ```powershell
   .\librespot.exe --name "RadioConsole" --device "CABLE Input (VB-Audio Virtual Cable)" --verbose
   ```

---

### Problem: Green bars on CABLE Output, but no visualization in RadioConsole

**Cause:** RadioConsole is not capturing from CABLE Output

**Fix 1: Check configuration**

Edit `src/Radio.API/appsettings.Development.json`:
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

**Important:** Device name must be **exactly** `"CABLE Output"` (without the full name)

**Fix 2: Restart RadioConsole**
After changing config, restart RadioConsole for changes to take effect.

**Fix 3: Check device manager logs**

Look for this in logs:
```
Available capture devices:
  - CABLE Output (VB-Audio Virtual Cable)
  - [other devices...]
```

If CABLE Output is NOT in the list, SoundFlow can't see it.

---

### Problem: "No active device found" error

**This is expected in Loopback mode!**

In Loopback mode:
- ❌ RadioConsole **cannot** send play/pause commands to Spotify API
- ✅ RadioConsole **can** capture and visualize audio that Spotify plays
- ✅ Control playback from Spotify app
- ✅ RadioConsole is passive (receiver/visualizer)

**Solution:** Control playback from Spotify app:
1. Open Spotify app
2. Connect to "RadioConsole" device
3. Play songs from Spotify
4. RadioConsole will capture and visualize

---

## Understanding the Audio Flow

```
Spotify App (Your control)
    ↓ Sends to
Librespot (RadioConsole device)
    ↓ Outputs to
CABLE Input (Virtual Speaker)
    ↓ Internal loopback (automatic)
CABLE Output (Virtual Microphone) ← CHECK HERE for green bars
    ↓ Captured by
RadioConsole / SoundFlow ← CHECK HERE for visualization
    ↓ Processed and sent to
Real Speakers (with visualization!) 🎨
```

---

## Quick Verification Checklist

- [ ] ✅ VB-Audio Cable installed
- [ ] ✅ Librespot running and showing "Using output device: CABLE Input"
- [ ] ✅ Spotify connected to "RadioConsole" device
- [ ] ✅ Song playing in Spotify
- [ ] ✅ CABLE Output shows green bars in Sound settings
- [ ] ✅ RadioConsole config has Mode: "Loopback" and LoopbackDeviceName: "CABLE Output"
- [ ] ✅ RadioConsole has Spotify selected as active source
- [ ] ✅ Visualization in RadioConsole is moving (spectrum/waveform)

**If all checked:** Loopback is working perfectly! 🎉

---

## Still Not Working?

### Get detailed diagnostics:
```powershell
.\scripts\Verify-AudioLoopback.ps1
```

### Check RadioConsole startup logs for:
- Device enumeration (what devices SoundFlow can see)
- Spotify initialization messages
- Any error messages about devices or configuration

### Common device name issues:

**Wrong (❌):**
- `"CABLE Output (VB-Audio Virtual Cable)"` - Too long
- `"Cable Output"` - Wrong capitalization
- `"VB-Audio Cable"` - Wrong device

**Correct (✅):**
- `"CABLE Output"` - Exact match for capture device

---

## Success Indicators

You'll know it's working when:
1. ✅ Green bars move on CABLE Output when Spotify plays
2. ✅ Spectrum analyzer shows frequency bars in RadioConsole
3. ✅ Waveform display shows audio pattern
4. ✅ VU meters respond to audio levels
5. ✅ Audio plays through RadioConsole (not directly from Spotify)

**That's it!** You've successfully set up Spotify loopback mode! 🎵🎨
