#!/usr/bin/env bash
# radio-audio-verify.sh (v1 — diagnostic) — Post-boot audio health verifier.
# Runs AFTER radio-api.service. v1 ONLY verifies + logs the audio pipeline state
# so a controlled reboot reveals the real recovery gap. It does NOT modify device,
# Bluetooth, or service state — self-heal actions are added in v2 once the gap is
# observed on a real reboot. Boundary: reads hci0 (music) only; never touches hci1.
set -uo pipefail

MUSIC_ADAPTER_MAC="78:20:51:F5:FB:A7"
MUSIC_DEVICES_CONF="/opt/radio-console/config/bt-music-devices.conf"
API="http://localhost:5000"
PW_USER="mmack"
PW_UID="1000"

log() { logger -t radio-audio-verify "$1"; echo "[radio-audio-verify] $1"; }
wp() { sudo -u "$PW_USER" XDG_RUNTIME_DIR="/run/user/$PW_UID" "$@" 2>/dev/null; }

log "=== post-boot audio verify start ==="

# 1. Wait for radio-api to answer its health endpoint (up to 60s).
ok=no
for i in $(seq 1 60); do
  if curl -sf "$API/api/health/version" >/dev/null 2>&1; then
    ok=yes; log "radio-api healthy after ${i}s"; break
  fi
  sleep 1
done
[[ "$ok" == yes ]] || log "WARNING: radio-api NOT healthy after 60s"

# 2. Default PipeWire sink.
log "default-sink: $(wp pactl get-default-sink || echo '?')"

# 3. Active output per Radio.API (device selection is the app's persisted preference).
active=$(curl -s "$API/api/devices/output" 2>/dev/null \
  | grep -o '"isActive":true[^}]*' | grep -o '"name":"[^"]*"' | head -1)
log "api-active-output: ${active:-unknown}"

# 4. Radio.API present in the PipeWire graph (should be linked to a sink).
if wp wpctl status | grep -q "Radio.API"; then
  log "Radio.API in PipeWire graph: yes"
else
  log "WARNING: Radio.API NOT in PipeWire graph (possible output=0 condition)"
fi

# 5. BT capture stream (only present when a phone is connected for A2DP music).
if wp wpctl status | grep -qi "bluez_input"; then
  log "bluez_input capture: present"
else
  log "bluez_input capture: none (no BT phone connected — normal for non-BT sources)"
fi

# 6. Connected music devices on hci0 (diagnostic only — no reconnect in v1).
conn=$(echo -e "select ${MUSIC_ADAPTER_MAC}\ndevices Connected\nquit" \
  | bluetoothctl 2>/dev/null | grep -c "^Device" || true)
log "hci0 connected music devices: ${conn:-0}"

log "=== post-boot audio verify complete ==="
exit 0
