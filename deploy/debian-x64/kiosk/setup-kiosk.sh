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

# APPS_DIR, DESKTOP_DIR and ICON_DIR below are all derived from $HOME — the user RUNNING this
# script — while KIOSK_USER decides which user the services are switched to. Those are the same
# user in the documented invocation, and this script has always assumed so. Since KIOSK-2 the
# assumption is load-bearing rather than cosmetic: ICON_DIR is substituted into `Icon=` inside
# the rendered entries, so a mismatch writes entries pointing into another user's home. Warned
# rather than refused, because the mismatch is legitimate for an inspection run.
if [ "$KIOSK_USER" != "$(whoami)" ]; then
  echo "  WARNING: installing as $(whoami) but configuring for $KIOSK_USER."
  echo "           Desktop entries, icons and the rendered Icon= path all go to $HOME."
  echo "           Run this as $KIOSK_USER unless that is deliberate."
  echo ""
fi

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
GTK_DIR="/usr/local/share/radio-console/gtk-touch"

mkdir -p "$APPS_DIR" "$DESKTOP_DIR" "$ICON_DIR"

# The rotary-phone unit name is DISCOVERED, not assumed. A wrong name here would make the
# launcher report PHONE as a hard failure forever — which is exactly the cry-wolf failure the
# Google Voice rule exists to prevent. It is derived once, ahead of every numbered step, because
# render_template() substitutes it into bin/radio-console-open and the script runs under
# `set -u`. Verified on the box 2026-08-18: the unit
# is `rotary-phone.service` (a separate `rotary-phone-cookies.service` is a oneshot cookie
# refresh, not the API — the ^ anchor plus `head -1` keeps it out).
# The trailing `|| true` is required, not belt-and-braces: this script runs under
# `set -euo pipefail`, and with pipefail a no-match `grep` fails the whole pipeline, which
# would abort setup-kiosk.sh before it installed anything — and would make the `if [ -z ]`
# fallback directly below it unreachable dead code.
# The leading `sed` strips the status bullet that `systemctl list-units --all` prefixes to units
# in a failed or not-found state. Without it `awk '{print $1}'` yields the bullet, the `^` anchor
# never matches, and discovery silently misses a rotary-phone unit that happens to be FAILED at
# install time — exactly when getting the name right matters most.
ROTARY_UNIT="${ROTARY_UNIT:-$(systemctl list-units --type=service --all --no-legend \
  | sed 's/^[[:space:]]*[^[:alnum:]][[:space:]]*//' \
  | awk '{print $1}' | grep -iE '^(rotary-?phone|rotaryphone)\.service$' | head -1 || true)}"
if [ -z "$ROTARY_UNIT" ]; then
  echo "  WARNING: no rotary-phone service unit found; PHONE repair will be a no-op."
  ROTARY_UNIT="rotary-phone.service"
fi

# ---- 1/11. Install icon assets ----
echo "[1/11] Installing icon assets..."

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

# ---- 2/11. Install desktop shortcuts ----
echo ""
echo "[2/11] Installing desktop shortcuts..."

# The placeholders are what let the repo stay the source of truth for files whose content
# depends on this box: an absolute icon path under the login user's home, and a discovered unit
# name, cannot be committed literally. Three files carry one — radio-console.desktop and
# radio-kiosk-autostart.desktop carry @ICON_DIR@, and bin/radio-console-open carries
# @ROTARY_UNIT@ — so the substitution lives here once and is shared by the entry installer, the
# autostart entry and the helper-script loop. @KIOSK_USER@ is carried by no file today; it is
# inherited from KIOSK-1 and left in place because a substitution that matches nothing costs
# nothing.
render_template() {   # $1 = source file, $2 = destination for the rendered copy
  sed -e "s|@ICON_DIR@|$ICON_DIR|g" \
      -e "s|@KIOSK_USER@|$KIOSK_USER|g" \
      -e "s|@ROTARY_UNIT@|$ROTARY_UNIT|g" \
      "$1" > "$2"
}

