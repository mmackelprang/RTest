# Visualization Debug - FilePlayer (Known Working Audio)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Visualization Debug - FilePlayer Source" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Good news: You can hear the audio!" -ForegroundColor Green
Write-Host "This means:" -ForegroundColor Cyan
Write-Host "  ✅ FilePlayer is working" -ForegroundColor Gray
Write-Host "  ✅ Audio is flowing through SoundFlow" -ForegroundColor Gray
Write-Host "  ✅ AudioManager is routing audio correctly" -ForegroundColor Gray
Write-Host "  ✅ Output device is working" -ForegroundColor Gray
Write-Host ""
Write-Host "The problem is isolated to: VisualizerService → Browser" -ForegroundColor Yellow
Write-Host ""

# Check 1: VisualizerService receiving audio?
Write-Host "[1/5] Is VisualizerService receiving audio samples?" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Check RadioConsole console output for:" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Look for these lines:" -ForegroundColor White
Write-Host "    [DBG] VisualizerService: Processing audio samples: 2048 samples" -ForegroundColor Gray
Write-Host "    [DBG] VisualizerService: FFT computed, bins: 256" -ForegroundColor Gray
Write-Host "    [DBG] VisualizerService: RMS level: 0.XX" -ForegroundColor Gray
Write-Host ""
$hasSampleLogs = Read-Host "  Do you see 'Processing audio samples' messages? (y/n)"

if ($hasSampleLogs -eq 'y') {
    Write-Host "  ✅ VisualizerService is receiving audio!" -ForegroundColor Green
} else {
    Write-Host "  ❌ VisualizerService is NOT receiving audio samples" -ForegroundColor Red
    Write-Host ""
    Write-Host "  This means the problem is:" -ForegroundColor Yellow
    Write-Host "    - VisualizerService not connected to AudioManager output" -ForegroundColor Gray
    Write-Host "    - VisualizerService not started" -ForegroundColor Gray
    Write-Host "    - Audio tap not configured in mixer" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Enable debug logging:" -ForegroundColor Cyan
    Write-Host "    Edit appsettings.Development.json:" -ForegroundColor White
    Write-Host '    "Radio.Infrastructure.Audio.Services.VisualizerService": "Debug"' -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Then restart RadioConsole" -ForegroundColor White
}

# Check 2: Broadcasting data?
Write-Host ""
Write-Host "[2/5] Is VisualizerService broadcasting data?" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Look for:" -ForegroundColor Cyan
Write-Host "    [DBG] VisualizerService: Broadcasting visualization data to X clients" -ForegroundColor Gray
Write-Host "    [DBG] SignalR: Sent VisualizationData to connection XYZ" -ForegroundColor Gray
Write-Host ""
$hasBroadcastLogs = Read-Host "  Do you see 'Broadcasting' messages? (y/n)"

if ($hasBroadcastLogs -eq 'y') {
    Write-Host "  ✅ VisualizerService is broadcasting data!" -ForegroundColor Green
    Write-Host "     Clients connected: Check the number in logs" -ForegroundColor Gray
} else {
    Write-Host "  ❌ VisualizerService is NOT broadcasting" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Possible causes:" -ForegroundColor Yellow
    Write-Host "    - No clients connected (check 'X clients' in logs)" -ForegroundColor Gray
    Write-Host "    - SignalR hub not wired up correctly" -ForegroundColor Gray
    Write-Host "    - Broadcasting disabled or failing" -ForegroundColor Gray
}

# Check 3: Browser receiving data?
Write-Host ""
Write-Host "[3/5] Is browser receiving WebSocket messages?" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Open Browser DevTools (F12):" -ForegroundColor Cyan
Write-Host ""
Write-Host "  1. Go to Network tab" -ForegroundColor White
Write-Host "  2. Filter by 'WS' (WebSocket)" -ForegroundColor White
Write-Host "  3. Find the SignalR hub connection" -ForegroundColor White
Write-Host "  4. Click on it" -ForegroundColor White
Write-Host "  5. Go to 'Messages' tab" -ForegroundColor White
Write-Host "  6. Look for continuous stream of messages" -ForegroundColor White
Write-Host ""
Write-Host "  Press Enter after checking..." -NoNewline
Read-Host

Write-Host ""
$hasWSMessages = Read-Host "  Do you see messages flowing? (y/n)"

if ($hasWSMessages -eq 'y') {
    Write-Host "  ✅ Browser is receiving WebSocket messages!" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Click on a message to see its content:" -ForegroundColor Cyan
    Write-Host "    Should show JSON with spectrum, waveform, levels data" -ForegroundColor Gray
} else {
    Write-Host "  ❌ Browser is NOT receiving messages" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Check:" -ForegroundColor Yellow
    Write-Host "    - Is SignalR connected? (Should show 'Connected' on page)" -ForegroundColor Gray
    Write-Host "    - Any errors in Console tab?" -ForegroundColor Gray
    Write-Host "    - Try refreshing the page (Ctrl+Shift+R)" -ForegroundColor Gray
}

# Check 4: JavaScript processing data?
Write-Host ""
Write-Host "[4/5] Is JavaScript receiving and processing data?" -ForegroundColor Yellow
Write-Host ""
Write-Host "  In Browser DevTools Console tab, run:" -ForegroundColor Cyan
Write-Host ""
Write-Host @"
  // Paste this in Console:
  let dataCount = 0;
  const originalLog = console.log;
  console.log = function(...args) {
    if (args[0] && args[0].includes && args[0].includes('visualization')) {
      dataCount++;
    }
    originalLog.apply(console, args);
  }
  
  // Wait 5 seconds, then check:
  setTimeout(() => console.log('Data received:', dataCount), 5000);
"@ -ForegroundColor Gray

