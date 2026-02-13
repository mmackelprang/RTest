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

echo "=== Radio Console Deploy ==="
echo "Target: $SSH_TARGET:$PI_PATH"
echo ""

# Step 1: Build both projects
echo "[1/4] Building for linux-arm64..."

COMMON_ARGS="--configuration Release --runtime linux-arm64 -f net8.0"

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
  sudo rsync -a --delete /tmp/radio-deploy-api/ $PI_PATH/api/ &&
  sudo rsync -a --delete /tmp/radio-deploy-web/ $PI_PATH/web/ &&
  sudo chown -R radio:radio $PI_PATH &&
  sudo chmod +x $PI_PATH/api/Radio.API $PI_PATH/web/Radio.Web &&
  rm -rf /tmp/radio-deploy-api /tmp/radio-deploy-web
"
echo "  Files synced"

# Step 4: Restart
if [ "$RESTART" = true ]; then
  echo "[4/4] Starting services..."
  ssh "$SSH_TARGET" "sudo systemctl daemon-reload && sudo systemctl start radio-api && sudo systemctl start radio-web"
  sleep 2

  API_STATUS=$(ssh "$SSH_TARGET" "systemctl is-active radio-api 2>/dev/null" || true)
  WEB_STATUS=$(ssh "$SSH_TARGET" "systemctl is-active radio-web 2>/dev/null" || true)

  if [ "$API_STATUS" = "active" ] && [ "$WEB_STATUS" = "active" ]; then
    echo ""
    echo "=== Deploy successful ==="
    echo "API: http://$PI_HOST:5000"
    echo "Web: http://$PI_HOST:5002"
  else
    echo ""
    echo "=== WARNING: One or more services may have failed ==="
    echo "  radio-api: $API_STATUS"
    echo "  radio-web: $WEB_STATUS"
    echo "Check: ssh $SSH_TARGET 'journalctl -u radio-api -u radio-web -n 20'"
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
