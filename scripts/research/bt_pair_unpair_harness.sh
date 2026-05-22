#!/usr/bin/env bash
# bt_pair_unpair_harness.sh — Scripted pair/unpair cycle harness.
#
# Drives a test phone through repeated BT power-cycle cycles to exercise the
# autoswitch gate (FM-BT-1) and gather production-quality measurement artifacts
# for Plan B (BT autoswitch gate) acceptance verification (plan task 8).
#
# Supports two phone drivers:
#   * adb (Android): `adb shell svc bluetooth disable && svc bluetooth enable`
#   * blueutil over SSH (iOS/macOS host): toggles BT power on a USB-tethered iPhone
#
# Auto-detects the driver from the environment unless overridden via --driver.
#
# Usage:
#   bt_pair_unpair_harness.sh \
#     --cycles N \
#     --period-sec X \
#     [--simulate-no-audio] \
#     [--driver adb|ssh-blueutil] \
#     [--ssh-host user@macmini] \
#     [--device-mac AA:BB:CC:DD:EE:FF]
#
# --simulate-no-audio: phone pairs but does NOT start playing audio. This is
# the FM-BT-1 trigger condition — Connected fires before any A2DP source node
# appears in PipeWire.
#
# Output: one log line per cycle on stdout, plus exit status 0 if all cycles
# completed.

set -euo pipefail

CYCLES=10
PERIOD_SEC=60
SIMULATE_NO_AUDIO=0
DRIVER=""
SSH_HOST=""
DEVICE_MAC=""

usage() {
  cat <<USAGE
Usage: $(basename "$0") --cycles N --period-sec X [--simulate-no-audio]
       [--driver adb|ssh-blueutil] [--ssh-host user@host] [--device-mac AA:BB:CC:DD:EE:FF]

Cycles BT power on the test phone every PERIOD_SEC seconds, for CYCLES iterations.

Drivers:
  adb            (default if 'adb devices' shows ≥1 device) — Android via USB ADB
  ssh-blueutil   macOS host with blueutil installed; requires --ssh-host

--simulate-no-audio  Pair-only; never start audio playback. Triggers FM-BT-1.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --cycles)             CYCLES="$2"; shift 2 ;;
    --period-sec)         PERIOD_SEC="$2"; shift 2 ;;
    --simulate-no-audio)  SIMULATE_NO_AUDIO=1; shift ;;
    --driver)             DRIVER="$2"; shift 2 ;;
    --ssh-host)           SSH_HOST="$2"; shift 2 ;;
    --device-mac)         DEVICE_MAC="$2"; shift 2 ;;
    -h|--help)            usage; exit 0 ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 2
      ;;
  esac
done

# Auto-detect driver
if [[ -z "$DRIVER" ]]; then
  if command -v adb >/dev/null 2>&1 && [[ "$(adb devices 2>/dev/null | grep -c device$ || true)" -gt 0 ]]; then
    DRIVER=adb
  elif [[ -n "$SSH_HOST" ]]; then
    DRIVER=ssh-blueutil
  else
    echo "ERROR: could not auto-detect driver. Pass --driver and any required hostname." >&2
    exit 2
  fi
fi

log() {
  printf '[%s] %s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "$*"
}

bt_power_off() {
  case "$DRIVER" in
    adb)           adb shell svc bluetooth disable ;;
    ssh-blueutil)  ssh "$SSH_HOST" 'blueutil --power 0' ;;
    *)             echo "Unsupported driver $DRIVER" >&2; return 1 ;;
  esac
}

bt_power_on() {
  case "$DRIVER" in
    adb)           adb shell svc bluetooth enable ;;
    ssh-blueutil)  ssh "$SSH_HOST" 'blueutil --power 1' ;;
    *)             echo "Unsupported driver $DRIVER" >&2; return 1 ;;
  esac
}

start_audio() {
  if [[ "$SIMULATE_NO_AUDIO" -eq 1 ]]; then
    log "    -> simulate-no-audio set, NOT starting audio (FM-BT-1 trigger)"
    return 0
  fi
  case "$DRIVER" in
    adb)
      # Trigger media play key. The phone must already have audio queued up
      # in its default media app for this to actually start playback.
      adb shell input keyevent 126  # KEYCODE_MEDIA_PLAY
      ;;
    ssh-blueutil)
      # No clean way to remote-trigger audio on macOS via blueutil alone.
      # Operator should pre-stage a paused track and rely on auto-resume on connect.
      log "    -> ssh-blueutil: cannot auto-trigger audio; ensure media is auto-resume"
      ;;
  esac
}

log "Starting harness: driver=$DRIVER cycles=$CYCLES period_sec=$PERIOD_SEC simulate_no_audio=$SIMULATE_NO_AUDIO"

for ((i = 1; i <= CYCLES; i++)); do
  log "Cycle $i/$CYCLES: BT power off"
  bt_power_off || log "    (power-off failed; continuing)"
  sleep 5

  log "Cycle $i/$CYCLES: BT power on"
  bt_power_on || log "    (power-on failed; continuing)"
  sleep 10
  start_audio || true

  remaining=$((PERIOD_SEC - 15))
  if (( remaining > 0 )); then
    log "Cycle $i/$CYCLES: sleeping ${remaining}s before next cycle"
    sleep "$remaining"
  fi
done

log "Harness complete after $CYCLES cycles."
