<#
.SYNOPSIS
  One-command build and deploy to Raspberry Pi from Windows.

.DESCRIPTION
  Cross-compiles Radio.API and Radio.Web for linux-arm64, syncs to the Pi via
  SCP/SSH, and restarts both services. Uses rsync over SSH for incremental transfers.

.PARAMETER NoRestart
  Deploy without restarting the services.

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
  [string]$PiUser = ($env:PI_USER ?? "mmack"),
  [string]$PiPath = ($env:PI_PATH ?? "/opt/radio-console")
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$ApiPublishDir = Join-Path $RepoRoot "publish\linux-arm64\api"
$WebPublishDir = Join-Path $RepoRoot "publish\linux-arm64\web"
$SshTarget = "${PiUser}@${PiHost}"

Write-Host "=== Radio Console Deploy ===" -ForegroundColor Cyan
Write-Host "Target: ${SshTarget}:${PiPath}"
Write-Host ""

# --- Step 1: Build both projects ---
Write-Host "[1/4] Building for linux-arm64..." -ForegroundColor Yellow

$commonArgs = @(
  "--configuration", "Release",
  "--runtime", "linux-arm64",
  "-f", "net8.0",
  "-v", "quiet"
)

if ($Quick) {
  $commonArgs += "--no-self-contained"
  Write-Host "  (framework-dependent - .NET runtime required on Pi)" -ForegroundColor DarkGray
} else {
  $commonArgs += @(
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:PublishTrimmed=false",
    "-p:IncludeNativeLibrariesForSelfExtract=true"
  )
}

# Restore for net8.0 (Windows conditional TFM means assets are built for windows TFM by default)
Write-Host "  Restoring for net8.0 / linux-arm64..." -ForegroundColor DarkGray
dotnet restore "$RepoRoot\src\Radio.API\Radio.API.csproj" --runtime linux-arm64 -p:TargetFramework=net8.0 -v quiet
dotnet restore "$RepoRoot\src\Radio.Web\Radio.Web.csproj" --runtime linux-arm64 -p:TargetFramework=net8.0 -v quiet

# Publish Radio.API
Write-Host "  Publishing Radio.API..." -ForegroundColor DarkGray
dotnet publish "$RepoRoot\src\Radio.API\Radio.API.csproj" --no-restore --output $ApiPublishDir @commonArgs
if ($LASTEXITCODE -ne 0) {
  Write-Host "API build failed!" -ForegroundColor Red
  exit 1
}

# Publish Radio.Web
Write-Host "  Publishing Radio.Web..." -ForegroundColor DarkGray
dotnet publish "$RepoRoot\src\Radio.Web\Radio.Web.csproj" --no-restore --output $WebPublishDir @commonArgs
if ($LASTEXITCODE -ne 0) {
  Write-Host "Web build failed!" -ForegroundColor Red
  exit 1
}

