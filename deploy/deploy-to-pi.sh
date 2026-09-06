#!/bin/bash
# deploy-to-pi.sh — One-command build + deploy to Raspberry Pi
#
# Builds both Radio.API and Radio.Web, syncs to the Pi, and restarts both services.
#
# Usage:
#   ./deploy/deploy-to-pi.sh              # Build, deploy, restart
#   ./deploy/deploy-to-pi.sh --no-restart # Build and deploy only
#   ./deploy/deploy-to-pi.sh --logs       # Build, deploy, restart, tail logs
#   ./deploy/deploy-to-pi.sh --quick      # Framework-dependent (smaller, faster, needs runtime on Pi)
#
# Environment variables:
#   PI_HOST  — Pi IP address or hostname (default: piradio)
#   PI_USER  — SSH user (default: pi)
#   PI_PATH  — Install path on Pi (default: /opt/radio-console)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

PI_HOST="${PI_HOST:-piradio}"
PI_USER="${PI_USER:-pi}"
PI_PATH="${PI_PATH:-/opt/radio-console}"

RESTART=true
TAIL_LOGS=false
QUICK=false

for arg in "$@"; do
  case "$arg" in
    --no-restart) RESTART=false ;;
    --logs) TAIL_LOGS=true ;;
    --quick) QUICK=true ;;
    --help|-h)
      echo "Usage: $0 [--no-restart] [--logs] [--quick]"
      echo ""
      echo "  --no-restart  Deploy without restarting the services"
      echo "  --logs        Tail journalctl after restart"
      echo "  --quick       Framework-dependent publish (smaller, needs .NET runtime on Pi)"
      echo ""
      echo "Environment:"
      echo "  PI_HOST=$PI_HOST"
      echo "  PI_USER=$PI_USER"
      echo "  PI_PATH=$PI_PATH"
      exit 0
      ;;
  esac
done

SSH_TARGET="$PI_USER@$PI_HOST"
API_PUBLISH_DIR="$REPO_ROOT/publish/linux-arm64/api"
WEB_PUBLISH_DIR="$REPO_ROOT/publish/linux-arm64/web"
API_PORT="${RADIO_API_PORT:-5000}"

# Capture the local git SHA so we can (a) bake it into the assembly via
# -p:SourceRevisionId and (b) verify the deployed binary reports the same SHA
# from /api/health/version after restart.
EXPECTED_SHA="$(git -C "$REPO_ROOT" rev-parse HEAD 2>/dev/null || echo unknown)"
if [ "$EXPECTED_SHA" = "unknown" ]; then
  echo "WARNING: could not read git HEAD; deploy verification will be skipped" >&2
fi

echo "=== Radio Console Deploy ==="
echo "Target: $SSH_TARGET:$PI_PATH"
echo "Commit: $EXPECTED_SHA"
echo ""

# Step 1: Build both projects
echo "[1/4] Building for linux-arm64..."

COMMON_ARGS="--configuration Release --runtime linux-arm64 -f net10.0 -p:SourceRevisionId=$EXPECTED_SHA"

if [ "$QUICK" = true ]; then
  COMMON_ARGS="$COMMON_ARGS --no-self-contained"
  echo "  (framework-dependent — .NET runtime required on Pi)"
else
  COMMON_ARGS="$COMMON_ARGS --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true"
fi

echo "  Publishing Radio.API..."
dotnet publish "$REPO_ROOT/src/Radio.API/Radio.API.csproj" \
  $COMMON_ARGS \
  --output "$API_PUBLISH_DIR" \
  -v quiet

echo "  Publishing Radio.Web..."
dotnet publish "$REPO_ROOT/src/Radio.Web/Radio.Web.csproj" \
  $COMMON_ARGS \
  --output "$WEB_PUBLISH_DIR" \
  -v quiet

echo "  Build complete (API: $(du -sh "$API_PUBLISH_DIR" | cut -f1), Web: $(du -sh "$WEB_PUBLISH_DIR" | cut -f1))"

# Step 2: Stop services
if [ "$RESTART" = true ]; then
  echo "[2/4] Stopping services on Pi..."
  ssh "$SSH_TARGET" "sudo systemctl stop radio-web 2>/dev/null; sudo systemctl stop radio-api 2>/dev/null; true"
else
  echo "[2/4] Skipping service stop (--no-restart)"
fi

# Step 3: Sync files
echo "[3/4] Syncing files to Pi..."

echo "  Syncing API..."
rsync -avz --delete "$API_PUBLISH_DIR/" "$SSH_TARGET:/tmp/radio-deploy-api/"

echo "  Syncing Web..."
rsync -avz --delete "$WEB_PUBLISH_DIR/" "$SSH_TARGET:/tmp/radio-deploy-web/"

