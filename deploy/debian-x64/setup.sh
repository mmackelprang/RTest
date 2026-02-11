#!/bin/bash
# setup.sh — Debian/Ubuntu x64 setup for Radio Console
# Tested on: Debian 12 Bookworm, Ubuntu 22.04/24.04 (x64)
#
# Usage:
#   sudo ./setup.sh
#
# Prerequisites:
#   - Debian/Ubuntu x64 system
#   - Internet connection
#   - Audio output configured

set -euo pipefail

APP_DIR="/opt/radio-console"
APP_USER="radio"
DOTNET_VERSION="8.0"

echo "========================================="
echo "Radio Console — Debian x64 Setup"
echo "========================================="

# Must run as root
if [ "$(id -u)" -ne 0 ]; then
  echo "Error: This script must be run as root (sudo)."
  exit 1
fi

# ---- 1. System Dependencies ----
echo ""
echo "[1/7] Installing system dependencies..."
apt-get update
apt-get install -y \
  libasound2-dev \
  libmp3lame-dev \
  avahi-daemon \
  avahi-utils \
  bluez \
  pulseaudio \
  pulseaudio-module-bluetooth \
  libgdiplus \
  curl \
  wget \
  unzip

# ---- 2. .NET 8 Runtime ----
echo ""
echo "[2/7] Installing .NET $DOTNET_VERSION runtime..."
if ! command -v dotnet &>/dev/null; then
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel "$DOTNET_VERSION" --runtime aspnetcore --install-dir /usr/share/dotnet
  ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
else
  echo ".NET already installed: $(dotnet --version)"
fi

# ---- 3. Create Application User ----
echo ""
echo "[3/7] Creating application user..."
if ! id "$APP_USER" &>/dev/null; then
  useradd --system --shell /usr/sbin/nologin --home-dir "$APP_DIR" --groups audio,bluetooth,pulse-access "$APP_USER"
  echo "Created user: $APP_USER"
else
  echo "User $APP_USER already exists"
  usermod -aG audio,bluetooth,pulse-access "$APP_USER" 2>/dev/null || true
fi

# ---- 4. Create Application Directory ----
echo ""
echo "[4/7] Creating application directory..."
mkdir -p "$APP_DIR"
mkdir -p "$APP_DIR/data/config"
mkdir -p "$APP_DIR/data/metrics"
mkdir -p "$APP_DIR/data/fingerprints"
mkdir -p "$APP_DIR/data/secrets"
mkdir -p "$APP_DIR/data/albumart"
mkdir -p "$APP_DIR/data/backups"
mkdir -p "$APP_DIR/logs"
mkdir -p "$APP_DIR/tools/fpcalc"

chown -R "$APP_USER:$APP_USER" "$APP_DIR"

# ---- 5. Install fpcalc (Chromaprint) ----
echo ""
echo "[5/7] Installing fpcalc (Chromaprint)..."
if ! command -v fpcalc &>/dev/null && [ ! -f "$APP_DIR/tools/fpcalc/fpcalc" ]; then
  apt-get install -y libchromaprint-tools 2>/dev/null || true
  if command -v fpcalc &>/dev/null; then
    cp "$(which fpcalc)" "$APP_DIR/tools/fpcalc/fpcalc"
    echo "Installed fpcalc from system package"
  else
    echo "WARNING: fpcalc not available. Audio fingerprinting will be disabled."
    echo "Install manually: apt install libchromaprint-tools"
  fi
else
  echo "fpcalc already available"
fi

# ---- 6. Install systemd Service ----
echo ""
echo "[6/7] Installing systemd service..."
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SERVICE_SRC="$SCRIPT_DIR/../common/radio-console.service"

if [ -f "$SERVICE_SRC" ]; then
  cp "$SERVICE_SRC" /etc/systemd/system/radio-console.service
else
  cat > /etc/systemd/system/radio-console.service << 'EOF'
[Unit]
Description=Radio Console Application
After=network.target sound.target bluetooth.target avahi-daemon.service
Wants=avahi-daemon.service

[Service]
Type=notify
User=radio
Group=audio
WorkingDirectory=/opt/radio-console
ExecStart=/opt/radio-console/Radio.API
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=radio-console
TimeoutStopSec=30
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_EnableDiagnostics=0
SupplementaryGroups=bluetooth pulse-access
ProtectSystem=strict
ReadWritePaths=/opt/radio-console/data /opt/radio-console/logs
ProtectHome=true
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
EOF
fi

systemctl daemon-reload
systemctl enable radio-console.service
echo "Service installed and enabled"

# ---- 7. Configure Audio & Bluetooth ----
echo ""
echo "[7/7] Configuring audio and Bluetooth..."

# Enable Bluetooth service
systemctl enable bluetooth.service
systemctl start bluetooth.service 2>/dev/null || true

# Enable Avahi (mDNS for Cast device discovery)
systemctl enable avahi-daemon.service
systemctl start avahi-daemon.service 2>/dev/null || true

# ---- Done ----
echo ""
echo "========================================="
echo "Setup complete!"
echo "========================================="
echo ""
echo "Next steps:"
echo "  1. Copy your published application to $APP_DIR/"
echo "     scp -r publish/linux-x64/* user@<ip>:$APP_DIR/"
echo ""
echo "  2. Set permissions:"
echo "     sudo chown -R $APP_USER:$APP_USER $APP_DIR"
echo "     sudo chmod +x $APP_DIR/Radio.API"
echo ""
echo "  3. Start the service:"
echo "     sudo systemctl start radio-console"
echo ""
echo "  4. Check status:"
echo "     sudo systemctl status radio-console"
echo "     sudo journalctl -u radio-console -f"
echo ""
echo "  5. Access the UI at: http://<host-ip>:5000"
echo ""
