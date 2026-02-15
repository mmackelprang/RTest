# Debug Logging Enabled for Visualization

## What Changed

Updated `appsettings.Development.json` to enable detailed logging for:
- ✅ All Audio infrastructure (`Radio.Infrastructure.Audio`)
- ✅ VisualizerService specifically
- ✅ AudioManager
- ✅ SoundFlow engine
- ✅ SignalR Hubs

## What to Do Next

### 1. Restart RadioConsole

**Stop RadioConsole** and **start it again** for the logging changes to take effect.

### 2. Look for These Startup Messages

**When RadioConsole starts, look for:**

```
[INF] Registering VisualizerService
[INF] VisualizerService initialized
[INF] VisualizerService started
[INF] Audio tap configured for visualization
```

**Or errors like:**
```
[ERR] Failed to initialize VisualizerService
[ERR] VisualizerService not registered
[ERR] Audio tap could not be configured
```

### 3. While Playing Audio

**With FilePlayer playing, you should see:**

```
[DBG] AudioManager: Processing audio frame, samples: 2048
[DBG] Audio tap: Captured 2048 samples for analysis
[DBG] VisualizerService: Processing audio samples: 2048
[DBG] VisualizerService: FFT computed, bins: 256
[DBG] VisualizerService: RMS level: 0.XX
[DBG] VisualizerService: Broadcasting visualization data to X clients
[DBG] SignalR Hub: Sent VisualizationData message
```

**If you DON'T see these:**
- No "Processing audio samples" = Audio tap not working
- No "Broadcasting" = SignalR not configured
- No "Sent VisualizationData" = Hub not connected to service

### 4. Check Visualizer Page

With debug logging enabled and audio playing:

1. Open visualizer page in browser
2. Open DevTools (F12)
3. Go to Console tab
4. **You should now see logs** if visualization is working

### 5. Report Back

After restarting with debug logging, search logs for:

**Search 1:** `VisualizerService`
- Copy any lines you find

**Search 2:** `Audio tap`
- Copy any lines you find

**Search 3:** `visualization`
- Copy any lines you find

**Search 4:** `FFT` or `spectrum`
- Copy any lines you find

## Common Scenarios

### Scenario A: No VisualizerService logs at all

**This means:** Service is not registered or failed to start

**Check:** Look for error during startup about VisualizerService

### Scenario B: VisualizerService started but no "Processing audio"

**This means:** Audio tap is not connected to mixer

**Check:** Look for "Audio tap configured" or similar

### Scenario C: Processing audio but no "Broadcasting"

**This means:** SignalR hub not wired up

**Check:** Look for SignalR hub registration errors

### Scenario D: Broadcasting but browser receives nothing

**This means:** Client-side JavaScript issue

**Check:** Browser console for SignalR connection errors

## What the Debug Logs Will Reveal

The debug logs will show us **exactly where the visualization pipeline breaks**:

```
Audio Source → Mixer → Audio Tap → VisualizerService → SignalR → Browser
                ↑                    ↑                   ↑          ↑
            Check here           Check here        Check here  Check here
```

## Next Steps

1. ✅ **Restart RadioConsole** (logging config changed)
2. ✅ **Play audio** (FilePlayer)
3. ✅ **Search logs** for the keywords above
4. ✅ **Report findings** - which logs appear, which don't

---

**The debug logs will tell us exactly what's wrong!**
