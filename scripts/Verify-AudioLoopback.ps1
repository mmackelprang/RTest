# Verify Audio Loopback to SoundFlow

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Audio Loopback Verification" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check 1: CABLE Output recording device is active
Write-Host "[1/5] Checking CABLE Output device..." -ForegroundColor Yellow

# Try to detect audio devices using .NET
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public class AudioDevice {
    [DllImport("winmm.dll", SetLastError = true)]
    public static extern uint waveInGetNumDevs();
}
"@

$deviceCount = [AudioDevice]::waveInGetNumDevs()
Write-Host "  Found $deviceCount recording devices" -ForegroundColor Gray

# Manual check instruction
Write-Host ""
Write-Host "  To verify CABLE Output is receiving audio:" -ForegroundColor Cyan
Write-Host "  1. Right-click speaker icon → Open Sound settings" -ForegroundColor White
Write-Host "  2. Scroll down → More sound settings" -ForegroundColor White
Write-Host "  3. Go to 'Recording' tab" -ForegroundColor White
Write-Host "  4. Find 'CABLE Output (VB-Audio Virtual Cable)'" -ForegroundColor White
Write-Host "  5. Play a song in Spotify" -ForegroundColor White
Write-Host "  6. Watch for GREEN BARS moving on 'CABLE Output'" -ForegroundColor White
Write-Host ""
Write-Host "  ✅ If bars move = Audio is flowing through loopback" -ForegroundColor Green
Write-Host "  ❌ If no bars = Audio is NOT reaching loopback" -ForegroundColor Red
Write-Host ""

# Check 2: RadioConsole configuration
Write-Host "[2/5] Checking RadioConsole Spotify configuration..." -ForegroundColor Yellow

