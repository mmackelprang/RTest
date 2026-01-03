# Visualizer Data Flow Diagnostics

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Visualizer Data Flow Diagnostics" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check 1: Is audio source playing?
Write-Host "[1/6] Checking Audio Source Status..." -ForegroundColor Yellow

Write-Host "  This requires checking RadioConsole logs/API" -ForegroundColor Gray
Write-Host "  Look for:" -ForegroundColor Cyan
Write-Host "  - Active audio source should be 'Spotify'" -ForegroundColor White
Write-Host "  - Source state should be 'Playing' or 'Paused'" -ForegroundColor White
Write-Host ""
Write-Host "  Quick test:" -ForegroundColor Cyan
Write-Host "  1. Open RadioConsole UI" -ForegroundColor White
Write-Host "  2. Go to Audio Sources page" -ForegroundColor White
Write-Host "  3. Check current source shows 'Playing' status" -ForegroundColor White
Write-Host ""

# Check 2: SignalR connection
Write-Host "[2/6] Checking SignalR Connection..." -ForegroundColor Yellow

Write-Host "  The visualizer page shows 'Connected' - this is good!" -ForegroundColor Green
Write-Host "  ✅ SignalR connection is established" -ForegroundColor Green
Write-Host ""
Write-Host "  But 'Connected' only means WebSocket connection is up" -ForegroundColor Yellow
Write-Host "  It does NOT guarantee audio data is flowing" -ForegroundColor Yellow
Write-Host ""

# Check 3: Check for visualization service initialization
Write-Host "[3/6] Checking VisualizerService Logs..." -ForegroundColor Yellow
Write-Host ""
Write-Host "  In RadioConsole console output, look for:" -ForegroundColor Cyan
Write-Host ""
Write-Host "  ✅ GOOD - Service started:" -ForegroundColor Green
Write-Host "     [INFO] VisualizerService started" -ForegroundColor Gray
Write-Host "     [INFO] Starting visualization processing" -ForegroundColor Gray
Write-Host "     [INFO] Visualization data update rate: X Hz" -ForegroundColor Gray
Write-Host ""
Write-Host "  ✅ GOOD - Audio data received:" -ForegroundColor Green
Write-Host "     [DEBUG] Processing audio samples: X samples" -ForegroundColor Gray
Write-Host "     [DEBUG] FFT computed, frequency bins: X" -ForegroundColor Gray
Write-Host "     [DEBUG] Broadcasting visualization data to X clients" -ForegroundColor Gray
Write-Host ""
Write-Host "  ❌ BAD - No audio:" -ForegroundColor Red
Write-Host "     [WARN] No audio data available for visualization" -ForegroundColor Gray
Write-Host "     [WARN] Audio source not playing" -ForegroundColor Gray
Write-Host "     [ERROR] Failed to get audio samples" -ForegroundColor Gray
Write-Host ""

# Check 4: Browser developer console
Write-Host "[4/6] Checking Browser Console..." -ForegroundColor Yellow
Write-Host ""
Write-Host "  Open browser Developer Tools (F12):" -ForegroundColor Cyan
Write-Host ""
Write-Host "  1. Go to Console tab" -ForegroundColor White
Write-Host "  2. Look for SignalR messages:" -ForegroundColor White
Write-Host ""
Write-Host "     ✅ GOOD:" -ForegroundColor Green
Write-Host "     'SignalR connected'" -ForegroundColor Gray
Write-Host "     'Received visualization data: {type: spectrum, ...}'" -ForegroundColor Gray
Write-Host "     'Updating visualization with X data points'" -ForegroundColor Gray
Write-Host ""
Write-Host "     ❌ BAD:" -ForegroundColor Red
Write-Host "     'SignalR connection lost'" -ForegroundColor Gray
Write-Host "     'No visualization data received in X seconds'" -ForegroundColor Gray
Write-Host "     JavaScript errors about canvas or rendering" -ForegroundColor Gray
Write-Host ""
Write-Host "  3. Go to Network tab" -ForegroundColor White
Write-Host "  4. Filter by 'WS' (WebSocket)" -ForegroundColor White
Write-Host "  5. Click on the visualizer hub connection" -ForegroundColor White
Write-Host "  6. Check Messages tab - should see data flowing" -ForegroundColor White
Write-Host ""

# Check 5: Audio flow verification
Write-Host "[5/6] Verifying Audio Data Flow..." -ForegroundColor Yellow
Write-Host ""
Write-Host "  The audio data chain is:" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Spotify → librespot → CABLE Input" -ForegroundColor White
Write-Host "    ↓" -ForegroundColor Gray
Write-Host "  CABLE Output (Check: Green bars in Sound settings)" -ForegroundColor White
Write-Host "    ↓" -ForegroundColor Gray
Write-Host "  SpotifyAudioSource / SoundFlow Capture" -ForegroundColor White
Write-Host "    ↓" -ForegroundColor Gray
Write-Host "  AudioManager / Mixer" -ForegroundColor White
Write-Host "    ↓" -ForegroundColor Gray
Write-Host "  VisualizerService (FFT processing)" -ForegroundColor White
Write-Host "    ↓" -ForegroundColor Gray
Write-Host "  SignalR Hub → Browser" -ForegroundColor White
Write-Host "    ↓" -ForegroundColor Gray
Write-Host "  Canvas rendering (visualization)" -ForegroundColor White
Write-Host ""
Write-Host "  Test each point:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Point 1: Sound Settings → CABLE Output → Green bars?" -ForegroundColor White
$soundCheck = Read-Host "    Do you see green bars? (y/n)"

