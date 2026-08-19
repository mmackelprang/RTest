#!/bin/bash
# setup-kiosk.sh — Configure Ubuntu GNOME kiosk mode for Radio Console
#
# Usage:
#   ./setup-kiosk.sh [USERNAME]        # positional, NOT --user USERNAME
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

# ---- Shared paths and discovered values ----

APPS_DIR="$HOME/.local/share/applications"
DESKTOP_DIR="$HOME/Desktop"
BIN_DIR="/usr/local/bin"
ICON_DIR="$HOME/.local/share/icons/radio-console"

mkdir -p "$APPS_DIR" "$DESKTOP_DIR" "$ICON_DIR"

# The rotary-phone unit name is DISCOVERED, not assumed. A wrong name here would make the
# KIOSK-2 launcher report PHONE as a hard failure forever — which is exactly the cry-wolf
# failure the Google Voice rule exists to prevent. Defined in this row because install_entry()
# substitutes it and the script runs under `set -u`. Verified on the box 2026-08-18: the unit
# is `rotary-phone.service` (a separate `rotary-phone-cookies.service` is a oneshot cookie
# refresh, not the API — the ^ anchor plus `head -1` keeps it out).
# The trailing `|| true` is required, not belt-and-braces: this script runs under
# `set -euo pipefail`, and with pipefail a no-match `grep` fails the whole pipeline, which
# would abort setup-kiosk.sh at step 1/9 having installed nothing — and would make the
# `if [ -z ]` fallback directly below it unreachable dead code.
ROTARY_UNIT="${ROTARY_UNIT:-$(systemctl list-units --type=service --all --no-legend \
  | awk '{print $1}' | grep -iE '^(rotary-?phone|rotaryphone)\.service$' | head -1 || true)}"
if [ -z "$ROTARY_UNIT" ]; then
  echo "  WARNING: no rotary-phone service unit found; PHONE repair will be a no-op."
  ROTARY_UNIT="rotary-phone.service"
fi

# ---- 1/10. Install icon assets ----
echo "[1/10] Installing icon assets..."

# Both files are byte copies of branding/favicon.svg and branding/icon-512.png — the Anderson
# Console mark, which had never shipped anywhere until now.
#
# The 256px and 128px renders the plan listed are deliberately NOT shipped: rsvg-convert,
# inkscape and convert are all absent from the box (measured 2026-08-18), so nothing on either
# side could rasterise them and fabricating them was the only alternative. Little is lost by
# that — the SVG is the file the desktop actually uses. librsvg2-common is installed and
# gdk-pixbuf carries a working SVG loader (libpixbufloader-svg.so, measured the same day), so an
# absolute-path `.svg` in an `Icon=` line resolves and renders. The 512px PNG rides along as the
# raster fallback.
#
# $ICON_DIR is under $HOME, outside /opt/radio-console, so this survives deploys —
# Deploy-ToLinux.ps1 wipes api/ and web/ only.
install -m 644 "$SCRIPT_DIR/icons/radio-console.svg" "$ICON_DIR/radio-console.svg"
install -m 644 "$SCRIPT_DIR/icons/radio-console-512.png" "$ICON_DIR/radio-console-512.png"
echo "  Installed: $ICON_DIR/radio-console.svg"
echo "  Installed: $ICON_DIR/radio-console-512.png"

# ---- 2/10. Install desktop shortcuts ----
echo ""
echo "[2/10] Installing desktop shortcuts..."

# Mode 755, NOT `chmod +x`. With the default umask `chmod +x` yields 775, and GNOME REFUSES to
# launch a group-writable .desktop file — silently, with no error anywhere. That mode bit is
# why the box's GV-Bridge entry never worked. `install -m 755` states the mode instead of
# incrementing it, so the umask cannot leak in.
#
# The placeholders substituted below (@ICON_DIR@, @KIOSK_USER@, @ROTARY_UNIT@) are what let the
# repo stay the source of truth for entries whose content depends on this box: an absolute icon
# path and a unit name cannot be committed literally. No entry carries ANY of the three yet —
# they arrive with KIOSK-2 — so today all three substitutions are a no-op on every file. They
# are here because the installer, not the entries, is what this row fixes.
install_entry() {
  local src="$1" name; name="$(basename "$src")"
  sed -e "s|@ICON_DIR@|$ICON_DIR|g" \
      -e "s|@KIOSK_USER@|$KIOSK_USER|g" \
      -e "s|@ROTARY_UNIT@|$ROTARY_UNIT|g" \
      "$src" > "$DESKTOP_DIR/$name.tmp"
  install -m 755 "$DESKTOP_DIR/$name.tmp" "$DESKTOP_DIR/$name"
  install -m 644 "$DESKTOP_DIR/$name.tmp" "$APPS_DIR/$name"
  rm -f "$DESKTOP_DIR/$name.tmp"
  # Mark as trusted so GNOME doesn't show the "untrusted application launcher" warning.
  gio set "$DESKTOP_DIR/$name" metadata::trusted true 2>/dev/null || true
  if command -v desktop-file-validate >/dev/null 2>&1; then
    desktop-file-validate "$DESKTOP_DIR/$name" || echo "  WARNING: $name failed validation"
  fi
  echo "  Installed: $name (mode $(stat -c '%a' "$DESKTOP_DIR/$name"))"
}

# Installing from the repo is the whole point of this block. Nothing had ever copied these
# entries onto the box, so ~/Desktop was hand-maintained and drifted: the in-tree
# radio-console.desktop has carried --password-store=basic since 2026-08-11 and the live copy
# still did not. Three separate instances of that drift turned up in one day.
for file in radio-console.desktop radio-exit-browser.desktop radio-shutdown.desktop; do
  install_entry "$SCRIPT_DIR/$file"
