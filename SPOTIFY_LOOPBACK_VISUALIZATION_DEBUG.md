# Visualizer Troubleshooting Guide

## Quick Diagnosis Checklist

### ✅ What's Working
- [x] SignalR connection established (shows "Connected")
- [x] Browser can communicate with server
- [x] Visualizer page loads

### ❓ What to Check
- [ ] Is audio actually playing through RadioConsole?
- [ ] Is VisualizerService receiving audio samples?
- [ ] Is visualization data being generated?
- [ ] Is SignalR broadcasting the data?
- [ ] Is browser receiving the data?
- [ ] Is canvas rendering the data?

---

## Step-by-Step Verification

### Step 1: Verify Audio is Playing

**Check in RadioConsole UI:**
1. Go to Audio Sources page
2. Verify Spotify is the **active source**
3. Check status shows **"Playing"** (not Paused or Stopped)
4. Verify metadata is updating (track name, artist)

**Check in Windows Sound Settings:**
1. Right-click speaker icon → Sound settings → More sound settings
2. Recording tab → CABLE Output
3. **Green bars should be moving** while Spotify plays

**Result:**
- ✅ Green bars moving = Audio is flowing
- ❌ No green bars = Audio not reaching loopback (fix this first!)

---

### Step 2: Check RadioConsole Logs

**Look for these messages in console output:**

**✅ GOOD SIGNS:**
```
[INF] Spotify loopback mode initialized successfully
[INF] VisualizerService started
[DBG] Processing audio samples: 2048 samples
[DBG] FFT computed, bins: 256
[DBG] Broadcasting visualization data to 1 clients
```

**❌ BAD SIGNS:**
```
[WRN] No audio data available for visualization
[WRN] Audio source not playing
[ERR] Failed to get audio samples from mixer
[ERR] VisualizerService not initialized
```

**If logs are too quiet:**
Enable debug logging (see "Enable Detailed Logging" section below)

---

### Step 3: Check Browser Developer Console

**Open DevTools (F12):**

**Console Tab - Look for:**
```javascript
// GOOD
"SignalR connected"
"Received visualization data: {type: 'spectrum', data: [...]}"
"Updating canvas with 256 data points"

// BAD
"SignalR connection lost"
"No visualization data received"
Error: Cannot read property 'getContext' of null
```

**Network Tab - Check WebSocket:**
1. Filter by "WS" (WebSocket)
2. Find connection to `/hubs/visualization` or similar
3. Click on it
4. Go to "Messages" tab
5. **Should see messages flowing every ~33ms (30 FPS)**

**Messages Tab should show:**
```json
{
  "type": "VisualizationData",
  "data": {
    "spectrum": [0.1, 0.2, 0.3, ...],
    "waveform": [0.05, 0.1, ...],
    "levels": { "left": 0.8, "right": 0.7 }
  }
}
```

**Result:**
- ✅ Messages flowing = Data is being sent
- ❌ No messages = VisualizerService not broadcasting

---

### Step 4: Check Canvas Rendering

**In Browser DevTools, Elements tab:**

1. Find the canvas element: `<canvas id="visualizer-canvas">`
2. Right-click → Inspect
3. Check if canvas has width/height attributes
4. Check if canvas is visible (not display:none)

**In Console tab, run:**
```javascript
// Check if canvas exists
const canvas = document.getElementById('visualizer-canvas');
console.log('Canvas exists:', canvas !== null);

// Check if context is available
const ctx = canvas?.getContext('2d');
console.log('Context available:', ctx !== null);

// Check canvas dimensions
console.log('Canvas size:', canvas?.width, 'x', canvas?.height);
```

**Result:**
- ✅ Canvas exists and has size = Rendering possible
- ❌ Canvas null or zero size = UI problem

---

## Common Issues & Fixes

### Issue 1: "Connected" but no visualization

**Symptoms:**
- SignalR shows "Connected"
- No spectrum/waveform/VU meters moving
- No errors in console

**Cause:**
Audio data is not flowing from SpotifyAudioSource to VisualizerService

**Fix:**
```powershell
# 1. Verify audio is flowing at OS level
# Sound Settings → CABLE Output → Should see green bars

# 2. Check if Spotify source is actually playing
# RadioConsole UI → Audio Sources → Status should be "Playing"

# 3. Restart audio playback
# Stop RadioConsole, restart, select Spotify, play song

# 4. Check logs for audio capture messages
# Should see "Processing audio samples" in logs
```

---

### Issue 2: Audio playing but VisualizerService not receiving data

**Symptoms:**
- Spotify is playing
- CABLE Output shows green bars
- RadioConsole logs show "Audio source playing"
- No "Processing audio samples" messages

**Cause:**
VisualizerService not connected to AudioManager output

**Fix:**
Check if VisualizerService is registered and started:

```csharp
// In Startup.cs or Program.cs, should have:
services.AddSingleton<IVisualizerService, VisualizerService>();

// And started:
var visualizer = app.Services.GetRequiredService<IVisualizerService>();
await visualizer.StartAsync();
```

**Check logs for:**
```
[INF] VisualizerService registered
[INF] VisualizerService started
```

---

### Issue 3: Data generated but not reaching browser

**Symptoms:**
- Logs show "Broadcasting visualization data"
- Browser shows "Connected"
- No data appearing in Network tab WebSocket messages

**Cause:**
SignalR hub not broadcasting or client not subscribed

**Fix:**

