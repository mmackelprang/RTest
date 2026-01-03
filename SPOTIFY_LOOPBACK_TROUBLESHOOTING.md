# Quick Spotify Loopback Fix Guide

## Your Error Explained

**Error:** `Player command failed: No active device found`

**What it means:** The Spotify API can't find a Spotify Connect device (librespot) to send playback commands to.

**Root cause:** Librespot is either:
- Not running
- Not connected to network
- Not visible to Spotify
- Blocked by firewall

---

## Quick Fix Steps

### Step 1: Is librespot running?

**Check:**
```powershell
Get-Process -Name "librespot" -ErrorAction SilentlyContinue
```

**If nothing appears:** Librespot is NOT running (this is your issue!)

**Fix:** Start librespot
```powershell
cd ~\librespot
.\target\release\librespot.exe --name "RadioConsole" --device "CABLE Input (VB-Audio Virtual Cable)" --bitrate 320 --verbose
```

**Keep this window open** while using Spotify.

---

### Step 2: Verify device appears in Spotify

1. Open Spotify app (mobile or desktop)
2. Start playing any song
3. Tap/click the **device icon** (speaker with waves)
4. Look for **"RadioConsole"** in the device list

**If NOT visible:**
- Check firewall: Allow librespot.exe
- Check network: Same WiFi/network as Spotify app?
- Restart librespot with `--verbose` flag for logs

---

### Step 3: Connect Spotify to RadioConsole

1. In Spotify app, tap "RadioConsole" device
2. Play a song
3. **Audio should NOT play directly from Spotify**
4. Audio should be captured by RadioConsole for visualization

---

## Run Diagnostic Script

**Automated check:**
```powershell
.\scripts\Test-SpotifyLoopback.ps1
```

This will check:
- ✅ VB-Audio Cable installed
- ✅ CABLE Output device exists
- ✅ Librespot process running
- ✅ Network connectivity
- ✅ RadioConsole configuration
- ✅ Firewall rules
- ✅ Spotify device visibility

---

## Common Issues

### Issue: "librespot.exe not found"

**Fix:** Build librespot first
```powershell
cd ~
git clone https://github.com/librespot-org/librespot.git
cd librespot
cargo build --release
```

### Issue: "CABLE Output not found"

**Fix:** Install VB-Audio Virtual Cable
1. Download: https://vb-audio.com/Cable/
2. Run installer as Administrator
3. Restart computer
4. Verify in Sound settings → Recording tab

### Issue: "Device not appearing in Spotify"

**Fix:** Check firewall
```powershell
# Add firewall exception
$librespotPath = "$env:USERPROFILE\librespot\target\release\librespot.exe"
New-NetFirewallRule -DisplayName "Librespot" -Direction Inbound -Program $librespotPath -Action Allow
```

### Issue: "Audio plays directly from Spotify, not RadioConsole"

**Fix:** Make sure you're connected to RadioConsole device
1. Spotify app → Device icon
2. Select "RadioConsole" (NOT your computer name)
3. Play song
4. Audio should route through loopback

---

## Understanding Loopback Mode

**How it works:**

```
Spotify App
    ↓ (Sends commands)
Spotify Connect API
    ↓ (Plays on device)
Librespot (RadioConsole device)
    ↓ (Outputs audio to)
CABLE Input (Virtual Speaker)
    ↓ (Loopback)
CABLE Output (Virtual Mic)
    ↓ (Captured by)
RadioConsole (SpotifyAudioSource)
    ↓ (Processes & visualizes)
Speakers/Output
```

**Key points:**
1. **Librespot MUST be running** for Spotify to find the device
2. You must **actively connect** Spotify to "RadioConsole" device
3. Audio flows: Spotify → librespot → loopback → RadioConsole
4. RadioConsole **does NOT** control Spotify directly in loopback mode

---

## Alternative: Use Remote Control Mode

If loopback is too complex, switch back to RemoteControl mode:

**Edit:** `appsettings.Development.json`
```json
{
  "Devices": {
    "Spotify": {
      "Mode": "RemoteControl"
    }
  }
}
```

**Trade-off:**
- ✅ Simpler setup (no librespot needed)
- ❌ No visualization (audio doesn't flow through RadioConsole)

---

## Verify Setup Checklist

Before playing Spotify in RadioConsole:

- [ ] ✅ VB-Audio Cable installed
- [ ] ✅ Librespot running (`Get-Process librespot`)
- [ ] ✅ "RadioConsole" appears in Spotify device list
- [ ] ✅ Spotify connected to "RadioConsole" device
- [ ] ✅ Test song plays through RadioConsole (not directly)
- [ ] ✅ RadioConsole shows visualization

---

## Still Having Issues?

**Run full diagnostic:**
```powershell
.\scripts\Test-SpotifyLoopback.ps1
```

**Check logs:**
- Librespot console output (verbose mode)
- RadioConsole logs (Application/Logs directory)
- Windows Event Viewer (Application logs)

**Get detailed help:**
- Read: `SPOTIFY_LOOPBACK_SETUP.md` → Troubleshooting section
- Review: `SPOTIFY_LOOPBACK_TESTING.md` → Windows testing steps

---

## Quick Command Reference

**Start librespot:**
```powershell
cd ~\librespot
.\target\release\librespot.exe --name "RadioConsole" --device "CABLE Input" --verbose
```

**Check if running:**
```powershell
Get-Process librespot
```

**Stop librespot:**
```powershell
Stop-Process -Name librespot
```

**Test loopback audio:**
1. Open Sound settings → Recording → CABLE Output
2. Speak test → Should see green bars when audio plays

---

**Most likely fix:** Start librespot.exe and connect Spotify to "RadioConsole" device!