if ($soundCheck -eq 'y') {
    Write-Host "    ✅ Audio reaching loopback device" -ForegroundColor Green
} else {
    Write-Host "    ❌ Audio NOT reaching loopback - Fix this first!" -ForegroundColor Red
    Write-Host "       Run: .\scripts\Verify-AudioLoopback.ps1" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "  Point 2: RadioConsole logs → 'Processing audio samples'?" -ForegroundColor White
$logsCheck = Read-Host "    Check logs now and press Enter when done"

Write-Host ""
Write-Host "  Point 3: Browser DevTools → SignalR messages flowing?" -ForegroundColor White
$browserCheck = Read-Host "    Check browser console (F12) and press Enter when done"

# Check 6: Common issues
Write-Host ""
Write-Host "[6/6] Common Visualization Issues..." -ForegroundColor Yellow
Write-Host ""

Write-Host "Issue 1: SignalR connected but no visualization" -ForegroundColor Cyan
Write-Host "  Cause: Audio source not playing or no audio data" -ForegroundColor Gray
Write-Host "  Fix:" -ForegroundColor White
Write-Host "    - Verify Spotify is playing (not paused)" -ForegroundColor Gray
Write-Host "    - Check Spotify source is selected in RadioConsole" -ForegroundColor Gray
Write-Host "    - Verify CABLE Output shows green bars" -ForegroundColor Gray
Write-Host ""

Write-Host "Issue 2: Visualization was working, then stopped" -ForegroundColor Cyan
Write-Host "  Cause: Audio source stopped/changed, or SignalR disconnected" -ForegroundColor Gray
Write-Host "  Fix:" -ForegroundColor White
Write-Host "    - Refresh the browser page" -ForegroundColor Gray
Write-Host "    - Restart audio playback" -ForegroundColor Gray
Write-Host "    - Check for JavaScript errors in console" -ForegroundColor Gray
Write-Host ""

Write-Host "Issue 3: Canvas element not rendering" -ForegroundColor Cyan
Write-Host "  Cause: JavaScript error or canvas not initialized" -ForegroundColor Gray
Write-Host "  Fix:" -ForegroundColor White
Write-Host "    - F12 → Console → Look for errors" -ForegroundColor Gray
Write-Host "    - Hard refresh (Ctrl+Shift+R)" -ForegroundColor Gray
Write-Host "    - Check if canvas element exists in DOM" -ForegroundColor Gray
Write-Host ""

Write-Host "Issue 4: VisualizerService not processing audio" -ForegroundColor Cyan
Write-Host "  Cause: Service not started or audio source not connected" -ForegroundColor Gray
Write-Host "  Fix:" -ForegroundColor White
Write-Host "    - Check RadioConsole startup logs for VisualizerService" -ForegroundColor Gray
Write-Host "    - Verify audio source is in 'Playing' state" -ForegroundColor Gray
Write-Host "    - Check if AudioManager has active source" -ForegroundColor Gray
Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Diagnostic Steps Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Step 1: Verify audio is flowing" -ForegroundColor Yellow
Write-Host "  → Sound Settings → CABLE Output → Green bars moving" -ForegroundColor White
Write-Host ""

Write-Host "Step 2: Check RadioConsole logs" -ForegroundColor Yellow
Write-Host "  → Look for 'Processing audio samples' messages" -ForegroundColor White
Write-Host "  → Look for 'Broadcasting visualization data' messages" -ForegroundColor White
Write-Host ""

Write-Host "Step 3: Check browser console (F12)" -ForegroundColor Yellow
Write-Host "  → Console tab: Look for SignalR messages" -ForegroundColor White
Write-Host "  → Network tab: Check WebSocket messages" -ForegroundColor White
Write-Host ""

Write-Host "Step 4: Enable detailed logging" -ForegroundColor Yellow
Write-Host "  → Edit appsettings.Development.json" -ForegroundColor White
Write-Host "  → Set Radio.Infrastructure.Audio to 'Debug'" -ForegroundColor White
Write-Host "  → Set Radio.Infrastructure.Visualization to 'Debug'" -ForegroundColor White
Write-Host "  → Restart RadioConsole" -ForegroundColor White
Write-Host ""

Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Would you like to see the detailed logging configuration? (y/n): " -NoNewline -ForegroundColor Yellow
$showConfig = Read-Host

if ($showConfig -eq 'y') {
    Write-Host ""
    Write-Host "Add this to appsettings.Development.json:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host @"
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning",
        "Radio.Infrastructure.Audio": "Debug",
        "Radio.Infrastructure.Audio.Services.VisualizerService": "Debug",
        "Radio.Infrastructure.Audio.Services.AudioManager": "Debug"
      }
    }
  }
}
"@ -ForegroundColor Gray
    Write-Host ""
    Write-Host "Then restart RadioConsole and check logs" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
