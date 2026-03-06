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

# ---- 3. Switch services to run as login user ----
echo ""
echo "[3/7] Switching radio services to run as $KIOSK_USER..."

# On a kiosk/desktop system, the radio services need to run as the login user
# so they have access to PipeWire/PulseAudio audio (which runs per-user).
# The default 'radio' system user can't access the PipeWire socket.
for svc in radio-api radio-web; do
  SVC_FILE="/etc/systemd/system/$svc.service"
  if [ -f "$SVC_FILE" ]; then
    if grep -q "User=radio" "$SVC_FILE"; then
      sudo sed -i "s/User=radio/User=$KIOSK_USER/" "$SVC_FILE"
      sudo sed -i "s/Group=radio/Group=$KIOSK_USER/" "$SVC_FILE"
      sudo sed -i "s/Group=audio/Group=$KIOSK_USER/" "$SVC_FILE"
      # Update HOME for PipeWire socket access
      sudo sed -i "s|HOME=/opt/radio-console|HOME=/home/$KIOSK_USER|" "$SVC_FILE"
      echo "  $svc.service: switched to User=$KIOSK_USER"
    else
      echo "  $svc.service: already running as non-radio user"
    fi
  fi
done

sudo chown -R "$KIOSK_USER:$KIOSK_USER" /opt/radio-console
sudo systemctl daemon-reload
echo "  Services updated."

# ---- 4. Configure GNOME auto-login ----
echo ""
echo "[4/7] Configuring GNOME auto-login..."

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

# ---- 5. Disable screen blanking and lock ----
echo ""
echo "[5/7] Disabling screen blanking and lock..."

gsettings set org.gnome.desktop.session idle-delay 0
gsettings set org.gnome.desktop.screensaver lock-enabled false
gsettings set org.gnome.desktop.screensaver idle-activation-enabled false

# Disable X11 DPMS (Display Power Management Signaling).
# GNOME screensaver settings above don't cover DPMS, which is a separate X11/kernel
# feature that can blank/suspend the display independently.
xset s off 2>/dev/null || true
xset -dpms 2>/dev/null || true
xset s noblank 2>/dev/null || true
echo "  Screen blanking disabled."
echo "  Screen lock disabled."
echo "  X11 DPMS disabled."

# ---- 6. Install unclutter (hide idle mouse cursor) ----
echo ""
echo "[6/7] Installing unclutter..."

if ! command -v unclutter &>/dev/null; then
  sudo apt-get install -y unclutter
  echo "  unclutter installed."
else
  echo "  unclutter already installed."
fi

# Add DPMS disable to autostart (xset commands only apply to the current session,
# so they must run on every login)
DPMS_AUTOSTART="$AUTOSTART_DIR/disable-dpms.desktop"
if [ ! -f "$DPMS_AUTOSTART" ]; then
  cat > "$DPMS_AUTOSTART" << 'EOF'
[Desktop Entry]
Name=Disable DPMS
Comment=Disable display power management for kiosk mode
Exec=bash -c "xset s off; xset -dpms; xset s noblank"
Terminal=false
Type=Application
X-GNOME-Autostart-enabled=true
NoDisplay=true
EOF
  echo "  DPMS disable autostart entry created."
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

# ---- 7. Install browser refresh helper ----
echo ""
echo "[7/7] Installing browser refresh helper..."

REFRESH_SCRIPT="/usr/local/bin/radio-refresh-browser"
sudo tee "$REFRESH_SCRIPT" > /dev/null << 'EOF'
#!/bin/bash
# Refresh the Radio Console kiosk browser.
# Uses xdotool to send F5 to the Chrome window.
# Called after deploys or manually when needed.
export DISPLAY=:0
if command -v xdotool &>/dev/null; then
  WID=$(xdotool search --name "Radio Console" 2>/dev/null | head -1)
  if [ -n "$WID" ]; then
    xdotool key --window "$WID" F5
    echo "Browser refreshed (window $WID)"
  else
    # Try any Chrome window
    WID=$(xdotool search --class chrome 2>/dev/null | head -1)
    if [ -n "$WID" ]; then
      xdotool key --window "$WID" F5
      echo "Browser refreshed (window $WID)"
    else
      echo "No browser window found"
    fi
  fi
else
  echo "xdotool not installed — install with: sudo apt install xdotool"
fi
EOF
sudo chmod +x "$REFRESH_SCRIPT"

if ! command -v xdotool &>/dev/null; then
  sudo apt-get install -y xdotool
fi
echo "  Installed: $REFRESH_SCRIPT"
echo "  Usage: radio-refresh-browser (after deploy or code update)"

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
echo "  Browser refresh:   $REFRESH_SCRIPT"
echo ""
echo "Next steps:"
echo "  1. Reboot to test auto-login + auto-launch"
echo "  2. Use 'Exit Browser' shortcut to close kiosk"
echo "  3. Use 'Shutdown System' shortcut to power off"
echo "  4. After deploys, run: radio-refresh-browser"
echo ""
