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
  [string]$TargetHost = $(if ($env:PI_HOST) { $env:PI_HOST } else { "radio" }),
  [Alias("PiUser")]
  [string]$TargetUser = $(if ($env:PI_USER) { $env:PI_USER } else { "mmack" }),
  [Alias("PiPath")]
  [string]$TargetPath = $(if ($env:PI_PATH) { $env:PI_PATH } else { "/opt/radio-console" }),
  [ValidateSet("linux-arm64", "linux-x64")]
  [string]$Runtime = "linux-x64"
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$ApiPublishDir = Join-Path $RepoRoot "publish\$Runtime\api"
$WebPublishDir = Join-Path $RepoRoot "publish\$Runtime\web"
$SshTarget = "${TargetUser}@${TargetHost}"
$ApiPort = if ($env:RADIO_API_PORT) { $env:RADIO_API_PORT } else { "5000" }
$WebPort = if ($env:RADIO_WEB_PORT) { $env:RADIO_WEB_PORT } else { "5002" }

# Determine which config directory to use based on runtime
$configDir = switch ($Runtime) {
  "linux-arm64" { "raspberry-pi" }
  "linux-x64"   { "debian-x64" }
}

# Capture the local git SHA so we can (a) bake it into the assembly via
# -p:SourceRevisionId and (b) verify the deployed binary reports the same SHA
# from /api/health/version after restart. Guarded so a missing `git` (or a
# non-git checkout) just downgrades to "unknown" instead of crashing on a
# .Trim() against $null.
$ExpectedSha = "unknown"
try {
  $gitOutput = & git -C $RepoRoot rev-parse HEAD 2>$null
  if ($gitOutput) {
    $trimmed = ([string]$gitOutput).Trim()
    if (-not [string]::IsNullOrWhiteSpace($trimmed)) {
      $ExpectedSha = $trimmed
    }
  }
} catch {
  # git not installed or repo unreadable — fall through with "unknown"
}
if ($ExpectedSha -eq "unknown") {
  Write-Host "WARNING: could not read git HEAD; deploy verification will be skipped" -ForegroundColor Yellow
}

Write-Host "=== Radio Console Deploy ===" -ForegroundColor Cyan
Write-Host "Target:  ${SshTarget}:${TargetPath}"
Write-Host "Runtime: $Runtime"
Write-Host "Commit:  $ExpectedSha"
Write-Host ""

# --- Step 1: Build both projects ---
Write-Host "[1/4] Building for $Runtime..." -ForegroundColor Yellow

