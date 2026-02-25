#!/bin/bash
# setup-kiosk.sh — Configure Ubuntu GNOME kiosk mode for Radio Console
#
# Usage:
#   ./setup-kiosk.sh [--user USERNAME]
#
# Installs desktop shortcuts, configures auto-login, disables screen blanking,
# and sets up autostart for the Radio Console Web UI in Chromium kiosk mode.

set -euo pipefail

KIOSK_USER="${1:-$(whoami)}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "========================================="
echo "Radio Console — Kiosk Mode Setup"
echo "========================================="
echo "User: $KIOSK_USER"
echo ""

# ---- 1. Install desktop shortcuts ----
echo "[1/5] Installing desktop shortcuts..."

APPS_DIR="$HOME/.local/share/applications"
DESKTOP_DIR="$HOME/Desktop"

mkdir -p "$APPS_DIR"
mkdir -p "$DESKTOP_DIR"

for file in radio-console.desktop radio-exit-browser.desktop radio-shutdown.desktop; do
  cp "$SCRIPT_DIR/$file" "$APPS_DIR/$file"
  cp "$SCRIPT_DIR/$file" "$DESKTOP_DIR/$file"
  chmod +x "$DESKTOP_DIR/$file"
  # Mark as trusted so GNOME doesn't show "untrusted" warning
  gio set "$DESKTOP_DIR/$file" metadata::trusted true 2>/dev/null || true
  echo "  Installed: $file"
done

echo "  Desktop shortcuts installed."

# ---- 2. Install autostart entry ----
echo ""
echo "[2/5] Installing autostart entry..."

AUTOSTART_DIR="$HOME/.config/autostart"
mkdir -p "$AUTOSTART_DIR"

cp "$SCRIPT_DIR/radio-kiosk-autostart.desktop" "$AUTOSTART_DIR/radio-kiosk-autostart.desktop"
echo "  Autostart entry installed to $AUTOSTART_DIR/"

# ---- 3. Configure GNOME auto-login ----
echo ""
echo "[3/5] Configuring GNOME auto-login..."

GDM_CONF="/etc/gdm3/custom.conf"
if [ -f "$GDM_CONF" ]; then
  if grep -q "^AutomaticLoginEnable" "$GDM_CONF"; then
    echo "  Auto-login already configured."
  else
    # Add auto-login under [daemon] section
    sudo sed -i "/^\[daemon\]/a AutomaticLoginEnable=true\nAutomaticLogin=$KIOSK_USER" "$GDM_CONF"
    echo "  Auto-login enabled for user: $KIOSK_USER"
  fi
else
  echo "  WARNING: $GDM_CONF not found. Auto-login must be configured manually."
fi

# ---- 4. Disable screen blanking and lock ----
echo ""
echo "[4/5] Disabling screen blanking and lock..."

gsettings set org.gnome.desktop.session idle-delay 0
gsettings set org.gnome.desktop.screensaver lock-enabled false
gsettings set org.gnome.desktop.screensaver idle-activation-enabled false
echo "  Screen blanking disabled."
echo "  Screen lock disabled."

# ---- 5. Install unclutter (hide idle mouse cursor) ----
echo ""
echo "[5/5] Installing unclutter..."

if ! command -v unclutter &>/dev/null; then
  sudo apt-get install -y unclutter
  echo "  unclutter installed."
else
  echo "  unclutter already installed."
fi

# Add unclutter to autostart if not already there
UNCLUTTER_AUTOSTART="$AUTOSTART_DIR/unclutter.desktop"
if [ ! -f "$UNCLUTTER_AUTOSTART" ]; then
  cat > "$UNCLUTTER_AUTOSTART" << 'EOF'
[Desktop Entry]
Name=Unclutter
Comment=Hide mouse cursor when idle
Exec=unclutter -idle 3
Terminal=false
Type=Application
X-GNOME-Autostart-enabled=true
EOF
  echo "  unclutter autostart entry created."
fi

# ---- Done ----
echo ""
echo "========================================="
echo "Kiosk setup complete!"
echo "========================================="
echo ""
echo "Installed:"
echo "  Desktop shortcuts: $DESKTOP_DIR/radio-*.desktop"
echo "  App menu entries:  $APPS_DIR/radio-*.desktop"
echo "  Autostart:         $AUTOSTART_DIR/radio-kiosk-autostart.desktop"
echo ""
echo "Next steps:"
echo "  1. Reboot to test auto-login + auto-launch"
echo "  2. Use 'Exit Browser' shortcut to close kiosk"
echo "  3. Use 'Shutdown System' shortcut to power off"
echo ""
