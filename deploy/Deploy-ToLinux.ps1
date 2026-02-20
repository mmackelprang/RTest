<#
.SYNOPSIS
  One-command build and deploy to a Linux target from Windows.

.DESCRIPTION
  Cross-compiles Radio.API and Radio.Web for the specified Linux runtime,
  syncs to the target via SCP/SSH, and restarts both services.
  Uses rsync over SSH for incremental transfers when available.

.PARAMETER NoRestart
  Deploy without restarting the services.

.PARAMETER Logs
  Tail journalctl after restart.

.PARAMETER Quick
  Framework-dependent publish (smaller/faster, needs .NET runtime on target).

.PARAMETER TargetHost
  Target hostname or IP. Default: piradio (override with env var PI_HOST).

.PARAMETER TargetUser
  SSH user. Default: mmack (override with env var PI_USER).

.PARAMETER TargetPath
  Install path on target. Default: /opt/radio-console (override with env var PI_PATH).

.PARAMETER Runtime
  .NET runtime identifier. Default: linux-arm64.
  Common values: linux-arm64 (Raspberry Pi), linux-x64 (Ubuntu x64).

.EXAMPLE
  .\deploy\Deploy-ToLinux.ps1
  .\deploy\Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64
  .\deploy\Deploy-ToLinux.ps1 -Logs
  .\deploy\Deploy-ToLinux.ps1 -Quick -NoRestart
  .\deploy\Deploy-ToLinux.ps1 -TargetHost 192.168.86.44
#>
[CmdletBinding()]
param(
  [switch]$NoRestart,
  [switch]$Logs,
  [switch]$Quick,
  [Alias("PiHost")]
  [string]$TargetHost = $(if ($env:PI_HOST) { $env:PI_HOST } else { "piradio" }),
  [Alias("PiUser")]
  [string]$TargetUser = $(if ($env:PI_USER) { $env:PI_USER } else { "mmack" }),
  [Alias("PiPath")]
  [string]$TargetPath = $(if ($env:PI_PATH) { $env:PI_PATH } else { "/opt/radio-console" }),
  [ValidateSet("linux-arm64", "linux-x64")]
  [string]$Runtime = "linux-arm64"
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$ApiPublishDir = Join-Path $RepoRoot "publish\$Runtime\api"
$WebPublishDir = Join-Path $RepoRoot "publish\$Runtime\web"
$SshTarget = "${TargetUser}@${TargetHost}"

# Determine which config directory to use based on runtime
$configDir = switch ($Runtime) {
  "linux-arm64" { "raspberry-pi" }
  "linux-x64"   { "debian-x64" }
}

Write-Host "=== Radio Console Deploy ===" -ForegroundColor Cyan
Write-Host "Target:  ${SshTarget}:${TargetPath}"
Write-Host "Runtime: $Runtime"
Write-Host ""

# --- Step 1: Build both projects ---
Write-Host "[1/4] Building for $Runtime..." -ForegroundColor Yellow

$commonArgs = @(
  "--configuration", "Release",
  "--runtime", $Runtime,
  "-f", "net8.0",
  "-v", "quiet"
)

if ($Quick) {
  $commonArgs += "--no-self-contained"
  Write-Host "  (framework-dependent - .NET runtime required on target)" -ForegroundColor DarkGray
} else {
  $commonArgs += @(
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:PublishTrimmed=false",
    "-p:IncludeNativeLibrariesForSelfExtract=true"
  )
}

# Restore for net8.0 (Windows conditional TFM means assets are built for windows TFM by default)
Write-Host "  Restoring for net8.0 / $Runtime..." -ForegroundColor DarkGray
dotnet restore "$RepoRoot\src\Radio.API\Radio.API.csproj" --runtime $Runtime -p:TargetFramework=net8.0 -v quiet
dotnet restore "$RepoRoot\src\Radio.Web\Radio.Web.csproj" --runtime $Runtime -p:TargetFramework=net8.0 -v quiet

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
  Write-Host "[2/4] Stopping services..." -ForegroundColor Yellow
  ssh $SshTarget "sudo systemctl stop radio-web 2>/dev/null; sudo systemctl stop radio-api 2>/dev/null; true"
} else {
  Write-Host "[2/4] Skipping service stop (--NoRestart)" -ForegroundColor DarkGray
}

# --- Step 3: Sync files ---
Write-Host "[3/4] Syncing files..." -ForegroundColor Yellow

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

# Move files into place, preserving data, logs, and Production config
ssh $SshTarget "sudo mkdir -p $TargetPath/api $TargetPath/web $TargetPath/data $TargetPath/logs && sudo rsync -a --delete --exclude='appsettings.Production.json' /tmp/radio-deploy-api/ $TargetPath/api/ && sudo rsync -a --delete --exclude='appsettings.Production.json' /tmp/radio-deploy-web/ $TargetPath/web/ && sudo chown -R radio:radio $TargetPath && sudo chmod +x $TargetPath/api/Radio.API $TargetPath/web/Radio.Web && rm -rf /tmp/radio-deploy-api /tmp/radio-deploy-web"

# Deploy target-specific Production config if not already present
$targetConfigPath = Join-Path $RepoRoot "deploy\$configDir\appsettings.Production.json"
if (Test-Path $targetConfigPath) {
  ssh $SshTarget "test -f $TargetPath/api/appsettings.Production.json" 2>$null
  if ($LASTEXITCODE -ne 0) {
    Write-Host "  Deploying Production config from deploy/$configDir/..." -ForegroundColor DarkGray
    scp $targetConfigPath "${SshTarget}:/tmp/appsettings.Production.json"
    ssh $SshTarget "sudo cp /tmp/appsettings.Production.json $TargetPath/api/ && sudo cp /tmp/appsettings.Production.json $TargetPath/web/ && sudo chown radio:radio $TargetPath/api/appsettings.Production.json $TargetPath/web/appsettings.Production.json && rm /tmp/appsettings.Production.json"
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
    Write-Host "API: http://${TargetHost}:5000"
    Write-Host "Web: http://${TargetHost}:5002"
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