Write-Host ""
Write-Host "  OR look for existing console.log statements:" -ForegroundColor Cyan
Write-Host "    'Received visualization data'" -ForegroundColor Gray
Write-Host "    'Updating spectrum'" -ForegroundColor Gray
Write-Host "    'Drawing waveform'" -ForegroundColor Gray
Write-Host ""
Write-Host "  Press Enter after checking..." -NoNewline
Read-Host

Write-Host ""
$hasJSLogs = Read-Host "  Do you see JavaScript receiving data? (y/n)"

if ($hasJSLogs -eq 'y') {
    Write-Host "  ✅ JavaScript is receiving data!" -ForegroundColor Green
} else {
    Write-Host "  ⚠️  JavaScript may not be processing data" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Check:" -ForegroundColor Cyan
    Write-Host "    - Any JavaScript errors in Console?" -ForegroundColor Gray
    Write-Host "    - Is the SignalR handler registered?" -ForegroundColor Gray
    Write-Host "    - Look for: connection.on('ReceiveVisualizationData', ...)" -ForegroundColor Gray
}

# Check 5: Canvas rendering?
Write-Host ""
Write-Host "[5/5] Is canvas element rendering?" -ForegroundColor Yellow
Write-Host ""
Write-Host "  In Browser Console, run:" -ForegroundColor Cyan
Write-Host ""
Write-Host @"
  const canvas = document.querySelector('canvas');
  console.log('Canvas found:', canvas !== null);
  console.log('Canvas size:', canvas?.width, 'x', canvas?.height);
  const ctx = canvas?.getContext('2d');
  console.log('Context:', ctx !== null);
  
  // Test drawing:
  if (ctx) {
    ctx.fillStyle = 'red';
    ctx.fillRect(10, 10, 50, 50);
    console.log('Drew test rectangle - check canvas!');
  }
"@ -ForegroundColor Gray

Write-Host ""
Write-Host "  Press Enter after running test..." -NoNewline
Read-Host

Write-Host ""
$canvasWorks = Read-Host "  Did you see a red rectangle on the canvas? (y/n)"

if ($canvasWorks -eq 'y') {
    Write-Host "  ✅ Canvas can render!" -ForegroundColor Green
    Write-Host ""
    Write-Host "  This means the problem is in the visualization update logic" -ForegroundColor Yellow
} else {
    Write-Host "  ❌ Canvas is not rendering" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Possible issues:" -ForegroundColor Yellow
    Write-Host "    - Canvas element doesn't exist (check HTML)" -ForegroundColor Gray
    Write-Host "    - Canvas has zero size (check CSS)" -ForegroundColor Gray
    Write-Host "    - Canvas is hidden (display:none)" -ForegroundColor Gray
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Debug Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Data flow check:" -ForegroundColor Yellow
Write-Host "  Audio playing through SoundFlow: ✅ (You can hear it)" -ForegroundColor Green
Write-Host "  VisualizerService receiving samples: " -NoNewline
if ($hasSampleLogs -eq 'y') { Write-Host "✅" -ForegroundColor Green } else { Write-Host "❌" -ForegroundColor Red }
Write-Host "  VisualizerService broadcasting: " -NoNewline
if ($hasBroadcastLogs -eq 'y') { Write-Host "✅" -ForegroundColor Green } else { Write-Host "❌" -ForegroundColor Red }
Write-Host "  Browser receiving WebSocket: " -NoNewline
if ($hasWSMessages -eq 'y') { Write-Host "✅" -ForegroundColor Green } else { Write-Host "❌" -ForegroundColor Red }
Write-Host "  JavaScript processing data: " -NoNewline
if ($hasJSLogs -eq 'y') { Write-Host "✅" -ForegroundColor Green } else { Write-Host "?" -ForegroundColor Yellow }
Write-Host "  Canvas rendering: " -NoNewline
if ($canvasWorks -eq 'y') { Write-Host "✅" -ForegroundColor Green } else { Write-Host "❌" -ForegroundColor Red }
Write-Host ""

# Recommendations
Write-Host "Recommendations:" -ForegroundColor Yellow
Write-Host ""

if ($hasSampleLogs -ne 'y') {
    Write-Host "1. Enable debug logging for VisualizerService" -ForegroundColor White
    Write-Host "   Add to appsettings.Development.json:" -ForegroundColor Gray
    Write-Host '   "Radio.Infrastructure.Audio.Services.VisualizerService": "Debug"' -ForegroundColor Gray
    Write-Host ""
}

if ($hasBroadcastLogs -ne 'y') {
    Write-Host "2. Check VisualizerService initialization" -ForegroundColor White
    Write-Host "   Look for 'VisualizerService started' in startup logs" -ForegroundColor Gray
    Write-Host ""
}

if ($hasWSMessages -ne 'y') {
    Write-Host "3. Check SignalR hub connection" -ForegroundColor White
    Write-Host "   Refresh the page (Ctrl+Shift+R)" -ForegroundColor Gray
    Write-Host "   Check for SignalR errors in browser console" -ForegroundColor Gray
    Write-Host ""
}

if ($canvasWorks -ne 'y') {
    Write-Host "4. Check canvas element setup" -ForegroundColor White
    Write-Host "   Inspect HTML for <canvas> element" -ForegroundColor Gray
    Write-Host "   Check CSS for width/height and display properties" -ForegroundColor Gray
    Write-Host ""
}

Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Check RadioConsole logs for the debug messages listed above" -ForegroundColor White
Write-Host "  2. Check browser DevTools Network and Console tabs" -ForegroundColor White
Write-Host "  3. Run canvas test code to verify rendering works" -ForegroundColor White
Write-Host "  4. Report which checkpoint is failing" -ForegroundColor White
Write-Host ""

Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
