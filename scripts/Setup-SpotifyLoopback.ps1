# Spotify Loopback Setup Script for Windows
# This script helps automate the setup of Spotify loopback mode

param(
  [switch]$InstallLibrespot,
  [switch]$RunLibrespot,
  [string]$DeviceName = "RadioConsole",
  [string]$VBCableDevice = "CABLE Input (VB-Audio Virtual Cable)",
  [int]$Bitrate = 320,
  [int]$Volume = 75
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Spotify Loopback Setup for RadioConsole" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if VB-Audio Cable is installed
Write-Host "[1/5] Checking VB-Audio Virtual Cable..." -ForegroundColor Yellow
$vbCableExists = Get-WmiObject Win32_SoundDevice | Where-Object { $_.Name -like "*VB-Audio*" }

if ($vbCableExists) {
  Write-Host "  ✅ VB-Audio Virtual Cable is installed" -ForegroundColor Green
} else {
  Write-Host "  ❌ VB-Audio Virtual Cable NOT found" -ForegroundColor Red
  Write-Host "     Please download and install from: https://vb-audio.com/Cable/" -ForegroundColor Yellow
  Write-Host "     After installation, restart this script" -ForegroundColor Yellow
  exit 1
}

# Check if Rust is installed
Write-Host "[2/5] Checking Rust installation..." -ForegroundColor Yellow
try {
  $rustVersion = cargo --version
  Write-Host "  ✅ Rust is installed: $rustVersion" -ForegroundColor Green
} catch {
  Write-Host "  ❌ Rust is NOT installed" -ForegroundColor Red
  Write-Host "     Installing Rust via winget..." -ForegroundColor Yellow
  winget install Rustlang.Rust.GNU
  Write-Host "     Please restart PowerShell and run this script again" -ForegroundColor Yellow
  exit 1
}

# Install/Build Librespot
if ($InstallLibrespot) {
  Write-Host "[3/5] Installing librespot..." -ForegroundColor Yellow
  
  $librespotPath = Join-Path $env:USERPROFILE "librespot"
  
  if (Test-Path $librespotPath) {
    Write-Host "  Librespot directory already exists, updating..." -ForegroundColor Yellow
    Push-Location $librespotPath
    git pull
  } else {
    Write-Host "  Cloning librespot repository..." -ForegroundColor Yellow
    git clone https://github.com/librespot-org/librespot.git $librespotPath
    Push-Location $librespotPath
  }
  
  Write-Host "  Building librespot (this may take 5-10 minutes)..." -ForegroundColor Yellow
  cargo build --release
  
  if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✅ Librespot built successfully" -ForegroundColor Green
    Write-Host "     Location: $librespotPath\target\release\librespot.exe" -ForegroundColor Green
  } else {
    Write-Host "  ❌ Failed to build librespot" -ForegroundColor Red
    Pop-Location
    exit 1
  }
  
  Pop-Location
} else {
  Write-Host "[3/5] Checking librespot..." -ForegroundColor Yellow
  $librespotPath = Join-Path $env:USERPROFILE "librespot"
  $librespotExe = Join-Path $librespotPath "target\release\librespot.exe"
  
  if (Test-Path $librespotExe) {
    Write-Host "  ✅ Librespot found at: $librespotExe" -ForegroundColor Green
  } else {
    Write-Host "  ⚠️  Librespot not found. Run with -InstallLibrespot to build it" -ForegroundColor Yellow
  }
}

# Generate configuration
Write-Host "[4/5] Generating RadioConsole configuration..." -ForegroundColor Yellow

$configPath = Join-Path $PSScriptRoot "appsettings.Development.Spotify.json"
$config = @{
  "Devices" = @{
    "Spotify" = @{
      "Mode" = "Loopback"
      "LoopbackDeviceName" = "CABLE Output"
    }
  }
  "Spotify" = @{
    "ClientID" = "`${secret:spotify_client_id}"
    "ClientSecret" = "`${secret:spotify_client_secret}"
    "RefreshToken" = "`${secret:spotify_refresh_token}"
  }
} | ConvertTo-Json -Depth 10

Write-Host "  Configuration preview:" -ForegroundColor Cyan
Write-Host $config -ForegroundColor Gray
Write-Host ""

$saveConfig = Read-Host "  Save this configuration to $configPath? (y/n)"
if ($saveConfig -eq 'y') {
  $config | Out-File -FilePath $configPath -Encoding UTF8
  Write-Host "  ✅ Configuration saved" -ForegroundColor Green
} else {
  Write-Host "  ⏭️  Configuration not saved" -ForegroundColor Yellow
}

# Run Librespot
if ($RunLibrespot) {
  Write-Host "[5/5] Starting librespot..." -ForegroundColor Yellow
  
  $librespotExe = Join-Path $env:USERPROFILE "librespot\target\release\librespot.exe"
  
  if (-not (Test-Path $librespotExe)) {
    Write-Host "  ❌ Librespot executable not found. Run with -InstallLibrespot first" -ForegroundColor Red
    exit 1
  }
  
  Write-Host "  Starting librespot with device name: $DeviceName" -ForegroundColor Cyan
  Write-Host "  Output device: $VBCableDevice" -ForegroundColor Cyan
  Write-Host "  Bitrate: $Bitrate kbps, Volume: $Volume%" -ForegroundColor Cyan
  Write-Host ""
  Write-Host "  Press Ctrl+C to stop librespot" -ForegroundColor Yellow
  Write-Host ""
  
  & $librespotExe `
    --name $DeviceName `
    --backend rodio `
    --device $VBCableDevice `
    --bitrate $Bitrate `
    --initial-volume $Volume `
    --verbose
    
} else {
  Write-Host "[5/5] Setup complete!" -ForegroundColor Yellow
  Write-Host ""
  Write-Host "Next steps:" -ForegroundColor Cyan
  Write-Host "  1. Run this script with -RunLibrespot to start librespot" -ForegroundColor White
  Write-Host "     Example: .\Setup-SpotifyLoopback.ps1 -RunLibrespot" -ForegroundColor Gray
  Write-Host ""
  Write-Host "  2. Or run librespot manually:" -ForegroundColor White
  Write-Host "     cd $env:USERPROFILE\librespot" -ForegroundColor Gray
  Write-Host "     .\target\release\librespot.exe --name `"$DeviceName`" --device `"$VBCableDevice`"" -ForegroundColor Gray
  Write-Host ""
  Write-Host "  3. Start RadioConsole and select Spotify source" -ForegroundColor White
  Write-Host "  4. Open Spotify app and connect to `"$DeviceName`"" -ForegroundColor White
  Write-Host "  5. Play a song and verify visualization works" -ForegroundColor White
  Write-Host ""
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "For troubleshooting, see: SPOTIFY_LOOPBACK_SETUP.md" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
