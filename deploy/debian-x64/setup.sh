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
  libasound2-plugins \
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
mkdir -p "$APP_DIR/api"
mkdir -p "$APP_DIR/web"
mkdir -p "$APP_DIR/data/config"
mkdir -p "$APP_DIR/data/metrics"
mkdir -p "$APP_DIR/data/fingerprints"
mkdir -p "$APP_DIR/data/secrets"
mkdir -p "$APP_DIR/data/albumart"
mkdir -p "$APP_DIR/data/backups"
mkdir -p "$APP_DIR/logs"
mkdir -p "$APP_DIR/tools/fpcalc"

chown -R "$APP_USER:$APP_USER" "$APP_DIR"

# Create ALSA config to bypass PulseAudio/PipeWire redirect.
# PipeWire runs as the login user and doesn't allow cross-user connections.
# The radio system user needs direct ALSA hardware access for audio output.
if [ ! -f "$APP_DIR/.asoundrc" ]; then
  cat > "$APP_DIR/.asoundrc" << 'ALSAEOF'
# Radio Console: direct ALSA hardware access (bypass PipeWire/PulseAudio)
pcm.!default {
    type hw
    card 0
}
ctl.!default {
    type hw
    card 0
}

# Bluetooth audio capture via PipeWire-Pulse TCP.
# Routes through ALSA pulse plugin to access bt_capture.monitor source.
pcm.bt_capture {
    type pulse
    server tcp:localhost:4713
    device bt_capture.monitor
    hint {
        show on
        description "Bluetooth Audio Capture (bt_capture)"
    }
}
ALSAEOF
  chown "$APP_USER:$APP_USER" "$APP_DIR/.asoundrc"
  echo "Created .asoundrc for direct ALSA + BT capture access"
fi

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

# ---- 6. Install systemd Services ----
echo ""
echo "[6/7] Installing systemd services..."
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# Migrate from old single-service if present
if systemctl is-enabled radio-console.service &>/dev/null; then
  echo "  Migrating from old radio-console.service..."
  systemctl stop radio-console.service 2>/dev/null || true
  systemctl disable radio-console.service 2>/dev/null || true
  rm -f /etc/systemd/system/radio-console.service
  echo "  Old radio-console.service removed"
fi

# Install radio-api.service
API_SERVICE_SRC="$SCRIPT_DIR/../common/radio-api.service"
if [ -f "$API_SERVICE_SRC" ]; then
  cp "$API_SERVICE_SRC" /etc/systemd/system/radio-api.service
else
  cat > /etc/systemd/system/radio-api.service << 'EOF'
[Unit]
Description=Radio Console API (audio engine, REST, SignalR)
After=network.target sound.target bluetooth.target avahi-daemon.service
Wants=avahi-daemon.service

[Service]
Type=notify
User=radio
Group=audio
WorkingDirectory=/opt/radio-console
ExecStart=/opt/radio-console/api/Radio.API
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=radio-api
TimeoutStopSec=30
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_EnableDiagnostics=0
Environment=DOTNET_BUNDLE_EXTRACT_BASE_DIR=/opt/radio-console/api/.bundle
Environment=ASPNETCORE_CONTENTROOT=/opt/radio-console/api
Environment=HOME=/opt/radio-console
SupplementaryGroups=bluetooth pulse-access
ProtectSystem=strict
ReadWritePaths=/opt/radio-console
PrivateTmp=true
ProtectHome=true
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
EOF
fi

# Install radio-web.service
WEB_SERVICE_SRC="$SCRIPT_DIR/../common/radio-web.service"
if [ -f "$WEB_SERVICE_SRC" ]; then
  cp "$WEB_SERVICE_SRC" /etc/systemd/system/radio-web.service
else
  cat > /etc/systemd/system/radio-web.service << 'EOF'
[Unit]
Description=Radio Console Web UI (Blazor Server)
After=network.target radio-api.service
Requires=radio-api.service

[Service]
Type=simple
User=radio
Group=radio
WorkingDirectory=/opt/radio-console
ExecStart=/opt/radio-console/web/Radio.Web
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=radio-web
TimeoutStopSec=30
Environment=ASPNETCORE_URLS=http://0.0.0.0:5002
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_EnableDiagnostics=0
Environment=DOTNET_BUNDLE_EXTRACT_BASE_DIR=/opt/radio-console/web/.bundle
Environment=ASPNETCORE_CONTENTROOT=/opt/radio-console/web
Environment=ApiBaseUrl=http://localhost:5000
ProtectSystem=strict
ReadWritePaths=/opt/radio-console/logs /opt/radio-console/web
PrivateTmp=true
ProtectHome=true
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
EOF
fi

systemctl daemon-reload
systemctl enable radio-api.service
systemctl enable radio-web.service
echo "Services installed and enabled (radio-api, radio-web)"

# ---- 7. Configure Audio & Bluetooth ----
echo ""
echo "[7/7] Configuring audio and Bluetooth..."

# Enable Bluetooth service with auto-power-on after reboot
systemctl enable bluetooth.service
systemctl start bluetooth.service 2>/dev/null || true
if ! grep -q '^AutoEnable=true' /etc/bluetooth/main.conf 2>/dev/null; then
  sed -i 's/^#AutoEnable=true/AutoEnable=true/' /etc/bluetooth/main.conf 2>/dev/null || true
  echo "Enabled AutoEnable in /etc/bluetooth/main.conf"
fi

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
echo "     scp -r publish/linux-x64/api/* user@<ip>:$APP_DIR/api/"
echo "     scp -r publish/linux-x64/web/* user@<ip>:$APP_DIR/web/"
echo ""
echo "  2. Set permissions:"
echo "     sudo chown -R $APP_USER:$APP_USER $APP_DIR"
echo "     sudo chmod +x $APP_DIR/api/Radio.API $APP_DIR/web/Radio.Web"
echo ""
echo "  3. Start the services:"
echo "     sudo systemctl start radio-api radio-web"
echo ""
echo "  4. Check status:"
echo "     sudo systemctl status radio-api radio-web"
echo "     sudo journalctl -u radio-api -u radio-web -f"
echo ""
echo "  5. Access:"
echo "     API: http://<host-ip>:5000"
echo "     Web: http://<host-ip>:5002"
echo ""