# Mode 755, NOT `chmod +x`. With the default umask `chmod +x` yields 775, and GNOME REFUSES to
# launch a group-writable .desktop file — silently, with no error anywhere. That mode bit is
# why the box's GV-Bridge entry never worked. `install -m 755` states the mode instead of
# incrementing it, so the umask cannot leak in.
install_entry() {
  local src="$1" name; name="$(basename "$src")"
  render_template "$src" "$DESKTOP_DIR/$name.tmp"
  install -m 755 "$DESKTOP_DIR/$name.tmp" "$DESKTOP_DIR/$name"
  install -m 644 "$DESKTOP_DIR/$name.tmp" "$APPS_DIR/$name"
  rm -f "$DESKTOP_DIR/$name.tmp"
  # Mark as trusted so GNOME doesn't show the "untrusted application launcher" warning.
  gio set "$DESKTOP_DIR/$name" metadata::trusted true 2>/dev/null || true
  # Clear any hand-dragged icon position. This is a tidy-up, NOT the thing that delivers the
  # ordering — `keep-arranged` below is. Measured 2026-08-18: DING writes a fresh position for
  # every icon moments after these files are rewritten, so on its own this unset is overwritten
  # before it can matter. It is kept because it costs nothing and removes the stale coordinates
  # a future `keep-arranged false` would otherwise resurrect. Tolerant of failure: an entry that
  # never carried the attribute has nothing to clear.
  gio set -t unset "$DESKTOP_DIR/$name" metadata::nautilus-icon-position 2>/dev/null || true
  if command -v desktop-file-validate >/dev/null 2>&1; then
    desktop-file-validate "$DESKTOP_DIR/$name" || echo "  WARNING: $name failed validation"
  fi
  echo "  Installed: $name (mode $(stat -c '%a' "$DESKTOP_DIR/$name"))"
}

# Installing from the repo is the whole point of this block. Nothing had ever copied these
# entries onto the box, so ~/Desktop was hand-maintained and drifted: the in-tree
# radio-console.desktop has carried --password-store=basic since 2026-08-11 and the live copy
# still did not. Three separate instances of that drift turned up in one day.
#
# THE NAME ORDER OF THESE THREE ENTRIES IS THE ACCIDENTAL-SHUTDOWN MITIGATION, NOT COSMETICS.
# Sorted by name they are `Exit to Desktop` < `Radio Console` < `Shutdown System`, so the safe,
# most-tapped action sits physically BETWEEN the two "leaving" actions: Exit and Shutdown are
# never neighbours, and a fingertip that misses Exit lands on something harmless.
#
# What makes this robust is that `Radio Console` is the MIDDLE of three, and the middle of a
# three-element sort is the same element whichever direction the sort runs. That matters here,
# because the direction was NOT stable in measurement: with keep-arranged on (set below), two
# consecutive installs on 2026-08-18 laid the column out as Exit(y=34) / Console(206) /
# Shutdown(378), and then as Shutdown(34) / Console(206) / Exit(378) — the second stable across
# a 60s settle. Radio Console was in the middle both times, which is the property the design
# actually needs, so this comment claims that and not a specific top-to-bottom order.
#
# ANY FUTURE RENAME MUST KEEP `Radio Console` SORTING BETWEEN THE OTHER TWO. Renaming
# `Shutdown System` to, say, `Power Off` would sort it straight next to `Exit to Desktop` and
# quietly undo this, with nothing failing to say so.
for file in radio-console.desktop radio-exit-browser.desktop radio-shutdown.desktop; do
  install_entry "$SCRIPT_DIR/$file"
done

echo "  Desktop shortcuts installed."

