<#
.SYNOPSIS
  One-command build and deploy to Raspberry Pi from Windows.

.DESCRIPTION
  Cross-compiles for linux-arm64, syncs to the Pi via SCP/SSH, and restarts
  the radio-console service. Uses rsync over SSH for incremental transfers.

.PARAMETER NoRestart
  Deploy without restarting the service.

.PARAMETER Logs
  Tail journalctl after restart.

.PARAMETER Quick
  Framework-dependent publish (smaller/faster, needs .NET runtime on Pi).

.PARAMETER PiHost
  Pi hostname or IP. Default: piradio (override with env var PI_HOST).

.PARAMETER PiUser
  SSH user. Default: pi (override with env var PI_USER).

.PARAMETER PiPath
  Install path on Pi. Default: /opt/radio-console (override with env var PI_PATH).

.EXAMPLE
  .\deploy\Deploy-ToPi.ps1
  .\deploy\Deploy-ToPi.ps1 -Logs
  .\deploy\Deploy-ToPi.ps1 -Quick -NoRestart
  .\deploy\Deploy-ToPi.ps1 -PiHost 192.168.86.44
#>
[CmdletBinding()]
param(
  [switch]$NoRestart,
  [switch]$Logs,
  [switch]$Quick,
  [string]$PiHost = ($env:PI_HOST ?? "piradio"),
  [string]$PiUser = ($env:PI_USER ?? "pi"),
  [string]$PiPath = ($env:PI_PATH ?? "/opt/radio-console")
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$PublishDir = Join-Path $RepoRoot "publish\linux-arm64"
$SshTarget = "${PiUser}@${PiHost}"

Write-Host "=== Radio Console Deploy ===" -ForegroundColor Cyan
Write-Host "Target: ${SshTarget}:${PiPath}"
Write-Host ""

# --- Step 1: Build ---
Write-Host "[1/4] Building for linux-arm64..." -ForegroundColor Yellow

$publishArgs = @(
  "publish", "$RepoRoot\src\Radio.API\Radio.API.csproj",
  "--configuration", "Release",
  "--runtime", "linux-arm64",
  "--output", $PublishDir,
  "-v", "quiet"
)

if ($Quick) {
  $publishArgs += "--no-self-contained"
  Write-Host "  (framework-dependent - .NET runtime required on Pi)" -ForegroundColor DarkGray
} else {
  $publishArgs += @(
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:PublishTrimmed=false",
    "-p:IncludeNativeLibrariesForSelfExtract=true"
  )
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
  Write-Host "Build failed!" -ForegroundColor Red
  exit 1
}

$size = "{0:N1} MB" -f ((Get-ChildItem $PublishDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB)
Write-Host "  Build complete ($size)" -ForegroundColor Green

# --- Step 2: Stop service ---
if (-not $NoRestart) {
  Write-Host "[2/4] Stopping service on Pi..." -ForegroundColor Yellow
  ssh $SshTarget "sudo systemctl stop radio-console 2>/dev/null; true"
} else {
  Write-Host "[2/4] Skipping service stop (--NoRestart)" -ForegroundColor DarkGray
}

# --- Step 3: Sync files ---
Write-Host "[3/4] Syncing files to Pi..." -ForegroundColor Yellow

# Use scp to copy to a temp dir, then rsync locally on the Pi to preserve data/logs.
# This avoids needing rsync on Windows (not always available).
# First, check if rsync is available locally (Git Bash, WSL, etc.)
$useRsync = $false
try {
  $null = Get-Command rsync -ErrorAction Stop
  $useRsync = $true
} catch {
  # rsync not available, fall back to scp
}

if ($useRsync) {
  # rsync available (e.g., via Git for Windows, MSYS2, or WSL)
  rsync -avz --delete `
    --exclude='data' `
    --exclude='logs' `
    --exclude='appsettings.Production.json' `
    "${PublishDir}/" "${SshTarget}:/tmp/radio-deploy/"
} else {
  # Fallback: scp entire directory
  Write-Host "  (rsync not found, using scp - slower for incremental updates)" -ForegroundColor DarkGray
  scp -r "${PublishDir}\*" "${SshTarget}:/tmp/radio-deploy/"
}

if ($LASTEXITCODE -ne 0) {
  Write-Host "File sync failed!" -ForegroundColor Red
  exit 1
}

# Move files into place on the Pi, preserving data and logs
ssh $SshTarget @"
  sudo rsync -a --exclude='data' --exclude='logs' --exclude='appsettings.Production.json' /tmp/radio-deploy/ $PiPath/ &&
  sudo chown -R radio:radio $PiPath &&
  sudo chmod +x $PiPath/Radio.API &&
  rm -rf /tmp/radio-deploy
"@

if ($LASTEXITCODE -ne 0) {
  Write-Host "Remote file move failed!" -ForegroundColor Red
  exit 1
}

Write-Host "  Files synced" -ForegroundColor Green

# --- Step 4: Restart ---
if (-not $NoRestart) {
  Write-Host "[4/4] Starting service..." -ForegroundColor Yellow
  ssh $SshTarget "sudo systemctl start radio-console"
  Start-Sleep -Seconds 1

  $status = ssh $SshTarget "systemctl is-active radio-console 2>/dev/null"
  if ($status -eq "active") {
    Write-Host ""
    Write-Host "=== Deploy successful ===" -ForegroundColor Green
    Write-Host "UI: http://${PiHost}:5000"
  } else {
    Write-Host ""
    Write-Host "=== WARNING: Service may have failed to start ===" -ForegroundColor Red
    Write-Host "Check: ssh $SshTarget 'journalctl -u radio-console -n 20'"
  }
} else {
  Write-Host "[4/4] Skipping restart (--NoRestart)" -ForegroundColor DarkGray
  Write-Host ""
  Write-Host "=== Deploy complete (service not restarted) ===" -ForegroundColor Green
  Write-Host "Start manually: ssh $SshTarget 'sudo systemctl start radio-console'"
}

# --- Optional: tail logs ---
if ($Logs) {
  Write-Host ""
  Write-Host "--- Tailing logs (Ctrl+C to stop) ---" -ForegroundColor Cyan
  ssh $SshTarget "journalctl -u radio-console -f"
}
