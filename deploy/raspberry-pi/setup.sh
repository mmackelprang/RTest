#!/bin/bash
# setup.sh — Raspberry Pi (ARM64) setup for Radio Console
# Tested on: Raspberry Pi 5, Raspberry Pi OS Bookworm (64-bit)
#
# Usage:
#   sudo ./setup.sh
#
# Prerequisites:
#   - Raspberry Pi 5 with 64-bit OS
#   - Internet connection
#   - Audio output configured (HDMI, USB DAC, or 3.5mm jack)

set -euo pipefail

APP_DIR="/opt/radio-console"
APP_USER="radio"
DOTNET_VERSION="8.0"

echo "========================================="
echo "Radio Console — Raspberry Pi Setup"
echo "========================================="

# Must run as root
if [ "$(id -u)" -ne 0 ]; then
  echo "Error: This script must be run as root (sudo)."
  exit 1
fi

# ---- 1. System Dependencies ----
echo ""
echo "[1/9] Installing system dependencies..."
apt-get update
apt-get install -y \
  libasound2-dev \
  libmp3lame-dev \
  avahi-daemon \
  avahi-utils \
  bluez \
  pipewire \
  pipewire-pulse \
  wireplumber \
  libspa-0.2-bluetooth \
  libgdiplus \
  curl \
  wget \
  unzip

# ---- 2. RTL-SDR Support (optional) ----
echo ""
echo "[2/9] Installing RTL-SDR support..."
apt-get install -y librtlsdr-dev rtl-sdr 2>/dev/null || true

# Blacklist DVB kernel driver (conflicts with RTL-SDR userspace driver)
if [ ! -f /etc/modprobe.d/blacklist-rtl.conf ]; then
  echo "blacklist dvb_usb_rtl28xxu" > /etc/modprobe.d/blacklist-rtl.conf
  echo "Blacklisted DVB kernel driver for RTL-SDR"
  modprobe -r dvb_usb_rtl28xxu 2>/dev/null || true
fi

# ---- 3. .NET 8 Runtime ----
echo ""
echo "[3/9] Installing .NET $DOTNET_VERSION runtime..."
if ! command -v dotnet &>/dev/null; then
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel "$DOTNET_VERSION" --runtime aspnetcore --install-dir /usr/share/dotnet
  ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
else
  echo ".NET already installed: $(dotnet --version)"
fi

# ---- 4. Create Application User ----
echo ""
echo "[4/9] Creating application user..."
if ! id "$APP_USER" &>/dev/null; then
  useradd --system --shell /usr/sbin/nologin --home-dir "$APP_DIR" --groups audio,bluetooth "$APP_USER"
  echo "Created user: $APP_USER"
else
  echo "User $APP_USER already exists"
  usermod -aG audio,bluetooth "$APP_USER" 2>/dev/null || true
fi

# ---- 5. Create Application Directory ----
echo ""
echo "[5/9] Creating application directory..."
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

# ---- 6. Install fpcalc (Chromaprint) ----
echo ""
echo "[6/9] Installing fpcalc (Chromaprint) for audio fingerprinting..."
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

# ---- 7. Configure PipeWire & WirePlumber for Bluetooth A2DP Sink ----
echo ""
echo "[7/9] Configuring PipeWire and WirePlumber for Bluetooth audio..."

# Determine the login user who will run PipeWire (not the 'radio' system user).
# PipeWire runs as a user service under the logged-in desktop user.
LOGIN_USER="${SUDO_USER:-pi}"
LOGIN_HOME=$(eval echo "~$LOGIN_USER")

# PipeWire: Create virtual null sink for BT audio capture
PIPEWIRE_CONF_DIR="$LOGIN_HOME/.config/pipewire/pipewire.conf.d"
mkdir -p "$PIPEWIRE_CONF_DIR"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$SCRIPT_DIR/pipewire/bt-capture-sink.conf" ]; then
  cp "$SCRIPT_DIR/pipewire/bt-capture-sink.conf" "$PIPEWIRE_CONF_DIR/bt-capture-sink.conf"
else
  cat > "$PIPEWIRE_CONF_DIR/bt-capture-sink.conf" << 'PWEOF'