# Desktop icon layout and size (§2.1, §2.2), guarded because the schema exists only where the
# Desktop Icons NG extension is installed.
#
# `keep-arranged true` IS THE ACCIDENTAL-SHUTDOWN MITIGATION, and it is what actually delivers
# it — clearing the saved positions in install_entry() does not. Measured on the box 2026-08-18:
# DING re-persists a position for every icon moments after the entries are rewritten, so a
# one-shot unset is racing a writer that always wins. `keep-arranged` makes DING auto-arrange by
# `arrangeorder` and ignore stored positions outright, which is a setting rather than a race.
# Both are set explicitly rather than trusted as defaults: `keep-arranged` was `false` on this
# box, which is exactly why `arrangeorder` was already 'NAME' and yet had never applied.
#
# Verified after setting it: Exit to Desktop y=34, Radio Console y=206, Shutdown System y=378 —
# the safe, most-tapped action physically between the two "leaving" actions, which is the whole
# point. Before it, the live column ran GV-Bridge / Shutdown System / Exit Browser / Radio
# Console, i.e. Shutdown and Exit ADJACENT.
#
# `large` gives a ~96px tile — past the 56px touch-preferred target and legible from across the
# room, which is the glanceability bar every other surface in this app is held to. It cannot
# overflow the 720px height: three entries at ~130px per cell occupy about 390px of it. It was
# `standard` on the box when this was measured (2026-08-18).
if gsettings list-schemas 2>/dev/null | grep -q '^org.gnome.shell.extensions.ding$'; then
  gsettings set org.gnome.shell.extensions.ding arrangeorder 'NAME'
  gsettings set org.gnome.shell.extensions.ding keep-arranged true
  gsettings set org.gnome.shell.extensions.ding icon-size 'large'
  echo "  Desktop icons: auto-arranged by name, size 'large'."
else
  echo "  Desktop Icons NG schema not present; icon layout and size left as they are."
fi

# ---- 3/11. Install kiosk helper scripts ----
echo ""
echo "[3/11] Installing kiosk helper scripts..."

# These live in /usr/local/bin, not /opt/radio-console, deliberately: Deploy-ToLinux.ps1 wipes
# /opt/radio-console/{api,web} on every deploy and calls both of these scripts during that same
# deploy. /usr/local/bin survives.
for s in radio-kiosk-launch radio-kiosk-exit radio-shutdown-confirm; do
  sudo install -m 755 "$SCRIPT_DIR/bin/$s" "$BIN_DIR/$s"
  echo "  Installed: $BIN_DIR/$s"
done

# radio-console-open is installed separately because it carries @ROTARY_UNIT@ and so has to go
# through the same substitution the desktop entries get. It is rendered to a temp file first:
# render_template writes with this script's own privileges and $BIN_DIR is root-owned, so the
# redirect inside it cannot write there directly.
RCO_TMP="$(mktemp)"
render_template "$SCRIPT_DIR/bin/radio-console-open" "$RCO_TMP"
sudo install -m 755 "$RCO_TMP" "$BIN_DIR/radio-console-open"
rm -f "$RCO_TMP"
echo "  Installed: $BIN_DIR/radio-console-open (ROTARY_UNIT=$ROTARY_UNIT)"

# ---- 4/11. Install the touch GTK overrides ----
echo ""
echo "[4/11] Installing touch GTK overrides..."

# GTK's default button metrics do not meet the touch floor and there is no zenity flag for it,
# so the dialogs get a private GTK config dir that radio-console-open and radio-shutdown-confirm
# point XDG_CONFIG_HOME at. Nothing global is changed and no other application sees this.
#
# It lives under /usr/local/share rather than in $HOME for the same reason the helper scripts
# live in /usr/local/bin: those scripts read it, and it has to survive a deploy that wipes
# /opt/radio-console. gtk-4.0/ is the right subdirectory because zenity here is 4.0.1 linked
# against libgtk-4 (measured 2026-08-18); a gtk-3.0/ file would be read by nothing.
sudo install -d -m 755 "$GTK_DIR/gtk-4.0"
sudo install -m 644 "$SCRIPT_DIR/gtk-touch/gtk-4.0/gtk.css" "$GTK_DIR/gtk-4.0/gtk.css"
echo "  Installed: $GTK_DIR/gtk-4.0/gtk.css"

# ---- 5/11. Remove entries this setup no longer owns ----
echo ""
echo "[5/11] Removing superseded desktop entries..."

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