$configPath = "src\Radio.API\appsettings.Development.json"
if (Test-Path $configPath) {
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    
    $spotifyConfig = $null
    if ($config.Devices -and $config.Devices.Spotify) {
        $spotifyConfig = $config.Devices.Spotify
    }
    
    if ($spotifyConfig) {
        Write-Host "  ✅ Configuration found" -ForegroundColor Green
        Write-Host "     Mode: $($spotifyConfig.Mode)" -ForegroundColor Gray
        Write-Host "     LoopbackDevice: $($spotifyConfig.LoopbackDeviceName)" -ForegroundColor Gray
        
        if ($spotifyConfig.Mode -eq "Loopback") {
            if ($spotifyConfig.LoopbackDeviceName -eq "CABLE Output") {
                Write-Host "  ✅ Correct configuration for Windows loopback" -ForegroundColor Green
            } else {
                Write-Host "  ⚠️  Device name should be 'CABLE Output'" -ForegroundColor Yellow
                Write-Host "     Current: $($spotifyConfig.LoopbackDeviceName)" -ForegroundColor Yellow
            }
        } else {
            Write-Host "  ⚠️  Mode is '$($spotifyConfig.Mode)' - should be 'Loopback'" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  ❌ Spotify configuration not found in Devices section" -ForegroundColor Red
    }
} else {
    Write-Host "  ❌ Configuration file not found: $configPath" -ForegroundColor Red
}

# Check 3: Librespot output configuration
Write-Host ""
Write-Host "[3/5] Checking librespot audio output..." -ForegroundColor Yellow

$librespot = Get-Process -Name "librespot" -ErrorAction SilentlyContinue
if ($librespot) {
    Write-Host "  ✅ Librespot is running" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Verify librespot is outputting to CABLE Input:" -ForegroundColor Cyan
    Write-Host "  - Look at the librespot console window" -ForegroundColor White
    Write-Host "  - Should see: 'Using output device: CABLE Input'" -ForegroundColor White
    Write-Host ""
    Write-Host "  If you see a different device:" -ForegroundColor Yellow
    Write-Host "  - Stop librespot (Ctrl+C)" -ForegroundColor Gray
    Write-Host "  - Restart with: --device `"CABLE Input (VB-Audio Virtual Cable)`"" -ForegroundColor Gray
} else {
    Write-Host "  ❌ Librespot is NOT running" -ForegroundColor Red
}

# Check 4: Test audio recording from CABLE Output
Write-Host ""
Write-Host "[4/5] Testing audio capture from CABLE Output..." -ForegroundColor Yellow
Write-Host ""
Write-Host "  Would you like to test recording from CABLE Output? (y/n): " -ForegroundColor Cyan -NoNewline
$response = Read-Host

if ($response -eq 'y' -or $response -eq 'Y') {
    Write-Host ""
    Write-Host "  Starting 5-second test recording..." -ForegroundColor Green
    Write-Host "  Play a song in Spotify NOW!" -ForegroundColor Yellow
    Write-Host ""
    
    $testFile = "loopback-test-$(Get-Date -Format 'yyyyMMdd-HHmmss').wav"
    
    # Use SoundRecorder (Windows built-in) or ffmpeg if available
    if (Get-Command "ffmpeg" -ErrorAction SilentlyContinue) {
        # List audio devices
        Write-Host "  Detecting audio devices with ffmpeg..." -ForegroundColor Gray
        ffmpeg -list_devices true -f dshow -i dummy 2>&1 | Select-String "CABLE Output"
        
        Write-Host ""
        Write-Host "  Recording 5 seconds from CABLE Output..." -ForegroundColor Green
        
        # Record from CABLE Output
        ffmpeg -f dshow -i audio="CABLE Output (VB-Audio Virtual Cable)" -t 5 -y $testFile 2>&1 | Out-Null
        
        if (Test-Path $testFile) {
            $fileSize = (Get-Item $testFile).Length
            Write-Host ""
            if ($fileSize -gt 10000) {
                Write-Host "  ✅ Recording successful! File size: $([math]::Round($fileSize/1KB, 2)) KB" -ForegroundColor Green
                Write-Host "     Saved: $testFile" -ForegroundColor Gray
                Write-Host ""
                Write-Host "  Play the recording to verify audio? (y/n): " -ForegroundColor Cyan -NoNewline
                $playResponse = Read-Host
                
                if ($playResponse -eq 'y' -or $playResponse -eq 'Y') {
                    Start-Process -FilePath $testFile -Wait
                }
                
                Write-Host ""
                Write-Host "  ✅ If you heard audio = Loopback is working!" -ForegroundColor Green
                Write-Host "  ❌ If silence = Loopback is NOT working" -ForegroundColor Red
            } else {
                Write-Host "  ❌ Recording file is empty or too small ($fileSize bytes)" -ForegroundColor Red
                Write-Host "     This means no audio is flowing through CABLE Output" -ForegroundColor Yellow
            }
        } else {
            Write-Host "  ❌ Recording failed - file not created" -ForegroundColor Red
        }
    } else {
        Write-Host "  ⚠️  ffmpeg not found. Install ffmpeg to test recording." -ForegroundColor Yellow
        Write-Host "     Or manually check Sound settings → Recording → CABLE Output for green bars" -ForegroundColor Gray
    }
} else {
    Write-Host "  ⏭️  Skipped test recording" -ForegroundColor Gray
}

# Check 5: RadioConsole logs
Write-Host ""
Write-Host "[5/5] Checking RadioConsole logs..." -ForegroundColor Yellow

$logFiles = Get-ChildItem -Path "." -Recurse -Filter "*.log" -ErrorAction SilentlyContinue | 
            Where-Object { $_.LastWriteTime -gt (Get-Date).AddHours(-1) } |
            Select-Object -First 5

if ($logFiles) {
    Write-Host "  Recent log files:" -ForegroundColor Gray
    $logFiles | ForEach-Object { Write-Host "     - $($_.FullName)" -ForegroundColor Gray }
    Write-Host ""
    Write-Host "  Check these logs for:" -ForegroundColor Cyan
    Write-Host "  - 'Initializing Spotify loopback capture from device:'" -ForegroundColor White
    Write-Host "  - 'USB capture initialized on' or 'Capture device:'" -ForegroundColor White
    Write-Host "  - Any errors about 'CABLE Output' or 'device not found'" -ForegroundColor White
} else {
    Write-Host "  ⚠️  No recent log files found" -ForegroundColor Yellow
    Write-Host "     Check: src/Radio.API/Logs/ or Application output" -ForegroundColor Gray
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Summary & Next Steps" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "To verify loopback audio is reaching SoundFlow:" -ForegroundColor White
Write-Host ""
Write-Host "1. Visual Check (Easiest):" -ForegroundColor Cyan
Write-Host "   - Sound Settings → Recording → CABLE Output" -ForegroundColor Gray
Write-Host "   - Play Spotify, watch for green bars" -ForegroundColor Gray
Write-Host "   - Green bars = Audio flowing ✅" -ForegroundColor Gray
Write-Host ""
Write-Host "2. RadioConsole Visualization:" -ForegroundColor Cyan
Write-Host "   - Open RadioConsole web UI" -ForegroundColor Gray
Write-Host "   - Select Spotify source" -ForegroundColor Gray
Write-Host "   - Play song in Spotify app" -ForegroundColor Gray
Write-Host "   - Look for spectrum analyzer / waveform movement" -ForegroundColor Gray
Write-Host "   - Movement = Audio captured ✅" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Check Logs:" -ForegroundColor Cyan
Write-Host "   - Look for 'Spotify loopback mode initialized successfully'" -ForegroundColor Gray
Write-Host "   - Look for 'initialized on USB port' or similar" -ForegroundColor Gray
Write-Host "   - No errors about device not found" -ForegroundColor Gray
Write-Host ""

Write-Host "Common Issues:" -ForegroundColor Yellow
Write-Host "❌ Librespot not outputting to CABLE Input" -ForegroundColor Red
Write-Host "   → Check librespot console: Should say 'Using output device: CABLE Input'" -ForegroundColor Gray
Write-Host ""
Write-Host "❌ RadioConsole not capturing from CABLE Output" -ForegroundColor Red
Write-Host "   → Check config: LoopbackDeviceName should be 'CABLE Output'" -ForegroundColor Gray
Write-Host ""
Write-Host "❌ CABLE devices not linked" -ForegroundColor Red
Write-Host "   → VB-Audio Cable links CABLE Input (playback) to CABLE Output (recording)" -ForegroundColor Gray
Write-Host "   → This is automatic - no configuration needed" -ForegroundColor Gray
Write-Host ""

Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