$apiSize = "{0:N1} MB" -f ((Get-ChildItem $ApiPublishDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB)
$webSize = "{0:N1} MB" -f ((Get-ChildItem $WebPublishDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB)
Write-Host "  Build complete (API: $apiSize, Web: $webSize)" -ForegroundColor Green

# --- Step 2: Stop services ---
if (-not $NoRestart) {
  Write-Host "[2/4] Stopping services on Pi..." -ForegroundColor Yellow
  ssh $SshTarget "sudo systemctl stop radio-web 2>/dev/null; sudo systemctl stop radio-api 2>/dev/null; true"
} else {
  Write-Host "[2/4] Skipping service stop (--NoRestart)" -ForegroundColor DarkGray
}

# --- Step 3: Sync files ---
Write-Host "[3/4] Syncing files to Pi..." -ForegroundColor Yellow

# Check if rsync is available locally (Git Bash, WSL, etc.)
$useRsync = $false
try {
  $null = Get-Command rsync -ErrorAction Stop
  $useRsync = $true
} catch {
  # rsync not available, fall back to scp
}

# Sync API
Write-Host "  Syncing API..." -ForegroundColor DarkGray
if ($useRsync) {
  rsync -avz --delete "${ApiPublishDir}/" "${SshTarget}:/tmp/radio-deploy-api/"
} else {
  Write-Host "  (rsync not found, using scp)" -ForegroundColor DarkGray
  ssh $SshTarget "rm -rf /tmp/radio-deploy-api && mkdir -p /tmp/radio-deploy-api"
  scp -r $ApiPublishDir "${SshTarget}:/tmp/radio-deploy-api-tmp"
  ssh $SshTarget "mv /tmp/radio-deploy-api-tmp/* /tmp/radio-deploy-api/ 2>/dev/null; mv /tmp/radio-deploy-api-tmp/.[!.]* /tmp/radio-deploy-api/ 2>/dev/null; rm -rf /tmp/radio-deploy-api-tmp"
}
if ($LASTEXITCODE -ne 0) {
  Write-Host "API sync failed!" -ForegroundColor Red
  exit 1
}

# Sync Web
Write-Host "  Syncing Web..." -ForegroundColor DarkGray
if ($useRsync) {
  rsync -avz --delete "${WebPublishDir}/" "${SshTarget}:/tmp/radio-deploy-web/"
} else {
  ssh $SshTarget "rm -rf /tmp/radio-deploy-web && mkdir -p /tmp/radio-deploy-web"
  scp -r $WebPublishDir "${SshTarget}:/tmp/radio-deploy-web-tmp"
  ssh $SshTarget "mv /tmp/radio-deploy-web-tmp/* /tmp/radio-deploy-web/ 2>/dev/null; mv /tmp/radio-deploy-web-tmp/.[!.]* /tmp/radio-deploy-web/ 2>/dev/null; rm -rf /tmp/radio-deploy-web-tmp"
}
if ($LASTEXITCODE -ne 0) {
  Write-Host "Web sync failed!" -ForegroundColor Red
  exit 1
}

# Move files into place on the Pi, preserving data, logs, and Production config
ssh $SshTarget "sudo mkdir -p $PiPath/api $PiPath/web $PiPath/data $PiPath/logs && sudo rsync -a --delete --exclude='appsettings.Production.json' /tmp/radio-deploy-api/ $PiPath/api/ && sudo rsync -a --delete --exclude='appsettings.Production.json' /tmp/radio-deploy-web/ $PiPath/web/ && sudo chown -R radio:radio $PiPath && sudo chmod +x $PiPath/api/Radio.API $PiPath/web/Radio.Web && rm -rf /tmp/radio-deploy-api /tmp/radio-deploy-web"

# Deploy Pi-specific Production config if not already present
$piConfigPath = Join-Path $RepoRoot "deploy\raspberry-pi\appsettings.Production.json"
if (Test-Path $piConfigPath) {
  ssh $SshTarget "test -f $PiPath/api/appsettings.Production.json" 2>$null
  if ($LASTEXITCODE -ne 0) {
    Write-Host "  Deploying Production config..." -ForegroundColor DarkGray
    scp $piConfigPath "${SshTarget}:/tmp/appsettings.Production.json"
    ssh $SshTarget "sudo cp /tmp/appsettings.Production.json $PiPath/api/ && sudo cp /tmp/appsettings.Production.json $PiPath/web/ && sudo chown radio:radio $PiPath/api/appsettings.Production.json $PiPath/web/appsettings.Production.json && rm /tmp/appsettings.Production.json"
  }
}

if ($LASTEXITCODE -ne 0) {
  Write-Host "Remote file move failed!" -ForegroundColor Red
  exit 1
}

Write-Host "  Files synced" -ForegroundColor Green

# --- Step 4: Restart ---
if (-not $NoRestart) {
  Write-Host "[4/4] Starting services..." -ForegroundColor Yellow
  ssh $SshTarget "sudo systemctl daemon-reload && sudo systemctl start radio-api && sudo systemctl start radio-web"
  Start-Sleep -Seconds 2

  $apiStatus = ssh $SshTarget "systemctl is-active radio-api 2>/dev/null"
  $webStatus = ssh $SshTarget "systemctl is-active radio-web 2>/dev/null"

  if ($apiStatus -eq "active" -and $webStatus -eq "active") {
    Write-Host ""
    Write-Host "=== Deploy successful ===" -ForegroundColor Green
    Write-Host "API: http://${PiHost}:5000"
    Write-Host "Web: http://${PiHost}:5002"
  } else {
    Write-Host ""
    Write-Host "=== WARNING: One or more services may have failed ===" -ForegroundColor Red
    Write-Host "  radio-api: $apiStatus"
    Write-Host "  radio-web: $webStatus"
    Write-Host "Check: ssh $SshTarget 'journalctl -u radio-api -u radio-web -n 20'"
  }
} else {
  Write-Host "[4/4] Skipping restart (--NoRestart)" -ForegroundColor DarkGray
  Write-Host ""
  Write-Host "=== Deploy complete (services not restarted) ===" -ForegroundColor Green
  Write-Host "Start manually: ssh $SshTarget 'sudo systemctl start radio-api radio-web'"
}

# --- Optional: tail logs ---
if ($Logs) {
  Write-Host ""
  Write-Host "--- Tailing logs (Ctrl+C to stop) ---" -ForegroundColor Cyan
  ssh $SshTarget "journalctl -u radio-api -u radio-web -f"
}