$commonArgs = @(
  "--configuration", "Release",
  "--runtime", $Runtime,
  "-f", "net10.0",
  "-p:SourceRevisionId=$ExpectedSha",
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

# Restore for net10.0 (Windows conditional TFM means assets are built for windows TFM by default)
Write-Host "  Restoring for net10.0 / $Runtime..." -ForegroundColor DarkGray
dotnet restore "$RepoRoot\src\Radio.API\Radio.API.csproj" --runtime $Runtime -p:TargetFramework=net10.0 -v quiet
dotnet restore "$RepoRoot\src\Radio.Web\Radio.Web.csproj" --runtime $Runtime -p:TargetFramework=net10.0 -v quiet

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
  Write-Host "[2/4] Stopping services and kiosk browser..." -ForegroundColor Yellow
  # On stop: (a) wipe Chrome's HTTP disk cache so the relaunch can't serve a stale
  # HTML/CSS bundle that pre-dates the deploy — we hit that during the Radzen theme
  # migration, where Chrome served the old MudBlazor markup despite radio-web
  # returning the new HTML. (b) remove Chrome's Singleton lock files; the kill sends
  # SIGTERM/SIGKILL without the orderly shutdown that cleans those up, so on the next
  # relaunch Chrome would see "another instance" and refuse to start. Profile data
  # stays intact — only Cache/, Code Cache/, and the Singleton* locks are targeted.
  #
  # BOTH PATHS MOVED, and leaving the old ones here would have silently cleared nothing.
  # The kiosk now runs on --user-data-dir=~/.config/radio-kiosk-chrome, and a profile
  # started that way keeps its HTTP cache at <profile>/Default/Cache and its Singleton*
  # locks at <profile>/. The paths below therefore name the kiosk profile, not the
  # default one, and must move again if that profile ever does.
  #
  # ~/.cache/google-chrome and ~/.config/google-chrome are deliberately left alone. They are
  # the DEFAULT profile's, which the kiosk no longer uses but which other Chrome launches on
  # this box still could. The kiosk's old data there is therefore orphaned rather than
  # cleaned — a one-time cutover leftover, not something to delete on every deploy.
  #
  # radio-kiosk-exit matches on that same profile path, so the Google Voice bridge
  # Chrome (~/.config/gv-bridge-chrome) is left running. It replaces a
  # `pkill -f 'chrome.*kiosk'` that matched the shape of a command line rather than an
  # identity. Never widen this to `pkill -f chrome`.
  ssh $SshTarget "sudo systemctl stop radio-web 2>/dev/null; sudo systemctl stop radio-api 2>/dev/null; if [ -x /usr/local/bin/radio-kiosk-exit ]; then /usr/local/bin/radio-kiosk-exit 2>/dev/null; else echo 'WARNING: /usr/local/bin/radio-kiosk-exit is missing - run deploy/debian-x64/kiosk/setup-kiosk.sh on this box; the kiosk was NOT stopped'; fi; rm -rf ~/.config/radio-kiosk-chrome/Default/Cache ~/.config/radio-kiosk-chrome/Default/Code\ Cache 2>/dev/null; rm -f ~/.config/radio-kiosk-chrome/Singleton* 2>/dev/null; true"
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
#
# Each native call's exit code is captured on the line after the call, and the first
# non-zero one is what the check below reads. Before OPS-9 this block ran ssh, scp and
# ssh in sequence and then tested $LASTEXITCODE, which by then belonged to the LAST
# ssh — whose remote compound ends in `rm -rf`, and that returns 0 whether or not the
# directory was there. So the message said "API sync failed!" while the value it read
# came from the tidy-up, and a scp that transferred nothing reported success.
#
# The script-level $ErrorActionPreference = "Stop" does not cover this: whether a
# failing native command throws is governed by $PSNativeCommandUseErrorActionPreference,
# measured $false here on PowerShell 7.6.5. Setting it $true would make every native
# call in this file throw on failure instead of falling through to its own check, so
# it is not a local fix. The capture is done per call instead.
#
# Only the scp fallback was ever wrong — on the rsync path the rsync IS the last native
# call before the check. It is captured the same way regardless, so this file has one
# idiom rather than two; $moveExit below is the same shape.
#
# NOT CAUGHT HERE: the two `mv`s in the final ssh send stderr to /dev/null and are
# chained with `;`, so that command's exit code is `rm -rf`'s no matter how the moves
# went. A move that relocated nothing still reports 0. Narrowing that needs the fallback
# driven against a real target, which OPS-9 did not do; it is recorded as
# design/FUTURE-WORK.md section 27 rather than guessed at here.
Write-Host "  Syncing API..." -ForegroundColor DarkGray
if ($useRsync) {
  rsync -avz --delete "${ApiPublishDir}/" "${SshTarget}:/tmp/radio-deploy-api/"
  $apiSyncExit = $LASTEXITCODE
} else {
  Write-Host "  (rsync not found, using scp)" -ForegroundColor DarkGray
  # Clears the -tmp staging dir as well as the destination. Only the third ssh below
  # removes -tmp, and since OPS-9 that ssh is skipped when the transfer fails — so a
  # failed deploy can leave a partial -tmp behind. Clearing it here means the next
  # attempt cleans up after the previous one, without adding a network call to the
  # failure path, where the connection is the thing most likely to be broken.
  ssh $SshTarget "rm -rf /tmp/radio-deploy-api /tmp/radio-deploy-api-tmp && mkdir -p /tmp/radio-deploy-api"
  $apiSyncExit = $LASTEXITCODE
  if ($apiSyncExit -eq 0) {
    scp -r $ApiPublishDir "${SshTarget}:/tmp/radio-deploy-api-tmp"
    $apiSyncExit = $LASTEXITCODE
  }
  if ($apiSyncExit -eq 0) {
    ssh $SshTarget "mv /tmp/radio-deploy-api-tmp/* /tmp/radio-deploy-api/ 2>/dev/null; mv /tmp/radio-deploy-api-tmp/.[!.]* /tmp/radio-deploy-api/ 2>/dev/null; rm -rf /tmp/radio-deploy-api-tmp"
    $apiSyncExit = $LASTEXITCODE
  }
}
if ($apiSyncExit -ne 0) {
  Write-Host "API sync failed!" -ForegroundColor Red
  exit 1
}

# Sync Web — same shape and same reasoning as the API block above, which carries the
# explanation. Kept as two blocks rather than one helper because that is how the file
# already reads; if a third service is ever added, extract it instead of copying again.
Write-Host "  Syncing Web..." -ForegroundColor DarkGray
if ($useRsync) {
  rsync -avz --delete "${WebPublishDir}/" "${SshTarget}:/tmp/radio-deploy-web/"
  $webSyncExit = $LASTEXITCODE
} else {
  # Clears -tmp too; see the API block above for why.
  ssh $SshTarget "rm -rf /tmp/radio-deploy-web /tmp/radio-deploy-web-tmp && mkdir -p /tmp/radio-deploy-web"
  $webSyncExit = $LASTEXITCODE
  if ($webSyncExit -eq 0) {
    scp -r $WebPublishDir "${SshTarget}:/tmp/radio-deploy-web-tmp"
    $webSyncExit = $LASTEXITCODE
  }
  if ($webSyncExit -eq 0) {
    ssh $SshTarget "mv /tmp/radio-deploy-web-tmp/* /tmp/radio-deploy-web/ 2>/dev/null; mv /tmp/radio-deploy-web-tmp/.[!.]* /tmp/radio-deploy-web/ 2>/dev/null; rm -rf /tmp/radio-deploy-web-tmp"
    $webSyncExit = $LASTEXITCODE
  }
}
if ($webSyncExit -ne 0) {
  Write-Host "Web sync failed!" -ForegroundColor Red
  exit 1
}

# Move files into place, preserving data, logs, and Production config
ssh $SshTarget "sudo mkdir -p $TargetPath/api $TargetPath/web $TargetPath/data $TargetPath/logs && sudo rsync -a --delete --exclude='appsettings.Production.json' /tmp/radio-deploy-api/ $TargetPath/api/ && sudo rsync -a --delete --exclude='appsettings.Production.json' /tmp/radio-deploy-web/ $TargetPath/web/ && sudo chown -R ${TargetUser}:${TargetUser} $TargetPath && sudo chmod +x $TargetPath/api/Radio.API $TargetPath/web/Radio.Web && rm -rf /tmp/radio-deploy-api /tmp/radio-deploy-web"

# Captured immediately, not read later. Before OPS-7 this check sat AFTER the Production
# config block below, so by the time it ran, $LASTEXITCODE belonged to whichever native
# call that block made last — its `ssh ... test -f`, or its `ssh ... cp` when the seed
# branch was taken. Both $configDir values ship a seed file, so the block always ran and
# the move's own exit code was therefore always discarded.
#
# Not quite the same as "always masked": a move that failed early could leave
# $TargetPath/api absent, which made the seed's own `cp` fail, which tripped this check
# with a coincidentally correct message. The bug was that the check could not distinguish
# those cases and named a step it was not measuring.
$moveExit = $LASTEXITCODE
if ($moveExit -ne 0) {
  Write-Host "Remote file move failed!" -ForegroundColor Red
  exit 1
}

# Deploy target-specific Production config into each service directory that does not
# already have one.
#
# GUARDED PER DESTINATION, NOT ONCE FOR BOTH. Before OPS-7 a single `test -f` on api/
# gated a copy into BOTH api/ and web/, so a box with a web overlay and no api overlay had
# its web file OVERWRITTEN by the seed.
#
# What that costs: the web overlay is the RotaryPhone:Gv config's home, and the tracked
# seed has no RotaryPhone section at all — so the overwrite DELETES whatever is there
# rather than replacing it. On `radio` as measured 2026-09-02 that is
# RotaryPhone:Gv:MarkReadEnabled (INTEGRATIONS.md:994-997); the AuthKey the row was filed
# over is not yet set on any box, so the loss is of operator-authored state today and of
# the auth key once PHN-2's gate is turned on. Either way the file is not reconstructible
# from the repo.
#
# Sync-WpRule below is also per-destination, but do not read it as the model for this
# block's policy: its guard is compare-and-overwrite, which is the opposite decision. The
# shared idea is only that each destination is decided on its own.
#
# THE PROBE ASKS ONCE AND FAILS CLOSED. `ssh` reports its own transport errors as exit
# 255, which a per-destination `test -f` cannot tell apart from "file absent" (exit 1) —
# so on a WiFi-only box a dropped connection would read as "nothing there" and seed over a
# present overlay, which is the very thing this block exists to prevent. The remote script
# therefore always `exit 0`s and reports presence on stdout, leaving a non-zero exit to
# mean only "the question could not be asked" — in which case we abort rather than guess.
$targetConfigPath = Join-Path $RepoRoot "deploy\$configDir\appsettings.Production.json"
if (Test-Path $targetConfigPath) {
  $probe = ssh $SshTarget "for d in api web; do if [ -f $TargetPath/`$d/appsettings.Production.json ]; then echo `$d; fi; done; exit 0"
  if ($LASTEXITCODE -ne 0) {
    Write-Host "Could not determine which Production configs are present - aborting rather than risk overwriting one." -ForegroundColor Red
    exit 1
  }
  $present = @($probe | ForEach-Object { "$_".Trim() } | Where-Object { $_ })

  $seedStaged = $false
  foreach ($dest in @('api', 'web')) {
    if ($present -contains $dest) {
      Write-Host "    $dest/appsettings.Production.json present — left alone" -ForegroundColor DarkGray
      continue
    }

    if (-not $seedStaged) {
      scp $targetConfigPath "${SshTarget}:/tmp/appsettings.Production.json"
      if ($LASTEXITCODE -ne 0) {
        Write-Host "Production config upload failed!" -ForegroundColor Red
        exit 1
      }
      $seedStaged = $true
    }

    # Named per destination on purpose: the skip and the seed are separate decisions and
    # an operator needs to see which one each directory got. A single un-suffixed
    # "Deploying..." line cannot distinguish seeding api/ from seeding web/.
    Write-Host "    $dest/appsettings.Production.json absent — seeding from deploy/$configDir/" -ForegroundColor DarkGray
    ssh $SshTarget "sudo cp /tmp/appsettings.Production.json $TargetPath/$dest/ && sudo chown ${TargetUser}:${TargetUser} $TargetPath/$dest/appsettings.Production.json"
    if ($LASTEXITCODE -ne 0) {
      Write-Host "Production config seed into $dest/ failed!" -ForegroundColor Red
      exit 1
    }
  }

  if ($seedStaged) {
    ssh $SshTarget "rm -f /tmp/appsettings.Production.json"
  }
}

# Sync WirePlumber Lua rules.
#
# Two destinations, distinguished by source-filename prefix (we keep them
# flat in deploy/common/ so they live next to the existing config artifacts):
#   * 4x-*.lua            -> /etc/wireplumber/main.lua.d/
#                            (currently: stream-restore behaviour overrides)
#   * 8x-*.lua / 9x-*.lua -> /etc/wireplumber/bluetooth.lua.d/
#                            (bluez_monitor properties + bluez per-node rules)
#
# WirePlumber needs a SIGHUP-equivalent restart to reload Lua scripts. The
# user-mode unit is owned by the desktop user (mmack); we restart it as that
# user via XDG_RUNTIME_DIR. We do not run with `sudo --user mmack` because the
# script may already be running as mmack over SSH.
#
# Explicit file names (no `cp deploy/common/*.lua`) so dropping unrelated
# .lua artifacts into deploy/common/ never accidentally lands them in
# /etc/wireplumber. Mirror this list in deploy/debian-x64/setup.sh and
# deploy/common/radio-bt-setup.sh (verify_wp_configs).
$wpMainRules = @(
  "41-disable-bt-input-restore-target.lua"
)
# 85/87/89 are BT/audio-boundary-owned (adapter isolation + HFP-HF handoff to
# RotaryPhone + A2DP auto-connect). They were box-only until the IAC audit; keep
# them synced here so a deploy never leaves the box without them. Mirror this
# list in deploy/debian-x64/setup.sh and radio-bt-setup.sh (verify_wp_configs).
$wpBluetoothRules = @(
  "85-disable-hfp-hf.lua",
  "87-bt-adapter-select.lua",
  "89-bt-autoconnect.lua",
  "90-disable-bt-input-autolink.lua"
)

$wpRulesChanged = $false
function Sync-WpRule($name, $remoteDir) {
  $localPath = Join-Path $RepoRoot "deploy\common\$name"
  if (-not (Test-Path $localPath)) {
    Write-Host "  WARNING: missing $localPath (skipped)" -ForegroundColor Yellow
    return $false
  }
  $remoteTmp = "/tmp/wp-rule-$name"
  scp -q $localPath "${SshTarget}:${remoteTmp}" 2>&1 | Out-Null
  if ($LASTEXITCODE -ne 0) {
    Write-Host "  WARNING: scp $name failed" -ForegroundColor Yellow
    return $false
  }
  # Compare-and-install only when content changes, so we know whether to
  # restart wireplumber. cmp returns 0 = equal, 1 = different, 2 = missing.
  $cmpScript = "sudo mkdir -p $remoteDir && if cmp -s $remoteTmp $remoteDir/$name 2>/dev/null; then echo 'unchanged'; rm -f $remoteTmp; else sudo install -m 0644 -o root -g root $remoteTmp $remoteDir/$name && rm -f $remoteTmp && echo 'changed'; fi"
  $result = ssh $SshTarget $cmpScript
  if ($LASTEXITCODE -ne 0) {
    Write-Host "  WARNING: install $name failed" -ForegroundColor Yellow
    return $false
  }
  # Exact-match the sentinel. NOT `-match 'changed'` — that is a regex/substring
  # test, and "unchanged" CONTAINS "changed", so every UNCHANGED rule was reported
  # as changed → a needless `systemctl --user restart wireplumber` (which cycles
  # BT/audio) fired on every single deploy. Trim() guards a trailing newline from ssh.
  if ("$result".Trim() -eq 'changed') {
    Write-Host "    + $remoteDir/$name" -ForegroundColor DarkGray
    return $true
  }
  return $false
}

Write-Host "  Syncing WirePlumber rules..." -ForegroundColor DarkGray
foreach ($r in $wpMainRules) {
  if (Sync-WpRule -name $r -remoteDir "/etc/wireplumber/main.lua.d") { $wpRulesChanged = $true }
}
foreach ($r in $wpBluetoothRules) {
  if (Sync-WpRule -name $r -remoteDir "/etc/wireplumber/bluetooth.lua.d") { $wpRulesChanged = $true }
}

if ($wpRulesChanged) {
  Write-Host "  WirePlumber rules changed — restarting wireplumber..." -ForegroundColor DarkGray
  # User-mode systemd: must run as the desktop user, with XDG_RUNTIME_DIR so
  # systemctl --user can reach the right session bus.
  ssh $SshTarget "XDG_RUNTIME_DIR=/run/user/1000 systemctl --user restart wireplumber 2>&1 || true"
  # restart-stream module re-reads its state on next stream connect, no extra
  # action needed. Existing saved targets only re-apply on stream re-creation
  # (i.e. next BT reconnect) — the operator clears `~/.local/state/wireplumber/restore-stream`
  # to take effect for already-saved BT routings (see plan Task 2).
} else {
  Write-Host "    WirePlumber rules up to date" -ForegroundColor DarkGray
}

Write-Host "  Files synced" -ForegroundColor Green

# --- Step 4: Restart ---
if (-not $NoRestart) {
  Write-Host "[4/4] Starting services..." -ForegroundColor Yellow
  # Start radio-api first, then poll until its visualization hub is reachable, then
  # start radio-web. systemctl returns when the process is launched (not when its
  # listener is bound) so the previous "api && web" parallel-launch always lost the
  # race on slow boxes: radio-web's SignalR client tried to negotiate before radio-api
  # had opened port 5000, the initial StartAsync threw, and the visualization hub stayed
  # dead for the lifetime of the radio-web process (pre-Fix A behavior). The poll below
  # — ~10s max — eliminates the race regardless of hub-service code resilience.
  ssh $SshTarget "sudo systemctl daemon-reload && sudo systemctl start radio-api && for i in `$(seq 1 20); do curl -sf -X POST http://localhost:5000/hubs/visualization/negotiate?negotiateVersion=1 >/dev/null 2>&1 && break || sleep 0.5; done && sudo systemctl start radio-web"
  Start-Sleep -Seconds 2

  $apiStatus = ssh $SshTarget "systemctl is-active radio-api 2>/dev/null"
  $webStatus = ssh $SshTarget "systemctl is-active radio-web 2>/dev/null"

  if ($apiStatus -eq "active" -and $webStatus -eq "active") {
    # Verify the running API reports the SHA we just built. Poll because the
    # service takes a few seconds to bind its HTTP listener after start.
    if ($ExpectedSha -ne "unknown") {
      Write-Host "  Verifying deployed commit via /api/health/version..." -ForegroundColor DarkGray
      $verifyUrl = "http://${TargetHost}:${ApiPort}/api/health/version"
      $deployedSha = $null
      for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
          $resp = Invoke-RestMethod -Uri $verifyUrl -TimeoutSec 3 -ErrorAction Stop
          if ($resp -and $resp.gitSha) {
            $deployedSha = $resp.gitSha
            break
          }
        } catch {
          # Service not ready yet; retry
        }
        Start-Sleep -Seconds 2
      }

      if (-not $deployedSha) {
        Write-Host ""
        Write-Host "=== DEPLOY VERIFICATION FAILED ===" -ForegroundColor Red
        Write-Host "  Could not reach $verifyUrl after 10 attempts."
        Write-Host "  Check: ssh $SshTarget 'journalctl -u radio-api -n 50'"
        exit 1
      } elseif ($deployedSha -ne $ExpectedSha) {
        Write-Host ""
        Write-Host "=== DEPLOY VERIFICATION FAILED ===" -ForegroundColor Red
        Write-Host "  Expected commit: $ExpectedSha"
        Write-Host "  Running commit:  $deployedSha"
        Write-Host "  The deployed binary does not match the local HEAD."
        exit 1
      } else {
        Write-Host "  Verified: API is running commit $($deployedSha.Substring(0, 7))" -ForegroundColor Green
      }

      # Same check for radio-web. Until this existed the web half of a deploy was verified only
      # by `systemctl is-active`, which is true of a STALE binary just as much as a fresh one —
      # so a web fix that silently failed to land would be debugged as a code bug. OPS-1.
      Write-Host "  Verifying deployed commit via web /api/health/version..." -ForegroundColor DarkGray
      $webVerifyUrl = "http://${TargetHost}:${WebPort}/api/health/version"
      $deployedWebSha = $null
      for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
          $webResp = Invoke-RestMethod -Uri $webVerifyUrl -TimeoutSec 3 -ErrorAction Stop
          if ($webResp -and $webResp.gitSha) {
            $deployedWebSha = $webResp.gitSha
            break
          }
        } catch {
          # Service not ready yet; retry
        }
        Start-Sleep -Seconds 2
      }

      if (-not $deployedWebSha) {
        Write-Host ""
        Write-Host "=== DEPLOY VERIFICATION FAILED ===" -ForegroundColor Red
        Write-Host "  Could not reach $webVerifyUrl after 10 attempts."
        Write-Host "  Check: ssh $SshTarget 'journalctl -u radio-web -n 50'"
        exit 1
      } elseif ($deployedWebSha -ne $ExpectedSha) {
        Write-Host ""
        Write-Host "=== DEPLOY VERIFICATION FAILED ===" -ForegroundColor Red
        Write-Host "  Expected commit: $ExpectedSha"
        Write-Host "  Running commit:  $deployedWebSha  (radio-web)"
        Write-Host "  The deployed web binary does not match the local HEAD."
        Write-Host '  This is the exact failure that systemctl is-active could not see.'
        exit 1
      } else {
        Write-Host "  Verified: Web is running commit $($deployedWebSha.Substring(0, 7))" -ForegroundColor Green
      }
    }

    # Relaunch the kiosk browser.
    #
    # This used to be `DISPLAY=:0 nohup google-chrome ...`, and the comment that stood here
    # named it a known defect rather than fixing it: DISPLAY=:0 assumes X11, but the box runs
    # Wayland (loginctl session 1, seat0, Type=wayland), so the relaunch landed under XWayland
    # with a flag set that did not match the boot path — and in practice left the panel dead
    # after every deploy. `systemd-run --user` (inside radio-kiosk-launch) starts the browser
    # from the graphical session's OWN service manager, so it inherits that session's
    # WAYLAND_DISPLAY / DBUS_SESSION_BUS_ADDRESS / XDG_RUNTIME_DIR instead of an SSH shell's.
    # Verified by hand on the box 2026-08-18.
    #
    # The flag set is no longer duplicated here — radio-kiosk-launch owns it, and the autostart
    # entry calls the same script, so boot and deploy can no longer drift apart.
    # --password-store=basic lives there too and is still REQUIRED, not cosmetic: without it
    # Chrome asks gnome-keyring for the login keyring, which GDM auto-login never unlocks, and
    # gnome-shell raises a modal "Authentication required" prompt that grabs input and sits on
    # top of the kiosk. On 2026-08-02 that blocked the panel for ~33 hours and Chrome never even
    # reached navigation. See docs/uat/2026-08-03-osk-wayland-viability/.
    Write-Host "  Relaunching kiosk browser..." -ForegroundColor DarkGray
    ssh $SshTarget "if [ -x /usr/local/bin/radio-kiosk-launch ]; then /usr/local/bin/radio-kiosk-launch; else echo 'WARNING: /usr/local/bin/radio-kiosk-launch is missing - run deploy/debian-x64/kiosk/setup-kiosk.sh on this box'; fi"

    # Liveness, not process existence. During the 2026-08-02 outage Chrome was running and
    # radio-web returned 200 for ~33 hours while the panel showed an auth dialog and made ZERO
    # connections to :5002. Established connections are the check that would have caught it.
    #
    # This counts established TCP sockets whose source OR destination port is 5002, so a single
    # browser session shows up as more than one line — both ends of the socket are on this box.
    # The number is a liveness signal, not a tab count; only "at least one" is meaningful.
    #
    # The port test is ss's own filter, not `grep ':5002'`. That substring also matches the
    # ephemeral ports 50020-50029, which sit inside this box's ip_local_port_range (32768-60999)
    # — so an unrelated socket could have reported a dead kiosk as live, defeating the one check
    # this block exists to make. `-H` drops the header row so `grep -c .` counts sockets only.
    Write-Host "  Verifying the kiosk reached the UI..." -ForegroundColor DarkGray
    $kioskConns = 0
    for ($i = 0; $i -lt 10; $i++) {
      Start-Sleep -Seconds 2
      $raw = ssh $SshTarget "ss -Htn state established '( sport = :5002 or dport = :5002 )' | grep -c . || true"
      $parsed = 0
      if ([int]::TryParse((($raw | Select-Object -Last 1) -as [string]).Trim(), [ref]$parsed)) {
        $kioskConns = $parsed
      }
      if ($kioskConns -ge 1) { break }
    }
    # Corroborating evidence, not the gate. The connection count alone cannot distinguish a kiosk
    # THIS deploy relaunched from one that was never stopped — which is exactly what a missing
    # radio-kiosk-exit leaves behind. `radio-kiosk.service` is a transient unit that only exists
    # because radio-kiosk-launch created it, so `active` is positive proof of the new launch path.
    $rawUnit = ssh $SshTarget "systemctl --user is-active radio-kiosk.service 2>/dev/null || echo unknown"
    $kioskUnit = "$($rawUnit | Select-Object -Last 1)".Trim()

    if ($kioskConns -ge 1) {
      Write-Host "  Kiosk is live ($kioskConns established connections to :5002, radio-kiosk.service=$kioskUnit)" -ForegroundColor Green
    } else {
      # Deliberately a warning, not exit 1: the binaries deployed and verified successfully.
      # What failed is the browser relaunch, and saying so loudly is the whole point — the old
      # code said nothing at all and the owner found a dead screen.
      Write-Host "  WARNING: 0 established connections to :5002 - the kiosk did not reach the UI." -ForegroundColor Red
      Write-Host "    Check: ssh $SshTarget 'systemctl --user status radio-kiosk.service'"
      Write-Host "    Retry: ssh $SshTarget '/usr/local/bin/radio-kiosk-launch'"
    }

    Write-Host ""
    Write-Host "=== Deploy successful ===" -ForegroundColor Green
    Write-Host "API: http://${TargetHost}:${ApiPort}"
    Write-Host "Web: http://${TargetHost}:${WebPort}"
  } else {
    Write-Host ""
    Write-Host "=== WARNING: One or more services may have failed ===" -ForegroundColor Red
    Write-Host "  radio-api: $apiStatus"
    Write-Host "  radio-web: $webStatus"
    Write-Host "Check: ssh $SshTarget 'journalctl -u radio-api -u radio-web -n 20'"
    exit 1
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