ssh "$SSH_TARGET" "
  sudo mkdir -p $PI_PATH/api $PI_PATH/web $PI_PATH/data $PI_PATH/logs &&
  sudo rsync -a --delete --exclude='appsettings.Production.json' /tmp/radio-deploy-api/ $PI_PATH/api/ &&
  sudo rsync -a --delete --exclude='appsettings.Production.json' /tmp/radio-deploy-web/ $PI_PATH/web/ &&
  sudo chown -R radio:radio $PI_PATH &&
  sudo chmod +x $PI_PATH/api/Radio.API $PI_PATH/web/Radio.Web &&
  rm -rf /tmp/radio-deploy-api /tmp/radio-deploy-web
"
echo "  Files synced"

# Seed the Production config into each service directory that does not already have one.
# The two --exclude flags above stop rsync touching an overlay that exists; this block is
# what puts the file there on a box that has none.
#
# DECIDED PER DESTINATION. A single presence test on api/ gating a copy into both
# directories would seed over a web overlay on a box that has one and no api overlay.
#
# THE PROBE DEMANDS A POSITIVE VERDICT AND WILL NOT GUESS. The remote emits exactly one of
# `PRESENT:<dest>` or `ABSENT:<dest>` per destination, and a seed happens only on a matched
# `ABSENT:`. That closes the four ways the earlier form — presence is a line, absence is
# that line's omission — read "absent" from output that meant nothing of the sort:
#   * an `ssh` transport error (exit 255, which a per-destination `test -f` cannot tell
#     apart from "file absent", exit 1) — a dropped connection seeded over both overlays;
#   * a remote login profile writing an unterminated banner to stdout, gluing itself to the
#     first verdict ("Welcome to piradioapi") so that line no longer matched;
#   * CRLF on the wire, which made every line miss an anchored match;
#   * a remote-side shell error that empties stdout while the trailing `exit 0` masks it —
#     reproduced with a PI_PATH containing a space, where the unquoted `[` failed.
# The first now aborts on ssh's exit code; the rest yield neither marker for a destination,
# which is "the answer was not intelligible" and aborts too. CRLF alone is a well-formed
# answer in another dialect, so it is stripped and understood rather than rejected.
#
# WHAT IT STILL CANNOT CATCH: a remote that reads a directory successfully and answers
# honestly about the wrong one — a typo'd PI_PATH, or a PI_USER whose sudo lands somewhere
# other than the operator's box. `ABSENT:api` is then a true statement about a directory
# nobody cares about, and the seed goes there. Nothing in this block can distinguish that
# from the real thing; only the target being right makes the answer mean anything.
#
# Deploy-ToLinux.ps1:316,321,325 still uses the omission form, so this is deliberately
# stricter than the PowerShell twin rather than a port of it.
SEED_CONFIG="$REPO_ROOT/deploy/raspberry-pi/appsettings.Production.json"
if [ -f "$SEED_CONFIG" ]; then
  SEED_STAGED=false

  # By the time this block runs, Step 2 has stopped both services and Step 3 has replaced
  # both binaries — so every abort below leaves the radio off. One helper, so the recovery
  # instructions cannot drift between the four exits that need them.
  #
  # Cleanup lives here rather than in a `trap ... EXIT` because the trap would shell out to
  # the Pi on the one failure most likely to have killed the connection, and hang there.
  # Guarding on SEED_STAGED also means it only runs after an scp that actually succeeded.
  seed_abort() {
    echo "$1" >&2
    if [ "$SEED_STAGED" = true ]; then
      ssh "$SSH_TARGET" "rm -f /tmp/appsettings.Production.json" || true
    fi
    echo "Services are stopped and the binaries are already updated. Re-run the deploy, or start them manually: ssh $SSH_TARGET 'sudo systemctl start radio-api radio-web'" >&2
    exit 1
  }

  # Measured under bash 5.1.16 (WSL — the shape a Linux deploy actually runs in) and bash
  # 5.2.15 (Git-Bash), both with `set -euo pipefail`, both exit 42: a failing command
  # substitution in a plain assignment DOES abort the script. It aborts silently, though,
  # carrying only ssh's exit code — so the failure is handled explicitly to say why.
  #
  # The remote path is quoted inside the remote `[`, so a PI_PATH containing a space now
  # produces a correct verdict instead of "[: /opt/radio: binary operator expected" on
  # stderr and an empty stdout that read as "both overlays absent". That fixes the PROBE
  # only — the rsync command string at :113 and the seed `cp`/`chown` below both still
  # interpolate PI_PATH unquoted, so a spaced PI_PATH remains broken elsewhere here.
  PROBE_RAW="$(ssh "$SSH_TARGET" \
    "for d in api web; do if [ -f \"$PI_PATH/\$d/appsettings.Production.json\" ]; then echo \"PRESENT:\$d\"; else echo \"ABSENT:\$d\"; fi; done; exit 0")" || {
    seed_abort "Could not determine which Production configs are present — aborting rather than risk overwriting one."
  }
  PROBE="$(printf '%s' "$PROBE_RAW" | tr -d '\r')"

  for dest in api web; do
    if grep -qx "PRESENT:$dest" <<<"$PROBE"; then
      echo "  $dest/appsettings.Production.json present — left alone"
      continue
    fi

    if ! grep -qx "ABSENT:$dest" <<<"$PROBE"; then
      seed_abort "The Pi answered about $dest/ with neither PRESENT:$dest nor ABSENT:$dest — aborting rather than guess at presence. Probe said: $(tr '\n' '|' <<<"$PROBE")"
    fi

    # Named per destination: the skip and the seed are separate decisions, and a single
    # un-suffixed line cannot distinguish seeding api/ from seeding web/.
    echo "  $dest/appsettings.Production.json absent — seeding from deploy/raspberry-pi/"

    if [ "$SEED_STAGED" = false ]; then
      scp "$SEED_CONFIG" "$SSH_TARGET:/tmp/appsettings.Production.json" \
        || seed_abort "Production config upload to $SSH_TARGET:/tmp failed — $dest/ was not seeded. A partial /tmp/appsettings.Production.json may be left on the box; the upload failing is usually the transport, so this does not try to reach back and remove it."
      SEED_STAGED=true
    fi

    ssh "$SSH_TARGET" "sudo cp /tmp/appsettings.Production.json $PI_PATH/$dest/ && sudo chown radio:radio $PI_PATH/$dest/appsettings.Production.json" \
      || seed_abort "Production config seed into $dest/ failed — the copy or the chown did not complete, so $PI_PATH/$dest/appsettings.Production.json may be missing or owned by root."
  done

  if [ "$SEED_STAGED" = true ]; then
    ssh "$SSH_TARGET" "rm -f /tmp/appsettings.Production.json"
  fi