context.objects = [
    {
        factory = adapter
        args = {
            factory.name   = support.null-audio-sink
            node.name       = "bt_capture"
            node.description = "Bluetooth Audio Capture"
            media.class     = Audio/Sink
            object.linger   = true
            audio.position  = [ FL FR ]
            audio.rate      = 48000
            monitor.channel-volumes = true
            monitor.passthrough     = true
            priority.session = 0
            priority.driver  = 0
            node.passive     = true
        }
    }
]
PWEOF
fi

# WirePlumber: Enable A2DP sink role + route BT input to null sink
WP_CONF_DIR="$LOGIN_HOME/.config/wireplumber/wireplumber.conf.d"
mkdir -p "$WP_CONF_DIR"

if [ -f "$SCRIPT_DIR/wireplumber/50-bluez-a2dp-sink.conf" ]; then
  cp "$SCRIPT_DIR/wireplumber/50-bluez-a2dp-sink.conf" "$WP_CONF_DIR/50-bluez-a2dp-sink.conf"
else
  cat > "$WP_CONF_DIR/50-bluez-a2dp-sink.conf" << 'WPEOF'
wireplumber.profiles = {
    main = {
        monitor.bluez.seat-monitoring = disabled
    }
}

monitor.bluez.properties = {
    bluez5.roles = [ a2dp_sink, a2dp_source ]
    bluez5.codecs = [ sbc, sbc_xq ]
    bluez5.enable-sbc-xq = true
    bluez5.hfphsp-backend = "native"
}
WPEOF
fi

if [ -f "$SCRIPT_DIR/wireplumber/51-bt-capture-routing.conf" ]; then
  cp "$SCRIPT_DIR/wireplumber/51-bt-capture-routing.conf" "$WP_CONF_DIR/51-bt-capture-routing.conf"
else
  cat > "$WP_CONF_DIR/51-bt-capture-routing.conf" << 'WPEOF2'
monitor.bluez.rules = [
    {
        matches = [
            {
                node.name = "~bluez_input.*"
            }
        ]
        actions = {
            update-props = {
                node.target = "bt_capture"
            }
        }
    }
]
WPEOF2
fi

chown -R "$LOGIN_USER:$LOGIN_USER" "$LOGIN_HOME/.config/pipewire" "$LOGIN_HOME/.config/wireplumber"
echo "PipeWire/WirePlumber Bluetooth configs installed for user $LOGIN_USER"

# ---- 8. Install systemd Service ----
echo ""
echo "[8/9] Installing systemd service..."
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
SupplementaryGroups=bluetooth
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

# ---- 9. Configure System Services ----
echo ""
echo "[9/9] Enabling system services..."

# Enable Bluetooth service
systemctl enable bluetooth.service
systemctl start bluetooth.service 2>/dev/null || true

# Enable Avahi (mDNS for Cast device discovery)
systemctl enable avahi-daemon.service
systemctl start avahi-daemon.service 2>/dev/null || true

# Restart PipeWire/WirePlumber to pick up new configs
if [ -n "${LOGIN_USER:-}" ] && id "$LOGIN_USER" &>/dev/null; then
  LOGIN_UID=$(id -u "$LOGIN_USER")
  if [ -S "/run/user/$LOGIN_UID/bus" ]; then
    sudo -u "$LOGIN_USER" XDG_RUNTIME_DIR="/run/user/$LOGIN_UID" \
      DBUS_SESSION_BUS_ADDRESS="unix:path=/run/user/$LOGIN_UID/bus" \
      systemctl --user restart pipewire wireplumber 2>/dev/null || true
    echo "Restarted PipeWire/WirePlumber for user $LOGIN_USER"
  fi
fi

# ---- Done ----
echo ""
echo "========================================="
echo "Setup complete!"
echo "========================================="
echo ""
echo "Next steps:"
echo "  1. Copy your published application to $APP_DIR/"
echo "     scp -r publish/linux-arm64/* pi@<ip>:$APP_DIR/"
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
echo "  5. Access the UI at: http://<pi-ip>:5000"
echo ""
echo "Bluetooth A2DP sink is configured. Phones can stream audio to"
echo "\"$(hostname)\" after pairing via the Web UI or bluetoothctl."
echo ""