# The GV Bridge entry pointed at `systemctl --user start gv-bridge-chrome`, an ABANDONED
# snap-Chromium unit — different browser, different profile, different extension — rather than
# the live path. It was also mode 775, and GNOME silently refuses to launch a group-writable
# .desktop file; that mode bit, not a "permission problem", is why it never worked. Its job now
# lives inside radio-console-open, which calls the canonical ensure script
# (~/bin/gv-bridge-ensure.sh: google-chrome + ~/.config/gv-bridge-chrome +
# /opt/rotary-phone/ChromeExtension). Removing it also closes the ambiguity flagged in
# design/plans/IAC-PRISTINE-INSTALL-AUDIT.md §7.
#
# This removes a DESKTOP ENTRY and nothing else. The bridge itself — the script, its watchdog
# and nightly timers, its profile and its extension — is RotaryPhone-owned and is not touched
# here, now or ever.
#
# Measured 2026-08-18: the live entry is exactly `GV-Bridge.desktop` in ~/Desktop, and
# ~/.local/share/applications held only the three radio-* entries. The lower-case spellings are
# kept anyway — removing a file that is not there costs nothing, and missing a stale launcher
# over its capitalisation costs a broken icon nobody can account for.
for stale in "$DESKTOP_DIR/GV-Bridge.desktop" "$APPS_DIR/GV-Bridge.desktop" \
             "$DESKTOP_DIR/gv-bridge.desktop" "$APPS_DIR/gv-bridge.desktop"; do
  if [ -f "$stale" ]; then
    rm -f "$stale"
    echo "  Removed: $stale"
  fi
done

# ---- 6/11. Install autostart entry ----
echo ""
echo "[6/11] Installing autostart entry..."

AUTOSTART_DIR="$HOME/.config/autostart"
mkdir -p "$AUTOSTART_DIR"

# Rendered, not copied: since KIOSK-2 this entry carries @ICON_DIR@ too, and a plain `cp` would
# leave the placeholder in place and hand GNOME an icon path that cannot resolve.
AUTOSTART_ENTRY="$AUTOSTART_DIR/radio-kiosk-autostart.desktop"
render_template "$SCRIPT_DIR/radio-kiosk-autostart.desktop" "$AUTOSTART_ENTRY.tmp"
install -m 644 "$AUTOSTART_ENTRY.tmp" "$AUTOSTART_ENTRY"
rm -f "$AUTOSTART_ENTRY.tmp"
echo "  Autostart entry installed to $AUTOSTART_DIR/"

# ---- 7/11. Switch services to run as login user ----
echo ""
echo "[7/11] Switching radio services to run as $KIOSK_USER..."

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

# ---- 8/11. Configure GNOME auto-login ----
echo ""
echo "[8/11] Configuring GNOME auto-login..."

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

# ---- 9/11. Disable screen blanking and lock ----
echo ""
echo "[9/11] Disabling screen blanking and lock..."

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

# ---- 10/11. Install unclutter + display helpers ----
echo ""
echo "[10/11] Installing unclutter and display helpers..."
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

# ---- 11/11. Install browser refresh helper ----
echo ""
echo "[11/11] Installing browser refresh helper..."

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
echo "  Dialog GTK theme:  $GTK_DIR/gtk-4.0/gtk.css"
echo "  Autostart:         $AUTOSTART_DIR/radio-kiosk-autostart.desktop"
echo "  Kiosk helpers:     $BIN_DIR/{radio-kiosk-launch, radio-kiosk-exit,"
echo "                     radio-console-open, radio-shutdown-confirm}"
echo "  Browser refresh:   $REFRESH_SCRIPT (X11 only — inert on this Wayland box)"
echo ""
echo "Next steps:"
echo "  1. Reboot to test auto-login + auto-launch"
echo "  2. Use 'Exit to Desktop' shortcut to close the kiosk (spares the Google Voice bridge)"
echo "  3. Use 'Shutdown System' shortcut to power off"
echo "  4. Deploys relaunch the kiosk themselves and report whether it reached the UI."
echo "     To relaunch by hand: radio-kiosk-launch"
echo ""
echo "This script is the source of truth for ~/Desktop. Do not hand-edit those entries —"
echo "re-run it from a checkout instead, or the box drifts from the repo again."
echo ""