done

echo "  Desktop shortcuts installed."

# ---- 3/10. Install kiosk helper scripts ----
echo ""
echo "[3/10] Installing kiosk helper scripts..."

# These live in /usr/local/bin, not /opt/radio-console, deliberately: Deploy-ToLinux.ps1 wipes
# /opt/radio-console/{api,web} on every deploy and calls both of these scripts during that same
# deploy. /usr/local/bin survives.
for s in radio-kiosk-launch radio-kiosk-exit; do
  sudo install -m 755 "$SCRIPT_DIR/bin/$s" "$BIN_DIR/$s"
  echo "  Installed: $BIN_DIR/$s"
done

# ---- 4/10. Remove entries this setup no longer owns ----
echo ""
echo "[4/10] Removing superseded desktop entries..."

# `onboard` is dropped: docs/uat/2026-08-03-osk-wayland-viability/REPORT.md measured Chrome 151
# on Wayland issuing ZERO zwp_text_input_v3.enable() calls, so the OS keyboard cannot type into
# a web page here at all. The Web UI's built-in virtual keyboard is the only working text input.
# The package is dropped from deploy/provision/packages.sh; this disables the autostart entry a
# hand-provisioned box may still carry. Renamed rather than deleted so it is recoverable.
ONBOARD_AUTOSTART="$HOME/.config/autostart/onboard-autostart.desktop"
if [ -f "$ONBOARD_AUTOSTART" ]; then
  mv "$ONBOARD_AUTOSTART" "$ONBOARD_AUTOSTART.disabled"
  echo "  Disabled: onboard-autostart.desktop"
else
  echo "  onboard-autostart.desktop not present (already disabled, or never installed)."
fi
pkill -x onboard 2>/dev/null || true

# ---- 5/10. Install autostart entry ----
echo ""
echo "[5/10] Installing autostart entry..."

AUTOSTART_DIR="$HOME/.config/autostart"
mkdir -p "$AUTOSTART_DIR"

cp "$SCRIPT_DIR/radio-kiosk-autostart.desktop" "$AUTOSTART_DIR/radio-kiosk-autostart.desktop"
echo "  Autostart entry installed to $AUTOSTART_DIR/"

# ---- 6/10. Switch services to run as login user ----
echo ""
echo "[6/10] Switching radio services to run as $KIOSK_USER..."

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

# ---- 7/10. Configure GNOME auto-login ----
echo ""
echo "[7/10] Configuring GNOME auto-login..."

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

# ---- 8/10. Disable screen blanking and lock ----
echo ""
echo "[8/10] Disabling screen blanking and lock..."

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

# ---- 9/10. Install unclutter + display helpers ----
echo ""
echo "[9/10] Installing unclutter and display helpers..."
# Note: Virtual keyboard for text entry is built into the Radio Console Web UI.
# No system-level on-screen keyboard needed (onboard doesn't work on Wayland).

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

# ---- 10/10. Install browser refresh helper ----
echo ""
echo "[10/10] Installing browser refresh helper..."

REFRESH_SCRIPT="/usr/local/bin/radio-refresh-browser"
sudo tee "$REFRESH_SCRIPT" > /dev/null << 'EOF'
#!/bin/bash
# Refresh the Radio Console kiosk browser by sending F5 to the Chrome window with xdotool.
#
# KNOWN BROKEN ON THIS BOX, and installed anyway only so a working X11 host still has it:
# xdotool talks X11, the appliance runs Wayland, and `xdotool search` cannot see a native
# Wayland window — so this prints "No browser window found" and does nothing. It is NOT the
# post-deploy refresh path: Deploy-ToLinux.ps1 stops and relaunches the kiosk itself via
# radio-kiosk-exit / radio-kiosk-launch. Fixing this properly means driving CDP on :9223, which
# the dedicated kiosk profile has just made reachable again — tracked separately, not here.
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
echo "  NOTE: radio-refresh-browser does not work on Wayland (xdotool is X11-only)."
echo "        Deploys relaunch the kiosk themselves; nothing needs to call it."

# ---- Done ----
echo ""
echo "========================================="
echo "Kiosk setup complete!"
echo "========================================="
echo ""
echo "Installed:"
echo "  Desktop shortcuts: $DESKTOP_DIR/radio-*.desktop (mode 755)"
echo "  App menu entries:  $APPS_DIR/radio-*.desktop"
echo "  Icon assets:       $ICON_DIR/"
echo "  Autostart:         $AUTOSTART_DIR/radio-kiosk-autostart.desktop"
echo "  Kiosk helpers:     $BIN_DIR/radio-kiosk-launch, $BIN_DIR/radio-kiosk-exit"
echo "  Browser refresh:   $REFRESH_SCRIPT (X11 only — inert on this Wayland box)"
echo ""
echo "Next steps:"
echo "  1. Reboot to test auto-login + auto-launch"
echo "  2. Use 'Exit Browser' shortcut to close the kiosk (spares the Google Voice bridge)"
echo "  3. Use 'Shutdown System' shortcut to power off"
echo "  4. Deploys relaunch the kiosk themselves and report whether it reached the UI."
echo "     To relaunch by hand: radio-kiosk-launch"
echo ""
echo "This script is the source of truth for ~/Desktop. Do not hand-edit those entries —"
echo "re-run it from a checkout instead, or the box drifts from the repo again."
echo ""
