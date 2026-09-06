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
# THE PROBE ASKS ONCE AND FAILS CLOSED. `ssh` reports its own transport errors as exit
# 255, which a per-destination `test -f` cannot tell apart from "file absent" (exit 1) —
# so a dropped connection would read as "nothing there" and seed over a present overlay.
# The remote script always `exit 0`s and reports presence on stdout, leaving a non-zero
# `ssh` exit to mean only "the question could not be asked", which we abort on.
SEED_CONFIG="$REPO_ROOT/deploy/raspberry-pi/appsettings.Production.json"
if [ -f "$SEED_CONFIG" ]; then
  # Measured under `bash 5.2` with `set -euo pipefail`: a failing command substitution in
  # a plain assignment DOES abort the script. It aborts silently, though, carrying only
  # ssh's exit code — so the failure is handled explicitly to say why we stopped.
  PRESENT_CONFIGS="$(ssh "$SSH_TARGET" \
    "for d in api web; do [ -f $PI_PATH/\$d/appsettings.Production.json ] && echo \$d; done; exit 0")" || {
    echo "Could not determine which Production configs are present — aborting rather than risk overwriting one." >&2
    exit 1
  }

  SEED_STAGED=false
  for dest in api web; do
    if printf '%s\n' "$PRESENT_CONFIGS" | grep -qx "$dest"; then
      echo "  $dest/appsettings.Production.json present — left alone"
      continue
    fi

    if [ "$SEED_STAGED" = false ]; then
      scp "$SEED_CONFIG" "$SSH_TARGET:/tmp/appsettings.Production.json"
      SEED_STAGED=true
    fi

    # Named per destination: the skip and the seed are separate decisions, and a single
    # un-suffixed line cannot distinguish seeding api/ from seeding web/.
    echo "  $dest/appsettings.Production.json absent — seeding from deploy/raspberry-pi/"
    ssh "$SSH_TARGET" "sudo cp /tmp/appsettings.Production.json $PI_PATH/$dest/ && sudo chown radio:radio $PI_PATH/$dest/appsettings.Production.json"
  done

  if [ "$SEED_STAGED" = true ]; then
    ssh "$SSH_TARGET" "rm -f /tmp/appsettings.Production.json"
  fi
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
