# Quick Spotify Status Check

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Spotify Status Quick Check" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Is librespot running?
Write-Host "[1/4] Librespot Status" -ForegroundColor Yellow
$librespot = Get-Process -Name "librespot" -ErrorAction SilentlyContinue
if ($librespot) {
    Write-Host "  ✅ Running (PID: $($librespot.Id))" -ForegroundColor Green
} else {
    Write-Host "  ❌ NOT running" -ForegroundColor Red
}

# 2. VB-Audio Cable
Write-Host ""
Write-Host "[2/4] VB-Audio Cable" -ForegroundColor Yellow
$vbCable = Get-WmiObject Win32_SoundDevice | Where-Object { $_.Name -like "*VB-Audio*" -or $_.Name -like "*CABLE*" }
if ($vbCable) {
    Write-Host "  ✅ Installed" -ForegroundColor Green
} else {
    Write-Host "  ❌ NOT installed" -ForegroundColor Red
}

# 3. RadioConsole config
Write-Host ""
Write-Host "[3/4] RadioConsole Configuration" -ForegroundColor Yellow
$configPath = "src\Radio.API\appsettings.Development.json"
if (Test-Path $configPath) {
    Write-Host "  ✅ Config found: $configPath" -ForegroundColor Green
    
    try {
        $config = Get-Content $configPath -Raw | ConvertFrom-Json
        $mode = "Not configured"
        $device = "Not configured"
        
        if ($config.Devices -and $config.Devices.Spotify) {
            $mode = $config.Devices.Spotify.Mode
            $device = $config.Devices.Spotify.LoopbackDeviceName
        }
        
        Write-Host "     Mode: $mode" -ForegroundColor Gray
        Write-Host "     Device: $device" -ForegroundColor Gray
    } catch {
        Write-Host "  ⚠️  Could not read config" -ForegroundColor Yellow
    }
} else {
    Write-Host "  ❌ Config not found" -ForegroundColor Red
}

# 4. What to do next
Write-Host ""
Write-Host "[4/4] Next Steps" -ForegroundColor Yellow

if ($librespot) {
    Write-Host "  ✅ Librespot is running" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Since librespot is running and playing songs:" -ForegroundColor Cyan
    Write-Host "  1. Open Spotify app" -ForegroundColor White
    Write-Host "  2. Make sure you're connected to 'RadioConsole' device" -ForegroundColor White
    Write-Host "  3. In RadioConsole app, select Spotify as the source" -ForegroundColor White
    Write-Host "  4. Click Play/Resume in RadioConsole" -ForegroundColor White
    Write-Host ""
    Write-Host "  If still getting 'No active device' error:" -ForegroundColor Yellow
    Write-Host "  - The error is because RadioConsole is trying to send commands to Spotify API" -ForegroundColor Gray
    Write-Host "  - In loopback mode, you control playback from Spotify app, not RadioConsole" -ForegroundColor Gray
    Write-Host "  - Just play in Spotify app, RadioConsole will capture the audio" -ForegroundColor Gray
} else {
    Write-Host "  ❌ Start librespot first!" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Run this command:" -ForegroundColor Yellow
    Write-Host "  cd ~\librespot" -ForegroundColor Gray
    Write-Host "  .\target\release\librespot.exe --name `"RadioConsole`" --device `"CABLE Input`"" -ForegroundColor Gray
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