else
  echo "WARNING: $SEED_CONFIG is missing — nothing was seeded." >&2
  echo "         The rsync --exclude flags above are still in force, so a box with no" >&2
  echo "         Production overlay now has none at all and both services fall back to" >&2
  echo "         appsettings.json defaults. Restore the file from the repo and re-deploy." >&2
fi

# Step 4: Restart
if [ "$RESTART" = true ]; then
  echo "[4/4] Starting services..."
  ssh "$SSH_TARGET" "sudo systemctl daemon-reload && sudo systemctl start radio-api && sudo systemctl start radio-web"
  sleep 2

  API_STATUS=$(ssh "$SSH_TARGET" "systemctl is-active radio-api 2>/dev/null" || true)
  WEB_STATUS=$(ssh "$SSH_TARGET" "systemctl is-active radio-web 2>/dev/null" || true)

  if [ "$API_STATUS" = "active" ] && [ "$WEB_STATUS" = "active" ]; then
    # Verify the running API reports the SHA we just built. Poll because the
    # service takes a few seconds to bind its HTTP listener after start.
    if [ "$EXPECTED_SHA" != "unknown" ]; then
      echo "  Verifying deployed commit via /api/health/version..."
      VERIFY_URL="http://$PI_HOST:$API_PORT/api/health/version"
      DEPLOYED_SHA=""
      for attempt in 1 2 3 4 5 6 7 8 9 10; do
        DEPLOYED_SHA=$(curl -sf --max-time 3 "$VERIFY_URL" 2>/dev/null \
          | sed -n 's/.*"gitSha"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
        if [ -n "$DEPLOYED_SHA" ]; then
          break
        fi
        sleep 2
      done

      if [ -z "$DEPLOYED_SHA" ]; then
        echo ""
        echo "=== DEPLOY VERIFICATION FAILED ==="
        echo "  Could not reach $VERIFY_URL after 10 attempts."
        echo "  Check: ssh $SSH_TARGET 'journalctl -u radio-api -n 50'"
        exit 1
      elif [ "$DEPLOYED_SHA" != "$EXPECTED_SHA" ]; then
        echo ""
        echo "=== DEPLOY VERIFICATION FAILED ==="
        echo "  Expected commit: $EXPECTED_SHA"
        echo "  Running commit:  $DEPLOYED_SHA"
        echo "  The deployed binary does not match the local HEAD."
        exit 1
      else
        echo "  Verified: API is running commit ${DEPLOYED_SHA:0:7}"
      fi
    fi

    echo ""
    echo "=== Deploy successful ==="
    echo "API: http://$PI_HOST:$API_PORT"
    echo "Web: http://$PI_HOST:5002"
  else
    echo ""
    echo "=== WARNING: One or more services may have failed ==="
    echo "  radio-api: $API_STATUS"
    echo "  radio-web: $WEB_STATUS"
    echo "Check: ssh $SSH_TARGET 'journalctl -u radio-api -u radio-web -n 20'"
    exit 1
  fi
else
  echo "[4/4] Skipping restart (--no-restart)"
  echo ""
  echo "=== Deploy complete (services not restarted) ==="
  echo "Start manually: ssh $SSH_TARGET 'sudo systemctl start radio-api radio-web'"
fi

# Optional: tail logs
if [ "$TAIL_LOGS" = true ]; then
  echo ""
  echo "--- Tailing logs (Ctrl+C to stop) ---"
  ssh "$SSH_TARGET" "journalctl -u radio-api -u radio-web -f"
fi
