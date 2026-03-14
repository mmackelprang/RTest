#!/usr/bin/env bash
# radio-bt-setup.sh — Post-boot BT adapter setup for Radio Console.
# Runs as root (oneshot systemd service) before radio-api starts.
# Sets adapter aliases, discoverable state, PipeWire default sink,
# and verifies WirePlumber patches are intact.
#
# Usage:
#   radio-bt-setup.sh              # Full setup (boot)
#   radio-bt-setup.sh --patch-only # Only verify patches (APT hook)

set -euo pipefail

MUSIC_ADAPTER_PATH="/org/bluez/hci0"
MUSIC_ADAPTER_MAC="78:20:51:F5:FB:A7"
MUSIC_ADAPTER_ALIAS="Grandpas Radio"

VOICE_ADAPTER_PATH="/org/bluez/hci1"
VOICE_ADAPTER_ALIAS="Grandpas Phone"
VOICE_ADAPTER_MAC="10:91:D1:FE:00:46"

MUSIC_DEVICES_CONF="/opt/radio-console/config/bt-music-devices.conf"
BLUEZ_LUA="/usr/share/wireplumber/scripts/monitors/bluez.lua"
BLUEZ_LUA_BAK="/usr/share/wireplumber/scripts/monitors/bluez.lua.bak"
WP_BT_DIR="/etc/wireplumber/bluetooth.lua.d"
PW_USER="mmack"
PW_UID="1000"
DEFAULT_SINK="alsa_output.pci-0000_00_1f.3.analog-stereo"

PATCH_ONLY=false
if [[ "${1:-}" == "--patch-only" ]]; then
  PATCH_ONLY=true
fi

log() { logger -t radio-bt-setup "$1"; echo "[radio-bt-setup] $1"; }

# --- Step 5: Verify bluez.lua patch ---
verify_bluez_patch() {
  if [[ ! -f "$BLUEZ_LUA" ]]; then
    log "WARNING: $BLUEZ_LUA not found — WirePlumber may not be installed"
    return
  fi

  if grep -q 'if true or properties\["api.bluez5.connection"\]' "$BLUEZ_LUA"; then
    log "bluez.lua patch: OK"
  else
    log "WARNING: bluez.lua patch missing — applying now"
    # Create backup if one doesn't exist
    [[ -f "$BLUEZ_LUA_BAK" ]] || cp "$BLUEZ_LUA" "$BLUEZ_LUA_BAK"
    # Apply the patch: replace the connection check with always-true
    sed -i 's/if properties\["api.bluez5.connection"\] == "connected" then/-- PipeWire 1.0.7 quirk: api.bluez5.connection may report "disconnected"\n  -- even when BlueZ Connected=true. Always activate to let profile policy decide.\n  if true or properties["api.bluez5.connection"] == "connected" then/' "$BLUEZ_LUA"
    log "bluez.lua patch applied — restarting wireplumber"
    sudo -u "$PW_USER" XDG_RUNTIME_DIR="/run/user/$PW_UID" systemctl --user restart wireplumber || true
  fi
}

# --- Step 6: Verify WP custom configs ---
verify_wp_configs() {
  local missing=0
  for f in 85-disable-hfp-hf.lua 87-bt-adapter-select.lua 89-bt-autoconnect.lua 90-disable-bt-input-autolink.lua; do
    if [[ ! -f "$WP_BT_DIR/$f" ]]; then
      log "WARNING: Missing WP config: $WP_BT_DIR/$f"
      missing=$((missing + 1))
    fi
  done
  if [[ $missing -eq 0 ]]; then
    log "WP bluetooth configs: OK (4/4 present)"
  else
    log "WARNING: $missing WP bluetooth config(s) missing — BT audio may not work correctly"
  fi
}

# If --patch-only, just verify patches and exit
if $PATCH_ONLY; then
  log "Running in patch-only mode (APT hook)"
  verify_bluez_patch
  verify_wp_configs
  log "Patch verification complete"
  exit 0
fi