**Check SignalR Hub implementation:**
```csharp
// Should have method like:
public async Task BroadcastVisualizationData(VisualizationData data)
{
    await Clients.All.SendAsync("ReceiveVisualizationData", data);
}
```

**Check client subscription:**
```javascript
// Browser should subscribe to hub:
connection.on("ReceiveVisualizationData", (data) => {
    console.log("Received data:", data);
    updateVisualization(data);
});
```

---

### Issue 4: Data reaching browser but canvas not rendering

**Symptoms:**
- Browser DevTools shows data in WebSocket messages
- Console logs "Received visualization data"
- Canvas remains blank/static

**Cause:**
JavaScript rendering code not executing or failing

**Fix:**

**Check for JavaScript errors:**
```javascript
// F12 → Console → Look for errors like:
TypeError: Cannot read property 'getContext' of null
ReferenceError: updateVisualization is not defined
```

**Test canvas manually:**
```javascript
// In browser console:
const canvas = document.getElementById('visualizer-canvas');
const ctx = canvas.getContext('2d');
ctx.fillStyle = 'red';
ctx.fillRect(0, 0, 100, 100);
// Should draw a red square
```

**If test works but visualization doesn't:**
- Check visualization update function
- Add console.log in update function
- Verify data format matches expected structure

---

## Enable Detailed Logging

Edit `appsettings.Development.json`:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning",
        "Microsoft.AspNetCore": "Warning",
        "Radio.Infrastructure.Audio": "Debug",
        "Radio.Infrastructure.Audio.Services.VisualizerService": "Debug",
        "Radio.Infrastructure.Audio.Services.AudioManager": "Debug",
        "Radio.Infrastructure.Audio.Sources": "Debug"
      }
    }
  }
}
```

**Restart RadioConsole after changing logging levels**

**What to look for in debug logs:**
```
[DBG] AudioManager: Active source = Spotify, State = Playing
[DBG] SpotifyAudioSource: Audio captured from CABLE Output
[DBG] VisualizerService: Processing 2048 audio samples
[DBG] VisualizerService: FFT completed, 256 frequency bins
[DBG] VisualizerService: Broadcasting to 1 connected clients
[DBG] SignalR Hub: Sent VisualizationData to client abc123
```

---

## Verification Script

**Run automated diagnostics:**
```powershell
.\scripts\Test-VisualizerDataFlow.ps1
```

This will guide you through checking each point in the data flow.

---

## The Complete Data Flow

```
┌─────────────────────────────────────────────┐
│ 1. Spotify App → Playing song               │
│    Check: Spotify shows playing status      │
└─────────────────┬───────────────────────────┘
                  ↓
┌─────────────────────────────────────────────┐
│ 2. Librespot → Receiving & playing          │
│    Check: Librespot console shows activity  │
└─────────────────┬───────────────────────────┘
                  ↓
┌─────────────────────────────────────────────┐
│ 3. CABLE Output → Recording audio           │
│    Check: Green bars in Sound settings ✅   │
└─────────────────┬───────────────────────────┘
                  ↓
┌─────────────────────────────────────────────┐
│ 4. SpotifyAudioSource → Capturing           │
│    Check: Logs show "Audio captured"        │
└─────────────────┬───────────────────────────┘
                  ↓
┌─────────────────────────────────────────────┐
│ 5. AudioManager → Mixing & routing          │
│    Check: Active source = Spotify           │
└─────────────────┬───────────────────────────┘
                  ↓
┌─────────────────────────────────────────────┐
│ 6. VisualizerService → Processing FFT       │
│    Check: "Processing audio samples" logs   │
└─────────────────┬───────────────────────────┘
                  ↓
┌─────────────────────────────────────────────┐
│ 7. SignalR Hub → Broadcasting                │
│    Check: "Broadcasting to X clients" logs  │
└─────────────────┬───────────────────────────┘
                  ↓
┌─────────────────────────────────────────────┐
│ 8. Browser → Receiving WebSocket messages   │
│    Check: Network tab shows WS messages ✅  │
└─────────────────┬───────────────────────────┘
                  ↓
┌─────────────────────────────────────────────┐
│ 9. JavaScript → Processing data              │
│    Check: Console shows "Received data"      │
└─────────────────┬───────────────────────────┘
                  ↓
┌─────────────────────────────────────────────┐
│ 10. Canvas → Rendering visualization 🎨     │
│     Check: Bars/waveform moving on screen    │
└─────────────────────────────────────────────┘
```

**Find where it stops and fix that point!**

---

## Quick Test Commands

**Check audio at OS level:**
```powershell
# Verify CABLE Output is recording
# Manual: Sound Settings → Recording → CABLE Output → Green bars?
```

**Check RadioConsole audio:**
```powershell
# Look for in console output:
# [DBG] Processing audio samples
```

**Check SignalR connection:**
```javascript
// In browser console (F12):
console.log('SignalR state:', connection.state);
// Should show: "Connected"
```

**Check canvas rendering:**
```javascript
// In browser console:
const canvas = document.getElementById('visualizer-canvas');
console.log('Canvas:', canvas?.width, 'x', canvas?.height);
```

---

## Still Not Working?

1. **Enable debug logging** (see above)
2. **Run diagnostics:** `.\scripts\Test-VisualizerDataFlow.ps1`
3. **Check each point** in the data flow diagram
4. **Look for error messages** in logs and browser console
5. **Restart everything:** Stop RadioConsole → Stop librespot → Start librespot → Start RadioConsole

---

**Most common issue:** Audio is not actually flowing from Spotify → librespot → CABLE Output → RadioConsole. Verify the green bars first!
