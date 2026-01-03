# Spotify Loopback Diagnostics Script for Windows

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Spotify Loopback Diagnostics" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$issues = @()
$warnings = @()

# Check 1: VB-Audio Cable
Write-Host "[1/7] Checking VB-Audio Virtual Cable..." -ForegroundColor Yellow
$vbCableDevices = Get-WmiObject Win32_SoundDevice | Where-Object { $_.Name -like "*VB-Audio*" }

if ($vbCableDevices) {
  Write-Host "  ✅ VB-Audio Virtual Cable is installed" -ForegroundColor Green
  $vbCableDevices | ForEach-Object {
    Write-Host "     - $($_.Name)" -ForegroundColor Gray
  }
} else {
  Write-Host "  ❌ VB-Audio Virtual Cable NOT found" -ForegroundColor Red
  $issues += "VB-Audio Virtual Cable is not installed. Download from: https://vb-audio.com/Cable/"
}

# Check 2: Recording devices (should see CABLE Output)
Write-Host ""
Write-Host "[2/7] Checking recording devices..." -ForegroundColor Yellow

# List all audio devices using PowerShell
$audioDevices = Get-CimInstance Win32_SoundDevice | Select-Object Name, Status

$cableOutput = $audioDevices | Where-Object { $_.Name -like "*CABLE Output*" }
if ($cableOutput) {
  Write-Host "  ✅ CABLE Output found" -ForegroundColor Green
  Write-Host "     Status: $($cableOutput.Status)" -ForegroundColor Gray
} else {
  Write-Host "  ⚠️  CABLE Output not found in devices" -ForegroundColor Yellow
  $warnings += "CABLE Output not detected. Check Sound settings → Recording tab"
}

Write-Host ""
Write-Host "  Available audio devices:" -ForegroundColor Cyan
$audioDevices | ForEach-Object {
  Write-Host "     - $($_.Name) ($($_.Status))" -ForegroundColor Gray
}

# Check 3: Librespot process
Write-Host ""
Write-Host "[3/7] Checking for librespot process..." -ForegroundColor Yellow

$librespotProcess = Get-Process -Name "librespot" -ErrorAction SilentlyContinue

if ($librespotProcess) {
  Write-Host "  ✅ librespot is running" -ForegroundColor Green
  Write-Host "     PID: $($librespotProcess.Id)" -ForegroundColor Gray
  Write-Host "     Memory: $([math]::Round($librespotProcess.WorkingSet64 / 1MB, 2)) MB" -ForegroundColor Gray
} else {
  Write-Host "  ❌ librespot is NOT running" -ForegroundColor Red
  $issues += "librespot is not running. This is required for loopback mode."
  
  # Check if librespot.exe exists
  $librespotPath = Join-Path $env:USERPROFILE "librespot\target\release\librespot.exe"
  if (Test-Path $librespotPath) {
    Write-Host "     Found: $librespotPath" -ForegroundColor Gray
    Write-Host "     Run: .\librespot.exe --name `"RadioConsole`" --device `"CABLE Input`"" -ForegroundColor Yellow
  } else {
    Write-Host "     Not found at: $librespotPath" -ForegroundColor Yellow
    $issues += "librespot.exe not found. Build it or run Setup-SpotifyLoopback.ps1 -InstallLibrespot"
  }
}

# Check 4: Network connectivity (for Spotify Connect)
Write-Host ""
Write-Host "[4/7] Checking network connectivity..." -ForegroundColor Yellow

try {
  $response = Test-Connection -ComputerName "api.spotify.com" -Count 1 -Quiet
  if ($response) {
    Write-Host "  ✅ Can reach Spotify API" -ForegroundColor Green
  } else {
    Write-Host "  ⚠️  Cannot reach Spotify API" -ForegroundColor Yellow
    $warnings += "Network connectivity issue. Check firewall and internet connection."
  }
} catch {
  Write-Host "  ⚠️  Cannot test network connectivity" -ForegroundColor Yellow
}

# Check 5: RadioConsole configuration
Write-Host ""
Write-Host "[5/7] Checking RadioConsole configuration..." -ForegroundColor Yellow

$configPaths = @(
  "src\Radio.API\appsettings.Development.json",
  "src\Radio.API\appsettings.Development.Spotify.json"
)

$configFound = $false
foreach ($configPath in $configPaths) {
  if (Test-Path $configPath) {
    Write-Host "  ✅ Found config: $configPath" -ForegroundColor Green
    
    try {
      $config = Get-Content $configPath -Raw | ConvertFrom-Json
      
      if ($config.PSObject.Properties.Name -contains "Devices") {
        if ($config.Devices.PSObject.Properties.Name -contains "Spotify") {
          $spotifyConfig = $config.Devices.Spotify
          Write-Host "     Mode: $($spotifyConfig.Mode)" -ForegroundColor Gray
          Write-Host "     LoopbackDevice: $($spotifyConfig.LoopbackDeviceName)" -ForegroundColor Gray
          
          if ($spotifyConfig.Mode -eq "Loopback") {
            Write-Host "  ✅ Loopback mode is configured" -ForegroundColor Green
          } else {
            Write-Host "  ℹ️  RemoteControl mode is configured (loopback disabled)" -ForegroundColor Cyan
          }
        } else {
          Write-Host "  ⚠️  Spotify device configuration not found" -ForegroundColor Yellow
          $warnings += "Devices.Spotify configuration missing in $configPath"
        }
      }
      
      $configFound = $true
      break
    } catch {
      Write-Host "  ⚠️  Could not parse config file" -ForegroundColor Yellow
      Write-Host "     Error: $($_.Exception.Message)" -ForegroundColor Gray
    }
  }
}

