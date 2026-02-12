#!/bin/bash
# deploy-to-pi.sh — One-command build + deploy to Raspberry Pi
#
# Usage:
#   ./deploy/deploy-to-pi.sh              # Build, deploy, restart
#   ./deploy/deploy-to-pi.sh --no-restart # Build and deploy only
#   ./deploy/deploy-to-pi.sh --logs       # Build, deploy, restart, tail logs
#   ./deploy/deploy-to-pi.sh --quick      # Framework-dependent (smaller, faster, needs runtime on Pi)
#
# Environment variables:
#   PI_HOST  — Pi IP address or hostname (default: raspberry-pi.local)
#   PI_USER  — SSH user (default: pi)
#   PI_PATH  — Install path on Pi (default: /opt/radio-console)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

PI_HOST="${PI_HOST:-raspberry-pi.local}"
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
      echo "  --no-restart  Deploy without restarting the service"
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
PUBLISH_DIR="$REPO_ROOT/publish/linux-arm64"

echo "=== Radio Console Deploy ==="
echo "Target: $SSH_TARGET:$PI_PATH"
echo ""

# Step 1: Build
echo "[1/4] Building for linux-arm64..."
if [ "$QUICK" = true ]; then
  dotnet publish "$REPO_ROOT/src/Radio.API/Radio.API.csproj" \
    --configuration Release \
    --runtime linux-arm64 \
    --no-self-contained \
    --output "$PUBLISH_DIR" \
    -p:PublishSingleFile=false \
    -v quiet
  echo "  (framework-dependent — .NET runtime required on Pi)"
else
  dotnet publish "$REPO_ROOT/src/Radio.API/Radio.API.csproj" \
    --configuration Release \
    --runtime linux-arm64 \
    --self-contained true \
    --output "$PUBLISH_DIR" \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -v quiet
fi
echo "  Build complete ($(du -sh "$PUBLISH_DIR" | cut -f1))"

# Step 2: Stop service
if [ "$RESTART" = true ]; then
  echo "[2/4] Stopping service on Pi..."
  ssh "$SSH_TARGET" "sudo systemctl stop radio-console 2>/dev/null || true"
else
  echo "[2/4] Skipping service stop (--no-restart)"
fi

# Step 3: Sync files
echo "[3/4] Syncing files to Pi..."
rsync -avz --delete \
  --exclude='data' \
  --exclude='logs' \
  --exclude='appsettings.Production.json' \
  "$PUBLISH_DIR/" "$SSH_TARGET:/tmp/radio-deploy/"

ssh "$SSH_TARGET" "
  sudo rsync -a /tmp/radio-deploy/ $PI_PATH/ &&
  sudo chown -R radio:radio $PI_PATH &&
  sudo chmod +x $PI_PATH/Radio.API &&
  rm -rf /tmp/radio-deploy
"

CHANGED=$(rsync -avz --dry-run --delete \
  --exclude='data' --exclude='logs' --exclude='appsettings.Production.json' \
  "$PUBLISH_DIR/" "$SSH_TARGET:$PI_PATH/" 2>/dev/null | grep -c '^[^.]' || true)
echo "  Files synced"

# Step 4: Restart
if [ "$RESTART" = true ]; then
  echo "[4/4] Starting service..."
  ssh "$SSH_TARGET" "sudo systemctl start radio-console"
  sleep 1
  # Quick health check
  if ssh "$SSH_TARGET" "systemctl is-active --quiet radio-console"; then
    echo ""
    echo "=== Deploy successful ==="
    echo "UI: http://$PI_HOST:5000"
  else
    echo ""
    echo "=== WARNING: Service may have failed to start ==="
    echo "Check: ssh $SSH_TARGET 'journalctl -u radio-console -n 20'"
  fi
else
  echo "[4/4] Skipping restart (--no-restart)"
  echo ""
  echo "=== Deploy complete (service not restarted) ==="
  echo "Start manually: ssh $SSH_TARGET 'sudo systemctl start radio-console'"
fi

# Optional: tail logs
if [ "$TAIL_LOGS" = true ]; then
  echo ""
  echo "--- Tailing logs (Ctrl+C to stop) ---"
  ssh "$SSH_TARGET" "journalctl -u radio-console -f"
fi