# --- Step 1: Wait for BlueZ + specific adapter ---
log "Waiting for BlueZ adapter $MUSIC_ADAPTER_MAC..."
for i in $(seq 1 30); do
  ADDR=$(busctl call org.bluez "$MUSIC_ADAPTER_PATH" org.freedesktop.DBus.Properties Get ss org.bluez.Adapter1 Address 2>/dev/null | grep -oP '"[^"]*"' | tr -d '"' || true)
  if [[ "$ADDR" == "$MUSIC_ADAPTER_MAC" ]]; then
    log "BlueZ adapter $MUSIC_ADAPTER_MAC ready (attempt $i)"
    break
  fi
  if [[ $i -eq 30 ]]; then
    log "ERROR: BlueZ adapter $MUSIC_ADAPTER_MAC not ready after 30s — aborting"
    exit 1
  fi
  sleep 1
done

# --- Step 2: Set adapter aliases and discoverable state ---
log "Configuring adapter aliases and discoverable state..."

busctl call org.bluez "$MUSIC_ADAPTER_PATH" org.freedesktop.DBus.Properties Set ssv org.bluez.Adapter1 Alias s "$MUSIC_ADAPTER_ALIAS" 2>/dev/null && \
  log "hci0 alias: $MUSIC_ADAPTER_ALIAS" || log "WARNING: Failed to set hci0 alias"

busctl call org.bluez "$MUSIC_ADAPTER_PATH" org.freedesktop.DBus.Properties Set ssv org.bluez.Adapter1 Discoverable b true 2>/dev/null && \
  log "hci0 discoverable: on" || log "WARNING: Failed to set hci0 discoverable"

# hci1 may not exist (single-adapter setups) — don't fail
if busctl call org.bluez "$VOICE_ADAPTER_PATH" org.freedesktop.DBus.Properties Get ss org.bluez.Adapter1 Address 2>/dev/null >/dev/null; then
  busctl call org.bluez "$VOICE_ADAPTER_PATH" org.freedesktop.DBus.Properties Set ssv org.bluez.Adapter1 Alias s "$VOICE_ADAPTER_ALIAS" 2>/dev/null && \
    log "hci1 alias: $VOICE_ADAPTER_ALIAS" || log "WARNING: Failed to set hci1 alias"

  busctl call org.bluez "$VOICE_ADAPTER_PATH" org.freedesktop.DBus.Properties Set ssv org.bluez.Adapter1 Discoverable b false 2>/dev/null && \
    log "hci1 discoverable: off" || log "WARNING: Failed to set hci1 discoverable"
else
  log "hci1 not present — skipping voice adapter config"
fi

# --- Step 3: Remove stale hci1 music pairings ---
DELETIONS=0
if [[ -f "$MUSIC_DEVICES_CONF" ]]; then
  while IFS= read -r line; do
    # Skip comments and blank lines
    MAC=$(echo "$line" | sed 's/#.*//' | xargs)
    [[ -z "$MAC" ]] && continue

    PAIRING_DIR="/var/lib/bluetooth/$VOICE_ADAPTER_MAC/$MAC"
    if [[ -d "$PAIRING_DIR" ]]; then
      log "Removing stale hci1 pairing for $MAC"
      rm -rf "$PAIRING_DIR"
      DELETIONS=$((DELETIONS + 1))
    fi
  done < "$MUSIC_DEVICES_CONF"

  if [[ $DELETIONS -gt 0 ]]; then
    log "Removed $DELETIONS stale pairing(s) from hci1 — restarting BlueZ"
    systemctl restart bluetooth
    # Wait for BlueZ to come back
    sleep 2
  else
    log "No stale hci1 pairings found"
  fi
else
  log "No music devices config at $MUSIC_DEVICES_CONF — skipping pairing cleanup"
fi

# --- Step 4: Set default PipeWire sink ---
log "Setting default PipeWire sink to $DEFAULT_SINK..."
for i in $(seq 1 30); do
  if sudo -u "$PW_USER" XDG_RUNTIME_DIR="/run/user/$PW_UID" pactl set-default-sink "$DEFAULT_SINK" 2>/dev/null; then
    log "PipeWire default sink: $DEFAULT_SINK (attempt $i)"
    break
  fi
  if [[ $i -eq 30 ]]; then
    log "WARNING: Failed to set PipeWire default sink after 30s — PipeWire may not be running yet"
  fi
  sleep 1
done

# --- Steps 5-6: Verify patches ---
verify_bluez_patch
verify_wp_configs

# --- Step 7: Summary ---
log "BT setup complete: adapters configured, $DELETIONS stale pairings removed, PipeWire sink set, patches verified"