if (-not $configFound) {
  Write-Host "  ❌ RadioConsole configuration not found" -ForegroundColor Red
  $issues += "Configuration file not found. Check: $($configPaths -join ', ')"
}

# Check 6: Firewall rules
Write-Host ""
Write-Host "[6/7] Checking Windows Firewall..." -ForegroundColor Yellow

$librespotPath = Join-Path $env:USERPROFILE "librespot\target\release\librespot.exe"
if (Test-Path $librespotPath) {
  $firewallRules = Get-NetFirewallApplicationFilter | Where-Object { $_.Program -eq $librespotPath }
  
  if ($firewallRules) {
    Write-Host "  ✅ Firewall rule exists for librespot" -ForegroundColor Green
  } else {
    Write-Host "  ⚠️  No firewall rule found for librespot" -ForegroundColor Yellow
    $warnings += "Add firewall exception: New-NetFirewallRule -DisplayName 'Librespot' -Direction Inbound -Program '$librespotPath' -Action Allow"
  }
} else {
  Write-Host "  ⏭️  Skipping (librespot not found)" -ForegroundColor Gray
}

# Check 7: Spotify device visibility
Write-Host ""
Write-Host "[7/7] Checking Spotify device visibility..." -ForegroundColor Yellow

if ($librespotProcess) {
  Write-Host "  ℹ️  librespot is running, device should appear as 'RadioConsole' in Spotify app" -ForegroundColor Cyan
  Write-Host "     Open Spotify app → Tap device icon → Look for 'RadioConsole'" -ForegroundColor Gray
} else {
  Write-Host "  ⏭️  Skipping (librespot not running)" -ForegroundColor Gray
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Diagnostic Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if ($issues.Count -eq 0 -and $warnings.Count -eq 0) {
  Write-Host ""
  Write-Host "✅ All checks passed!" -ForegroundColor Green
  Write-Host ""
  Write-Host "Setup appears correct. If you're still having issues:" -ForegroundColor White
  Write-Host "  1. Open Spotify app and connect to 'RadioConsole' device" -ForegroundColor Gray
  Write-Host "  2. Start playing a song in Spotify" -ForegroundColor Gray
  Write-Host "  3. Verify audio plays through RadioConsole (not directly)" -ForegroundColor Gray
  Write-Host "  4. Check RadioConsole logs for any errors" -ForegroundColor Gray
} else {
  if ($issues.Count -gt 0) {
    Write-Host ""
    Write-Host "❌ Issues Found ($($issues.Count)):" -ForegroundColor Red
    $issues | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
  }
  
  if ($warnings.Count -gt 0) {
    Write-Host ""
    Write-Host "⚠️  Warnings ($($warnings.Count)):" -ForegroundColor Yellow
    $warnings | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
  }
  
  Write-Host ""
  Write-Host "Next Steps:" -ForegroundColor Cyan
  Write-Host "  1. Address the issues listed above" -ForegroundColor White
  Write-Host "  2. Run this diagnostic script again" -ForegroundColor White
  Write-Host "  3. Review: SPOTIFY_LOOPBACK_SETUP.md for detailed instructions" -ForegroundColor White
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Offer to start librespot if not running
if (-not $librespotProcess) {
  Write-Host "Would you like to start librespot now? (y/n): " -NoNewline -ForegroundColor Yellow
  $response = Read-Host
  
  if ($response -eq 'y' -or $response -eq 'Y') {
    $librespotPath = Join-Path $env:USERPROFILE "librespot\target\release\librespot.exe"
    
    if (Test-Path $librespotPath) {
      Write-Host ""
      Write-Host "Starting librespot..." -ForegroundColor Green
      Write-Host "Device name: RadioConsole" -ForegroundColor Gray
      Write-Host "Output: CABLE Input (VB-Audio Virtual Cable)" -ForegroundColor Gray
      Write-Host ""
      Write-Host "Press Ctrl+C to stop" -ForegroundColor Yellow
      Write-Host ""
      
      Start-Process -FilePath $librespotPath -ArgumentList "--name", "RadioConsole", "--backend", "rodio", "--device", "CABLE Input (VB-Audio Virtual Cable)", "--bitrate", "320", "--verbose" -NoNewWindow -Wait
    } else {
      Write-Host ""
      Write-Host "❌ Cannot find librespot.exe at: $librespotPath" -ForegroundColor Red
      Write-Host "   Run: Setup-SpotifyLoopback.ps1 -InstallLibrespot" -ForegroundColor Yellow
    }
  }
}

Write-Host ""
